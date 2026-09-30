using System.IO;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class InstalledInventoryMergeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_same_content_and_unique_local_model_replaces_staged_candidate(bool sameContent)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-installed-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var staged = Path.Combine(root, "staged.pth"); var local = Path.Combine(root, "local.pth");
            await File.WriteAllTextAsync(staged, "aaaa"); await File.WriteAllTextAsync(local, sameContent ? "aaaa" : "bbbb");
            var instance = Instance(root);
            var resource = new ImportResource("staged", staged, "fixture.pth", ResourceKind.Model, "models/upscale_models/fixture.pth", 4,
                await ResourceImportService.HashAsync(staged), RecognitionState.Confirmed, "test");
            var installed = new LocalResource("local", ResourceKind.Model, "fixture.pth", local, "upscale_models/fixture.pth", "upscale_models");
            var inventory = new ResourceInventory(instance, [installed], [], []);
            var merged = await ImportInventoryService.MergeVerifiedAsync(inventory, [resource]);
            Assert.Equal(!sameContent, merged.Resources.Any(x => x.IsStaged));
            Assert.Contains(installed, merged.Resources);
            var ambiguous = await ImportInventoryService.MergeVerifiedAsync(inventory with { Resources = [installed, installed with { Id = "second" }] }, [resource]);
            Assert.Contains(ambiguous.Resources, x => x.IsStaged);
            var otherCategory = await ImportInventoryService.MergeVerifiedAsync(inventory with { Resources = [installed with { Category = "checkpoints", RelativePath = "checkpoints/fixture.pth" }] }, [resource]);
            Assert.Contains(otherCategory.Resources, x => x.IsStaged);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Identical_node_source_keeps_unverified_runtime_state_and_extra_local_files_prevent_reuse()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-installed-node-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "staged", "custom_nodes", "fixture");
        var local = Path.Combine(root, "custom_nodes", "fixture");
        Directory.CreateDirectory(source); Directory.CreateDirectory(local);
        try
        {
            var init = Path.Combine(source, "__init__.py");
            const string raw = "NODE_CLASS_MAPPINGS = {'FixturePass': FixturePass}";
            await File.WriteAllTextAsync(init, raw); await File.WriteAllTextAsync(Path.Combine(local, "__init__.py"), raw);
            var resource = new ImportResource("node", init, "__init__.py", ResourceKind.CustomNode, "custom_nodes/fixture/__init__.py", new FileInfo(init).Length,
                await ResourceImportService.HashAsync(init), RecognitionState.Confirmed, "test");
            var installed = new LocalResource("local", ResourceKind.CustomNode, "fixture", local, "custom_nodes/fixture", NodeTypes: ["FixturePass"]);
            var inventory = new ResourceInventory(Instance(root), [installed], [], []);
            var merged = await ImportInventoryService.MergeVerifiedAsync(inventory, [resource]);
            Assert.DoesNotContain(merged.Resources, x => x.IsStaged);
            var workflow = WorkflowDocumentFactory.Create("fixture", "fixture", """{"1":{"class_type":"FixturePass","inputs":{}}}""");
            var analysis = new InventoryDependencyAnalyzer().Analyze([workflow], merged);
            Assert.Equal(DependencyState.Unresolved, Assert.Single(analysis.Dependencies).State);
            await File.WriteAllTextAsync(Path.Combine(local, "unexpected.py"), "# extra runtime file");
            Assert.Contains((await ImportInventoryService.MergeVerifiedAsync(inventory, [resource])).Resources, x => x.IsStaged);
        }
        finally { Directory.Delete(root, true); }
    }

    private static InstanceDescriptor Instance(string root) => new("fixture", "fixture", "desktop-2", null,
        root, root, root, Path.Combine(root, "user"), Path.Combine(root, "workflows"), Path.Combine(root, "custom_nodes"),
        null, [Path.Combine(root, "models")], Path.Combine(root, "models"), [], "fixture", []);
}
