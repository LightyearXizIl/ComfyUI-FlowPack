using System.Security.Cryptography;
using System.Text;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

/// <summary>
/// The only abstraction that is allowed to describe a Desktop target. Discovery is read-only;
/// a normal user Desktop is deliberately never marked writable by this adapter.
/// </summary>
public interface IDesktopAdapter
{
    Task<DesktopAdapterInspection?> InspectAsync(CancellationToken cancellationToken = default);
    Task<InstallPlanSnapshot> CreateInstallPlanAsync(PackageManifest package, DesktopAdapterInspection target, CancellationToken cancellationToken = default);
}

public sealed record DesktopAdapterInspection(
    ComfyDesktopLocation Location,
    InstanceFingerprint Fingerprint,
    string TargetFingerprint,
    bool IsRecognizedOfficialDesktop,
    bool AllowsWriteExecution,
    IReadOnlyList<string> BlockingReasons);

/// <summary>
/// Conservative Desktop adapter for the Windows Desktop layout. It reads the existing detector
/// result and creates a frozen plan only. Production construction never enables write execution;
/// the optional isolated-test switch exists solely for a separately provisioned test instance.
/// </summary>
public sealed class OfficialComfyDesktopAdapter : IDesktopAdapter
{
    private readonly IComfyDesktopDetector _detector;
    private readonly bool _isolatedTestInstance;

    public OfficialComfyDesktopAdapter(IComfyDesktopDetector? detector = null, bool isolatedTestInstance = false)
    {
        _detector = detector ?? new ComfyDesktopDetector();
        _isolatedTestInstance = isolatedTestInstance;
    }

    public async Task<DesktopAdapterInspection?> InspectAsync(CancellationToken cancellationToken = default)
    {
        var location = await _detector.DetectAsync(cancellationToken);
        if (location is null) return null;

        var recognized = location.InstallRoot is not null &&
                         File.Exists(Path.Combine(location.InstallRoot, "main.py"));
        var instance = new InstanceFingerprint(
            location.InstallRoot ?? location.BasePath,
            location.PythonPath ?? string.Empty,
            location.WorkflowsDirectory is null ? string.Empty : Path.GetDirectoryName(location.WorkflowsDirectory) ?? string.Empty,
            null,
            location.InspectedAt);
        var reasons = new List<string>();
        if (!recognized) reasons.Add("未确认官方 Desktop 实例布局；仅允许检查和打包。");
        if (string.IsNullOrWhiteSpace(location.PythonPath)) reasons.Add("未确认该实例的绝对 Python 解释器。");
        if (!_isolatedTestInstance) reasons.Add("当前目标不是经隔离实机验收的测试实例，禁止写入。");

        return new DesktopAdapterInspection(
            location,
            instance,
            CreateTargetFingerprint(location),
            recognized,
            recognized && !string.IsNullOrWhiteSpace(location.PythonPath) && _isolatedTestInstance,
            reasons);
    }

    public async Task<InstallPlanSnapshot> CreateInstallPlanAsync(
        PackageManifest package,
        DesktopAdapterInspection target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(target);
        var preliminary = await new SafeInstallPlanner().PlanAsync(package, target.Fingerprint, cancellationToken);
        var reasons = preliminary.BlockingReasons?.ToList() ?? [];
        reasons.AddRange(target.BlockingReasons);
        var executable = preliminary.IsExecutable && target.AllowsWriteExecution && reasons.Count == 0;
        return new InstallPlanSnapshot(
            Guid.NewGuid().ToString("N"),
            target.TargetFingerprint,
            preliminary.Actions,
            preliminary.PeakRequiredBytes,
            DateTimeOffset.UtcNow,
            executable,
            reasons.Distinct(StringComparer.Ordinal).ToArray(),
            ["写入前重新计算目标指纹。", "配置变更先备份。", "失败时依据 journal 和文件哈希恢复。"]);
    }

    private static string CreateTargetFingerprint(ComfyDesktopLocation location)
    {
        var value = string.Join("\n", new[]
        {
            Path.GetFullPath(location.BasePath).ToUpperInvariant(),
            location.InstallRoot is null ? string.Empty : Path.GetFullPath(location.InstallRoot).ToUpperInvariant(),
            location.ModelsDirectory ?? string.Empty,
            location.CustomNodesDirectory ?? string.Empty,
            location.WorkflowsDirectory ?? string.Empty,
            location.PythonPath ?? string.Empty
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}

public sealed record InstallPlanSnapshot(
    string PlanId,
    string TargetFingerprint,
    IReadOnlyList<InstallAction> Actions,
    long PeakRequiredBytes,
    DateTimeOffset CreatedAt,
    bool IsExecutable,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> RecoverySteps);
