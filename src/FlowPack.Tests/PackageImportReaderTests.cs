using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PackageImportReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"FlowPack.PackageImport.{Guid.NewGuid():N}");

    [Fact]
    public async Task Offline_archive_verifies_the_payload_before_returning_a_manifest()
    {
        Directory.CreateDirectory(_directory);
        var payload = Encoding.UTF8.GetBytes("verified payload");
        var archivePath = Path.Combine(_directory, "sample.cpack");
        CreateArchive(archivePath, payload, Hash(payload));

        var imported = await new PackageImportReader().ReadAsync(archivePath);

        Assert.Equal(PackageImportKind.OfflineArchive, imported.Kind);
        Assert.True(imported.Manifest.IsComplete);
        Assert.Equal("payload/model.bin", Assert.Single(imported.Manifest.Resources).PackagePath);
        var workflow = Assert.Single(imported.Workflows);
        Assert.Equal("main", workflow.Id);
        Assert.Equal(WorkflowFormat.UiV10, workflow.Format);
    }

    [Fact]
    public async Task Hash_mismatch_in_an_offline_archive_is_rejected()
    {
        Directory.CreateDirectory(_directory);
        var archivePath = Path.Combine(_directory, "corrupt.cpack");
        CreateArchive(archivePath, Encoding.UTF8.GetBytes("actual"), new string('0', 64));

        await Assert.ThrowsAsync<InvalidDataException>(() => new PackageImportReader().ReadAsync(archivePath));
    }

    [Fact]
    public async Task Offline_archive_missing_a_declared_entry_workflow_is_rejected()
    {
        Directory.CreateDirectory(_directory);
        var payload = Encoding.UTF8.GetBytes("payload");
        var archivePath = Path.Combine(_directory, "missing-workflow.cpack");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var manifest = archive.CreateEntry("manifest.json");
            await using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8, leaveOpen: false))
            {
                await writer.WriteAsync(CreateManifest(payload.Length, Hash(payload)));
            }
            var resource = archive.CreateEntry("payload/model.bin", CompressionLevel.NoCompression);
            await using var stream = resource.Open();
            await stream.WriteAsync(payload);
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PackageImportReader().ReadAsync(archivePath));

        Assert.Contains("入口工作流", error.Message);
    }

    [Fact]
    public async Task Expanded_directory_verifies_the_payload_before_returning_a_manifest()
    {
        var payload = Encoding.UTF8.GetBytes("directory payload");
        var payloadPath = Path.Combine(_directory, "payload", "model.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        await File.WriteAllBytesAsync(payloadPath, payload);
        var workflowPath = Path.Combine(_directory, "payload", "workflows", "main.json");
        Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
        await File.WriteAllTextAsync(workflowPath, "{\"version\":\"1.0\",\"nodes\":[]}");
        await File.WriteAllTextAsync(Path.Combine(_directory, "manifest.json"), CreateManifest(payload.Length, Hash(payload)));

        var imported = await new PackageImportReader().ReadAsync(_directory);

        Assert.Equal(PackageImportKind.ExpandedDirectory, imported.Kind);
        Assert.Equal("offline.example", imported.Manifest.Id);
        Assert.Equal("main", Assert.Single(imported.Workflows).Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static void CreateArchive(string archivePath, byte[] payload, string hash)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var manifest = archive.CreateEntry("manifest.json");
        using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(CreateManifest(payload.Length, hash));
        }
        var resource = archive.CreateEntry("payload/model.bin", CompressionLevel.NoCompression);
        using (var payloadStream = resource.Open())
        {
            payloadStream.Write(payload);
        }
        var workflow = archive.CreateEntry("payload/workflows/main.json", CompressionLevel.Optimal);
        using var workflowWriter = new StreamWriter(workflow.Open(), Encoding.UTF8, leaveOpen: false);
        workflowWriter.Write("{\"version\":\"1.0\",\"nodes\":[]}");
    }

    private static string CreateManifest(long size, string hash) => $$"""
        {
          "formatVersion": "1",
          "id": "offline.example",
          "name": "离线示例",
          "version": "1.0.0",
          "author": { "name": "LightyearXizIl", "url": "https://github.com/LightyearXizIl" },
          "source": "https://example.invalid/offline.example",
          "distribution": "OfflineComplete",
          "entryWorkflows": [{ "id": "main", "path": "payload/workflows/main.json" }],
          "resources": [{
            "id": "model",
            "name": "model.bin",
            "kind": "Model",
            "sizeBytes": {{size}},
            "sha256": "{{hash}}",
            "packagePath": "payload/model.bin",
            "distribution": "OfflineComplete"
          }]
        }
        """;

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
}
