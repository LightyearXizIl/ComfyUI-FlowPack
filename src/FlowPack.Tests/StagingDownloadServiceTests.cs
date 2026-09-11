using System.IO;
using System.Net;
using System.Net.Http;
using FlowPack.Infrastructure;
using Xunit;

namespace FlowPack.Tests;

public class StagingDownloadServiceTests
{
    [Fact]
    public async Task Download_writes_bytes_and_reports_progress()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var client = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(payload)));
        var service = new StagingDownloadService(client);
        var staging = Path.Combine(Path.GetTempPath(), "flowpack_staging_" + Guid.NewGuid().ToString("N") + ".bin");
        var reported = new List<long>();

        var result = await service.DownloadAsync(new Uri("https://example.com/file.bin"), staging, progress: new Progress<long>(reported.Add));

        Assert.Equal(8L, result.BytesWritten);
        Assert.True(File.Exists(staging));
        Assert.Equal(64, result.Sha256.Length);
        Assert.Contains(8L, reported);
        File.Delete(staging);
    }

    [Fact]
    public async Task Download_rejects_non_https_source()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes([1])));
        var service = new StagingDownloadService(client);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DownloadAsync(new Uri("http://example.com/file.bin"), Path.GetTempFileName()));
    }

    [Fact]
    public async Task Download_verifies_expected_sha()
    {
        var payload = new byte[] { 9, 8, 7 };
        using var client = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(payload)));
        var service = new StagingDownloadService(client);
        var staging = Path.GetTempFileName();
        var realHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));

        var ok = await service.DownloadAsync(new Uri("https://example.com/x.bin"), staging, expectedSha256: realHash);
        Assert.Equal(realHash, ok.Sha256);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadAsync(new Uri("https://example.com/y.bin"), Path.GetTempFileName(), expectedSha256: new string('0', 64)));
        File.Delete(staging);
    }
}
