using System.Net.Http;
using System.IO;
using System.Text.Json;

namespace FlowPack.App.Services;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);
}

/// <summary>Verified release metadata. Downloading and launching an installer stays in the Worker boundary.</summary>
public sealed record UpdateInfo(Version Version, Uri InstallerUri, string InstallerFileName, string Sha256, Uri ChecksumsUri);

/// <summary>
/// Reads the latest GitHub release and accepts it only when a setup asset and its exact SHA-256
/// entry are both present. This class never downloads or starts an installer.
/// </summary>
public sealed class GitHubReleaseUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly Uri _latestReleaseUri;

    public GitHubReleaseUpdateService(HttpClient httpClient, string repository = "LightyearXizIl/ComfyUI-FlowPack")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _latestReleaseUri = new Uri($"https://api.github.com/repos/{repository}/releases/latest");
    }

    public async Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseUri);
        request.Headers.UserAgent.ParseAdd("ComfyUI-FlowPack/0.1");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var release = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!release.RootElement.TryGetProperty("tag_name", out var tag) || !TryParseVersion(tag.GetString(), out var version))
            throw new InvalidDataException("更新版本信息无效。");
        if (version <= currentVersion) return null;
        if (!release.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("新版本缺少安装资产。");

        var installer = assets.EnumerateArray().FirstOrDefault(asset =>
            asset.TryGetProperty("name", out var name) && name.GetString()?.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) == true);
        var checksums = assets.EnumerateArray().FirstOrDefault(asset =>
            asset.TryGetProperty("name", out var name) && string.Equals(name.GetString(), "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (installer.ValueKind == JsonValueKind.Undefined || checksums.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("新版本缺少安装器或 SHA256SUMS.txt。");

        var installerName = installer.GetProperty("name").GetString();
        var installerUrl = installer.GetProperty("browser_download_url").GetString();
        var checksumsUrl = checksums.GetProperty("browser_download_url").GetString();
        if (string.IsNullOrWhiteSpace(installerName) || !Uri.TryCreate(installerUrl, UriKind.Absolute, out var parsedInstaller) ||
            !Uri.TryCreate(checksumsUrl, UriKind.Absolute, out var parsedChecksums) || parsedInstaller.Scheme != Uri.UriSchemeHttps || parsedChecksums.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("更新下载地址无效，必须使用 HTTPS。");
        }

        var checksum = await ReadChecksumAsync(parsedChecksums, installerName, cancellationToken);
        return checksum is null ? throw new InvalidDataException("新版本安装器缺少匹配的 SHA-256 校验记录。") : new UpdateInfo(version, parsedInstaller, installerName, checksum, parsedChecksums);
    }

    private async Task<string?> ReadChecksumAsync(Uri checksumsUri, string installerName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, checksumsUri);
        request.Headers.UserAgent.ParseAdd("ComfyUI-FlowPack/0.1");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var lines = (await response.Content.ReadAsStringAsync(cancellationToken)).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && string.Equals(parts[^1].TrimStart('*'), installerName, StringComparison.Ordinal) &&
                parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit))
            {
                return parts[0].ToUpperInvariant();
            }
        }
        return null;
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        return Version.TryParse(tag?.Trim().TrimStart('v', 'V'), out version!);
    }
}
