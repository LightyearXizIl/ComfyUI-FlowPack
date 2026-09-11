using System;
using System.Net.Http;
using FlowPack.App.Services;

namespace FlowPack.Tests;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_requires_a_newer_release_installer_and_matching_checksum()
    {
        const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json(hash + "  ComfyUI-FlowPack-0.1.0-Setup.exe\n")
                : StubHttpMessageHandler.Json("""
                  { "tag_name": "v0.1.0", "assets": [
                    { "name": "ComfyUI-FlowPack-0.1.0-Setup.exe", "browser_download_url": "https://example.invalid/setup.exe" },
                    { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.invalid/SHA256SUMS.txt" }
                  ] }
                  """)));

        var update = await new GitHubReleaseUpdateService(http, "owner/repo").CheckAsync(new Version(0, 0, 2));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 1, 0), update!.Version);
        Assert.Equal(hash, update.Sha256);
    }

    [Fact]
    public async Task CheckAsync_rejects_missing_or_mismatched_checksum()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB  other.exe\n")
                : StubHttpMessageHandler.Json("""
                  { "tag_name": "v0.1.0", "assets": [
                    { "name": "ComfyUI-FlowPack-0.1.0-Setup.exe", "browser_download_url": "https://example.invalid/setup.exe" },
                    { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.invalid/SHA256SUMS.txt" }
                  ] }
                  """)));

        Assert.Null(await new GitHubReleaseUpdateService(http, "owner/repo").CheckAsync(new Version(0, 0, 2)));
    }
}
