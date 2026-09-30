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

    [Theory]
    [InlineData("http://example.invalid/model.pth")]
    [InlineData("https://user:secret@example.invalid/model.pth")]
    [InlineData("https://example.invalid/model.pth#fragment")]
    public async Task Unsafe_redirect_is_rejected_before_followup_request(string location)
    {
        var calls = 0;
        var handler = new StubHandler(_ => { calls++; var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location); return response; });
        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedDownloadService(new HttpClient(handler)).DownloadAsync(
            new(new Uri("https://example.invalid/start"), Path.Combine(_directory, "model.pth"), null)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Relative_https_redirect_keeps_resume_range_and_verifies_complete_content()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "payload"); await File.WriteAllTextAsync(path, "abc");
        var calls = 0;
        var handler = new StubHandler(request =>
        {
            Assert.Equal("bytes=3-", request.Headers.Range!.ToString());
            if (calls++ == 0) { var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect); redirect.Headers.Location = new Uri("/final", UriKind.Relative); return redirect; }
            Assert.Equal("https://example.invalid/final", request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("def"u8.ToArray()) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6); return response;
        });
        var result = await new VerifiedDownloadService(new HttpClient(handler)).DownloadAsync(new(new Uri("https://example.invalid/start"), path, Hash("abcdef"u8.ToArray())));
        Assert.True(result.Resumed); Assert.Equal(2, calls); Assert.Equal("abcdef", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("\uFEFF \r\n<!DOCTYPE html><html>login</html>")]
    [InlineData("<HTML>download error</HTML>")]
    public async Task Binary_labelled_html_is_rejected_without_overwriting_existing_staging(string html)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "model.pth"); await File.WriteAllTextAsync(path, "preserved partial");
        var handler = new StubHandler(_ => { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(html)) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); return response; });
        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedDownloadService(new HttpClient(handler)).DownloadAsync(new(new Uri("https://example.invalid/model.pth"), path, null)));
        Assert.Equal("preserved partial", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Html_split_between_existing_prefix_and_range_response_is_rejected()
    {
        Directory.CreateDirectory(_directory); var path = Path.Combine(_directory, "model.pth");
        await File.WriteAllTextAsync(path, "<!do");
        var bytes = "<!doctype html>error"u8.ToArray();
        var handler = new StubHandler(_ => { var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[4..]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, bytes.Length - 1, bytes.Length); return response; });
        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedDownloadService(new HttpClient(handler)).DownloadAsync(new(new Uri("https://example.invalid/model.pth"), path, Hash(bytes))));
        Assert.Equal("<!do", await File.ReadAllTextAsync(path));
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
