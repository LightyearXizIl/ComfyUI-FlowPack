using System.IO;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PlannedZipExportRecoveryTests
{
    [Theory]
    [InlineData("existing-temporary")]
    [InlineData("same-size-change")]
    [InlineData("size-change")]
    [InlineData("cancelled")]
    public async Task Failed_export_never_commits_output_or_removes_a_preexisting_temporary(string failure)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-export-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "workflow.json");
            await File.WriteAllTextAsync(source, "original");
            var plan = new ExportPlan(Guid.NewGuid().ToString("N"),
                [new(source, "workflows/workflow.json", new FileInfo(source).Length, await ResourceImportService.HashAsync(source))], [], new FileInfo(source).Length);
            var output = Path.Combine(root, "output.zip");
            var temporary = output + "." + plan.Id + ".tmp";
            if (failure == "existing-temporary") await File.WriteAllTextAsync(temporary, "retained interrupted export");
            if (failure == "same-size-change") await File.WriteAllTextAsync(source, "modified");
            if (failure == "size-change") await File.WriteAllTextAsync(source, "different size");
            var service = new PlannedZipExportService();
            if (failure == "cancelled")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(plan, output, token: new CancellationToken(true)));
            else
                await Assert.ThrowsAnyAsync<IOException>(() => service.ExportAsync(plan, output));
            Assert.False(File.Exists(output));
            if (failure == "existing-temporary") Assert.Equal("retained interrupted export", await File.ReadAllTextAsync(temporary));
            else Assert.False(File.Exists(temporary));
            Assert.True(File.Exists(source));
        }
        finally { Directory.Delete(root, true); }
    }
}
