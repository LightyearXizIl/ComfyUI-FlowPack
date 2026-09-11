using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class VerifiedDownloadServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"FlowPack.DownloadTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Valid_206_response_resumes_the_existing_staging_file()
    {
        var full = Encoding.UTF8.GetBytes("hello verified download");
        var existing = full[..6];
        var remaining = full[6..];
        var stagingPath = Path.Combine(_directory, "download.part");
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(stagingPath, existing);
        var handler = new StubHandler(request =>
        {
            Assert.Equal("bytes=6-", request.Headers.Range!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(remaining)
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(6, full.Length - 1, full.Length);
            return response;
        });

        var result = await new VerifiedDownloadService(new HttpClient(handler)).DownloadAsync(
            new DownloadRequest(new Uri("https://example.invalid/resource"), stagingPath, Hash(full)));

        Assert.True(result.Resumed);
        Assert.Equal(full.Length, result.BytesWritten);
        Assert.Equal(full, await File.ReadAllBytesAsync(stagingPath));
    }

    [Fact]
    public async Task Server_200_response_restarts_instead_of_appending_to_stale_data()
    {
        var full = Encoding.UTF8.GetBytes("fresh response");
        var stagingPath = Path.Combine(_directory, "download.part");
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(stagingPath, Encoding.UTF8.GetBytes("stale"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(full) });

        var result = await new VerifiedDownloadService(new HttpClient(handler)).DownloadAsync(
            new DownloadRequest(new Uri("https://example.invalid/resource"), stagingPath, Hash(full)));

        Assert.False(result.Resumed);
        Assert.Equal(full, await File.ReadAllBytesAsync(stagingPath));
    }

    [Fact]
    public async Task Hash_mismatch_removes_the_untrusted_staging_file()
    {
        var stagingPath = Path.Combine(_directory, "download.part");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("untrusted"))
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedDownloadService(new HttpClient(handler)).DownloadAsync(
            new DownloadRequest(new Uri("https://example.invalid/resource"), stagingPath, new string('0', 64))));

        Assert.False(File.Exists(stagingPath));
    }

    [Fact]
    public void Non_https_source_is_rejected_before_network_access()
    {
        var handler = new StubHandler(_ => throw new Xunit.Sdk.XunitException("network must not be reached"));

        Assert.Throws<ArgumentException>(() => new VerifiedDownloadService(new HttpClient(handler)).DownloadAsync(
            new DownloadRequest(new Uri("http://example.invalid/resource"), Path.Combine(_directory, "download.part"), new string('0', 64))).GetAwaiter().GetResult());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
