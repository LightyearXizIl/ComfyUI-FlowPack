using System.IO;
using System.Text.Json;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class DeploymentRecoveryCollisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_backup_does_not_overwrite_partial_or_prevent_worker_restart(bool sameBytes)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-recovery-collision-" + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid().ToString("N");
        var target = Path.Combine(root, "models", "model.pth");
        var partial = target + ".flowpack-" + id + ".tmp";
        var backup = Path.Combine(root, "state", "recovered-files", id, "0.tmp");
        var journalRoot = Path.Combine(root, "state", "journal");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!); Directory.CreateDirectory(journalRoot);
        try
        {
            await File.WriteAllTextAsync(partial, "current-partial");
            await File.WriteAllTextAsync(backup, sameBytes ? "current-partial" : "earlier-backup");
            await File.WriteAllTextAsync(Path.Combine(journalRoot, id + ".json"), JsonSerializer.Serialize(
                new DeploymentJournal(id, "Running", [new(target, "expected-hash", "Intent") { TemporaryPath = partial }])));
            var service = new ResourceInstallationService(root);
            var first = Assert.Single(await service.RecoverAsync());
            Assert.Equal("NeedsReview", first.State); Assert.Contains("均保留", first.Error);
            Assert.Equal(backup, Assert.Single(first.Files).RecoveryPath);
            var second = Assert.Single(await service.RecoverAsync());
            Assert.Equal(first.Error, second.Error);
            Assert.Equal("current-partial", await File.ReadAllTextAsync(partial));
            Assert.Equal(sameBytes ? "current-partial" : "earlier-backup", await File.ReadAllTextAsync(backup));
            Assert.False(File.Exists(target));
            await using var worker = new PersistentWorkerService(new(root)); await worker.InitializeAsync();
        }
        finally { Directory.Delete(root, true); }
    }
}
