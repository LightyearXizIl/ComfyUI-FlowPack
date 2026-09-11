namespace FlowPack.Core;

/// <summary>Creates an inspectable plan before the worker is allowed to change an instance.</summary>
public sealed class SafeInstallPlanner : IInstallPlanner
{
    public Task<InstallPlan> PlanAsync(PackageManifest package, InstanceFingerprint target, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.InstancePath);
        var actions = package.Resources.Select(resource => new InstallAction(
            $"下载并校验 {resource.Name}",
            resource.SourceUrl is null ? InstallActionKind.Conflict : InstallActionKind.Download,
            resource.SizeBytes,
            resource.SourceUrl is null)).ToArray();
        var peak = actions.Sum(action => action.RequiredBytes);
        return Task.FromResult(new InstallPlan(package, target, actions, peak, DateTimeOffset.UtcNow));
    }
}
