namespace FlowPack.Infrastructure;

// UI continuation data only; deliberately contains no executable installation plan or capability.
public sealed record ImportSessionState(string PlanId, IReadOnlyDictionary<string, string> DownloadJobs)
{
    public int Version { get; init; } = 1;
    public long Revision { get; init; }
    // Optional for legacy sessions. Draft preferences only, never an executable plan.
    public IReadOnlyList<ImportResourceChoice>? ResourceChoices { get; init; }
}
public sealed record ImportResourceChoice(string ResourceId, string SourcePath, bool IsSelected, string TargetRelativePath);
public sealed record RestoredImportSession(ImportPlan Plan, ImportSessionState State);
public sealed record OnlineDownloadInput(string PlanId, string ResourceId, string FileName);
