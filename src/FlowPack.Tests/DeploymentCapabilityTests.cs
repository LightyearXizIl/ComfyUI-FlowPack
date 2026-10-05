using System.IO;
using FlowPack.ComfyUI;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class DeploymentCapabilityTests : IDisposable
{
    [Theory]
    [InlineData("1.0.47.0", "260908ensm0r3cr", "1.0.47")]
    [InlineData("1.0.48", "1.0.47", "1.0.48")]
    [InlineData("1.0.47.1", "1.0.47", "1.0.47.1")]
    [InlineData(null, "1.0.47.0", "1.0.47")]
    [InlineData("1.0.47-beta", "1.0.47.0", null)]
    [InlineData("1.0", "1.0.47.0", null)]
    [InlineData(null, "260908ensm0r3cr", null)]
    public void Product_version_is_authoritative_and_unrecognized_revisions_do_not_inherit_qualification(
        string? product, string? file, string? expected) =>
        Assert.Equal(expected, DeploymentCapabilityProvider.ResolveDesktopVersion(product, file));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-capability-" + Guid.NewGuid().ToString("N"));
    private InstanceDescriptor Instance()
    {
        Directory.CreateDirectory(_root); var exe = Path.Combine(_root, "Desktop.exe"); File.WriteAllText(exe, "fixture");
        return new("a", "A", "desktop-2", exe, _root, _root, _root, _root, _root, _root, exe, [], _root, [], "fingerprint", [])
        { ConfigurationRoot = _root, DesktopLayout = "standalone-native" };
    }
    [Fact]
    public void Older_versions_and_incomplete_records_cannot_inherit_current_qualification()
    {
        var instance = Instance();
        var result = new DeploymentCapabilityProvider(readVersion: _ => "1.0.47.0").Evaluate(instance, false);
        Assert.False(result.Allows(false)); Assert.Contains(result.Reasons, x => x.Contains("尚未通过"));
        var provider = new DeploymentCapabilityProvider([new("1.0.47", "standalone-native", true, "fixture-only")], _ => "1.0.47");
        Assert.False(provider.Evaluate(instance with { ConfigurationRoot = null }, false).Allows(false));
        Assert.False(provider.Evaluate(instance with { Generation = "legacy" }, false).Allows(false));
        Assert.False(provider.Evaluate(instance with { Issues = ["配置失效"] }, false).Allows(false));
    }
    [Theory]
    [InlineData("standalone-native")]
    [InlineData("standalone-adopted")]
    public void Shipped_qualification_matches_verified_Desktop_114_layouts_only(string layout)
    {
        var instance = Instance() with { DesktopLayout = layout };
        var provider = new DeploymentCapabilityProvider(readVersion: _ => "1.1.4.0");
        var result = provider.Evaluate(instance, true);
        Assert.True(result.Allows(true));
        Assert.Equal("desktop-1.1.4-transfer-20261005", result.EvidenceId);
        Assert.False(new DeploymentCapabilityProvider(readVersion: _ => "1.1.4.1").Evaluate(instance, true).Allows(true));
        Assert.False(provider.Evaluate(instance with { DesktopLayout = "unknown" }, true).Allows(true));
    }
    [Fact]
    public void Qualification_is_exact_version_layout_and_python_scope()
    {
        var instance = Instance(); var version = "1.0.47.0";
        var provider = new DeploymentCapabilityProvider([new("1.0.47", "standalone-native", false, "fixture-only")], _ => version);
        var allowed = provider.Evaluate(instance, false);
        Assert.True(allowed.Allows(false)); Assert.False(allowed.Allows(true));
        Assert.Equal(instance.ConfigurationFingerprint, allowed.ConfigurationFingerprint);
        Assert.False(provider.Evaluate(instance, true).Allows(true));
        Assert.False(provider.Evaluate(instance with { DesktopLayout = "standalone-adopted" }, false).Allows(false));
        version = "1.0.48";
        Assert.False(provider.Evaluate(instance, false).Allows(false));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
