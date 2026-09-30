using System.IO;
using System.Text.Json;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PythonRecoveryCorruptionTests
{
    [Theory]
    [InlineData("{\"state\":")]
    [InlineData("[]")]
    [InlineData("{\"state\":123}")]
    [InlineData("{\"state\":\"future-state\"}")]
    public async Task Bad_journal_is_preserved_and_does_not_hide_other_interrupted_environments(string invalid)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-python-recovery-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "state", "python-attempts"); Directory.CreateDirectory(directory);
        var bad = Path.Combine(directory, "bad.json"); var good = Path.Combine(directory, "interrupted.json");
        try
        {
            await File.WriteAllTextAsync(bad, invalid);
            await File.WriteAllTextAsync(good, JsonSerializer.Serialize(new { state = "Installing", PythonPath = "C:/fixture/python.exe", before = new { six = "1.17.0" } }));
            var service = new PythonDependencyService(root);
            var issues = await service.RecoverAsync(); Assert.Equal(2, issues.Count);
            Assert.Null(Assert.Single(issues, x => x.PlanId == "bad").PythonPath);
            Assert.Contains("需要人工修复", Assert.Single(issues, x => x.PlanId == "bad").Message);
            Assert.Equal(invalid, await File.ReadAllTextAsync(bad));
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(good));
            Assert.Equal("NeedsRepair", result.RootElement.GetProperty("state").GetString());
            Assert.Equal(2, (await service.RecoverAsync()).Count);
            var blocked = await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync("must-not-run",
                new PythonDependencyPlan(Path.Combine(root, "does-not-exist.exe"), new Dictionary<string, string>(),
                    [new PythonWheelFile("fixture", "1", Path.Combine(root, "missing.whl"), "not-a-hash")])));
            Assert.Contains("需要人工修复", blocked.Message);
            await using (var worker = new PersistentWorkerService(new(root))) await worker.InitializeAsync();
            await using var database = new ResourceLibraryDatabase(root);
            var firstJobs = (await database.LoadJobsAsync()).Select(x => JsonSerializer.Deserialize<WorkerJob>(x)!).ToArray();
            Assert.Equal(2, firstJobs.Length);
            Assert.All(firstJobs, job =>
            {
                Assert.Equal("install.recovery", job.Operation);
                Assert.Equal("NeedsReview", job.State);
                Assert.Contains("Python", job.Stage);
                Assert.False(string.IsNullOrWhiteSpace(job.Error));
                Assert.Empty(job.AvailableActions);
            });
            Assert.Contains(bad, Assert.Single(firstJobs, x => x.Id == "python-recovery-bad").Error);
            await using (var worker = new PersistentWorkerService(new(root))) await worker.InitializeAsync();
            var secondJobs = (await database.LoadJobsAsync()).Select(x => JsonSerializer.Deserialize<WorkerJob>(x)!).ToArray();
            Assert.Equal(2, secondJobs.Length);
            Assert.All(secondJobs, job => Assert.Equal(firstJobs.Single(x => x.Id == job.Id).CreatedAt, job.CreatedAt));
        }
        finally { Directory.Delete(root, true); }
    }
}
