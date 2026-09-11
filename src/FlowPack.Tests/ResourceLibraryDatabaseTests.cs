using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;
using Microsoft.Data.Sqlite;

namespace FlowPack.Tests;

public sealed class ResourceLibraryDatabaseTests : IDisposable
{
    private readonly string _libraryPath = Path.Combine(Path.GetTempPath(), $"FlowPack.LibraryTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Imported_package_versions_survive_a_database_reopen()
    {
        var manifest = new PackageManifest("author.example-pack", "示例资源包", "1.2.0", [
            new ResourceEntry("model", "model.safetensors", ResourceKind.Model, 1024, "AABB", "https://example.invalid/model")]);
        var importedAt = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

        await using (var database = new ResourceLibraryDatabase(_libraryPath))
        {
            await database.SaveImportedPackageAsync(manifest, "C:\\packages\\example.cpack.json", importedAt);
        }

        await using var reopened = new ResourceLibraryDatabase(_libraryPath);
        var packages = await reopened.LoadImportedPackagesAsync();

        var stored = Assert.Single(packages);
        Assert.Equal(manifest.Id, stored.Manifest.Id);
        Assert.Equal(manifest.Name, stored.Manifest.Name);
        Assert.Equal(manifest.Version, stored.Manifest.Version);
        Assert.Equal(manifest.FormatVersion, stored.Manifest.FormatVersion);
        var expectedResource = Assert.Single(manifest.Resources);
        var storedResource = Assert.Single(stored.Manifest.Resources);
        Assert.Equal(expectedResource.Id, storedResource.Id);
        Assert.Equal(expectedResource.Name, storedResource.Name);
        Assert.Equal(expectedResource.Kind, storedResource.Kind);
        Assert.Equal(expectedResource.SizeBytes, storedResource.SizeBytes);
        Assert.Equal(expectedResource.Sha256, storedResource.Sha256);
        Assert.Equal(expectedResource.SourceUrl, storedResource.SourceUrl);
        Assert.Equal("C:\\packages\\example.cpack.json", stored.Source);
        Assert.Equal(importedAt, stored.ImportedAt);
        Assert.True(File.Exists(reopened.DatabasePath));
    }

    [Fact]
    public void Sibling_path_with_a_shared_prefix_is_not_rejected_as_an_app_child()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack");
        var sibling = Path.Combine(Path.GetTempPath(), "FlowPackLibrary");

        Assert.True(ResourceLibraryPathValidator.IsSafeLibraryPath(sibling, root, null, out var reason));
        Assert.Equal(string.Empty, reason);
        Assert.False(ResourceLibraryPathValidator.IsSafeLibraryPath(Path.Combine(root, "library"), root, null, out _));
    }

    [Fact]
    public async Task Candidate_instance_survives_a_database_reopen_without_becoming_verified()
    {
        var candidate = new InstanceFingerprint(
            "C:\\isolated\\ComfyUI",
            "C:\\isolated\\ComfyUI\\venv\\Scripts\\python.exe",
            "C:\\isolated\\ComfyUI\\user",
            null,
            new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero));
        var selectedAt = new DateTimeOffset(2026, 9, 11, 10, 1, 0, TimeSpan.Zero);

        await using (var database = new ResourceLibraryDatabase(_libraryPath))
        {
            await database.SaveCandidateInstanceAsync(candidate, selectedAt);
        }

        await using var reopened = new ResourceLibraryDatabase(_libraryPath);
        var stored = Assert.Single(await reopened.LoadCandidateInstancesAsync());
        Assert.Equal(candidate, stored.Instance);
        Assert.Equal(selectedAt, stored.SelectedAt);
    }

    [Fact]
    public async Task Imported_workflow_preserves_the_raw_json_across_a_database_reopen()
    {
        const string rawJson = """{ "version": "1.0", "nodes": [], "unknown_field": "preserve-me" }""";
        var workflow = new WorkflowDocument("workflow-id", "我的工作流", WorkflowFormat.UiV10, rawJson);
        var importedAt = new DateTimeOffset(2026, 9, 11, 11, 0, 0, TimeSpan.Zero);

        await using (var database = new ResourceLibraryDatabase(_libraryPath))
        {
            await database.SaveWorkflowAsync(workflow, "C:\\workflows\\mine.json", importedAt);
        }

        await using var reopened = new ResourceLibraryDatabase(_libraryPath);
        var stored = Assert.Single(await reopened.LoadWorkflowsAsync());
        Assert.Equal(workflow, stored.Workflow);
        Assert.Equal("C:\\workflows\\mine.json", stored.Source);
        Assert.Equal(importedAt, stored.ImportedAt);
    }

    [Fact]
    public async Task Package_draft_survives_a_database_reopen()
    {
        var draft = new PackageDraft(
            "draft-id",
            "workflow-id",
            "肖像包",
            "0.1.0",
            "待补齐资源。",
            "LightyearXizIl",
            "https://github.com/LightyearXizIl",
            "https://example.invalid/portrait",
            DistributionDeclaration.OnlineOnly,
            3);
        var updatedAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        await using (var database = new ResourceLibraryDatabase(_libraryPath))
        {
            await database.SaveDraftAsync(draft, updatedAt);
        }

        await using var reopened = new ResourceLibraryDatabase(_libraryPath);
        var stored = Assert.Single(await reopened.LoadDraftsAsync());
        Assert.Equal(draft, stored.Draft);
        Assert.Equal(updatedAt, stored.UpdatedAt);
    }

    [Fact]
    public async Task Future_schema_is_rejected_before_missing_current_tables_are_created()
    {
        var databasePath = Path.Combine(_libraryPath, "state", "flowpack.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE SchemaInfo (name TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO SchemaInfo (name, value) VALUES ('schema_version', '6');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var database = new ResourceLibraryDatabase(_libraryPath);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => database.InitializeAsync());

        Assert.Contains("更新版本", exception.Message);
        await using (var verificationConnection = new SqliteConnection(connectionString))
        {
            await verificationConnection.OpenAsync();
            await using var verificationCommand = verificationConnection.CreateCommand();
            verificationCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'PackageDrafts';";
            Assert.Null(await verificationCommand.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task Worker_task_survives_a_database_reopen_without_inventing_byte_progress()
    {
        var task = new WorkerTask(
            "task-id", WorkerTaskKind.Download, WorkerTaskState.Running, "下载模型", "正在下载",
            null, null, null,
            new DateTimeOffset(2026, 9, 11, 13, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 11, 13, 1, 0, TimeSpan.Zero));

        await using (var database = new ResourceLibraryDatabase(_libraryPath))
        {
            await database.SaveTaskAsync(task);
        }

        await using var reopened = new ResourceLibraryDatabase(_libraryPath);
        var stored = Assert.Single(await reopened.LoadTasksAsync());
        Assert.Equal(task, stored);
        Assert.Null(stored.CompletedBytes);
        Assert.Null(stored.TotalBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_libraryPath)) Directory.Delete(_libraryPath, recursive: true);
    }
}
