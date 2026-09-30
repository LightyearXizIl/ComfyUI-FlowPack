using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class CoreSafetyRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-safety-" + Guid.NewGuid().ToString("N"));
    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path;
    }
    private InstanceDescriptor Instance => new("fixture", "fixture", "desktop-2", null, _root, _root, _root,
        Path.Combine(_root, "user"), Path.Combine(_root, "user/default/workflows"), Path.Combine(_root, "custom_nodes"), null,
        [Path.Combine(_root, "models")], Path.Combine(_root, "models"), [], "fixture", []);

    [Fact]
    public async Task Model_category_relative_path_and_explicit_hash_are_not_interchangeable()
    {
        var model = Write("tiny.safetensors", "correct");
        var wrong = Write("other.safetensors", "wrong");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("correct")));
        var workflow = WorkflowDocumentFactory.Create("w", "w", """{"nodes":[{"id":1,"type":"CheckpointLoaderSimple","widgets_values":["tiny.safetensors"]}],"links":[],"models":[{"name":"tiny.safetensors","sha256":"HASH"}]}""".Replace("HASH", hash));
        var inventory = new ResourceInventory(Instance, [
            new("loras", ResourceKind.Model, "tiny.safetensors", model, "loras/tiny.safetensors", "loras"),
            new("nested", ResourceKind.Model, "tiny.safetensors", model, "checkpoints/nested/tiny.safetensors", "checkpoints"),
            new("wrong", ResourceKind.Model, "tiny.safetensors", wrong, "checkpoints/tiny.safetensors", "checkpoints")], ["CheckpointLoaderSimple"], []);
        Assert.Equal(DependencyState.Missing, Assert.Single((await new InventoryDependencyAnalyzer().AnalyzeAsync([workflow], inventory)).Dependencies).State);
        inventory = inventory with { Resources = inventory.Resources.Append(new("right", ResourceKind.Model, "tiny.safetensors", model, "checkpoints/tiny.safetensors", "checkpoints")).ToArray() };
        var dependency = Assert.Single((await new InventoryDependencyAnalyzer().AnalyzeAsync([workflow], inventory)).Dependencies);
        Assert.Equal(DependencyState.Present, dependency.State); Assert.Equal("right", Assert.Single(dependency.Candidates).Id);
    }

    [Fact]
    public void Node_directory_and_wrong_version_cannot_satisfy_a_workflow()
    {
        var workflow = WorkflowDocumentFactory.Create("w", "w", """{"nodes":[{"id":1,"type":"Custom","properties":{"cnr_id":"test-node","ver":"1.2.3"}}],"links":[]}""");
        var inventory = new ResourceInventory(Instance, [new("n", ResourceKind.CustomNode, "n", _root, "custom_nodes/n", NodeTypes: ["Custom"], Version: "1.2.2") { PackageIdentity = "test-node" }], [], []);
        Assert.Equal(DependencyState.Unresolved, Assert.Single(new InventoryDependencyAnalyzer().Analyze([workflow], inventory).Dependencies).State);
        inventory = inventory with { Resources = [inventory.Resources[0] with { Version = "1.2.3" }] };
        Assert.Equal(DependencyState.Unresolved, Assert.Single(new InventoryDependencyAnalyzer().Analyze([workflow], inventory).Dependencies).State);
        inventory = inventory with { RuntimeFingerprint = Instance.ConfigurationFingerprint,
            Resources = [inventory.Resources[0] with { RuntimeChecked = true, LoadedNodeTypes = ["Custom"] }] };
        Assert.Equal(DependencyState.Present, Assert.Single(new InventoryDependencyAnalyzer().Analyze([workflow], inventory).Dependencies).State);
        inventory = inventory with { RuntimeFingerprint = "another-instance" };
        Assert.Equal(DependencyState.Unresolved, Assert.Single(new InventoryDependencyAnalyzer().Analyze([workflow], inventory).Dependencies).State);
    }

    [Theory]
    [InlineData("NodeVersionStatusActive", "1.2.3", true)]
    [InlineData("NodeVersionStatusBanned", "1.2.3", false)]
    [InlineData("NodeVersionStatusActive", "1.2.4", false)]
    public async Task Registry_download_is_bound_to_requested_identity_version_and_active_status(string status, string version, bool accepted)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://api.comfy.org/nodes/test-node/install?version=1.2.3", request.RequestUri!.ToString());
            return Json(new { node_id = "test-node", status, version, downloadUrl = "https://example.invalid/fixed.zip", dependencies = new[] { "six==1.17.0" } });
        }));
        var resolver = new DependencySourceResolver(http);
        if (!accepted) await Assert.ThrowsAsync<InvalidDataException>(() => resolver.ResolveRegistryAsync("test-node", "1.2.3"));
        else
        {
            var source = await resolver.ResolveRegistryAsync("test-node", "1.2.3");
            Assert.Equal("1.2.3", source.Revision); Assert.Null(source.ExpectedSha256); Assert.Single(source.PythonDependencies);
        }
    }

    [Fact]
    public async Task GitHub_branch_archive_is_resolved_to_fixed_commit()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://api.github.com/repos/owner/node/commits/main", request.RequestUri!.ToString());
            return Json(new { sha = new string('a', 40) });
        }));
        var source = await new DependencySourceResolver(http).PinGitHubAsync("https://github.com/owner/node/archive/refs/heads/main.zip");
        Assert.Equal("https://codeload.github.com/owner/node/zip/" + new string('a', 40), source.DownloadUrl);
        Assert.Null(source.ExpectedSha256);
    }

    [Fact]
    public async Task Hashless_resume_uses_strong_ETag_without_claiming_source_hash_verification()
    {
        var path = Write("resource.part", "hello ");
        Write("resource.part.download.json", JsonSerializer.Serialize(new { Source = "https://example.invalid/file", ETag = "\"v1\"", Length = 11 }));
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("bytes=6-", request.Headers.Range!.ToString()); Assert.Equal("\"v1\"", request.Headers.IfRange!.EntityTag!.Tag);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StringContent("world") };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\""); response.Content.Headers.ContentRange = new(6, 10, 11); return response;
        }));
        var result = await new VerifiedDownloadService(http).DownloadAsync(new(new("https://example.invalid/file"), path, null));
        Assert.True(result.Resumed); Assert.False(result.SourceHashVerified); Assert.Equal("hello world", File.ReadAllText(path));
    }

    [Fact]
    public async Task Truncated_body_and_HTML_do_not_become_successful_downloads()
    {
        using var truncated = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("short") };
            response.Content.Headers.ContentLength = 20; return response;
        }));
        await Assert.ThrowsAnyAsync<IOException>(() => new VerifiedDownloadService(truncated).DownloadAsync(new(new("https://example.invalid/file"), Path.Combine(_root, "short"), null)));
        using var html = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("<html/>", Encoding.UTF8, "text/html") }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedDownloadService(html).DownloadAsync(new(new("https://example.invalid/page"), Path.Combine(_root, "page"), null)));
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
