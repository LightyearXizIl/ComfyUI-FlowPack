using System.IO;
using System.Reflection;
using System.Text.Json;
using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PostInstallRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Install_command_refreshes_real_worker_inventory_and_replaces_staged_labels(bool failRefresh)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-post-install-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "library"); var target = Path.Combine(root, "target");
        Directory.CreateDirectory(Path.Combine(library, "state")); Directory.CreateDirectory(target);
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "workflows")); Directory.CreateDirectory(Path.Combine(source, "models", "upscale_models"));
        await File.WriteAllTextAsync(Path.Combine(source, "workflows", "fixture.json"), """{"1":{"class_type":"UpscaleModelLoader","inputs":{"model_name":"fixture.pth"}}}""");
        await File.WriteAllTextAsync(Path.Combine(source, "models", "upscale_models", "fixture.pth"), "fixture bytes, not a real model");
        await File.WriteAllTextAsync(Path.Combine(target, "nodes.py"), "NODE_CLASS_MAPPINGS = {'UpscaleModelLoader': UpscaleModelLoader}");
        var executable = Path.Combine(root, "Desktop.exe"); await File.WriteAllTextAsync(executable, "test fixture only");
        var instance = new InstanceDescriptor("fixture", "fixture", "desktop-2", executable, target, target, target,
            Path.Combine(target, "user"), Path.Combine(target, "user", "default", "workflows"), Path.Combine(target, "custom_nodes"),
            null, [Path.Combine(target, "models")], Path.Combine(target, "models"), [], "fixture", [])
            { ConfigurationRoot = root, DesktopLayout = "standalone-native" };
        var capabilities = new DeploymentCapabilityProvider([new("1.0.47", "standalone-native", false, "unit-test-only")], _ => "1.0.47");
        var pipe = "flowpack-post-install-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(library, "state", "worker-session.json"), JsonSerializer.Serialize(new { Pipe = pipe, Secret = "fixture" }));
        var binding = new LibraryBindingStore(Path.Combine(root, "binding.json")); await binding.SaveAsync(new(library, DateTimeOffset.UtcNow));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var worker = new PersistentWorkerService(new(library), _ => Task.FromResult<IReadOnlyList<InstanceDescriptor>>([instance]), capabilities);
        await worker.InitializeAsync();
        var server = new NamedPipeWorkerServer(pipe, "fixture");
        var installationStarted = false;
        var serving = Task.Run(async () =>
        {
            while (!timeout.IsCancellationRequested) await server.ServeOnceAsync((request, token) =>
            {
                if (request.Command == "install.execute") installationStarted = true;
                if (failRefresh && installationStarted && request.Command == "inventory.scan")
                    return Task.FromResult(new WorkerResponse(request.RequestId, false, Error: new("test-scan-failed", "test scan unavailable")));
                return worker.HandleAsync(request, token);
            }, timeout.Token);
        });
        var client = new WorkerLibraryClient(library, allowWorkerLaunch: false);
        try
        {
            var vm = new ShellViewModel(themeStore: new(Path.Combine(root, "theme.json")), libraryBindingStore: binding,
                libraryClientFactory: _ => client);
            typeof(ShellViewModel).GetField("_settingInstance", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, true);
            vm.SelectedInstance = instance;
            await (Task)typeof(ShellViewModel).GetMethod("ScanSelectedInstanceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null)!;
            await vm.ImportSourceAsync(source);
            Assert.True(Assert.Single(vm.DependencyRows).IsStaged);
            Assert.Contains("新增 2 个", vm.DeploymentSummary);
            Assert.True(vm.ExecuteDeploymentCommand.CanExecute(null));
            vm.ExecuteDeploymentCommand.Execute(null);
            while (!vm.CoreReady) await Task.Delay(10, timeout.Token);
            if (failRefresh)
            {
                Assert.Contains("文件已部署，但重新检查失败", vm.CoreNotice);
                Assert.Contains("test scan unavailable", vm.CoreNotice);
                Assert.Empty(vm.DependencyRows);
                Assert.False(vm.ExecuteDeploymentCommand.CanExecute(null));
            }
            else
            {
                Assert.Contains("文件已部署，已重新检查本地资源", vm.CoreNotice);
                Assert.False(Assert.Single(vm.DependencyRows).IsStaged);
                Assert.Contains("本地可复用 1 项", vm.DeploymentSummary);
                Assert.Contains("新增 0 个，已有同内容 2 个", vm.DeploymentSummary);
                Assert.Contains(vm.LocalModels, x => x.Name == "fixture.pth");
            }
            Assert.True(File.Exists(Path.Combine(instance.WorkflowsDirectory, "fixture.json")));
            var jobs = await client.CallAsync<IReadOnlyList<WorkerJob>>("job.list", new { });
            Assert.Single(jobs, x => x.Operation == "install.execute" && x.State == "Completed");
            if (!failRefresh) Assert.True(jobs.Count(x => x.Operation == "inventory.scan" && x.State == "Completed") >= 2);
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
