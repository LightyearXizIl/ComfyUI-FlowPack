using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class OnlineSourceEditingTests
{
    private static ImportPlan Plan(ResourceKind kind = ResourceKind.Model, string? hash = null) => new("old", "fixture", "staging", [], [], [])
    { PendingDownloads = [new("r", "resource.pth", kind, 42, hash, null) { DeploymentPurpose = "models/upscale_models/resource.pth" }] };

    [Theory]
    [InlineData("http://example.invalid/file.pth")]
    [InlineData("https://user:password@example.invalid/file.pth")]
    [InlineData("https://example.invalid/file.pth#fragment")]
    [InlineData("https://example.invalid/search?q=file.pth")]
    [InlineData("https://huggingface.co/a/b/blob/main/file.pth")]
    [InlineData("https://github.com/a/b/archive/main.zip")]
    public void Rejects_web_pages_and_unsafe_sources(string url) => Assert.Throws<InvalidDataException>(() =>
        OnlineSourceEditing.Apply(Plan(), new("old", "r", url, 1)));

    [Fact]
    public void Preserves_hash_size_identity_and_destination()
    {
        var plan = Plan(hash: new string('a', 64));
        var result = OnlineSourceEditing.Apply(plan, new("old", "r", "https://example.invalid/model.pth", 1));
        Assert.Equal(plan.PendingDownloads[0], result.PendingDownloads[0] with { SourceUrl = null });
        Assert.Empty(result.Resources); Assert.NotEqual(plan.Id, result.Id);
    }

    [Fact]
    public void Node_without_hash_requires_fixed_archive()
    {
        var plan = Plan(ResourceKind.CustomNode);
        Assert.Throws<InvalidDataException>(() => OnlineSourceEditing.Apply(plan, new("old", "r", "https://example.invalid/node.zip", 1)));
        var url = "https://codeload.github.com/owner/repo/zip/" + new string('a', 40);
        Assert.Equal(url, OnlineSourceEditing.Apply(plan, new("old", "r", url, 1)).PendingDownloads[0].SourceUrl);
    }
}
