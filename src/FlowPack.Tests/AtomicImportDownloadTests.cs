using System.IO;
using System.Text.Json;
using FlowPack.Infrastructure;
using Microsoft.Data.Sqlite;

namespace FlowPack.Tests;

public sealed class AtomicImportDownloadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_job_and_session_link_commit_together_or_both_rollback(bool rejectSessionWrite)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-atomic-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var db = new ResourceLibraryDatabase(root))
            {
                await db.InitializeAsync();
                await db.SaveImportSessionAsync(new("before", new Dictionary<string, string>()));
                if (rejectSessionWrite)
                {
                    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()); await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = "CREATE TRIGGER reject_session BEFORE UPDATE ON SchemaInfo WHEN NEW.name='import_session_v1' BEGIN SELECT RAISE(ABORT,'injected session write failure'); END;";
                    await command.ExecuteNonQueryAsync();
                }
                var job = new WorkerJob("download", "task.download", JsonSerializer.SerializeToElement(new { }), "Queued", "queued", null, null, 1, DateTimeOffset.UtcNow);
                var state = new ImportSessionState("after", new Dictionary<string, string> { ["resource"] = job.Id });
                if (rejectSessionWrite) await Assert.ThrowsAsync<SqliteException>(() => db.SaveDownloadAndImportSessionAsync(job, state));
                else await db.SaveDownloadAndImportSessionAsync(job, state);
            }
            await using var reopened = new ResourceLibraryDatabase(root); await reopened.InitializeAsync();
            var session = await reopened.LoadImportSessionAsync(); var jobs = await reopened.LoadJobsAsync();
            if (rejectSessionWrite) { Assert.Empty(jobs); Assert.Equal("before", session!.PlanId); Assert.Empty(session.DownloadJobs); }
            else { Assert.Single(jobs); Assert.Equal("after", session!.PlanId); Assert.Equal("download", session.DownloadJobs["resource"]); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
