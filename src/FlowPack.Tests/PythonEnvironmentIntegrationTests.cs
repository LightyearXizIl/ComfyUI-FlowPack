using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PythonEnvironmentFactAttribute : FactAttribute
{
    public PythonEnvironmentFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FLOWPACK_TEST_PYTHON_BASE") is null)
            Skip = "Opt-in: creates a disposable Python environment and downloads a verified wheel from PyPI.";
    }
}

public sealed class PythonEnvironmentIntegrationTests
{
    [PythonEnvironmentFact]
    public async Task Compatible_wheel_installs_and_existing_version_replacement_is_blocked()
    {
        var parent = Path.GetFullPath(Environment.GetEnvironmentVariable("FLOWPACK_OFFICIAL_TEST_ROOT") ?? throw new InvalidOperationException("Output path required"));
        var root = Path.Combine(parent, "python-integration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var environment = Path.Combine(root, "environment");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4)); var token = timeout.Token;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("FLOWPACK_TEST_PYTHON_BASE")!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-I", "-m", "venv", environment }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync(token); var stdout = process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token); Assert.True(process.ExitCode == 0, await stderr); await stdout;
        var python = Path.Combine(environment, "Scripts", "python.exe");
        var instance = new InstanceDescriptor("python-fixture", "python-fixture", "desktop-2", null, root, root, root,
            Path.Combine(root, "user"), Path.Combine(root, "workflows"), Path.Combine(root, "custom_nodes"), python,
            [Path.Combine(root, "models")], Path.Combine(root, "models"), [], "fixture", []);
        var service = new PythonDependencyService(Path.Combine(root, "library"));
        var install = new ResourceInstallPlan(Guid.NewGuid().ToString("N"), instance, [], [], 0, DateTimeOffset.UtcNow) { PythonRequirements = ["six==1.17.0"] };
        try
        {
            var before = await PythonDependencyService.SnapshotAsync(python, token); Assert.DoesNotContain("six", before.Keys);
            var plan = await service.PrepareAsync(install, token); Assert.NotNull(plan); Assert.Single(plan.Wheels);
            await service.InstallAsync(install.Id, plan, token);
            var after = await PythonDependencyService.SnapshotAsync(python, token); Assert.Equal("1.17.0", after["six"]);
            Assert.All(before, entry => Assert.Equal(entry.Value, after[entry.Key]));
            var compatible = await service.PrepareAsync(install with { Id = Guid.NewGuid().ToString("N"), PythonRequirements = ["six>=1.16"] }, token);
            Assert.NotNull(compatible); Assert.Empty(compatible.Wheels);
            await Assert.ThrowsAsync<IOException>(() => service.PrepareAsync(install with { Id = Guid.NewGuid().ToString("N"), PythonRequirements = ["six==1.16.0"] }, token));
            var final = await PythonDependencyService.SnapshotAsync(python, token); Assert.Equal("1.17.0", final["six"]);
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed = true, scope = "disposable Python environment; verified PyPI wheel; existing version preserved", before, after, final }), token);
        }
        finally
        {
            if (!ResourceImportService.Inside(root, environment)) throw new InvalidOperationException("Unsafe cleanup path");
            if (Directory.Exists(environment)) Directory.Delete(environment, true);
        }
    }
}
