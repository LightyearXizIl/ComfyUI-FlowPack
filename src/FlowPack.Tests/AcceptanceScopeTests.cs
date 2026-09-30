using System.IO;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Smoke;

namespace FlowPack.Tests;

public sealed class AcceptanceScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-scope-" + Guid.NewGuid().ToString("N"));
    private readonly AcceptanceScope _scope;
    private readonly InstanceDescriptor _instance;
    public AcceptanceScopeTests()
    {
        var fixture = Path.Combine(_root, "fixtures", "one");
        var external = Path.Combine(_root, "external", "one");
        var profile = Path.Combine(fixture, "profile");
        Directory.CreateDirectory(profile); Directory.CreateDirectory(external);
        _scope = new(fixture, profile, external, ["a"]);
        _instance = new("a", "test", "desktop-2", null, Path.Combine(fixture, "target"), Path.Combine(fixture, "target", "ComfyUI"),
            external, Path.Combine(external, "user"), Path.Combine(external, "user", "default", "workflows"),
            Path.Combine(external, "custom_nodes"), Path.Combine(external, ".venv", "Scripts", "python.exe"),
            [Path.Combine(external, "models")], Path.Combine(external, "models"), [], "fingerprint", [])
            { ConfigurationRoot = profile, InputDirectory = Path.Combine(external, "input"), DesktopLayout = "standalone-adopted" };
        WriteRegistry(false, "");
    }

    [Fact]
    public void Native_and_adopted_paths_are_scoped_without_granting_production_qualification()
    {
        _scope.ValidateRoots(Path.Combine(_root, "fixtures"), Path.Combine(_root, "external"));
        _scope.ValidateInstance(_instance);
        var native = _instance with { DataDirectory = _scope.FixtureRoot,
            PythonPath = Path.Combine(_scope.FixtureRoot, ".venv", "Scripts", "python.exe"), DesktopLayout = "standalone-native" };
        _scope.ValidateInstance(native);
        Assert.False(_scope.Evaluate(_instance, false).Allows(false)); // No real Desktop version, so no capability.
    }

    [Theory]
    [InlineData("models")]
    [InlineData("python")]
    [InlineData("input")]
    [InlineData("extra")]
    [InlineData("profile")]
    [InlineData("id")]
    public void Effective_external_paths_or_foreign_identity_are_rejected(string field)
    {
        var outside = Path.Combine(_scope.ExternalDataRoot + "-lookalike", "resource");
        var altered = field switch
        {
            "models" => _instance with { ModelRoots = [outside] },
            "python" => _instance with { PythonPath = outside },
            "input" => _instance with { InputDirectory = outside },
            "extra" => _instance with { ExtraPaths = [new("loras", outside)] },
            "profile" => _instance with { ConfigurationRoot = outside },
            _ => _instance with { Id = "foreign" }
        };
        Assert.Throws<IOException>(() => _scope.ValidateInstance(altered));
        Assert.False(_scope.Evaluate(altered, true).Allows(true));
    }

    [Fact]
    public void Broad_roots_shared_output_and_output_override_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => (_scope with { ExternalDataRoot = Path.Combine(_root, "external") })
            .ValidateRoots(Path.Combine(_root, "fixtures"), Path.Combine(_root, "external")));
        WriteRegistry(true, ""); Assert.Throws<IOException>(() => _scope.ValidateInstance(_instance));
        WriteRegistry(false, "--output-directory=\"" + Path.Combine(_root, "original-output") + "\"");
        Assert.Throws<IOException>(() => _scope.ValidateInstance(_instance));
        WriteRegistry(false, "--output-directory=\"" + Path.Combine(_scope.ExternalDataRoot, "output") + "\"");
        _scope.ValidateInstance(_instance);
    }

    private void WriteRegistry(bool sharedOutput, string args) => File.WriteAllText(Path.Combine(_scope.DesktopProfile, "installations.json"),
        JsonSerializer.Serialize(new[] { new { id = "a", useSharedOutput = sharedOutput, launchArgs = args } }));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
