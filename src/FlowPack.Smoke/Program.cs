using System.Windows;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using FlowPack.App;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Smoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new FlowPack.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow();
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
                var errorText = worker.StandardError.ReadToEnd();
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
