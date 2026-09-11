using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace FlowPack.Infrastructure;

/// <summary>
/// Streams a resource into staging for user-initiated downloads (requirement 8). Unlike
/// <see cref="VerifiedDownloadService"/> this does not require a precomputed SHA-256; the user decides
/// to download from a source link whose hash we generally do not know in advance. It still enforces
/// HTTPS-only sources and optionally verifies a caller-supplied SHA-256. Downloads target staging only
/// (never a ComfyUI Desktop directory) per the phase-E safety boundary.
/// </summary>
public sealed class StagingDownloadService
{
    private readonly HttpClient _httpClient;

    public StagingDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<StagingDownloadResult> DownloadAsync(
        Uri source,
        string stagingPath,
        string? expectedSha256 = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (source.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(source.UserInfo) || !string.IsNullOrEmpty(source.Fragment))
        {
            throw new ArgumentException("下载来源必须是不含凭据或片段的 HTTPS 地址。", nameof(source));
        }
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("staging 路径不能为空。", nameof(stagingPath));
        if (expectedSha256 is not null && (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit)))
        {
            throw new ArgumentException("预期 SHA-256 必须是 64 位十六进制值。", nameof(expectedSha256));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(stagingPath))!;
        Directory.CreateDirectory(directory);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, source);
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var written = 0L;
        await using (var destination = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None, 131_072, useAsync: true))
        await using (var content = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var buffer = new byte[131_072];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                progress?.Report(written);
            }
        }

        var hash = await HashFileAsync(stagingPath, cancellationToken);
        if (expectedSha256 is not null && !hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
            throw new InvalidDataException("下载内容的 SHA-256 与预期不一致，已隔离该 staging 文件。");
        }

        return new StagingDownloadResult(stagingPath, written, hash);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}

public sealed record StagingDownloadResult(string StagingPath, long BytesWritten, string Sha256);
