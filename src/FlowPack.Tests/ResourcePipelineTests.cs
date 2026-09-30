using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class ResourcePipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-pipeline-" + Guid.NewGuid().ToString("N"));
    private const string Workflow = """{"nodes":[{"id":1,"type":"CheckpointLoaderSimple","widgets_values":["tiny.safetensors"]}],"links":[]}""";
    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content); return path;
    }
    private InstanceDescriptor Instance()
    {
        var data = Path.Combine(_root, "target");
        return new("isolated-fixture", "fixture", "desktop-2", null, data, data, data, Path.Combine(data, "user"),
            Path.Combine(data, "user", "default", "workflows"), Path.Combine(data, "custom_nodes"), null,
            [Path.Combine(data, "models")], Path.Combine(data, "models"), [], "fixture-fingerprint", []);
    }
    [Fact]
    public async Task Directory_model_roundtrip_keeps_nested_members_and_reuses_workflow_reference()
    {
        Write("source/models/diffusers/pipeline/model_index.json", "{\"_class_name\":\"StableDiffusionPipeline\"}");
        Write("source/models/diffusers/pipeline/unet/config.json", "{\"_class_name\":\"UNet2DConditionModel\"}");
        Write("source/models/diffusers/pipeline/unet/model-00001.safetensors", "weight-one");
        Write("source/models/diffusers/pipeline/unet/model-00002.safetensors", "weight-two");
        Write("source/models/diffusers/pipeline/unet/model.safetensors.index.json", "{\"weight_map\":{\"one\":\"model-00001.safetensors\",\"two\":\"model-00002.safetensors\"}}");
        Write("source/models/diffusers/pipeline/tokenizer/tokenizer.json", "{\"version\":\"1.0\"}");
        Write("source/models/diffusers/pipeline/unrelated.json", "{\"note\":\"not model configuration\"}");
        var source = Instance() with { ModelRoots = [Path.Combine(_root, "source", "models")] };
        var scanned = await new ResourceInventoryService().ScanAsync(source);
        var bundle = Assert.Single(scanned.Resources, x => x.Kind == ResourceKind.Model && Directory.Exists(x.SourcePath));
        Assert.Equal("diffusers/pipeline", bundle.RelativePath);
        var export = new PlannedZipExportService();
        var exportPlan = await export.PlanAsync([bundle]);
        Assert.Empty(exportPlan.Issues); Assert.Equal(6, exportPlan.Files.Count);
        Assert.DoesNotContain(exportPlan.Files, x => x.ArchivePath.Contains("unrelated"));
        var zip = Path.Combine(_root, "directory-model.zip");
        await export.ExportAsync(exportPlan, zip);
        var imported = await new ResourceImportService().ImportAsync(zip, Path.Combine(_root, "stage"));
        Assert.All(imported.Resources, x => Assert.Equal(RecognitionState.Confirmed, x.State));
        var workflow = WorkflowDocumentFactory.Create("w", "pipeline flow", """{"version":1,"nodes":[{"id":1,"type":"DiffusersLoader","widgets_values":["pipeline"]}]}""");
        var local = new ResourceInventory(Instance(), [], ["DiffusersLoader"], []);
        var analysis = new InventoryDependencyAnalyzer().Analyze([workflow], ImportInventoryService.Merge(local, imported.Resources));
        Assert.Equal(DependencyState.Present, Assert.Single(analysis.Dependencies).State);
        var incomplete = imported.Resources.Where(x => !x.TargetRelativePath.EndsWith("tokenizer.json")).ToArray();
        analysis = new InventoryDependencyAnalyzer().Analyze([workflow], ImportInventoryService.Merge(local, incomplete));
        Assert.Equal(DependencyState.Unresolved, Assert.Single(analysis.Dependencies).State);
        var installer = new ResourceInstallationService(Path.Combine(_root, "library"));
        Assert.Contains((await installer.PlanAsync(Instance(), incomplete)).BlockingReasons, x => x.Contains("未全部选中"));
        var installPlan = await installer.PlanAsync(Instance(), imported.Resources);
        Assert.Empty(installPlan.BlockingReasons);
        await installer.ExecuteAsync(installPlan, (instance, ct) => Task.FromResult(instance));
        foreach (var file in installPlan.Files) Assert.Equal(file.Sha256, await ResourceImportService.HashAsync(file.TargetPath));
        var target = await new ResourceInventoryService().ScanAsync(Instance());
        analysis = new InventoryDependencyAnalyzer().Analyze([workflow], target with { CoreNodeTypes = ["DiffusersLoader"] });
        Assert.Equal(DependencyState.Present, Assert.Single(analysis.Dependencies).State);
    }

    [Fact]
    public async Task Directory_model_missing_or_escaping_shards_blocks_complete_export()
    {
        var root = Path.GetDirectoryName(Write("bundle/config.json", "{\"model_type\":\"fixture\"}"))!;
        Write("bundle/model.safetensors", "weight");
        Write("bundle/model.safetensors.index.json", "{\"weight_map\":{\"a\":\"missing.safetensors\",\"b\":\"../outside.safetensors\"}}");
        var service = new PlannedZipExportService();
        var plan = await service.PlanAsync([new(root, ResourceKind.Model, "bundle", root, "text_encoders/bundle", "text_encoders") { ModelDirectoryRoot = root }]);
        Assert.Equal(2, plan.Issues.Count);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExportAsync(plan, Path.Combine(_root, "blocked.zip")));
        Assert.False(File.Exists(Path.Combine(_root, "blocked.zip")));
    }

    [Fact]
    public async Task Single_file_node_roundtrip_preserves_source_and_deploys_at_custom_nodes_root()
    {
        const string code = "class Test: pass\nNODE_CLASS_MAPPINGS = {'SingleNode': Test}\n";
        var source = Write("source/my_node.py", code);
        Write("source/unrelated.py", "print('not a node')");
        var import = await new ResourceImportService().ImportAsync(source, Path.Combine(_root, "stage"));
        var node = Assert.Single(import.Resources);
        Assert.Equal(ResourceKind.CustomNode, node.Kind);
        Assert.Equal(RecognitionState.Confirmed, node.State);
        Assert.Equal("custom_nodes/my_node.py", node.TargetRelativePath);
        var inventory = ImportInventoryService.Merge(new(Instance(), [], [], []), import.Resources);
        Assert.Contains("SingleNode", Assert.Single(inventory.Resources).NodeTypes!);
        var service = new PlannedZipExportService();
        var plan = await service.PlanAsync(inventory.Resources);
        Assert.Equal("custom_nodes/my_node.py", Assert.Single(plan.Files).ArchivePath);
        var zip = Path.Combine(_root, "single-node.zip");
        await service.ExportAsync(plan, zip);
        var again = await new ResourceImportService().ImportAsync(zip, Path.Combine(_root, "stage-again"));
        var installation = new ResourceInstallationService(Path.Combine(_root, "library"));
        var deployment = await installation.PlanAsync(Instance(), again.Resources);
        Assert.Empty(deployment.BlockingReasons);
        await installation.ExecuteAsync(deployment, (instance, ct) => Task.FromResult(instance));
        var target = Path.Combine(Instance().CustomNodesDirectory, "my_node.py");
        Assert.Equal(code, await File.ReadAllTextAsync(target));
        var scanned = await new ResourceInventoryService().ScanAsync(Instance());
        Assert.Contains(scanned.Resources, x => x.SourcePath == target && x.NodeTypes!.Contains("SingleNode"));
        Assert.False(File.Exists(Path.Combine(Instance().CustomNodesDirectory, "unrelated.py")));
    }

    [Fact]
    public async Task Ordinary_python_script_is_not_auto_classified_as_a_node()
    {
        var source = Write("source/tool.py", "print('ordinary script')");
        var plan = await new ResourceImportService().ImportAsync(source, Path.Combine(_root, "stage"));
        Assert.Equal(RecognitionState.Unknown, Assert.Single(plan.Resources).State);
    }

    [Fact]
    public async Task Third_party_wrapped_zip_does_not_require_manifest_or_guess_unknown_json()
    {
        Write("source/wrapper/workflows/flow.json", Workflow);
        Write("source/wrapper/models/checkpoints/tiny.safetensors", "model");
        Write("source/wrapper/settings.json", "{\"theme\":\"dark\"}");
        Write("source/wrapper/loose.bin", "unknown model category");
        var zip = Path.Combine(_root, "third-party.zip"); ZipFile.CreateFromDirectory(Path.Combine(_root, "source"), zip);
        var plan = await new ResourceImportService().ImportAsync(zip, Path.Combine(_root, "staging"));
        Assert.Single(plan.Workflows);
        Assert.Contains(plan.Resources, x => x.TargetRelativePath == "workflows/flow.json" && x.State == RecognitionState.Confirmed);
        Assert.Contains(plan.Resources, x => x.TargetRelativePath == "models/checkpoints/tiny.safetensors" && x.State == RecognitionState.Confirmed);
        Assert.Contains(plan.Resources, x => x.OriginalPath.EndsWith("settings.json") && x.State == RecognitionState.Unknown);
        Assert.Contains(plan.Resources, x => x.OriginalPath.EndsWith("loose.bin") && x.State == RecognitionState.NeedsConfirmation);
    }
    [Fact]
    public async Task Node_repository_zip_preserves_source_and_omits_credentials_and_cache()
    {
        Write("repo/MyNode-main/__init__.py", "NODE_CLASS_MAPPINGS = {'MyNode': MyNode}");
        Write("repo/MyNode-main/requirements.txt", "numpy>=1");
        Write("repo/MyNode-main/.env", "SECRET=fixture");
        Write("repo/MyNode-main/__pycache__/node.pyc", "cache");
        var plan = await new ResourceImportService().ImportAsync(Path.Combine(_root, "repo"), Path.Combine(_root, "stage"));
        Assert.Equal(2, plan.Resources.Count);
        Assert.All(plan.Resources, x => Assert.Equal(ResourceKind.CustomNode, x.Kind));
    }
    [Fact]
    public async Task Export_roundtrip_deduplicates_shared_resource_and_preserves_workflow_bytes()
    {
        var workflow = Write("source/a.json", Workflow);
        var model = Write("source/tiny.safetensors", "model");
        var selected = new LocalResource[] { new("w", ResourceKind.Workflow, "a", workflow, "workflows/a.json"),
            new("m", ResourceKind.Model, "tiny.safetensors", model, "checkpoints/tiny.safetensors", "checkpoints") };
        var service = new PlannedZipExportService();
        var plan = await service.PlanAsync(selected.Concat(selected).ToArray());
        Assert.Equal(2, plan.Files.Count);
        var output = Path.Combine(_root, "roundtrip.zip"); await service.ExportAsync(plan, output);
        var imported = await new ResourceImportService().ImportAsync(output, Path.Combine(_root, "stage"));
        Assert.Equal(2, imported.Resources.Count);
        Assert.Equal(Workflow, Assert.Single(imported.Workflows).RawJson);
    }
    [Fact]
    public async Task Changed_export_source_never_commits_output()
    {
        var file = Write("source/model.safetensors", "first");
        var service = new PlannedZipExportService();
        var plan = await service.PlanAsync([new("m", ResourceKind.Model, "m", file, "checkpoints/model.safetensors")]);
        File.WriteAllText(file, "other");
        var output = Path.Combine(_root, "changed.zip");
        await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(plan, output));
        Assert.False(File.Exists(output)); Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }
    [Fact]
    public async Task Same_name_different_content_is_not_renamed_even_for_partial_export()
    {
        var service = new PlannedZipExportService();
        var plan = await service.PlanAsync([
            new("a", ResourceKind.Model, "m", Write("a/m.safetensors", "a"), "checkpoints/m.safetensors"),
            new("b", ResourceKind.Model, "m", Write("b/m.safetensors", "b"), "checkpoints/m.safetensors")]);
        Assert.NotEmpty(plan.Issues);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExportAsync(plan, Path.Combine(_root, "conflict.zip"), true));
    }
    [Fact]
    public async Task Install_fixture_reuses_identical_files_and_refuses_changed_configuration()
    {
        var imported = await new ResourceImportService().ImportAsync(Write("source/w.json", Workflow), Path.Combine(_root, "stage"));
        var service = new ResourceInstallationService(Path.Combine(_root, "library")); var instance = Instance();
        var plan = await service.PlanAsync(instance, imported.Resources); Assert.Empty(plan.BlockingReasons);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteAsync(plan, (x, ct) => Task.FromResult(x with { ConfigurationFingerprint = "changed" })));
        Assert.False(Directory.Exists(instance.WorkflowsDirectory));
        await service.ExecuteAsync(plan, (x, ct) => Task.FromResult(x));
        var repeated = await service.PlanAsync(instance, imported.Resources);
        Assert.True(Assert.Single(repeated.Files).Reuse); Assert.Equal(0, repeated.RequiredBytes);
        File.WriteAllText(repeated.Files[0].TargetPath, "user modified");
        Assert.NotEmpty((await service.PlanAsync(instance, imported.Resources)).BlockingReasons);
    }
    [Fact]
    public async Task Competing_install_wait_can_be_cancelled_without_touching_targets()
    {
        var imported = await new ResourceImportService().ImportAsync(Write("source/w.json", Workflow), Path.Combine(_root, "stage"));
        var service = new ResourceInstallationService(Path.Combine(_root, "library"));
        var plan = await service.PlanAsync(Instance(), imported.Resources);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = service.ExecuteAsync(plan, (x, ct) => Task.FromResult(x), token: timeout.Token,
            prepareEnvironment: async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); });
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(plan with { Id = Guid.NewGuid().ToString("N") },
                (x, ct) => Task.FromResult(x), token: cancelled.Token));
            Assert.False(File.Exists(plan.Files[0].TargetPath));
        }
        finally { release.TrySetResult(); await first; }
        Assert.True(File.Exists(plan.Files[0].TargetPath));
    }

    [Fact]
    public async Task Python_callback_failure_is_reported_without_claiming_environment_rollback()
    {
        var imported = await new ResourceImportService().ImportAsync(Write("source/w.json", Workflow), Path.Combine(_root, "stage"));
        var service = new ResourceInstallationService(Path.Combine(_root, "library"));
        var plan = await service.PlanAsync(Instance(), imported.Resources);
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(plan, (x, ct) => Task.FromResult(x),
            installEnvironment: ct => throw new IOException("Python partially changed")));
        Assert.True(File.Exists(plan.Files[0].TargetPath));
        var journal = JsonSerializer.Deserialize<DeploymentJournal>(File.ReadAllText(Path.Combine(_root, "library", "state", "journal", plan.Id + ".json")))!;
        Assert.Equal("NeedsReview", journal.State); Assert.Contains("Python", journal.Error);
        Assert.Single(await service.RecoverAsync());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changes_during_python_preparation_block_file_commit(bool configurationChanged)
    {
        Write("source/custom_nodes/Test/__init__.py", "NODE_CLASS_MAPPINGS = {'Test': Test}");
        var imported = await new ResourceImportService().ImportAsync(Path.Combine(_root, "source"), Path.Combine(_root, "stage"));
        var service = new ResourceInstallationService(Path.Combine(_root, "library"));
        var instance = Instance();
        var plan = await service.PlanAsync(instance, imported.Resources);
        var changed = false;
        var error = await Record.ExceptionAsync(() => service.ExecuteAsync(plan,
            (x, ct) => Task.FromResult(changed && configurationChanged ? x with { ConfigurationFingerprint = "changed" } : x),
            prepareEnvironment: ct =>
            {
                changed = true;
                if (!configurationChanged) Write("target/custom_nodes/Test/added.py", "user changes");
                return Task.CompletedTask;
            }));
        if (configurationChanged) Assert.IsType<InvalidDataException>(error);
        else Assert.IsType<IOException>(error);
        Assert.False(File.Exists(plan.Files[0].TargetPath));
    }

    [Fact]
    public async Task Incomplete_node_selection_does_not_satisfy_dependency()
    {
        Write("source/custom_nodes/Test/__init__.py", "NODE_CLASS_MAPPINGS = {'Test': Test}");
        Write("source/custom_nodes/Test/runtime.py", "class Test: pass");
        var imported = await new ResourceImportService().ImportAsync(Path.Combine(_root, "source"), Path.Combine(_root, "stage"));
        var local = await new ResourceInventoryService().ScanAsync(Instance());
        Assert.DoesNotContain(ImportInventoryService.Merge(local, imported.Resources.Take(1).ToArray()).Resources, x => x.Kind == ResourceKind.CustomNode);
        Assert.Single(ImportInventoryService.Merge(local, imported.Resources).Resources, x => x.Kind == ResourceKind.CustomNode);
    }

    [Theory]
    [InlineData("models/../evil")]
    [InlineData("models/checkpoints/CON.bin")]
    [InlineData("C:/evil")]
    [InlineData("models/checkpoints/x:stream")]
    public void Deployment_paths_reject_traversal_and_windows_aliases(string relative)
        => Assert.Throws<InvalidDataException>(() => ResourceInstallationService.ResolveTarget(Instance(), relative));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
