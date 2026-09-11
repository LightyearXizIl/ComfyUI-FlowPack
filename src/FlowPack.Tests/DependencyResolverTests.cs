using System.IO;
using FlowPack.ComfyUI;
using Xunit;

namespace FlowPack.Tests;

public sealed class DependencyResolverTests
{
    private const string UiWorkflowJson = """
        {
          "nodes": [
            {"type": "CheckpointLoaderSimple", "widgets_values": ["sd_xl_base_1.0.safetensors"]},
            {"type": "LoraLoader", "widgets_values": ["loras/cat_lora.safetensors", 0.75]},
            {"type": "Note", "widgets_values": ["just a note"]}
          ],
          "links": [],
          "version": 1
        }
        """;

    private const string SingleNodeJson = """
        {"nodes": [{"type": "NodeA", "widgets_values": []}], "links": [], "version": 1}
        """;

    [Fact]
    public async Task Resolve_marks_everything_missing_when_local_is_null()
    {
        var dependency = new WorkflowDependencyAnalyzer().AnalyzeRaw(UiWorkflowJson, "ui");

        var result = await new DependencyResolver().ResolveAsync(null, new[] { dependency });

        Assert.All(result.Models, m => Assert.False(m.PresentLocally));
        Assert.All(result.Nodes, n => Assert.False(n.PresentLocally));
    }

    [Fact]
    public async Task Resolve_detects_present_models_by_category_and_name()
    {
        var local = MakeLocal(out var root);
        try
        {
            WriteFile(Path.Combine(root, "models", "checkpoints", "sd_xl_base_1.0.safetensors"), "M");
            WriteFile(Path.Combine(root, "models", "loras", "cat_lora.safetensors"), "M");

            var dependency = new WorkflowDependencyAnalyzer().AnalyzeRaw(UiWorkflowJson, "ui");
            var result = await new DependencyResolver().ResolveAsync(local, new[] { dependency });

            var checkpoint = result.Models.Single(m => m.FileName == "sd_xl_base_1.0.safetensors");
            var lora = result.Models.Single(m => m.FileName == "cat_lora.safetensors");

            Assert.True(checkpoint.PresentLocally);
            Assert.Equal("checkpoints/sd_xl_base_1.0.safetensors", checkpoint.LocalPath);
            Assert.True(lora.PresentLocally);
            Assert.Equal("loras/cat_lora.safetensors", lora.LocalPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Resolve_marks_node_present_when_registry_package_installed()
    {
        var local = MakeLocal(out var root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "custom_nodes", "ComfyUI-Manager"));
            var registry = new StubRegistry(new() { ["NodeA"] = "ComfyUI-Manager" });

            var dependency = new WorkflowDependencyAnalyzer().AnalyzeRaw(SingleNodeJson, "n");
            var result = await new DependencyResolver(registry).ResolveAsync(local, new[] { dependency });

            var node = result.Nodes.Single();
            Assert.True(node.PresentLocally);
            Assert.Equal(NodePresenceSource.CustomInstalled, node.Source);
            Assert.Equal("ComfyUI-Manager", node.LocalPackage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Resolve_marks_node_missing_when_registry_unknown()
    {
        var local = MakeLocal(out var root);
        try
        {
            var dependency = new WorkflowDependencyAnalyzer().AnalyzeRaw(SingleNodeJson, "n");
            var result = await new DependencyResolver(new StubRegistry(new())).ResolveAsync(local, new[] { dependency });

            var node = result.Nodes.Single();
            Assert.False(node.PresentLocally);
            Assert.Equal(NodePresenceSource.CustomUnknown, node.Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Resolve_marks_node_missing_when_registry_package_not_installed()
    {
        var local = MakeLocal(out var root);
        try
        {
            // Registry says NodeA -> ComfyUI-Manager, but that package directory is absent locally.
            var registry = new StubRegistry(new() { ["NodeA"] = "ComfyUI-Manager" });
            var dependency = new WorkflowDependencyAnalyzer().AnalyzeRaw(SingleNodeJson, "n");
            var result = await new DependencyResolver(registry).ResolveAsync(local, new[] { dependency });

            var node = result.Nodes.Single();
            Assert.False(node.PresentLocally);
            Assert.Equal(NodePresenceSource.CustomUnknown, node.Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubRegistry : ICustomNodeRegistry
    {
        private readonly Dictionary<string, string> _map;
        public StubRegistry(Dictionary<string, string> map) => _map = map;
        public string? ResolveInstalledPackage(string nodeType) =>
            _map.TryGetValue(nodeType, out var package) ? package : null;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static ComfyDesktopLocation MakeLocal(out string root)
    {
        root = Temp();
        Directory.CreateDirectory(Path.Combine(root, "models"));
        Directory.CreateDirectory(Path.Combine(root, "custom_nodes"));
        return new ComfyDesktopLocation(
            "test", root, null,
            Path.Combine(root, "models"),
            Path.Combine(root, "custom_nodes"),
            null, null, DateTimeOffset.UtcNow);
    }

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "flowpack-res-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
