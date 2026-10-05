using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class ResourceTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-transfer-" + Guid.NewGuid().ToString("N"));
    private InstanceDescriptor Instance => new("transfer", "transfer", "desktop-2", null, _root, _root, _root,
        Path.Combine(_root, "user"), Path.Combine(_root, "user", "default", "workflows"), Path.Combine(_root, "custom_nodes"),
        null, [Path.Combine(_root, "models")], Path.Combine(_root, "models"), [], "test-fingerprint", []);
    public ResourceTransferTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(ResourceKind.CustomNode)]
    [InlineData(ResourceKind.Model)]
    [InlineData(ResourceKind.Workflow)]
    public async Task Independent_export_roundtrips_only_selected_kind(ResourceKind kind)
    {
        var package = Path.Combine(Instance.CustomNodesDirectory, "sample_nodes"); Directory.CreateDirectory(package);
        await File.WriteAllTextAsync(Path.Combine(package, "__init__.py"), "NODE_CLASS_MAPPINGS = {'TransferNode': object}\n");
        await File.WriteAllTextAsync(Path.Combine(package, "requirements.txt"), "fixture==1\n");
        var workflow = Path.Combine(_root, "reference.json");
        await File.WriteAllTextAsync(workflow, """{"version":1,"nodes":[{"id":1,"type":"TransferNode"},{"id":2,"type":"CheckpointLoaderSimple","widgets_values":["missing.safetensors"]}]}""");
        var model = Path.Combine(_root, "standalone.safetensors"); await File.WriteAllTextAsync(model, "fixture model bytes");
        var node = new LocalResource("node", ResourceKind.CustomNode, "sample_nodes", package, "custom_nodes/sample_nodes", NodeTypes: ["TransferNode"]);
        var resources = new[] { node, new("w", ResourceKind.Workflow, "reference", workflow, "workflows/reference.json"),
            new("m", ResourceKind.Model, "standalone", model, "checkpoints/standalone.safetensors", "checkpoints") };
        var analysis = new DependencyAnalysis([new("missing", ResourceKind.Model, "missing.safetensors", "checkpoints", DependencyState.Missing, [], ["reference"], "fixture")], ["missing model"]);
        var selection = ExportSelectionPolicy.Build(resources, analysis, new HashSet<ResourceKind> { kind });
        Assert.Single(selection.Resources); Assert.Empty(selection.Issues);
        var progress = new Recorder();
        var plan = await new PlannedZipExportService().PlanAsync(selection.Resources, selection.Issues, progress: progress);
        var archive = Path.Combine(_root, kind + ".zip");
        await new PlannedZipExportService().ExportAsync(plan, archive, progress: progress);
        using (var zip = ZipFile.OpenRead(archive))
        {
            var prefix = kind switch { ResourceKind.CustomNode => "custom_nodes/", ResourceKind.Model => "models/", _ => "workflows/" };
            Assert.All(zip.Entries.Where(x => x.FullName != "flowpack-manifest.json"), x => Assert.StartsWith(prefix, x.FullName));
            if (kind == ResourceKind.CustomNode) Assert.Contains(zip.Entries, x => x.FullName.EndsWith("requirements.txt"));
        }
        var imported = await new ResourceImportService().ImportAsync(archive, Path.Combine(_root, "staging"), progress: progress);
        Assert.All(imported.Resources, x => Assert.Equal(kind, x.Kind));
        Assert.All(imported.Resources, x => Assert.Equal(RecognitionState.Confirmed, x.State));
        Assert.Equal(plan.Files.Count, imported.Resources.Count);
        Assert.Contains(progress.Values, x => x.Stage == "写入资源包" && x.Total > 0 && x.Completed == x.Total);
        Assert.Contains(progress.Values, x => x.Stage == "解压资源包" && x.Total > 0);
    }

    [Fact]
    public async Task Mixed_export_deduplicates_shared_package_and_ignores_unselected_missing_dependencies()
    {
        var package = Path.Combine(_root, "custom_nodes", "shared"); Directory.CreateDirectory(package);
        await File.WriteAllTextAsync(Path.Combine(package, "__init__.py"), "NODE_CLASS_MAPPINGS = {'TransferNode': object}");
        var workflow = Path.Combine(_root, "workflow.json"); await File.WriteAllTextAsync(workflow, "{\"version\":1,\"nodes\":[]}");
        var model = Path.Combine(_root, "model.safetensors"); await File.WriteAllTextAsync(model, "model");
        var node = new LocalResource("n", ResourceKind.CustomNode, "shared", package, "custom_nodes/shared");
        var w = new LocalResource("w", ResourceKind.Workflow, "workflow", workflow, "workflows/workflow.json");
        var m = new LocalResource("m", ResourceKind.Model, "model", model, "checkpoints/model.safetensors");
        var analysis = new DependencyAnalysis([new("missing", ResourceKind.Model, "unselected.safetensors", "checkpoints", DependencyState.Missing, [], ["reference"], "fixture")], ["missing"]);
        var selection = ExportSelectionPolicy.Build([w, node, m, node], analysis, new HashSet<ResourceKind> { ResourceKind.Workflow, ResourceKind.Model, ResourceKind.CustomNode });
        Assert.Equal(3, selection.Resources.Count); Assert.Empty(selection.Issues);
        var plan = await new PlannedZipExportService().PlanAsync(selection.Resources, selection.Issues);
        var archive = Path.Combine(_root, "mixed.zip"); await new PlannedZipExportService().ExportAsync(plan, archive);
        using var zip = ZipFile.OpenRead(archive);
        Assert.Equal(plan.Files.Count + 1, zip.Entries.Count);
        Assert.Single(zip.Entries, x => x.FullName.StartsWith("custom_nodes/"));
        var imported = await new ResourceImportService().ImportAsync(archive, Path.Combine(_root, "staging"));
        Assert.Equal(new[] { ResourceKind.Workflow, ResourceKind.Model, ResourceKind.CustomNode }.Order(), imported.Resources.Select(x => x.Kind).Distinct().Order());
    }

    [Fact]
    public async Task Unified_entry_recognizes_ordinary_json_legacy_manifest()
    {
        var source = Path.Combine(_root, "old-manifest.json");
        await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new { formatVersion = "1", id = "old", name = "Old", version = "1", resources = new[] {
            new { id = "r", name = "old.pth", kind = "Model", sizeBytes = 1L, sourceUrl = "https://example.invalid/old.pth" } } }));
        var plan = await new ResourceImportService().ImportAsync(source, Path.Combine(_root, "staging"));
        Assert.NotNull(plan.OnlineManifest); Assert.Single(plan.PendingDownloads); Assert.Empty(plan.Resources);
    }

    [Fact]
    public void Reference_does_not_force_payload_and_manual_deselection_is_respected()
    {
        var node = new LocalResource("node", ResourceKind.CustomNode, "node", "node", "custom_nodes/node");
        var analysis = new DependencyAnalysis([new("node", ResourceKind.CustomNode, "Type", null, DependencyState.Present, [node], ["reference"], "fixture")], []);
        Assert.Empty(ExportSelectionPolicy.Build([], analysis, new HashSet<ResourceKind> { ResourceKind.CustomNode }).Resources);
        Assert.Single(ExportSelectionPolicy.Build([], analysis, new HashSet<ResourceKind> { ResourceKind.CustomNode }, true).Resources);
        Assert.Empty(ExportSelectionPolicy.Build([node], analysis, new HashSet<ResourceKind> { ResourceKind.Model }, true).Resources);
    }

    [Fact]
    public async Task Scanner_excludes_metadata_and_reports_disk_core_version_without_execution()
    {
        Directory.CreateDirectory(Instance.WorkflowsDirectory);
        await File.WriteAllTextAsync(Path.Combine(Instance.WorkflowsDirectory, ".index.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(Instance.WorkflowsDirectory, "settings.json"), "{\"value\":42}");
        await File.WriteAllTextAsync(Path.Combine(Instance.WorkflowsDirectory, "workflow.json"), "{\"version\":1,\"nodes\":[]}");
        await File.WriteAllTextAsync(Path.Combine(_root, "comfyui_version.py"), "__version__ = '1.2.3'\nraise Exception('never run')");
        var progress = new Recorder();
        var inventory = await new ResourceInventoryService().ScanAsync(Instance, progress: progress);
        Assert.Single(inventory.Resources, x => x.Kind == ResourceKind.Workflow);
        Assert.Equal("1.2.3", inventory.CoreVersion); Assert.Null(inventory.DesktopVersion);
        Assert.False(inventory.FrontendListVerified); Assert.Contains("尚未核对", inventory.FrontendNotice);
        Assert.Contains(progress.Values, x => x.Total is null);
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("error", false)]
    [InlineData("wrong", false)]
    [InlineData("multi", false)]
    public async Task Frontend_membership_requires_correct_instance_user_and_successful_list(string mode, bool verified)
    {
        var instance = Instance with { PythonPath = Path.Combine(_root, "python.exe") };
        using var http = new HttpClient(new FrontendResponses(instance, mode));
        var stale = new ResourceInventory(instance, [], [], []) { FrontendListVerified = true, FrontendWorkflows = ["stale.json"], RunningCoreVersion = "stale" };
        var inventory = await new RuntimeNodeInspector(http, new Endpoint()).InspectAsync(stale, [instance]);
        Assert.Equal(verified, inventory.FrontendListVerified);
        if (verified) { Assert.Equal(new[] { "nested/workflow.json" }, inventory.FrontendWorkflows); Assert.Equal("2.3.4", inventory.RunningCoreVersion); }
        else Assert.Empty(inventory.FrontendWorkflows);
    }

    [Fact]
    public async Task Real_hash_and_zip_progress_include_intermediate_bytes_and_cancel_cleans_temporary_output()
    {
        var file = Path.Combine(_root, "large.safetensors");
        await File.WriteAllBytesAsync(file, new byte[4 * 1024 * 1024]);
        var progress = new Recorder();
        var plan = await new PlannedZipExportService().PlanAsync([new("model", ResourceKind.Model, "large", file, "checkpoints/large.safetensors")], progress: progress);
        Assert.Contains(progress.Values, x => x.Completed > 0 && x.Completed < x.Total);
        using var cancel = new CancellationTokenSource();
        var output = Path.Combine(_root, "cancelled.zip");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PlannedZipExportService().ExportAsync(plan, output,
            token: cancel.Token, progress: new Canceller(cancel)));
        Assert.False(File.Exists(output)); Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void Disk_only_section_is_unverified_offline_and_resource_filter_preserves_checks()
    {
        var vm = new ShellViewModel();
        var instance = Instance;
        var row = new ResourceSelection(new("w", ResourceKind.Workflow, "nested", Path.Combine(instance.WorkflowsDirectory, "nested.json"), "workflows/nested.json")) { IsSelected = true };
        vm.LocalWorkflows.Add(row);
        HomeStatusTests.Set(vm, "_inventory", new ResourceInventory(instance, [row.Resource], [], []));
        Rebuild(vm); Assert.Empty(vm.DiskOnlyWorkflows); Assert.NotEmpty(vm.WorkflowTree);
        HomeStatusTests.Set(vm, "_inventory", new ResourceInventory(instance, [row.Resource], [], []) { FrontendListVerified = true });
        Rebuild(vm); Assert.Same(row, Assert.Single(vm.DiskOnlyWorkflows));
        vm.ResourceFilter = "no matching resource"; Assert.True(row.IsSelected);
        HomeStatusTests.Set(vm, "_settingInstance", true);
        HomeStatusTests.Set(vm, "_librarySelectionCaption", "旧实例依赖");
        typeof(ShellViewModel).GetProperty(nameof(ShellViewModel.LibraryAnalysisBusy))!.SetValue(vm, true);
        vm.SelectedInstance = instance;
        Assert.False(vm.LibraryAnalysisBusy); Assert.Equal("选择资源查看详情", vm.LibrarySelectionTitle);
    }
    private static void Rebuild(ShellViewModel vm) => typeof(ShellViewModel).GetMethod("RebuildResourceTrees", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(vm, null);
    private sealed class Recorder : IProgress<OperationProgress> { public List<OperationProgress> Values { get; } = []; public void Report(OperationProgress value) => Values.Add(value); }
    private sealed class Canceller(CancellationTokenSource source) : IProgress<OperationProgress> { public void Report(OperationProgress value) { if (value.Stage == "写入资源包" && value.Completed > 0) source.Cancel(); } }
    private sealed class Endpoint : IRuntimeEndpointResolver { public Task<RuntimeEndpoint?> ResolveAsync(InstanceDescriptor instance, IReadOnlyList<InstanceDescriptor> peers, CancellationToken token) => Task.FromResult<RuntimeEndpoint?>(new(1, 8000, DateTimeOffset.UnixEpoch)); }
    private sealed class FrontendResponses(InstanceDescriptor instance, string mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/userdata" && mode == "error") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            var args = new[] { Path.Combine(mode == "wrong" ? _WrongRoot : instance.CoreDirectory, "main.py"), "--base-directory", instance.DataDirectory, "--user-directory", instance.UserDirectory };
            var body = path switch { "/system_stats" => JsonSerializer.Serialize(new { system = new { argv = args, comfyui_version = "2.3.4" } }),
                "/object_info" => "{}", "/users" => mode == "multi" ? "{\"users\":{\"other\":\"Other\"}}" : "{\"storage\":\"server\"}",
                "/userdata" => "[{\"path\":\"nested/workflow.json\"}]", _ => throw new InvalidOperationException(path) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
        private static string _WrongRoot => Path.Combine(Path.GetTempPath(), "unrelated-instance");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
