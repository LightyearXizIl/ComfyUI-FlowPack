using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace FlowPack.Infrastructure;

public sealed record DownloadRequest(Uri Source, string StagingPath, string ExpectedSha256);
public sealed record DownloadResult(string StagingPath, long BytesWritten, string Sha256, bool Resumed);

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

    public async Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        return await DownloadCoreAsync(request, allowResume: true, cancellationToken);
    }

    private async Task<DownloadResult> DownloadCoreAsync(DownloadRequest request, bool allowResume, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(request.StagingPath))!;
        Directory.CreateDirectory(directory);
        var initialLength = allowResume && File.Exists(request.StagingPath) ? new FileInfo(request.StagingPath).Length : 0;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.Source);
        if (initialLength > 0) httpRequest.Headers.Range = new RangeHeaderValue(initialLength, null);
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && initialLength > 0)
        {
            var existingHash = await HashFileAsync(request.StagingPath, cancellationToken);
            if (existingHash.Equals(request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new DownloadResult(request.StagingPath, initialLength, existingHash, true);
            }
            DeleteIfExists(request.StagingPath);
            return await DownloadCoreAsync(request, allowResume: false, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        var resumed = initialLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (resumed && response.Content.Headers.ContentRange?.From != initialLength)
        {
            DeleteIfExists(request.StagingPath);
            return await DownloadCoreAsync(request, allowResume: false, cancellationToken);
        }

        var mode = resumed ? FileMode.Append : FileMode.Create;
        await using (var destination = new FileStream(request.StagingPath, mode, FileAccess.Write, FileShare.None, 131_072, useAsync: true))
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await source.CopyToAsync(destination, 131_072, cancellationToken);
        }

        var hash = await HashFileAsync(request.StagingPath, cancellationToken);
        if (!hash.Equals(request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            DeleteIfExists(request.StagingPath);
            throw new InvalidDataException("下载内容的 SHA-256 与清单不一致，已隔离该 staging 文件。");
        }
        return new DownloadResult(request.StagingPath, new FileInfo(request.StagingPath).Length, hash, resumed);
    }

    private static void ValidateRequest(DownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Source.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(request.Source.UserInfo) || !string.IsNullOrEmpty(request.Source.Fragment))
        {
            throw new ArgumentException("下载来源必须是不含凭据或片段的 HTTPS 地址。", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.StagingPath)) throw new ArgumentException("staging 路径不能为空。", nameof(request));
        if (request.ExpectedSha256.Length != 64 || !request.ExpectedSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("预期 SHA-256 必须是 64 位十六进制值。", nameof(request));
        }
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
