using System.Collections.ObjectModel;
using System.Text.Json;
using FlowPack.App;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class TaskSnapshotReconcilerTests
{
    private static WorkerJob Job(string id) => new(id, "task.download", JsonSerializer.SerializeToElement(new { }),
        "Running", "下载中", null, null, 1, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Unchanged_snapshot_keeps_row_identity_and_emits_no_collection_change()
    {
        var original = Job("a"); var rows = new ObservableCollection<WorkerJob> { original }; var changes = 0;
        rows.CollectionChanged += (_, _) => changes++;
        TaskSnapshotReconciler.Apply(rows, [Job("a")]);
        Assert.Same(original, rows[0]); Assert.Equal(0, changes);
    }

    [Fact]
    public void Snapshot_reorders_adds_removes_and_updates_progress_and_failure()
    {
        var first = Job("a"); var second = Job("b");
        var rows = new ObservableCollection<WorkerJob> { first, second, Job("removed") };
        var failed = first with { State = "Failed", Error = "断线", CompletedBytes = 12, TotalBytes = 100 };
        TaskSnapshotReconciler.Apply(rows, [Job("new"), second, failed]);
        Assert.Equal(new[] { "new", "b", "a" }, rows.Select(x => x.Id));
        Assert.Same(second, rows[1]); Assert.Same(failed, rows[2]);
        Assert.True(rows[2].CanControl("task.retry")); Assert.Equal(12, rows[2].CompletedBytes);
    }
}
