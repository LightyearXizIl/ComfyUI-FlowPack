using FlowPack.ComfyUI;
using Xunit;

namespace FlowPack.Tests;

public class MissingDependencyResolverTests
{
    private sealed class StubNodeSource : INodeSourceProvider
    {
        public Task<NodeSource?> GetSourceAsync(string nodeType, CancellationToken cancellationToken = default) =>
            Task.FromResult<NodeSource?>(new NodeSource("https://github.com/foo/ComfyUI-Bar", "ComfyUI-Bar",
                [new SourceLink("GitHub 仓库搜索", "https://github.com/search?q=Bar")]));
    }

    private sealed class StubModelSource : IModelSourceProvider
    {
        public Task<ModelSource?> GetSourceAsync(string fileName, string? categoryHint = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModelSource?>(new ModelSource("https://huggingface.co/foo/resolve/main/" + fileName, 1_234_567_890L,
                [new SourceLink("Hugging Face 搜索", "https://huggingface.co/models?search=" + fileName)]));
    }

    [Fact]
    public async Task ResolveDownloadPlan_excludes_present_items_and_lists_missing()
    {
        var resolution = new DependencyResolution(
            Nodes:
            [
                new NodeDependency("ComfyUI-Bar", PresentLocally: true, NodePresenceSource.CustomInstalled, "ComfyUI-Bar"),
                new NodeDependency("MissingNode", PresentLocally: false, NodePresenceSource.CustomUnknown, null)
            ],
            Models:
            [
                new ModelDependency("sd_xl.safetensors", "sd_xl.safetensors", "checkpoints", PresentLocally: true, "checkpoints/sd_xl.safetensors"),
                new ModelDependency("lora.safetensors", "lora.safetensors", "loras", PresentLocally: false, null)
            ],
            WorkflowDisplayNames: ["demo"]);

        var plan = await new MissingDependencyResolver(new StubNodeSource(), new StubModelSource())
            .ResolveDownloadPlanAsync(resolution);

        Assert.Equal(2, plan.Count);
        var node = plan.Single(e => e.Kind == DependencyKind.Node);
        Assert.Equal("MissingNode", node.Name);
        Assert.Equal("https://github.com/foo/ComfyUI-Bar", node.DirectUrl);
        var model = plan.Single(e => e.Kind == DependencyKind.Model);
        Assert.Equal("lora.safetensors", model.Name);
        Assert.Equal(1_234_567_890L, model.SizeBytes);
        Assert.StartsWith("https://huggingface.co/foo/resolve/main/", model.DirectUrl);
    }

    [Fact]
    public async Task ResolveDownloadPlan_returns_empty_when_nothing_missing()
    {
        var resolution = new DependencyResolution(
            [new NodeDependency("Core", PresentLocally: true, NodePresenceSource.Core, null)],
            [new ModelDependency("x.safetensors", "x.safetensors", null, PresentLocally: true, "x.safetensors")],
            ["w"]);

        var plan = await new MissingDependencyResolver(new StubNodeSource(), new StubModelSource())
            .ResolveDownloadPlanAsync(resolution);

        Assert.Empty(plan);
    }
}
