namespace FlowPack.Core;

public sealed record InstanceFingerprint(string InstancePath, string PythonPath, string UserDirectory, string? ServerAddress, DateTimeOffset InspectedAt);
public sealed record PackageManifest(string Id, string Name, string Version, IReadOnlyList<ResourceEntry> Resources, string FormatVersion = "1");
public sealed record ResourceEntry(string Id, string Name, ResourceKind Kind, long SizeBytes, string? Sha256, string? SourceUrl);
public enum ResourceKind { Model, CustomNode, Workflow, Asset, PythonWheel }
public enum VerificationLevel { None, FileIntegrity, EnvironmentReady, WorkflowStaticCheck, TrialRun }
public sealed record InstallAction(string Summary, InstallActionKind Kind, long RequiredBytes, bool NeedsConfirmation = false);
public enum InstallActionKind { Reuse, Download, Deploy, Conflict, Verify }
public sealed record InstallPlan(PackageManifest Package, InstanceFingerprint Target, IReadOnlyList<InstallAction> Actions, long PeakRequiredBytes, DateTimeOffset CreatedAt);
public sealed record VerificationReport(VerificationLevel Level, bool Passed, IReadOnlyList<string> Messages);

public interface IInstanceInspector { Task<InstanceFingerprint?> InspectAsync(string candidatePath, CancellationToken cancellationToken = default); }
public interface IWorkflowAnalyzer { Task<IReadOnlyList<ResourceEntry>> AnalyzeAsync(string workflowPath, CancellationToken cancellationToken = default); }
public interface IInstallPlanner { Task<InstallPlan> PlanAsync(PackageManifest package, InstanceFingerprint target, CancellationToken cancellationToken = default); }
public interface IInstallationVerifier { Task<VerificationReport> VerifyAsync(InstallPlan plan, CancellationToken cancellationToken = default); }
