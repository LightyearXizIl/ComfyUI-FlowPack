using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class TransferFlowTests
{
    [Theory]
    [InlineData(ResourceKind.Model)]
    [InlineData(ResourceKind.CustomNode)]
    public Task Standalone_model_or_node_exports_without_a_reference_workflow(ResourceKind kind) => RunStaAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync(false);
        var vm = fixture.ViewModel;
        await fixture.ScanAsync();
        (kind == ResourceKind.Model ? vm.LocalModels : vm.LocalNodes)[0].IsSelected = true;
        vm.BeginExportCommand.Execute(null);
        await fixture.IdleAsync();
        Assert.DoesNotContain(vm.LocalWorkflows, x => x.IsSelected);
        Assert.Equal(TransferState.Ready, vm.ExportState);
        Assert.All(vm.SelectedExportResources, x => Assert.Equal(kind, x.Resource.Kind));
        Assert.All(vm.ExportFiles, x => Assert.StartsWith(kind == ResourceKind.Model ? "models/" : "custom_nodes/", x.ArchivePath));
        var zip = Path.Combine(fixture.Root, "standalone.zip");
        await vm.ExportZipToAsync(zip);
        vm.SelectedInstance = fixture.TargetInstance;
        await fixture.IdleAsync();
        await vm.ImportSourceAsync(zip);
        Assert.Equal(TransferState.Ready, vm.ImportState);
        Assert.All(vm.ImportResources, x => Assert.Equal(kind, x.Resource.Kind));
        Assert.True(vm.ExecuteDeploymentCommand.CanExecute(null));
    });

    [Fact]
    public Task Multiple_workflows_share_one_model_and_one_node_payload_in_the_final_zip() => RunStaAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync(false);
        var second = Path.Combine(Path.GetDirectoryName(fixture.WorkflowPath)!, "second.json");
        await File.WriteAllTextAsync(second, await File.ReadAllTextAsync(fixture.WorkflowPath));
        var vm = fixture.ViewModel;
        await fixture.ScanAsync();
        foreach (var row in vm.LocalWorkflows) row.IsSelected = true;
        vm.BeginExportCommand.Execute(null);
        await fixture.IdleAsync();
        Assert.Equal(2, vm.ExportFiles.Count(x => x.ArchivePath.StartsWith("workflows/", StringComparison.Ordinal)));
        Assert.Single(vm.ExportFiles, x => x.ArchivePath.StartsWith("models/", StringComparison.Ordinal));
        Assert.Single(vm.ExportFiles, x => x.ArchivePath.StartsWith("custom_nodes/", StringComparison.Ordinal));
        Assert.Equal(vm.ExportFiles.Count, vm.ExportFiles.Select(x => x.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var zip = Path.Combine(fixture.Root, "mixed.zip");
        await vm.ExportZipToAsync(zip);
        Assert.Equal(TransferState.Completed, vm.ExportState);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Workflow_export_auto_adds_dependencies_and_preserves_manual_cancellation(bool withInput) => RunStaAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync(withInput);
        var vm = fixture.ViewModel;
        await fixture.ScanAsync();
        Assert.Equal(TransferState.Empty, vm.ImportState);
        Assert.False(vm.HasImportSource);
        Assert.False(vm.ExecuteDeploymentCommand.CanExecute(null));
        var workflow = Assert.Single(vm.LocalWorkflows);
        workflow.IsSelected = true;
        vm.BeginExportCommand.Execute(null);
        await fixture.IdleAsync();
        Assert.Equal(FlowPage.Packaging, vm.CurrentPage);
        Assert.True(Assert.Single(vm.LocalModels).IsSelected);
        Assert.True(Assert.Single(vm.LocalNodes).IsSelected);
        Assert.Contains(vm.ExportFiles, x => x.ArchivePath.StartsWith("models/", StringComparison.Ordinal));
        Assert.Contains(vm.ExportFiles, x => x.ArchivePath.StartsWith("custom_nodes/", StringComparison.Ordinal));
        if (withInput) Assert.Contains(vm.ExportFiles, x => x.ArchivePath == "input/fixture.png");
        var model = vm.LocalModels[0];
        model.IsSelected = false;
        await fixture.IdleAsync();
        vm.PreviewZipCommand.Execute(null);
        await fixture.IdleAsync();
        Assert.False(model.IsSelected);
        Assert.DoesNotContain(vm.ExportFiles, x => x.SourcePath == model.Path);
        vm.ReturnToResourcesCommand.Execute(null);
        vm.ResourceFilter = "workflow";
        vm.BeginExportCommand.Execute(null);
        await fixture.IdleAsync();
        Assert.False(model.IsSelected);
        Assert.Equal("workflow", vm.ResourceFilter);
        Assert.DoesNotContain(vm.ExportFiles, x => x.SourcePath == model.Path);
        vm.ReAddExportDependenciesCommand.Execute(null);
        await fixture.IdleAsync();
        Assert.True(model.IsSelected);
        Assert.Contains(vm.ExportFiles, x => x.SourcePath == model.Path);
        var zip = Path.Combine(fixture.Root, "resources.zip");
        await vm.ExportZipToAsync(zip);
        Assert.Equal(TransferState.Completed, vm.ExportState);
        Assert.True(File.Exists(zip));
        var exportedCount = vm.ExportFiles.Count;
        vm.SelectedInstance = fixture.TargetInstance;
        await fixture.IdleAsync();
        await vm.ImportSourceAsync(zip);
        await fixture.IdleAsync();
        Assert.True(vm.ImportState == TransferState.Ready, vm.ImportDetails + " / " + vm.CoreNotice);
        Assert.Equal(exportedCount, vm.ImportResources.Count);
        Assert.All(vm.ImportResources, x => Assert.True(x.IsSelected));
        Assert.Equal(exportedCount, vm.DeploymentFiles.Count);
        Assert.True(vm.ExecuteDeploymentCommand.CanExecute(null));
        Assert.DoesNotContain(vm.DependencyRows, x => x.Dependency.State == DependencyState.Missing);
        var jobs = await fixture.Client.CallAsync<IReadOnlyList<WorkerJob>>("job.list", new { });
        Assert.Contains(jobs, x => x.Operation == "export.execute" && x.State == "Completed");
        Assert.Contains(jobs, x => x.Operation == "resource.import" && x.State == "Completed");
        Assert.Contains(jobs, x => x.Operation == "install.plan" && x.State == "Completed");
    });

    [Fact]
    public Task Changed_selection_discards_an_in_flight_export_plan_before_enabling_save() => RunStaAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync(false);
        var vm = fixture.ViewModel;
        await fixture.ScanAsync();
        vm.LocalWorkflows[0].IsSelected = true;
        vm.BeginExportCommand.Execute(null);
        await fixture.IdleAsync();
        fixture.HoldExportPlan = true;
        var pending = InvokeAsync(vm, "PrepareZipAsync");
        await fixture.PlanEntered.Task.WaitAsync(fixture.Token);
        vm.LocalModels[0].IsSelected = false;
        fixture.ReleasePlan.TrySetResult();
        await pending;
        fixture.HoldExportPlan = false;
        await fixture.IdleAsync();
        Assert.DoesNotContain(vm.ExportFiles, x => x.SourcePath == vm.LocalModels[0].Path);
        Assert.False(vm.LocalModels[0].IsSelected);
    });

    [Fact]
    public Task Failed_new_import_clears_the_previous_plan_and_reports_a_failed_state() => RunStaAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync(false);
        var vm = fixture.ViewModel;
        await fixture.ScanAsync();
        var valid = Path.Combine(fixture.Root, "standalone.json");
        await File.WriteAllTextAsync(valid, """{"1":{"class_type":"SaveImage","inputs":{"filename_prefix":"fixture"}}}""");
        await vm.ImportSourceAsync(valid);
        Assert.True(vm.HasImportSource);
        Assert.True(vm.ExecuteDeploymentCommand.CanExecute(null));
        var invalid = Path.Combine(fixture.Root, "broken.zip");
        await File.WriteAllTextAsync(invalid, "not a ZIP archive");
        await vm.ImportSourceAsync(invalid);
        Assert.Equal(TransferState.Failed, vm.ImportState);
        Assert.NotEmpty(vm.ImportError);
        Assert.False(vm.HasImportSource);
        Assert.Empty(vm.ImportResources);
        Assert.Empty(vm.DeploymentFiles);
        Assert.False(vm.ExecuteDeploymentCommand.CanExecute(null));
        Assert.Null(typeof(ShellViewModel).GetField("_deployment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm));
    });

    private static Task RunStaAsync(Func<Task> body)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await body(); complete.TrySetResult(); }
                catch (Exception ex) { complete.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return complete.Task;
    }

    private static Task InvokeAsync(ShellViewModel vm, string method) =>
        (Task)typeof(ShellViewModel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FlowPack-transfer-flow-" + Guid.NewGuid().ToString("N"));
        public string WorkflowPath { get; private set; } = "";
        public ShellViewModel ViewModel { get; private set; } = null!;
        public WorkerLibraryClient Client { get; private set; } = null!;
        public InstanceDescriptor TargetInstance { get; private set; } = null!;
        public bool HoldExportPlan { get; set; }
        public TaskCompletionSource PlanEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePlan { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(60));
        public CancellationToken Token => _lifetime.Token;
        private PersistentWorkerService _worker = null!;
        private Task _serving = null!;
        private InstanceDescriptor _instance = null!;

        public static async Task<Fixture> CreateAsync(bool withInput)
        {
            var f = new Fixture();
            var source = Path.Combine(f.Root, "source");
            var workflows = Path.Combine(source, "user", "default", "workflows");
            var modelDirectory = Path.Combine(source, "models", "upscale_models");
            var package = Path.Combine(source, "custom_nodes", "TransferFixture");
            foreach (var directory in new[] { workflows, modelDirectory, package, Path.Combine(source, "input"), Path.Combine(f.Root, "library", "state") }) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(source, "nodes.py"), "NODE_CLASS_MAPPINGS = {'SaveImage': SaveImage, 'UpscaleModelLoader': UpscaleModelLoader, 'LoadImage': LoadImage}");
            await File.WriteAllTextAsync(Path.Combine(package, "__init__.py"), "NODE_CLASS_MAPPINGS = {'TransferPass': object}");
            await File.WriteAllTextAsync(Path.Combine(modelDirectory, "fixture.pth"), "test bytes, not a real model");
            await File.WriteAllTextAsync(Path.Combine(source, "input", "fixture.png"), "test bytes, not a real image");
            f.WorkflowPath = Path.Combine(workflows, "workflow.json");
            var graph = new Dictionary<string, object>
            {
                ["1"] = new { class_type = "UpscaleModelLoader", inputs = new { model_name = "fixture.pth" } },
                ["2"] = new { class_type = "TransferPass", inputs = new { } }
            };
            if (withInput) graph["3"] = new { class_type = "LoadImage", inputs = new { image = "fixture.png" } };
            await File.WriteAllTextAsync(f.WorkflowPath, JsonSerializer.Serialize(graph));
            var desktop = Path.Combine(f.Root, "Desktop.exe"); await File.WriteAllTextAsync(desktop, "test fixture only");
            f._instance = new("fixture", "Fixture", "desktop-2", desktop, source, source, source, Path.Combine(source, "user"), workflows,
                Path.Combine(source, "custom_nodes"), null, [Path.Combine(source, "models")], Path.Combine(source, "models"), [], "fixture", [])
                { ConfigurationRoot = f.Root, DesktopLayout = "standalone-native", InputDirectory = Path.Combine(source, "input") };
            var target = Path.Combine(f.Root, "target"); Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "nodes.py"), await File.ReadAllTextAsync(Path.Combine(source, "nodes.py")));
            f.TargetInstance = f._instance with { Id = "target", Name = "Target", InstallRoot = target, CoreDirectory = target, DataDirectory = target,
                UserDirectory = Path.Combine(target, "user"), WorkflowsDirectory = Path.Combine(target, "user", "default", "workflows"),
                CustomNodesDirectory = Path.Combine(target, "custom_nodes"), ModelRoots = [Path.Combine(target, "models")], ModelsWriteDirectory = Path.Combine(target, "models"),
                InputDirectory = Path.Combine(target, "input"), ConfigurationFingerprint = "target-fixture" };
            var library = Path.Combine(f.Root, "library");
            var capabilities = new DeploymentCapabilityProvider([new("1.1.4", "standalone-native", false, "unit-test-only")], _ => "1.1.4");
            f._worker = new PersistentWorkerService(new(library), _ => Task.FromResult<IReadOnlyList<InstanceDescriptor>>([f._instance, f.TargetInstance]), capabilities);
            await f._worker.InitializeAsync(f.Token);
            var pipe = "flowpack-transfer-flow-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(library, "state", "worker-session.json"), JsonSerializer.Serialize(new { Pipe = pipe, Secret = "fixture" }));
            var server = new NamedPipeWorkerServer(pipe, "fixture");
            f._serving = Task.Run(async () =>
            {
                while (!f.Token.IsCancellationRequested)
                    await server.ServeOnceAsync(async (request, token) =>
                    {
                        var response = await f._worker.HandleAsync(request, token);
                        if (f.HoldExportPlan && request.Command == "export.plan")
                        {
                            f.PlanEntered.TrySetResult(); await f.ReleasePlan.Task.WaitAsync(token);
                        }
                        return response;
                    }, f.Token);
            });
            f.Client = new WorkerLibraryClient(library, allowWorkerLaunch: false);
            var binding = new LibraryBindingStore(Path.Combine(f.Root, "binding.json")); await binding.SaveAsync(new(library, DateTimeOffset.UtcNow));
            f.ViewModel = new ShellViewModel(themeStore: new(Path.Combine(f.Root, "theme.json")), libraryBindingStore: binding, libraryClientFactory: _ => f.Client);
            return f;
        }

        public async Task ScanAsync()
        {
            var field = typeof(ShellViewModel).GetField("_settingInstance", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(ViewModel, true);
            ViewModel.SelectedInstance = _instance;
            await InvokeAsync(ViewModel, "ScanSelectedInstanceAsync");
            field.SetValue(ViewModel, false);
        }
        public async Task IdleAsync()
        {
            await Task.Delay(300, Token);
            while (!ViewModel.CoreReady) await Task.Delay(20, Token);
        }
        public async ValueTask DisposeAsync()
        {
            ViewModel.DetachWindow(); _lifetime.Cancel(); ReleasePlan.TrySetResult();
            try { await _serving; } catch (OperationCanceledException) { }
            await Client.DisposeAsync(); await _worker.DisposeAsync(); _lifetime.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
