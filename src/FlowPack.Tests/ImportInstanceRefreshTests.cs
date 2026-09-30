using System.IO;
using System.Reflection;
using System.Text.Json;
using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class ImportInstanceRefreshTests
{
    [Fact]
    public async Task Selecting_instance_after_import_reuses_verified_local_workflow_without_download()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-import-instance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "state"));
        var workflows = Path.Combine(root, "desktop", "user", "default", "workflows"); Directory.CreateDirectory(workflows);
        // Declare fixture core types so source-edit regression never needs the public Manager map.
        await File.WriteAllTextAsync(Path.Combine(root, "desktop", "nodes.py"),
            "NODE_CLASS_MAPPINGS = {'SaveImage': SaveImage, 'UpscaleModelLoader': UpscaleModelLoader}");
        var local = Path.Combine(workflows, "original.json");
        await File.WriteAllTextAsync(local, """{"1":{"class_type":"SaveImage","inputs":{"filename_prefix":"fixture"}}}""");
        var instance = new InstanceDescriptor("fixture", "fixture", "desktop-2", null,
            Path.Combine(root, "desktop"), Path.Combine(root, "desktop"), Path.Combine(root, "desktop"),
            Path.Combine(root, "desktop", "user"), workflows, Path.Combine(root, "desktop", "custom_nodes"),
            null, [Path.Combine(root, "desktop", "models")], Path.Combine(root, "desktop", "models"), [], "fixture", []);
        var manifest = Path.Combine(root, "bundle.cpack.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
        {
            formatVersion = "1", id = "fixture", name = "fixture", version = "1",
            resources = new[] { new { id = "workflow", name = "original.json", kind = "Workflow",
                sizeBytes = new FileInfo(local).Length, sha256 = await ResourceImportService.HashAsync(local),
                deploymentPurpose = "workflows/original.json" } }
        }));
        var pipe = "flowpack-instance-refresh-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(root, "state", "worker-session.json"), JsonSerializer.Serialize(new { Pipe = pipe, Secret = "fixture" }));
        var binding = new LibraryBindingStore(Path.Combine(root, "binding.json"));
        await binding.SaveAsync(new(root, DateTimeOffset.UtcNow));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var worker = new PersistentWorkerService(new(root), _ => Task.FromResult<IReadOnlyList<InstanceDescriptor>>([instance]));
        await worker.InitializeAsync();
        var server = new NamedPipeWorkerServer(pipe, "fixture");
        var holdPlan = false;
        var planEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePlan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = Task.Run(async () =>
        {
            while (!timeout.IsCancellationRequested) await server.ServeOnceAsync(async (request, token) =>
            {
                var response = await worker.HandleAsync(request, token);
                if (holdPlan && request.Command == "install.plan")
                {
                    planEntered.TrySetResult();
                    await releasePlan.Task.WaitAsync(token);
                }
                return response;
            }, timeout.Token);
        });
        var client = new WorkerLibraryClient(root, allowWorkerLaunch: false);
        try
        {
            var vm = new ShellViewModel(themeStore: new(Path.Combine(root, "theme.json")), libraryBindingStore: binding,
                libraryClientFactory: _ => client);
            await vm.ImportSourceAsync(manifest);
            Assert.Single(vm.OnlineResources); Assert.Empty(vm.ImportResources);
            Assert.Null(vm.SelectedInstance);
            // Invoke the same scan used by the selection command, without a fire-and-forget test race.
            typeof(ShellViewModel).GetField("_settingInstance", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, true);
            vm.SelectedInstance = instance;
            await (Task)typeof(ShellViewModel).GetMethod("ScanSelectedInstanceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;
            Assert.Empty(vm.OnlineResources);
            var imported = Assert.Single(vm.ImportResources);
            Assert.Equal(await ResourceImportService.HashAsync(local), imported.Resource.Sha256);
            Assert.Equal("workflows/original.json", imported.TargetRelativePath);
            var jobs = await client.CallAsync<IReadOnlyList<WorkerJob>>("job.list", new { });
            Assert.Contains(jobs, x => x.Operation == "resource.materialize-local" && x.State == "Completed");
            Assert.DoesNotContain(jobs, x => x.Operation is "task.download" or "install.execute");
            // Hold an actual IPC plan response, then change selection before it arrives.
            holdPlan = true;
            typeof(ShellViewModel).GetField("_coreBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, true);
            var pendingPlan = (Task)typeof(ShellViewModel).GetMethod("PrepareDeploymentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;
            await planEntered.Task.WaitAsync(timeout.Token);
            imported.IsSelected = false;
            releasePlan.TrySetResult();
            await pendingPlan;
            Assert.Null(typeof(ShellViewModel).GetField("_deployment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm));
            holdPlan = false;
            typeof(ShellViewModel).GetField("_coreBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, false);
            vm.SelectedInstance = null;
            Assert.Null(typeof(ShellViewModel).GetField("_inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm));
            Assert.Empty(vm.DependencyRows);
            // Selection/mapping changes use the same automatic persistence as the page.
            imported.IsSelected = false;
            while (!vm.CoreReady) await Task.Delay(10, timeout.Token);
            imported.TargetRelativePath = "workflows/renamed.json";
            while (!vm.CoreReady) await Task.Delay(10, timeout.Token);
            var savedSession = await client.CallAsync<RestoredImportSession>("import.session.load", new { });
            var choice = Assert.Single(savedSession.State.ResourceChoices!);
            Assert.False(choice.IsSelected);
            Assert.Equal("workflows/renamed.json", choice.TargetRelativePath);
            imported.IsSelected = true;
            while (!vm.CoreReady) await Task.Delay(10, timeout.Token);
            var restoredVm = new ShellViewModel(themeStore: new(Path.Combine(root, "theme.json")), libraryBindingStore: binding,
                libraryClientFactory: _ => client);
            typeof(ShellViewModel).GetField("_libraryDatabase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(restoredVm, client);
            await (Task)typeof(ShellViewModel).GetMethod("RestoreImportSessionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(restoredVm, null)!;
            var restoredChoice = Assert.Single(restoredVm.ImportResources);
            Assert.True(restoredChoice.IsSelected);
            Assert.Equal("workflows/renamed.json", restoredChoice.TargetRelativePath);
            Assert.Equal(RecognitionState.NeedsConfirmation, restoredChoice.Resource.State);
            Assert.Null(typeof(ShellViewModel).GetField("_deployment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(restoredVm));
            // Automatic source lookup must survive the install preview's dependency re-analysis.
            vm.SelectedInstance = instance;
            await (Task)typeof(ShellViewModel).GetMethod("ScanSelectedInstanceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;
            var missingWorkflow = Path.Combine(root, "missing-workflow.json");
            await File.WriteAllTextAsync(missingWorkflow, """
                {"version":0.4,"nodes":[{"id":1,"type":"UpscaleModelLoader","widgets_values":["missing.pth"]}],
                 "extra":{"models":[{"name":"missing.pth","url":"https://example.com/missing.pth"}]}}
                """);
            await vm.ImportSourceAsync(missingWorkflow);
            var missingModel = Assert.Single(vm.DependencyRows, x => x.Dependency.Kind == ResourceKind.Model);
            Assert.Equal(DependencyState.Missing, missingModel.Dependency.State);
            Assert.Equal("https://example.com/missing.pth", missingModel.DownloadUrl);
            Assert.NotNull(missingModel.ResolvedSource);
            missingModel.DownloadUrl = "https://example.com/manual.pth";
            missingModel.ExpectedSha256 = new string('a', 64);
            await (Task)typeof(ShellViewModel).GetMethod("PrepareDeploymentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null)!;
            var preserved = Assert.Single(vm.DependencyRows, x => x.Dependency.Kind == ResourceKind.Model);
            Assert.Equal("https://example.com/manual.pth", preserved.DownloadUrl);
            Assert.Equal(new string('a', 64), preserved.ExpectedSha256);
            Assert.Null(preserved.ResolvedSource); // Edited URLs cannot inherit the old provider metadata.
            await vm.ImportSourceAsync(missingWorkflow);
            missingModel = Assert.Single(vm.DependencyRows, x => x.Dependency.Kind == ResourceKind.Model);
            Assert.Equal("https://example.com/missing.pth", missingModel.DownloadUrl);
            Assert.Equal("", missingModel.ExpectedSha256);
            // File-plan reuse and workflow dependency reuse are different counts.
            Assert.Contains("安装文件：新增 1 个，已有同内容 0 个", vm.DeploymentSummary);
            vm.DependencyRows.Clear();
            vm.DependencyRows.Add(new(new("present", ResourceKind.Model, "present.pth", "upscale_models", DependencyState.Present, [], [], "fixture")));
            var staged = new DependencySelection(new("staged", ResourceKind.Model, "staged.pth", "upscale_models", DependencyState.Present, [], [], "fixture"));
            staged.MarkStaged();
            vm.DependencyRows.Add(staged);
            vm.DependencyRows.Add(missingModel);
            Assert.Contains("工作流依赖：本地可复用 1 项，已暂存待安装 1 项，待解决 1 项。", vm.DeploymentSummary);
            Assert.Contains("工作流依赖尚未解决", vm.DeploymentSummary);
        }
        finally
        {
            timeout.Cancel();
            try { await serving; } catch (OperationCanceledException) { }
            await client.DisposeAsync(); await worker.DisposeAsync();
            Directory.Delete(root, true);
        }
    }
}
