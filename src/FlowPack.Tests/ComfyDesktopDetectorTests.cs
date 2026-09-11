using System.IO;
using System.Runtime.InteropServices;
using FlowPack.ComfyUI;
using Xunit;

namespace FlowPack.Tests;

public sealed class ComfyDesktopDetectorTests
{
    [Fact]
    public async Task Detect_returns_null_when_no_candidate_exists()
    {
        var provider = new StubPathProvider(Temp(), Temp(), Temp());
        var detector = new ComfyDesktopDetector(provider);

        Assert.Null(await detector.DetectAsync());
    }

    [Fact]
    public async Task Detect_reads_basePath_from_config_json()
    {
        var roaming = Temp();
        var baseDir = Temp();
        Directory.CreateDirectory(Path.Combine(baseDir, "models"));
        Directory.CreateDirectory(Path.Combine(baseDir, "custom_nodes"));
        Directory.CreateDirectory(Path.Combine(baseDir, "user", "default", "workflows"));
        Directory.CreateDirectory(Path.Combine(roaming, "ComfyUI"));
        File.WriteAllText(
            Path.Combine(roaming, "ComfyUI", "config.json"),
            "{\"basePath\":\"" + baseDir.Replace("\\", "\\\\") + "\"}");

        var detector = new ComfyDesktopDetector(new StubPathProvider(Temp(), roaming, Temp()));

        var result = await detector.DetectAsync();

        Assert.NotNull(result);
        Assert.Equal("config.json", result!.DetectedVia);
        Assert.Equal(baseDir, result.BasePath);
        Assert.Equal(Path.Combine(baseDir, "models"), result.ModelsDirectory);
        Assert.Equal(Path.Combine(baseDir, "custom_nodes"), result.CustomNodesDirectory);
        Assert.Equal(Path.Combine(baseDir, "user", "default", "workflows"), result.WorkflowsDirectory);
    }

    [Fact]
    public async Task Detect_scans_ComfyUI_Installs_instances()
    {
        var local = Temp();
        var instance = Path.Combine(local, "Comfy-Desktop", "ComfyUI-Installs", "default");
        Directory.CreateDirectory(Path.Combine(instance, "models"));
        Directory.CreateDirectory(Path.Combine(instance, "custom_nodes"));
        File.WriteAllText(Path.Combine(instance, "main.py"), string.Empty);

        var detector = new ComfyDesktopDetector(new StubPathProvider(local, Temp(), Temp()));

        var result = await detector.DetectAsync();

        Assert.NotNull(result);
        Assert.Equal("ComfyUI-Installs", result!.DetectedVia);
        Assert.Equal(instance, result.BasePath);
        Assert.Equal(Path.Combine(instance, "models"), result.ModelsDirectory);
    }

    [Fact]
    public async Task Detect_prefers_config_json_over_partial_install()
    {
        var roaming = Temp();
        var baseDir = Temp();
        Directory.CreateDirectory(Path.Combine(baseDir, "models"));
        Directory.CreateDirectory(Path.Combine(roaming, "ComfyUI"));
        File.WriteAllText(
            Path.Combine(roaming, "ComfyUI", "config.json"),
            "{\"basePath\":\"" + baseDir.Replace("\\", "\\\\") + "\"}");
        var local = Temp();
        var partial = Path.Combine(local, "Comfy-Desktop", "ComfyUI-Installs", "partial");
        Directory.CreateDirectory(Path.Combine(partial, "custom_nodes"));

        var detector = new ComfyDesktopDetector(new StubPathProvider(local, roaming, Temp()));

        var result = await detector.DetectAsync();

        Assert.NotNull(result);
        Assert.Equal("config.json", result!.DetectedVia);
    }

    [Fact]
    public async Task Detect_returns_null_on_non_windows()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        var detector = new ComfyDesktopDetector(new StubPathProvider(Temp(), Temp(), Temp()));
        Assert.Null(await detector.DetectAsync());
    }

    private sealed class StubPathProvider : IDesktopPathProvider
    {
        public StubPathProvider(string local, string roaming, string docs)
        {
            LocalApplicationData = local;
            RoamingApplicationData = roaming;
            MyDocuments = docs;
        }

        public string LocalApplicationData { get; }
        public string RoamingApplicationData { get; }
        public string MyDocuments { get; }
        public IReadOnlyList<string> GetProcessModulePaths(string processName) => [];
    }

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "flowpack-desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
