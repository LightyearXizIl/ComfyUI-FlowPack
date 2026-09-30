using System.Collections.ObjectModel;
using FlowPack.Infrastructure;

namespace FlowPack.App;

public static class TaskSnapshotReconciler
{
    public static void Apply(ObservableCollection<WorkerJob> rows, IReadOnlyList<WorkerJob> snapshot)
    {
        var ids = snapshot.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = rows.Count - 1; index >= 0; index--)
            if (!ids.Contains(rows[index].Id)) rows.RemoveAt(index);
        for (var index = 0; index < snapshot.Count; index++)
        {
            var next = snapshot[index];
            var current = rows.FirstOrDefault(x => x.Id == next.Id);
            if (current is null) { rows.Insert(index, next); continue; }
            var oldIndex = rows.IndexOf(current);
            if (oldIndex != index) rows.Move(oldIndex, index);
            if (current.Operation != next.Operation || current.State != next.State || current.Stage != next.Stage ||
                current.Error != next.Error || current.CompletedBytes != next.CompletedBytes || current.TotalBytes != next.TotalBytes ||
                current.Attempt != next.Attempt || current.CreatedAt != next.CreatedAt)
                rows[index] = next;
        }
    }
}
