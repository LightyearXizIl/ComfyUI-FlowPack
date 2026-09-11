using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>
/// Produces a user-selected, local diagnostic summary. It intentionally excludes file
/// paths, workflow content, URLs, credentials and raw error text.
/// </summary>
public sealed record DiagnosticTaskSummary(string Kind, string State, string Stage, bool HasError);

public sealed record DiagnosticSnapshot(
    string FormatVersion,
    DateTimeOffset GeneratedAtUtc,
    string ApplicationVersion,
    bool ResourceLibraryAssociated,
    int PackageCount,
    int WorkflowCount,
    int TaskCount,
    IReadOnlyList<DiagnosticTaskSummary> RecentTasks);

public static class DiagnosticSnapshotExporter
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static DiagnosticSnapshot Create(
        string applicationVersion,
        bool resourceLibraryAssociated,
        int packageCount,
        int workflowCount,
        IReadOnlyList<WorkerTask> tasks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        ArgumentNullException.ThrowIfNull(tasks);
        if (packageCount < 0 || workflowCount < 0) throw new ArgumentOutOfRangeException("对象计数不能为负数。");

        var recentTasks = tasks
            .OrderByDescending(task => task.UpdatedAt)
            .Take(25)
            .Select(task => new DiagnosticTaskSummary(task.Kind.ToString(), task.State.ToString(), task.Stage, !string.IsNullOrWhiteSpace(task.ErrorMessage)))
            .ToArray();
        return new DiagnosticSnapshot("1", DateTimeOffset.UtcNow, applicationVersion, resourceLibraryAssociated, packageCount, workflowCount, tasks.Count, recentTasks);
    }

    public static async Task ExportAsync(string path, DiagnosticSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(snapshot, Options), cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
