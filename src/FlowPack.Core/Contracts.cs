namespace FlowPack.Core;

public sealed record InstanceFingerprint(string InstancePath, string PythonPath, string UserDirectory, string? ServerAddress, DateTimeOffset InspectedAt);
public sealed record PackageManifest(string Id, string Name, string Version, IReadOnlyList<ResourceEntry> Resources, string FormatVersion = "1")
{
    public string? Description { get; init; }
    public PackageAuthor? Author { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<WorkflowEntry> EntryWorkflows { get; init; } = [];
    public DistributionDeclaration Distribution { get; init; } = DistributionDeclaration.Unspecified;
    public IReadOnlyList<string> CompletenessIssues { get; init; } = [];
    public bool IsComplete => CompletenessIssues.Count == 0;
}

public sealed record PackageAuthor(string Name, string? Url);
public sealed record WorkflowEntry(string Id, string RelativePath, bool IsEntryPoint = true);
public enum WorkflowFormat { UiV04, UiV10, Api, Unknown }
public sealed record WorkflowDocument(string Id, string DisplayName, WorkflowFormat Format, string RawJson);
public sealed record PackageDraft(
    string Id,
    string? WorkflowId,
    string Name,
    string Version,
    string Description,
    string AuthorName,
    string? AuthorUrl,
    string Source,
    DistributionDeclaration Distribution,
    int CurrentStep);
public enum DistributionDeclaration { Unspecified, OnlineOnly, OfflineComplete, OfflinePartial }

public sealed record ResourceEntry(string Id, string Name, ResourceKind Kind, long SizeBytes, string? Sha256, string? SourceUrl)
{
    public string? PackagePath { get; init; }
    public string? DeploymentPurpose { get; init; }
    public DistributionDeclaration Distribution { get; init; } = DistributionDeclaration.Unspecified;
    public IReadOnlyList<string> DependencyIds { get; init; } = [];
}
public enum ResourceKind { Model, CustomNode, Workflow, Asset, PythonWheel }
public enum VerificationLevel { None, FileIntegrity, EnvironmentReady, WorkflowStaticCheck, TrialRun }
public sealed record InstallAction(string Summary, InstallActionKind Kind, long RequiredBytes, bool NeedsConfirmation = false);
public enum InstallActionKind { Reuse, Download, Deploy, Conflict, Verify }
public sealed record InstallPlan(
    PackageManifest Package,
    InstanceFingerprint Target,
    IReadOnlyList<InstallAction> Actions,
    long PeakRequiredBytes,
    DateTimeOffset CreatedAt,
    bool IsExecutable = true,
    IReadOnlyList<string>? BlockingReasons = null);
public sealed record VerificationReport(VerificationLevel Level, bool Passed, IReadOnlyList<string> Messages);

public interface IInstanceInspector { Task<InstanceFingerprint?> InspectAsync(string candidatePath, CancellationToken cancellationToken = default); }
public interface IWorkflowAnalyzer { Task<IReadOnlyList<ResourceEntry>> AnalyzeAsync(string workflowPath, CancellationToken cancellationToken = default); }
public interface IInstallPlanner { Task<InstallPlan> PlanAsync(PackageManifest package, InstanceFingerprint target, CancellationToken cancellationToken = default); }
public interface IInstallationVerifier { Task<VerificationReport> VerifyAsync(InstallPlan plan, CancellationToken cancellationToken = default); }
