using FlowPack.App;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class OnlineResourceRowTests
{
    [Theory]
    [InlineData("https://example.invalid/model.safetensors", "model.safetensors")]
    [InlineData("https://example.invalid/workflow.json?download=1", "workflow.json")]
    [InlineData("https://example.invalid/model.html", null)]
    [InlineData("https://huggingface.co/owner/repo/blob/main/model.safetensors", null)]
    [InlineData("https://example.invalid/search?q=model", null)]
    [InlineData("https://user:secret@example.invalid/model.pth", null)]
    [InlineData("http://example.invalid/model.pth", null)]
    [InlineData(null, null)]
    public void File_sources_do_not_accept_pages_credentials_or_plain_http(string? url, string? expected)
    {
        var row = new OnlineResourceRow(new("id", "name", ResourceKind.Model, 1, null, url));
        Assert.Equal(expected, row.DownloadFileName); Assert.Equal(expected is not null, row.CanDownload);
        Assert.Contains("未提供来源", row.HashNotice);
    }

    [Fact]
    public void Node_archives_require_a_fixed_commit_or_declared_hash()
    {
        var resource = new ResourceEntry("node", "node", ResourceKind.CustomNode, 1, null, "https://example.invalid/node.zip");
        Assert.False(new OnlineResourceRow(resource).CanDownload);
        Assert.True(new OnlineResourceRow(resource with { Sha256 = new string('A', 64) }).CanDownload);
        Assert.False(new OnlineResourceRow(resource with { SourceUrl = "https://github.com/owner/repo/archive/refs/heads/main.zip" }).CanDownload);
        Assert.False(new OnlineResourceRow(resource with { SourceUrl = "https://codeload.github.com/owner/repo/main.zip" }).CanDownload);
        var fixedNode = new OnlineResourceRow(resource with { SourceUrl = "https://codeload.github.com/owner/repo/zip/" + new string('a', 40) });
        Assert.True(fixedNode.CanDownload); Assert.EndsWith(".zip", fixedNode.DownloadFileName);
    }
}
