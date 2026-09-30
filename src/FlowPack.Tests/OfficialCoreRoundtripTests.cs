using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class OfficialCoreFactAttribute : FactAttribute
{
    public OfficialCoreFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FLOWPACK_TEST_OFFICIAL_CORE") != "1")
            Skip = "Opt-in isolated official-core test; requires explicit core, Python, model and output paths.";
    }
}

/// <summary>This exercises copied official ComfyUI cores, NOT the Desktop GUI or installer lifecycle.</summary>
public sealed class OfficialCoreRoundtripTests
{
    [OfficialCoreFact]
    public async Task Two_isolated_official_cores_export_install_and_run_real_upscale_model_and_custom_node()
    {
        string Setting(string name) => Path.GetFullPath(Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name + " required"));
        var sourceCore = Setting("FLOWPACK_TEST_CORE");
        var sourcePython = Setting("FLOWPACK_TEST_PYTHON_ENV");
        var sourceModel = Setting("FLOWPACK_TEST_MODEL");
        var parent = Setting("FLOWPACK_OFFICIAL_TEST_ROOT");
        var root = Path.Combine(parent, "official-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20)); var token = timeout.Token;
        var source = Descriptor("source"); var target = Descriptor("target");
        await File.WriteAllTextAsync(Path.Combine(root, "scope.txt"), "Copied official ComfyUI core integration test. Does not qualify Desktop GUI installation or upgrades.", token);
        try
        {
            foreach (var instance in new[] { source, target })
            {
                await CopyCoreAsync(sourceCore, instance.CoreDirectory, token);
                await CopyTreeAsync(sourcePython, Path.Combine(instance.CoreDirectory, ".venv"), token);
                Directory.CreateDirectory(instance.WorkflowsDirectory); Directory.CreateDirectory(instance.CustomNodesDirectory);
                Directory.CreateDirectory(instance.InputDirectory!);
            }
            var modelTarget = Path.Combine(source.ModelsWriteDirectory, "upscale_models", Path.GetFileName(sourceModel));
            Directory.CreateDirectory(Path.GetDirectoryName(modelTarget)!); File.Copy(sourceModel, modelTarget);
            var node = Path.Combine(source.CustomNodesDirectory, "FlowPackAcceptanceNode"); Directory.CreateDirectory(node);
            await File.WriteAllTextAsync(Path.Combine(node, "__init__.py"), """
                class FlowPackAcceptancePass:
                    @classmethod
                    def INPUT_TYPES(cls): return {"required": {"image": ("IMAGE",)}}
                    RETURN_TYPES = ("IMAGE",)
                    FUNCTION = "run"
                    CATEGORY = "FlowPack acceptance"
                    def run(self, image): return (image,)
                NODE_CLASS_MAPPINGS = {"FlowPackAcceptancePass": FlowPackAcceptancePass}
                """, token);
            var pixels = Enumerable.Range(0, 8 * 8 * 3).Select(x => (byte)(x % 256)).ToArray();
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(8, 8, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null, pixels, 24);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var imageFile = File.Create(Path.Combine(source.InputDirectory!, "fixture.png"))) encoder.Save(imageFile);
            var raw = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["1"] = new { class_type = "UpscaleModelLoader", inputs = new { model_name = Path.GetFileName(sourceModel) } },
                ["2"] = new { class_type = "LoadImage", inputs = new { image = "fixture.png" } },
                ["3"] = new { class_type = "FlowPackAcceptancePass", inputs = new { image = new object[] { "2", 0 } } },
                ["4"] = new { class_type = "ImageUpscaleWithModel", inputs = new { upscale_model = new object[] { "1", 0 }, image = new object[] { "3", 0 } } },
                ["5"] = new { class_type = "SaveImage", inputs = new { images = new object[] { "4", 0 }, filename_prefix = "flowpack_acceptance" } }
            });
            var workflowPath = Path.Combine(source.WorkflowsDirectory, "upscale.json"); await File.WriteAllTextAsync(workflowPath, raw, token);
            var firstRun = await RunCoreAsync(source, raw, root, token);
            var inventory = await new ResourceInventoryService().ScanAsync(source, token);
            var workflow = WorkflowDocumentFactory.Create("upscale", "upscale", raw);
            var analysis = await new InventoryDependencyAnalyzer().AnalyzeAsync([workflow], inventory, token);
            Assert.Empty(analysis.Issues);
            Assert.All(analysis.Dependencies, x => Assert.Equal(DependencyState.Present, x.State));
            var selected = inventory.Resources.Where(x => x.Kind == ResourceKind.Workflow).Concat(analysis.Dependencies.SelectMany(x => x.Candidates)).ToArray();
            var exporter = new PlannedZipExportService(); var export = await exporter.PlanAsync(selected, token: token);
            var archive = Path.Combine(root, "roundtrip.zip"); await exporter.ExportAsync(export, archive, token: token);
            var import = await new ResourceImportService().ImportAsync(archive, Path.Combine(root, "staging"), token);
            Assert.All(import.Resources, x => Assert.Equal(RecognitionState.Confirmed, x.State));
            var installer = new ResourceInstallationService(Path.Combine(root, "library"));
            var plan = await installer.PlanAsync(target, import.Resources, token); Assert.Empty(plan.BlockingReasons);
            await installer.ExecuteAsync(plan, (x, ct) => Task.FromResult(x), token: token);
            var secondRun = await RunCoreAsync(target, raw, root, token);
            var report = new { scope = "isolated official core; Desktop GUI not exercised", sourceCore, sourceModel,
                source = source.Id, target = target.Id, files = export.Files.Count, firstRun, secondRun, passed = true };
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(report), token);
            if (Environment.GetEnvironmentVariable("FLOWPACK_KEEP_OFFICIAL_TEST_ENV") == "1")
            {
                var profile = Path.Combine(root, "desktop-profile"); Directory.CreateDirectory(profile);
                var registrations = new List<object>();
                foreach (var instance in new[] { source, target })
                {
                    await File.WriteAllTextAsync(Path.Combine(instance.InstallRoot, ".comfyui-desktop-2"), instance.Id, token);
                    registrations.Add(new { id = instance.Id, name = "FlowPack acceptance " + instance.Name, sourceId = "standalone",
                        installPath = instance.InstallRoot, adopted = false, status = "installed", useSharedModels = false,
                        useSharedInput = false, useSharedOutput = false, launchArgs = "--cpu --listen 127.0.0.1 --disable-auto-launch" });
                }
                await File.WriteAllTextAsync(Path.Combine(profile, "installations.json"), JsonSerializer.Serialize(registrations), token);
                await File.WriteAllTextAsync(Path.Combine(profile, "settings.json"), JsonSerializer.Serialize(new {
                    modelsDirs = new[] { Path.Combine(root, "shared", "models") }, inputDir = Path.Combine(root, "shared", "input"),
                    outputDir = Path.Combine(root, "shared", "output"), installDir = Path.Combine(root, "installations") }), token);
            }
            Console.WriteLine("Official-core evidence: " + Path.Combine(root, "result.json"));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "failure.txt"), ex.ToString(), CancellationToken.None); throw;
        }
        finally
        {
            // Only disposable test-owned core/environment copies; reports, ZIP and inference output remain.
            foreach (var instance in Environment.GetEnvironmentVariable("FLOWPACK_KEEP_OFFICIAL_TEST_ENV") == "1" ? Array.Empty<InstanceDescriptor>() : new[] { source, target })
            {
                var disposable = Path.GetFullPath(instance.CoreDirectory);
                if (!ResourceImportService.Inside(root, disposable)) throw new InvalidOperationException("Unsafe test cleanup path");
                for (var attempt = 0; attempt < 15 && Directory.Exists(disposable); attempt++)
                {
                    try { Directory.Delete(disposable, true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (attempt == 14) await File.WriteAllTextAsync(Path.Combine(root, "cleanup-pending.txt"), disposable + "\n" + ex.Message);
                        else await Task.Delay(1000);
                    }
                }
            }
        }
        InstanceDescriptor Descriptor(string name)
        {
            var install = Path.Combine(root, name); var core = Path.Combine(install, "ComfyUI");
            return new(name + "-" + Guid.NewGuid().ToString("N"), name, "desktop-2", null, install, core, core, Path.Combine(core, "user"),
                Path.Combine(core, "user", "default", "workflows"), Path.Combine(core, "custom_nodes"), Path.Combine(core, ".venv", "Scripts", "python.exe"),
                [Path.Combine(core, "models")], Path.Combine(core, "models"), [], "isolated-official-core-fixture", []) { InputDirectory = Path.Combine(core, "input") };
        }
    }
    private static async Task CopyCoreAsync(string source, string target, CancellationToken token)
    {
        var excluded = new HashSet<string>(["models", "custom_nodes", "input", "output", "user", ".venv"], StringComparer.OrdinalIgnoreCase);
        foreach (var file in ResourceFiles.Enumerate(source))
        {
            token.ThrowIfCancellationRequested(); var relative = ResourceFiles.Relative(source, file);
            if (excluded.Contains(relative.Split('/')[0])) continue;
            await CopyAsync(file, Path.Combine(target, relative), token);
        }
    }
    private static async Task CopyTreeAsync(string source, string target, CancellationToken token)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
        {
            token.ThrowIfCancellationRequested(); var relative = ResourceFiles.Relative(source, file);
            if (relative.Split('/').Any(x => x is "__pycache__" or ".git") || Path.GetFileName(file).StartsWith(".env", StringComparison.OrdinalIgnoreCase)) continue;
            await CopyAsync(file, Path.Combine(target, relative), token);
        }
    }
    private static async Task CopyAsync(string source, string target, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var input = File.OpenRead(source); await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        await input.CopyToAsync(output, token);
    }
    private static async Task<string> RunCoreAsync(InstanceDescriptor instance, string raw, string evidence, CancellationToken token)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var outputDirectory = Path.Combine(evidence, instance.Name + "-output"); Directory.CreateDirectory(outputDirectory);
        var start = new ProcessStartInfo(instance.PythonPath!) { WorkingDirectory = instance.InstallRoot, UseShellExecute = false,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-B", "-s", "ComfyUI/main.py", "--cpu", "--listen", "127.0.0.1", "--port", port.ToString(), "--disable-auto-launch",
            "--base-directory", instance.DataDirectory, "--user-directory", instance.UserDirectory,
            "--input-directory", instance.InputDirectory!, "--output-directory", outputDirectory }) start.ArgumentList.Add(arg);
        start.Environment["PYTHONUTF8"] = "1"; start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        using var process = Process.Start(start) ?? throw new IOException("Cannot start isolated core");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            var ready = false;
            for (var attempt = 0; attempt < 180; attempt++)
            {
                if (process.HasExited) throw new IOException("Isolated core exited: " + await stderr);
                try
                {
                    using var response = await http.GetAsync("/object_info", token);
                    if (response.IsSuccessStatusCode)
                    {
                        using var info = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                        Assert.True(info.RootElement.TryGetProperty("FlowPackAcceptancePass", out _));
                        Assert.Contains("RealESRGAN", info.RootElement.GetProperty("UpscaleModelLoader").GetRawText());
                        ready = true; break;
                    }
                }
                catch (HttpRequestException) { }
                await Task.Delay(1000, token);
            }
            Assert.True(ready, "Isolated core startup timed out");
            using var promptJson = JsonDocument.Parse(raw);
            using var request = new StringContent(JsonSerializer.Serialize(new { prompt = promptJson.RootElement, client_id = Guid.NewGuid().ToString("N") }), System.Text.Encoding.UTF8, "application/json");
            using var queued = await http.PostAsync("/prompt", request, token);
            var queueText = await queued.Content.ReadAsStringAsync(token); Assert.True(queued.IsSuccessStatusCode, queueText);
            using var queue = JsonDocument.Parse(queueText); var id = queue.RootElement.GetProperty("prompt_id").GetString()!;
            for (var attempt = 0; attempt < 180; attempt++)
            {
                using var history = JsonDocument.Parse(await http.GetStringAsync("/history/" + id, token));
                if (history.RootElement.TryGetProperty(id, out var execution))
                {
                    await File.WriteAllTextAsync(Path.Combine(evidence, instance.Name + "-history.json"), execution.GetRawText(), token);
                    Assert.Equal("success", execution.GetProperty("status").GetProperty("status_str").GetString());
                    Assert.NotEmpty(Directory.EnumerateFiles(outputDirectory, "*.png"));
                    return id;
                }
                await Task.Delay(1000, token);
            }
            throw new TimeoutException("Our isolated prompt did not finish");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(evidence, instance.Name + "-stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(evidence, instance.Name + "-stderr.log"), await stderr);
        }
    }
}
