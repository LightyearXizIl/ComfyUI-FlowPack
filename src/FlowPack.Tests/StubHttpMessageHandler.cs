using System.Net;
using System.Net.Http;
using System.Text;

namespace FlowPack.Tests;

/// <summary>
/// Test double that returns scripted responses without any real network call.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

    public List<string> RequestUrls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
        return Task.FromResult(_factory(request));
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage Bytes(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(body)
        };
    }
}
