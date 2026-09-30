using System.IO;
using System.IO.Compression;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class LargeArchiveFactAttribute : FactAttribute
{
    public LargeArchiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FLOWPACK_TEST_ZIP64") != "1")
            Skip = "Opt in with FLOWPACK_TEST_ZIP64=1 and an explicit FLOWPACK_LARGE_TEST_ROOT (requires 13 GiB free).";
    }
}

public sealed class Zip64RoundtripTests
{
    [LargeArchiveFact]
    public async Task Actual_archive_over_four_GiB_exports_and_imports_with_matching_hash()
    {
        var parent = Environment.GetEnvironmentVariable("FLOWPACK_LARGE_TEST_ROOT") ?? throw new InvalidOperationException("Explicit test output root required.");
        var root = Path.Combine(Path.GetFullPath(parent), "zip64-" + Guid.NewGuid().ToString("N"));
        var required = 13L * 1024 * 1024 * 1024;
        Assert.True(new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace > required);
        Directory.CreateDirectory(root);
        try
        {
            const long size = (1L << 32) + 131072;
            var model = Path.Combine(root, "large.safetensors");
            await using (var stream = new FileStream(model, FileMode.CreateNew, FileAccess.Write))
            {
                stream.SetLength(size); stream.Position = size - 4;
                await stream.WriteAsync(new byte[] { 1, 2, 3, 4 });
            }
            var exporter = new PlannedZipExportService();
            var plan = await exporter.PlanAsync([new("large", ResourceKind.Model, "large", model, "checkpoints/large.safetensors")]);
            var archive = Path.Combine(root, "large.zip"); await exporter.ExportAsync(plan, archive);
            Assert.True(new FileInfo(archive).Length > uint.MaxValue);
            using (var zip = ZipFile.OpenRead(archive)) Assert.Equal(size, zip.GetEntry("models/checkpoints/large.safetensors")!.Length);
            var imported = await new ResourceImportService().ImportAsync(archive, Path.Combine(root, "staging"));
            var item = Assert.Single(imported.Resources);
            Assert.Equal(size, item.SizeBytes); Assert.Equal(plan.Files[0].Sha256, item.Sha256);
            Console.WriteLine($"ZIP64 verified: source={size}, archive={new FileInfo(archive).Length}, sha256={item.Sha256}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
