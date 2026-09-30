using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Smoke;

namespace FlowPack.Tests;

public sealed class DesktopRuntimeFactAttribute : FactAttribute
{
    public DesktopRuntimeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FLOWPACK_TEST_DESKTOP_RUNTIME") != "1")
            Skip = "Opt-in read-only verification of a Desktop GUI-launched isolated instance and its own prompt.";
    }
}

public sealed class DesktopRuntimeAcceptanceTests
{
    [DesktopRuntimeFact]
    public async Task Gui_launched_instance_binds_discovery_runtime_dependencies_and_successful_real_output()
    {
        string Setting(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name + " required");
        var profile = Path.GetFullPath(Setting("FLOWPACK_DESKTOP_TEST_PROFILE"));
        var root = Path.GetDirectoryName(profile)!;
        var instanceId = Setting("FLOWPACK_DESKTOP_TEST_INSTANCE");
        var promptId = Guid.Parse(Setting("FLOWPACK_DESKTOP_TEST_PROMPT")).ToString();
        var scenario = Environment.GetEnvironmentVariable("FLOWPACK_DESKTOP_TEST_SCENARIO") ?? "zip-roundtrip";
        Assert.Contains(scenario, new[] { "zip-roundtrip", "downloaded-model" });
        var downloadedModel = scenario == "downloaded-model";
        var expectedModel = downloadedModel ? "realesr-animevideov3.pth" : "RealESRGAN_x2plus.pth";
        var workflowName = downloadedModel ? "FlowPack-download-workflow.json" : "upscale.json";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var token = timeout.Token;
        var peers = await new DesktopInstanceDiscovery(configurationRoot: profile).DiscoverAsync(token);
        var instance = Assert.Single(peers, x => x.Id == instanceId);
        Assert.Empty(instance.Issues);
        // This test must not be pointed at the user's original data or Python.
        var scopeFile = Environment.GetEnvironmentVariable("FLOWPACK_DESKTOP_TEST_SCOPE");
        if (!string.IsNullOrWhiteSpace(scopeFile))
        {
            // Reuse the test host's bounded cross-drive scope, never an arbitrary extra allowed root.
            var scope = AcceptanceScope.Load(scopeFile);
            scope.ValidateInstance(instance);
        }
        else
        {
            foreach (var path in new[] { instance.InstallRoot, instance.CoreDirectory, instance.DataDirectory, instance.PythonPath! })
                Assert.StartsWith(root + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        }
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var resolver = new DesktopRuntimeEndpointResolver(profile);
        var endpoint = await resolver.ResolveAsync(instance, peers, token);
        Assert.NotNull(endpoint);
        var inventory = await new RuntimeNodeInspector(http, resolver).InspectAsync(await new ResourceInventoryService().ScanAsync(instance, token), peers, token);
        Assert.Equal(instance.ConfigurationFingerprint, inventory.RuntimeFingerprint);
        var node = Assert.Single(inventory.Resources, x => x.Kind == ResourceKind.CustomNode && x.Name == "FlowPackAcceptanceNode");
        Assert.True(node.RuntimeChecked); Assert.Contains("FlowPackAcceptancePass", node.LoadedNodeTypes);
        var raw = await File.ReadAllTextAsync(Path.Combine(instance.WorkflowsDirectory, workflowName), token);
        var analysis = await new InventoryDependencyAnalyzer().AnalyzeAsync([WorkflowDocumentFactory.Create("upscale", "upscale", raw)], inventory, token);
        Assert.Empty(analysis.Issues);
        Assert.All(analysis.Dependencies, x => Assert.Equal(DependencyState.Present, x.State));
        Assert.Contains(analysis.Dependencies, x => x.Kind == ResourceKind.Model && x.Reference == expectedModel);
        Assert.Contains(analysis.Dependencies, x => x.Kind == ResourceKind.CustomNode && x.Reference == "FlowPackAcceptancePass");
        var historyRaw = await http.GetStringAsync($"http://127.0.0.1:{endpoint.Port}/history/{promptId}", token);
        using var history = JsonDocument.Parse(historyRaw);
        var execution = history.RootElement.GetProperty(promptId);
        Assert.Equal("success", execution.GetProperty("status").GetProperty("status_str").GetString());
        Assert.True(execution.GetProperty("status").GetProperty("completed").GetBoolean());
        var cachedNodes = execution.GetProperty("status").GetProperty("messages").EnumerateArray()
            .Where(x => x[0].GetString() == "execution_cached")
            .SelectMany(x => x[1].GetProperty("nodes").EnumerateArray()).Select(x => x.GetString()).ToArray();
        // Reusing an input image is valid, but a cached model/output is not evidence of this run.
        Assert.DoesNotContain("1", cachedNodes);
        Assert.DoesNotContain("4", cachedNodes);
        Assert.DoesNotContain("5", cachedNodes);
        var prompt = execution.GetProperty("prompt");
        Assert.Equal("comfyui-frontend", prompt[3].GetProperty("comfy_usage_source").GetString());
        Assert.Equal("FlowPackAcceptancePass", prompt[2].GetProperty("3").GetProperty("class_type").GetString());
        Assert.Equal(expectedModel, prompt[2].GetProperty("1").GetProperty("inputs").GetProperty("model_name").GetString());
        Assert.Equal("fixture.png", prompt[2].GetProperty("2").GetProperty("inputs").GetProperty("image").GetString());
        Assert.Equal("ImageUpscaleWithModel", prompt[2].GetProperty("4").GetProperty("class_type").GetString());
        Assert.Equal("SaveImage", prompt[2].GetProperty("5").GetProperty("class_type").GetString());
        var output = execution.GetProperty("outputs").GetProperty("5").GetProperty("images")[0];
        Assert.Equal("", output.GetProperty("subfolder").GetString());
        var filename = output.GetProperty("filename").GetString()!;
        Assert.Equal(Path.GetFileName(filename), filename);
        var outputPath = Path.Combine(instance.DataDirectory, "output", filename);
        var png = await File.ReadAllBytesAsync(outputPath, token);
        var bitmap = System.Windows.Media.Imaging.BitmapFrame.Create(new MemoryStream(png));
        Assert.Equal(downloadedModel ? 32 : 16, bitmap.PixelWidth); Assert.Equal(downloadedModel ? 32 : 16, bitmap.PixelHeight);
        var evidence = Path.Combine(root, "desktop-gui-evidence", instance.Id, promptId); Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "history.json"), historyRaw, token);
        await File.WriteAllTextAsync(Path.Combine(evidence, "runtime.json"), JsonSerializer.Serialize(new {
            scope = "Desktop GUI launch/run and FlowPack discovery/runtime analysis; NOT FlowPack App install qualification",
            scenario, workflowName, expectedModel, cachedNodes,
            executionNote = downloadedModel ? "Existing acceptance canvas model changed through GUI; installed API JSON analyzed separately; output prefix may differ." : null,
            instance, endpoint, analysis, outputPath, outputSha256 = Convert.ToHexString(SHA256.HashData(png)),
            width = bitmap.PixelWidth, height = bitmap.PixelHeight,
            desktopVersion = FileVersionInfo.GetVersionInfo(instance.DesktopExecutable!).FileVersion }), token);
        Console.WriteLine("Desktop GUI runtime evidence: " + evidence);
    }
}
