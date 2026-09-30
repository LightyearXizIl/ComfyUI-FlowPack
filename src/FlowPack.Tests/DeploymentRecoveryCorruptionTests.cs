using System.IO;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class DeploymentRecoveryCorruptionTests
{
    [Theory]
    [InlineData("truncated")]
    [InlineData("null")]
    [InlineData("array")]
    [InlineData("unknown-state")]
    [InlineData("missing-files")]
    [InlineData("null-item")]
    [InlineData("wrong-id")]
    [InlineData("duplicate-target")]
    [InlineData("wrong-temporary")]
    [InlineData("completed-but-invalid")]
    public async Task Invalid_journal_is_preserved_visible_and_blocks_new_installation(string variant)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-deployment-corruption-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "state", "journal"); Directory.CreateDirectory(directory);
        try
        {
            var badId = Guid.NewGuid().ToString("N");
            var target = Path.Combine(root, "model.pth");
            var partial = target + ".flowpack-" + badId + ".tmp";
            await File.WriteAllTextAsync(partial, "preserve-invalid-record-partial");
            var first = new JournalFile(target, "expected", "Intent") { TemporaryPath = partial };
            var record = new DeploymentJournal(badId, "Running", [first]);
            var invalid = variant switch
            {
                "truncated" => "{\"PlanId\":",
                "null" => "null",
                "array" => "[]",
                "unknown-state" => JsonSerializer.Serialize(record with { State = "FutureState" }),
                "missing-files" => JsonSerializer.Serialize(new { PlanId = badId, State = "Running" }),
                "null-item" => JsonSerializer.Serialize(record with { Files = [first, null!] }),
                "wrong-id" => JsonSerializer.Serialize(record with { PlanId = Guid.NewGuid().ToString("N") }),
                "duplicate-target" => JsonSerializer.Serialize(record with { Files = [first, first] }),
                "wrong-temporary" => JsonSerializer.Serialize(record with { Files = [first,
                    new JournalFile(Path.Combine(root, "other.pth"), "expected", "Intent") { TemporaryPath = Path.Combine(root, "unrelated.tmp") }] }),
                "completed-but-invalid" => JsonSerializer.Serialize(record with { State = "FilesDeployed", Files = null! }),
                _ => throw new ArgumentOutOfRangeException(nameof(variant))
            };
            var bad = Path.Combine(directory, badId + ".json");
            await File.WriteAllTextAsync(bad, invalid);
            var goodId = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(directory, goodId + ".json"),
                JsonSerializer.Serialize(new DeploymentJournal(goodId, "Running", [])));
            var service = new ResourceInstallationService(root);
            var reports = await service.RecoverAsync();
            Assert.Equal(2, reports.Count);
            Assert.Equal("NeedsRepair", Assert.Single(reports, x => x.PlanId == badId).State);
            Assert.Equal("NeedsReview", Assert.Single(reports, x => x.PlanId == goodId).State);
            Assert.Equal(2, (await service.RecoverAsync()).Count);
            Assert.Equal(invalid, await File.ReadAllTextAsync(bad));
            Assert.Equal("preserve-invalid-record-partial", await File.ReadAllTextAsync(partial));
            Assert.False(File.Exists(target));

            var instance = new InstanceDescriptor("fixture", "fixture", "desktop-2", null, root, root, root,
                root, root, root, null, [], root, [], "fixture", []);
            var plan = new ResourceInstallPlan(Guid.NewGuid().ToString("N"), instance, [], [], 0, DateTimeOffset.UtcNow);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteAsync(plan,
                (_, _) => throw new InvalidOperationException("Must not reach environment preparation")));
            Assert.Contains("需要人工修复", error.Message);
            Assert.False(File.Exists(Path.Combine(directory, plan.Id + ".json")));

            await using (var worker = new PersistentWorkerService(new(root))) await worker.InitializeAsync();
            await using (var worker = new PersistentWorkerService(new(root))) await worker.InitializeAsync();
            await using var database = new ResourceLibraryDatabase(root);
            var issue = Assert.Single((await database.LoadJobsAsync()).Select(x => JsonSerializer.Deserialize<WorkerJob>(x)!),
                x => x.Operation == "install.recovery");
            Assert.Equal("NeedsReview", issue.State);
            Assert.Contains(bad, issue.Error);
            Assert.Empty(issue.AvailableActions);
        }
        finally { Directory.Delete(root, true); }
    }
}
