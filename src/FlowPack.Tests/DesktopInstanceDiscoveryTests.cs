using System.IO;
using System.Text.Json;
using FlowPack.ComfyUI;

namespace FlowPack.Tests;

public sealed class DesktopInstanceDiscoveryTests : IDisposable, IDesktopPathProvider
{
    [Fact]
    public async Task Explicit_profile_preserves_provenance_and_does_not_absorb_default_legacy_data()
    {
        var install = Path.Combine(_root, "managed");
        Write(Path.Combine(install, "ComfyUI", "main.py"), "");
        Write(Path.Combine(install, ".comfyui-desktop-2"), "a");
        Write(Path.Combine(install, "ComfyUI", ".venv", "Scripts", "python.exe"), "python");
        var records = JsonSerializer.Serialize(new[] { new { id = "a", sourceId = "standalone", installPath = install, useSharedModels = false } });
        var first = Path.Combine(_root, "profile-a"); var second = Path.Combine(_root, "profile-b");
        foreach (var profile in new[] { first, second })
        {
            Write(Path.Combine(profile, "installations.json"), records);
            Write(Path.Combine(profile, "data-location.json"), "{\"mode\":\"local-appdata\"}");
        }
        Write(Path.Combine(RoamingApplicationData, "ComfyUI", "config.json"), JsonSerializer.Serialize(new { basePath = _root }));
        var a = Assert.Single(await new DesktopInstanceDiscovery(this, first).DiscoverAsync());
        var b = Assert.Single(await new DesktopInstanceDiscovery(this, second).DiscoverAsync());
        Assert.Equal(first, a.ConfigurationRoot); Assert.Equal(second, b.ConfigurationRoot);
        Assert.NotEqual(a.ConfigurationFingerprint, b.ConfigurationFingerprint);
        Assert.Empty(a.Issues);
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-discovery-" + Guid.NewGuid().ToString("N"));
    public string LocalApplicationData => Path.Combine(_root, "local");
    public string RoamingApplicationData => Path.Combine(_root, "roaming");
    public string MyDocuments => Path.Combine(_root, "documents");
    public string UserProfile => Path.Combine(_root, "home");
    public string SystemDrive => Path.GetPathRoot(_root)!;
    public IReadOnlyList<string> GetRegisteredDesktopExecutables() => [Path.Combine(_root, "Desktop", "Comfy Desktop.exe")];
    public IReadOnlyList<string> GetProcessModulePaths(string processName) => [];

    [Fact]
    public async Task Adopted_registration_preserves_user_python_and_shared_models_without_creating_workflows()
    {
        var install = Path.Combine(_root, "new-instance");
        var data = Path.Combine(_root, "old-data");
        var shared = Path.Combine(_root, "shared-models");
        var python = Path.Combine(data, ".venv", "Scripts", "python.exe");
        Write(Path.Combine(install, "ComfyUI", "main.py"), "");
        Write(Path.Combine(install, ".comfyui-desktop-2"), "a");
        Write(python, "python");
        WriteConfig(new[] { new { id = "a", name = "接管实例", sourceId = "standalone", installPath = install,
            adopted = true, adoptedBaseDir = data, adoptedPythonPath = python, useSharedModels = true } }, new { modelsDirs = new[] { shared } });
        Write(Path.Combine(RoamingApplicationData, "ComfyUI", "config.json"), JsonSerializer.Serialize(new { basePath = data }));

        var instance = Assert.Single(await new DesktopInstanceDiscovery(this).DiscoverAsync());
        Assert.Empty(instance.Issues);
        Assert.Equal(data, instance.DataDirectory);
        Assert.Equal(python, instance.PythonPath);
        Assert.Equal(shared, instance.ModelsWriteDirectory);
        Assert.Contains(Path.Combine(data, "models"), instance.ModelRoots);
        Assert.Equal(Path.Combine(data, "user", "default", "workflows"), instance.WorkflowsDirectory);
        Assert.False(Directory.Exists(instance.WorkflowsDirectory));
    }

    [Fact]
    public async Task Managed_instances_ignore_cloud_and_honor_explicit_quoted_overrides()
    {
        var install = Path.Combine(_root, "managed");
        var user = Path.Combine(_root, "custom user");
        Write(Path.Combine(install, "ComfyUI", "main.py"), "");
        Write(Path.Combine(install, ".comfyui-desktop-2"), "a");
        Write(Path.Combine(install, "ComfyUI", ".venv", "Scripts", "python.exe"), "python");
        WriteConfig(new object[] {
            new { id = "a", name = "a", sourceId = "standalone", installPath = install, useSharedModels = false, launchArgs = "--user-directory=\"" + user + "\"" },
            new { id = "cloud", sourceId = "cloud", installPath = install }
        }, new { modelsDirs = new[] { Path.Combine(_root, "shared") } });
        var instance = Assert.Single(await new DesktopInstanceDiscovery(this).DiscoverAsync());
        Assert.Empty(instance.Issues);
        Assert.Equal(user, instance.UserDirectory);
        Assert.Single(instance.ModelRoots);
        Assert.Equal(Path.Combine(install, "ComfyUI", "models"), instance.ModelsWriteDirectory);
    }

    [Fact]
    public async Task Missing_adopted_python_does_not_fall_back_and_configuration_change_invalidates_fingerprint()
    {
        var install = Path.Combine(_root, "instance");
        Write(Path.Combine(install, "ComfyUI", "main.py"), "");
        Write(Path.Combine(install, ".comfyui-desktop-2"), "a");
        Write(Path.Combine(install, "ComfyUI", ".venv", "Scripts", "python.exe"), "wrong");
        WriteConfig(new[] { new { id = "a", sourceId = "standalone", installPath = install, adopted = true, adoptedBaseDir = install } }, new { modelsDirs = Array.Empty<string>() });
        var discovery = new DesktopInstanceDiscovery(this);
        var first = Assert.Single(await discovery.DiscoverAsync());
        Assert.Null(first.PythonPath);
        Assert.NotEmpty(first.Issues);
        Write(Path.Combine(install, "ComfyUI", "extra_model_paths.yaml"), "external:\n  base_path: .\n  loras: extras\n");
        var second = Assert.Single(await discovery.DiscoverAsync());
        Assert.NotEqual(first.ConfigurationFingerprint, second.ConfigurationFingerprint);
        Assert.Single(second.ExtraPaths);
    }

    [Fact]
    public async Task Invalid_record_does_not_hide_other_instances_and_shared_defaults_follow_marker()
    {
        var install = Path.Combine(_root, "managed");
        Write(Path.Combine(install, "ComfyUI", "main.py"), "");
        Write(Path.Combine(install, ".comfyui-desktop-2"), "good");
        Write(Path.Combine(install, "ComfyUI", ".venv", "Scripts", "python.exe"), "python");
        Write(Path.Combine(RoamingApplicationData, "Comfy Desktop", "data-location.json"), "{\"mode\":\"legacy-home\"}");
        WriteConfig(new[] {
            new { id = "bad", sourceId = "standalone", installPath = install, launchArgs = "--user-directory \"unclosed" },
            new { id = "good", sourceId = "standalone", installPath = install, launchArgs = "--input-directory \"" + Path.Combine(_root, "overridden") + "\"" }
        }, new { });
        var found = await new DesktopInstanceDiscovery(this).DiscoverAsync();
        Assert.Equal(2, found.Count);
        Assert.NotEmpty(found[0].Issues);
        Assert.Empty(found[1].Issues);
        Assert.Equal(Path.Combine(UserProfile, "ComfyUI-Shared", "input"), found[1].InputDirectory);
        Assert.Contains(Path.Combine(UserProfile, "ComfyUI-Shared", "models"), found[1].ModelRoots);
        Assert.False(Directory.Exists(UserProfile));
    }

    private void WriteConfig(object records, object settings)
    {
        Write(Path.Combine(RoamingApplicationData, "Comfy Desktop", "installations.json"), JsonSerializer.Serialize(records));
        Write(Path.Combine(RoamingApplicationData, "Comfy Desktop", "settings.json"), JsonSerializer.Serialize(settings));
    }
    private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
