using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace FlowPack.Infrastructure;

public sealed record DownloadRequest(Uri Source, string StagingPath, string? ExpectedSha256);
public sealed record DownloadResult(string StagingPath, long BytesWritten, string Sha256, bool Resumed)
{
    public string? SourceSha256 { get; init; }
    public bool SourceHashVerified => SourceSha256 is not null;
}
public sealed record DownloadProgress(long CompletedBytes, long? TotalBytes);

/// <summary>
/// Streams a resource only into staging. A caller must separately make a verified
/// resource available in a library or deployment target.
/// </summary>
public sealed class VerifiedDownloadService
{
    private readonly HttpClient _httpClient;

    public VerifiedDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default, IProgress<DownloadProgress>? progress = null)
    {
        ValidateRequest(request);
        var metadata = await ReadMetadataAsync(request, cancellationToken);
        var result = await DownloadCoreAsync(request, allowResume: request.ExpectedSha256 is not null || metadata?.ETag is not null, cancellationToken, progress, metadata);
        return result with { SourceSha256 = request.ExpectedSha256 };
    }

    private async Task<DownloadResult> DownloadCoreAsync(DownloadRequest request, bool allowResume, CancellationToken cancellationToken, IProgress<DownloadProgress>? progress = null, ResumeMetadata? metadata = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(request.StagingPath))!;
        Directory.CreateDirectory(directory);
        var initialLength = allowResume && File.Exists(request.StagingPath) ? new FileInfo(request.StagingPath).Length : 0;

        using var response = await SendWithCheckedRedirectsAsync(request.Source, initialLength, metadata?.ETag, cancellationToken);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && initialLength > 0)
        {
            var existingHash = await HashFileAsync(request.StagingPath, cancellationToken);
            if (existingHash.Equals(request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new DownloadResult(request.StagingPath, initialLength, existingHash, true);
            }
            DeleteIfExists(request.StagingPath);
            return await DownloadCoreAsync(request, allowResume: false, cancellationToken, progress);
        }

        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } finalUri && finalUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("下载重定向到非 HTTPS 地址。");
        if (response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() is "text/html" or "application/xhtml+xml")
            throw new InvalidDataException("来源返回网页而不是资源文件，请提供直接下载地址。");
        var resumed = initialLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (resumed && (response.Content.Headers.ContentRange?.From != initialLength ||
            (metadata?.ETag is not null && response.Headers.ETag?.Tag != metadata.ETag)))
        {
            DeleteIfExists(request.StagingPath);
            return await DownloadCoreAsync(request, allowResume: false, cancellationToken, progress);
        }
        if (!resumed && response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From != 0)
            throw new InvalidDataException("来源返回了不完整的文件区间。");
        var total = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is { } length ? length + (resumed ? initialLength : 0) : (long?)null);
        if (total is { } expected && new DriveInfo(Path.GetPathRoot(Path.GetFullPath(request.StagingPath))!).AvailableFreeSpace < expected - (resumed ? initialLength : 0) + 64L * 1024 * 1024)
            throw new IOException("暂存磁盘空间不足。");
        var strongEtag = response.Headers.ETag is { IsWeak: false } strong ? strong.Tag : null;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var prefix = new byte[512]; var prefixLength = 0;
        while (prefixLength < prefix.Length)
        {
            var count = await source.ReadAsync(prefix.AsMemory(prefixLength), cancellationToken);
            if (count == 0) break;
            prefixLength += count;
        }
        var contentStart = prefix.AsSpan(0, prefixLength).ToArray();
        if (resumed)
        {
            await using var partial = File.OpenRead(request.StagingPath);
            var existingPrefix = new byte[(int)Math.Min(512L, initialLength)];
            await partial.ReadExactlyAsync(existingPrefix, cancellationToken);
            contentStart = existingPrefix.Concat(contentStart).Take(512).ToArray();
        }
        if (LooksLikeHtml(contentStart))
            throw new InvalidDataException("来源返回网页内容而不是资源文件，即使响应标为二进制也不能安装。");
        await File.WriteAllTextAsync(request.StagingPath + ".download.json", JsonSerializer.Serialize(new ResumeMetadata(request.Source.ToString(), strongEtag, total)), cancellationToken);

        var mode = resumed ? FileMode.Append : FileMode.Create;
        await using (var destination = new FileStream(request.StagingPath, mode, FileAccess.Write, FileShare.None, 131_072, useAsync: true))
        {
            await destination.WriteAsync(prefix.AsMemory(0, prefixLength), cancellationToken);
            var buffer = new byte[131_072]; var completed = (resumed ? initialLength : 0L) + prefixLength; int read;
            if (total is not null && completed > total) throw new InvalidDataException("下载实际大小超过来源声明。");
            progress?.Report(new(completed, total));
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken); completed += read;
                if (total is not null && completed > total) throw new InvalidDataException("下载实际大小超过来源声明。");
                progress?.Report(new(completed, total));
            }
            if (total is not null && completed != total) throw new IOException("下载被截断，可继续任务重新获取缺少内容。");
            await destination.FlushAsync(cancellationToken); destination.Flush(flushToDisk: true);
        }

        var hash = await HashFileAsync(request.StagingPath, cancellationToken);
        if (request.ExpectedSha256 is not null && !hash.Equals(request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            DeleteIfExists(request.StagingPath);
            throw new InvalidDataException("下载内容的 SHA-256 与清单不一致，已删除不可信暂存文件。");
        }
        return new DownloadResult(request.StagingPath, new FileInfo(request.StagingPath).Length, hash, resumed);
    }

    private async Task<HttpResponseMessage> SendWithCheckedRedirectsAsync(Uri source, long offset, string? etag, CancellationToken token)
    {
        var current = source;
        for (var redirects = 0; redirects <= 8; redirects++)
        {
            if (current.Scheme != Uri.UriSchemeHttps || current.UserInfo.Length > 0 || current.Fragment.Length > 0)
                throw new InvalidDataException("下载跳转必须使用不含凭据或片段的 HTTPS 地址。");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            if (offset > 0 && etag is not null) request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(etag));
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var finalUri = response.RequestMessage?.RequestUri ?? current;
            if (finalUri.Scheme != Uri.UriSchemeHttps || finalUri.UserInfo.Length > 0 || finalUri.Fragment.Length > 0)
            { response.Dispose(); throw new InvalidDataException("下载响应来自不安全的跳转地址。"); }
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidDataException("下载跳转没有有效目标地址。");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }
        throw new InvalidDataException("下载跳转次数过多。");
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> bytes)
    {
        var prefix = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
            prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("<head", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateRequest(DownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Source.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(request.Source.UserInfo) || !string.IsNullOrEmpty(request.Source.Fragment))
        {
            throw new ArgumentException("下载来源必须是不含凭据或片段的 HTTPS 地址。", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.StagingPath)) throw new ArgumentException("staging 路径不能为空。", nameof(request));
        if (request.ExpectedSha256 is not null && (request.ExpectedSha256.Length != 64 || !request.ExpectedSha256.All(Uri.IsHexDigit)))
        {
            throw new ArgumentException("预期 SHA-256 必须是 64 位十六进制值。", nameof(request));
        }
    }
    private sealed record ResumeMetadata(string Source, string? ETag, long? Length);
    private static async Task<ResumeMetadata?> ReadMetadataAsync(DownloadRequest request, CancellationToken token)
    {
        var path = request.StagingPath + ".download.json";
        if (!File.Exists(path)) return null;
        try
        {
            var metadata = JsonSerializer.Deserialize<ResumeMetadata>(await File.ReadAllTextAsync(path, token));
            return metadata?.Source == request.Source.ToString() ? metadata : null;
        }
        catch (JsonException) { return null; }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
