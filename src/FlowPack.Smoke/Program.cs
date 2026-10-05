using System.Windows;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using FlowPack.App;
using FlowPack.Core;
using FlowPack.Infrastructure;
using FlowPack.ComfyUI;

namespace FlowPack.Smoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--acceptance-transfer", var transferScope, var transferRunId])
            return TransferAcceptanceHost.Run(transferScope, transferRunId);
        if (args is ["--acceptance-transfer", var productionScope, var productionRunId, "--production-worker", var workerExecutable])
            return TransferAcceptanceHost.Run(productionScope, productionRunId, workerExecutable);
        if (args is ["--acceptance-transfer", var scopedProduction, var scopedRun, "--production-worker", var scopedWorker, "--target", var targetId])
            return TransferAcceptanceHost.Run(scopedProduction, scopedRun, scopedWorker, targetId);
        if (args is ["--acceptance-update-coordination"])
            return UpdateCoordinationAcceptance.RunAsync().GetAwaiter().GetResult();
        if (args is ["--acceptance-update-child", var updateRoot, var updateRole])
            return UpdateCoordinationAcceptance.ChildAsync(updateRoot, updateRole).GetAwaiter().GetResult();
        if (args is ["--acceptance-crash-recovery"])
            return RecoveryCrashAcceptance.RunAsync().GetAwaiter().GetResult();
        if (args is ["--acceptance-crash-child", var crashRoot, var crashPhase])
            return RecoveryCrashAcceptance.ChildAsync(crashRoot, crashPhase).GetAwaiter().GetResult();
        if (args is ["--acceptance-ui" or "--acceptance-worker", var scopeFile, var runId])
            return AcceptanceHost.Run(args[0], scopeFile, runId);
        if (args is ["--acceptance-inspect", var inspectScope])
        {
            var scope = AcceptanceScope.Load(inspectScope);
            var instances = new DesktopInstanceDiscovery(configurationRoot: scope.DesktopProfile).DiscoverAsync().GetAwaiter().GetResult();
            foreach (var instance in instances) scope.ValidateInstance(instance);
            Console.WriteLine(JsonSerializer.Serialize(instances.Select(instance => new { instance, capability = scope.Evaluate(instance, false) })));
            return instances.Count == scope.InstanceIds.Count ? 0 : 1;
        }
        if (args.Contains("--inspect-desktop"))
        {
            var instances = new DesktopInstanceDiscovery().DiscoverAsync().GetAwaiter().GetResult();
            foreach (var instance in instances)
            {
                var inventory = new ResourceInventoryService().ScanAsync(instance).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(new { instance, workflows = inventory.Resources.Count(x => x.Kind == ResourceKind.Workflow),
                    models = inventory.Resources.Count(x => x.Kind == ResourceKind.Model), nodePackages = inventory.Resources.Count(x => x.Kind == ResourceKind.CustomNode),
                    coreNodeTypes = inventory.CoreNodeTypes.Count, inventory.Issues }));
            }
            return instances.Count > 0 ? 0 : 1;
        }
        var app = new FlowPack.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown, SuppressAutomaticWindow = true };
        app.InitializeComponent();
        var window = new MainWindow(new ShellViewModel());
        if (window.DataContext is not ShellViewModel || window.Title != "ComfyUI FlowPack")
        {
            Console.Error.WriteLine("FlowPack WPF shell did not initialize as expected.");
            return 1;
        }
        var theme = new ThemeDefinition("1", "默认", ThemeBase.Light, "#1E63D6", "#FFFFFF", "#12233D", 14, 12, ThemeDensity.Comfortable, false);
        if (!ThemeValidator.Validate(theme).IsValid || ThemeValidator.Validate(theme with { BodyFontSize = 22 }).IsValid)
        {
            Console.Error.WriteLine("Theme validation did not enforce the documented bounds.");
            return 1;
        }
        var planner = new SafeInstallPlanner();
        var plan = planner.PlanAsync(new PackageManifest("portrait", "Portrait", "1.0", [new ResourceEntry("model", "model.safetensors", ResourceKind.Model, 1024, new string('A', 64), "https://example.invalid/model")]), new InstanceFingerprint("C:\\ComfyUI", "python.exe", "C:\\ComfyUI\\user", null, DateTimeOffset.UtcNow)).GetAwaiter().GetResult();
        if (plan.PeakRequiredBytes != 1024 || plan.Actions.Count != 1)
        {
            Console.Error.WriteLine("Install planning did not preserve resource requirements.");
            return 1;
        }
        if (ResourceLibraryPathValidator.IsSafeLibraryPath("C:\\FlowPack\\resources", "C:\\FlowPack", null, out _))
        {
            Console.Error.WriteLine("Resource library safety check accepted the app directory.");
            return 1;
        }
        var manifestPath = Path.Combine(Path.GetTempPath(), $"flowpack-smoke-{Guid.NewGuid():N}.cpack.json");
        try
        {
            File.WriteAllText(manifestPath, """{"formatVersion":"1","id":"demo","name":"Demo","version":"1.0","resources":[{"id":"node","name":"Example node","kind":"CustomNode","sizeBytes":42,"sourceUrl":"https://example.invalid/node"}]}""");
            var manifest = new PackageManifestReader().ReadAsync(manifestPath).GetAwaiter().GetResult();
            if (manifest.Resources.Single().Kind != ResourceKind.CustomNode)
            {
                Console.Error.WriteLine("Package manifest reader did not parse resource metadata.");
                return 1;
            }
        }
        finally { File.Delete(manifestPath); }
        if (!VerifyWorkerTaskSnapshot()) return 1;
        Console.WriteLine("FlowPack WPF shell initialized successfully.");
        app.Shutdown();
        return 0;
    }

    private static bool VerifyWorkerTaskSnapshot()
    {
        var libraryPath = Path.Combine(Path.GetTempPath(), $"flowpack-worker-smoke-{Guid.NewGuid():N}");
        Process? worker = null;
        try
        {
            var database = new ResourceLibraryDatabase(libraryPath);
            database.SaveTaskAsync(new WorkerTask(
                "smoke-task", WorkerTaskKind.Download, WorkerTaskState.Completed, "验证下载", "已校验",
                42, 42, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).GetAwaiter().GetResult();
            database.DisposeAsync().GetAwaiter().GetResult();

            var workerPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FlowPack.Worker", "bin", "Release", "net10.0-windows", "ComfyUI.FlowPack.Worker.dll"));
            if (!File.Exists(workerPath))
            {
                Console.Error.WriteLine($"Worker binary was not found: {workerPath}");
                return false;
            }
            var pipeName = $"flowpack-smoke-{Guid.NewGuid():N}";
            const string secret = "flowpack-smoke-session-secret";
            worker = Process.Start(new ProcessStartInfo("dotnet", $"\"{workerPath}\" --pipe {pipeName} --secret {secret} --library \"{libraryPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            });
            if (worker is null)
            {
                Console.Error.WriteLine("Worker process did not start.");
                return false;
            }

            WorkerResponse? response = null;
            var client = new NamedPipeWorkerClient();
            for (var attempt = 0; attempt < 20 && response is null; attempt++)
            {
                try
                {
                    response = client.SendAsync(pipeName, new WorkerRequest(WorkerProtocol.Version, "smoke-status", secret, WorkerProtocol.StatusCommand), TimeSpan.FromMilliseconds(250)).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    Thread.Sleep(50);
                }
            }
            if (response is null || !response.Succeeded || !response.Payload!.Value.GetProperty("taskStoreAttached").GetBoolean() || response.Payload.Value.GetProperty("taskCount").GetInt32() != 1)
            {
                var responseText = response is null ? "<no response>" : JsonSerializer.Serialize(response);
                var errorText = worker.HasExited ? worker.StandardError.ReadToEnd() : "Worker is still running";
                Console.Error.WriteLine($"Worker did not return the persisted task snapshot status. Response: {responseText}; stderr: {errorText}");
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Worker task snapshot smoke failed: {exception.Message}");
            return false;
        }
        finally
        {
            if (worker is { HasExited: false }) worker.Kill(entireProcessTree: true);
            worker?.WaitForExit(5_000);
            if (Directory.Exists(libraryPath)) Directory.Delete(libraryPath, recursive: true);
        }
    }
}
