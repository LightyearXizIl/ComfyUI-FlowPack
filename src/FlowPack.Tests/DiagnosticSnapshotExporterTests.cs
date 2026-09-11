using System.IO;
using System.Text.Json;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class DiagnosticSnapshotExporterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"flowpack-diagnostic-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Diagnostic_export_keeps_counts_and_omits_sensitive_task_details()
    {
        var task = new WorkerTask("task", WorkerTaskKind.Download, WorkerTaskState.Failed, "下载模型", "下载失败", null, null,
            "https://example.invalid/file?token=secret", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        var snapshot = DiagnosticSnapshotExporter.Create("0.0.1", true, 2, 3, [task]);

        await DiagnosticSnapshotExporter.ExportAsync(_path, snapshot);
        var json = await File.ReadAllTextAsync(_path);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(2, document.RootElement.GetProperty("packageCount").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("workflowCount").GetInt32());
        Assert.True(document.RootElement.GetProperty("recentTasks")[0].GetProperty("hasError").GetBoolean());
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("example.invalid", json);
        Assert.DoesNotContain("task\"", json);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
