namespace FlowPack.Core;

/// <summary>Creates an inspectable plan before the worker is allowed to change an instance.</summary>
public sealed class SafeInstallPlanner : IInstallPlanner
{
    public Task<InstallPlan> PlanAsync(PackageManifest package, InstanceFingerprint target, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.InstancePath);
        ArgumentNullException.ThrowIfNull(package);
        if (!package.IsComplete)
        {
            var reasons = package.CompletenessIssues.ToArray();
            var blockedActions = reasons.Select(reason => new InstallAction(reason, InstallActionKind.Conflict, 0, true)).ToArray();
            return Task.FromResult(new InstallPlan(package, target, blockedActions, 0, DateTimeOffset.UtcNow, false, reasons));
        }
        var actions = package.Resources.Select(resource =>
        {
            if (resource.SourceUrl is null)
            {
                return new InstallAction($"资源 {resource.Name} 缺少可下载来源", InstallActionKind.Conflict, 0, true);
            }
            if (string.IsNullOrWhiteSpace(resource.Sha256))
            {
                return new InstallAction($"资源 {resource.Name} 缺少 SHA-256，不能安全下载", InstallActionKind.Conflict, 0, true);
            }
            return new InstallAction($"下载并校验 {resource.Name}", InstallActionKind.Download, resource.SizeBytes);
        }).ToArray();
        var peak = actions.Sum(action => action.RequiredBytes);
        var executionReasons = actions.Where(action => action.NeedsConfirmation).Select(action => action.Summary).ToArray();
        return Task.FromResult(new InstallPlan(package, target, actions, peak, DateTimeOffset.UtcNow, executionReasons.Length == 0, executionReasons));
    }
}
