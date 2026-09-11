namespace FlowPack.Core;

public enum WorkerTaskKind { Download, Install, PackageExport, Verification, Maintenance }
public enum WorkerTaskState { Queued, Running, Paused, Completed, Failed, Cancelled }

/// <summary>
/// Persisted task snapshot. Bytes are nullable when an operation has no meaningful
/// byte total, so callers never need to invent a percentage.
/// </summary>
public sealed record WorkerTask(
    string Id,
    WorkerTaskKind Kind,
    WorkerTaskState State,
    string Summary,
    string Stage,
    long? CompletedBytes,
    long? TotalBytes,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
