using System;
using System.IO;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class OfficialComfyDesktopAdapterTests
{
    [Fact]
    public async Task Default_adapter_creates_a_non_executable_plan_for_a_detected_desktop()
    {
        var root = Path.Combine(Path.GetTempPath(), "flowpack-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "main.py"), string.Empty);
        var location = new ComfyDesktopLocation("test", root, root, Path.Combine(root, "models"), Path.Combine(root, "custom_nodes"), Path.Combine(root, "user"), Path.Combine(root, "python.exe"), DateTimeOffset.UtcNow);
        var adapter = new OfficialComfyDesktopAdapter(new StubDetector(location));

        var inspection = await adapter.InspectAsync();
        var plan = await adapter.CreateInstallPlanAsync(new PackageManifest("sample", "Sample", "1.0.0", []), inspection!);

        Assert.False(plan.IsExecutable);
        Assert.Contains(plan.BlockingReasons, reason => reason.Contains("隔离实机验收", StringComparison.Ordinal));
        Assert.NotEmpty(plan.TargetFingerprint);
    }

    [Fact]
    public async Task Isolated_adapter_still_requires_a_complete_package()
    {
        var root = Path.Combine(Path.GetTempPath(), "flowpack-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "main.py"), string.Empty);
        var location = new ComfyDesktopLocation("test", root, root, null, null, Path.Combine(root, "user"), Path.Combine(root, "python.exe"), DateTimeOffset.UtcNow);
        var adapter = new OfficialComfyDesktopAdapter(new StubDetector(location), isolatedTestInstance: true);
        var package = new PackageManifest("sample", "Sample", "1.0.0", []) { CompletenessIssues = ["缺少工作流"] };

        var plan = await adapter.CreateInstallPlanAsync(package, (await adapter.InspectAsync())!);

        Assert.False(plan.IsExecutable);
        Assert.Contains("缺少工作流", plan.BlockingReasons);
    }

    private sealed class StubDetector(ComfyDesktopLocation location) : IComfyDesktopDetector
    {
        public Task<ComfyDesktopLocation?> DetectAsync(CancellationToken cancellationToken = default) => Task.FromResult<ComfyDesktopLocation?>(location);
    }
}
