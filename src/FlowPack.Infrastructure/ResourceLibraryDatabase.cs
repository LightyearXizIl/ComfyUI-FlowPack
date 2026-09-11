using System.Text.Json;
using System.Text.Json.Serialization;
using FlowPack.Core;
using Microsoft.Data.Sqlite;

namespace FlowPack.Infrastructure;

/// <summary>
/// Versioned, user-selected resource-library metadata storage. It records imported
/// manifests but never writes to a ComfyUI instance or changes package payload files.
/// </summary>
public sealed class ResourceLibraryDatabase : IAsyncDisposable
{
    private const int CurrentSchemaVersion = 6;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _initialized;

    public ResourceLibraryDatabase(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        LibraryPath = Path.GetFullPath(libraryPath);
        DatabasePath = Path.Combine(LibraryPath, "state", "flowpack.db");
    }

    public string LibraryPath { get; }
    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var hasSchemaInfo = await TableExistsAsync(connection, "SchemaInfo", cancellationToken);
            if (hasSchemaInfo)
            {
                var version = await GetSchemaVersionAsync(connection, cancellationToken);
                if (version > CurrentSchemaVersion)
                {
                    throw new InvalidDataException("资源库由更新版本的 FlowPack 创建，当前版本只读保护，不能写入。");
                }
            }

            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON; PRAGMA journal_mode = DELETE;", cancellationToken);
            if (hasSchemaInfo)
            {
                var version = await GetSchemaVersionAsync(connection, cancellationToken);
                if (version < CurrentSchemaVersion)
                {
                    await BackupBeforeMigrationAsync(connection, version, cancellationToken);
                }
            }
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS SchemaInfo (
                    name TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS PackageVersions (
                    package_id TEXT NOT NULL,
                    package_version TEXT NOT NULL,
                    manifest_format_version TEXT NOT NULL,
                    package_name TEXT NOT NULL,
                    manifest_json TEXT NOT NULL,
                    source TEXT NOT NULL,
                    imported_at_utc TEXT NOT NULL,
                    PRIMARY KEY (package_id, package_version)
                );
                CREATE TABLE IF NOT EXISTS CandidateInstances (
                    instance_key TEXT NOT NULL PRIMARY KEY,
                    instance_path TEXT NOT NULL,
                    python_path TEXT NOT NULL,
                    user_directory TEXT NOT NULL,
                    server_address TEXT NULL,
                    inspected_at_utc TEXT NOT NULL,
                    selected_at_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Workflows (
                    workflow_id TEXT NOT NULL PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    format TEXT NOT NULL,
                    raw_json TEXT NOT NULL,
                    source TEXT NOT NULL,
                    imported_at_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS PackageDrafts (
                    draft_id TEXT NOT NULL PRIMARY KEY,
                    workflow_id TEXT NULL,
                    name TEXT NOT NULL,
                    version TEXT NOT NULL,
                    description TEXT NOT NULL,
                    author_name TEXT NOT NULL,
                    author_url TEXT NULL,
                    source TEXT NOT NULL,
                    distribution TEXT NOT NULL,
                    current_step INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Tasks (
                    task_id TEXT NOT NULL PRIMARY KEY,
                    kind TEXT NOT NULL,
                    state TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    stage TEXT NOT NULL,
                    completed_bytes INTEGER NULL,
                    total_bytes INTEGER NULL,
                    error_message TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Resources (
                    resource_id TEXT NOT NULL PRIMARY KEY,
                    package_id TEXT NULL,
                    resource_kind TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    sha256 TEXT NULL,
                    size_bytes INTEGER NULL,
                    source_url TEXT NULL,
                    recorded_at_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS PackageReferences (
                    package_id TEXT NOT NULL,
                    package_version TEXT NOT NULL,
                    resource_id TEXT NOT NULL,
                    PRIMARY KEY (package_id, package_version, resource_id)
                );
                CREATE TABLE IF NOT EXISTS InstallRecords (
                    install_id TEXT NOT NULL PRIMARY KEY,
                    plan_id TEXT NOT NULL,
                    target_fingerprint TEXT NOT NULL,
                    state TEXT NOT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    report_json TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS DownloadCache (
                    cache_key TEXT NOT NULL PRIMARY KEY,
                    source_url TEXT NOT NULL,
                    etag TEXT NULL,
                    sha256 TEXT NULL,
                    local_path TEXT NOT NULL,
                    size_bytes INTEGER NULL,
                    updated_at_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS TaskAttempts (
                    task_id TEXT NOT NULL,
                    attempt_number INTEGER NOT NULL,
                    state TEXT NOT NULL,
                    error_code TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    PRIMARY KEY (task_id, attempt_number)
                );
                CREATE TABLE IF NOT EXISTS OperationJournal (
                    operation_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    state TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    PRIMARY KEY (operation_id, sequence)
                );
                CREATE TABLE IF NOT EXISTS Backups (
                    backup_id TEXT NOT NULL PRIMARY KEY,
                    backup_path TEXT NOT NULL,
                    reason TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS VerificationReports (
                    report_id TEXT NOT NULL PRIMARY KEY,
                    plan_id TEXT NULL,
                    level TEXT NOT NULL,
                    passed INTEGER NOT NULL,
                    report_json TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );
                """, cancellationToken);

            if (!hasSchemaInfo)
            {
                await ExecuteAsync(connection, $"INSERT INTO SchemaInfo (name, value) VALUES ('schema_version', '{CurrentSchemaVersion}');", cancellationToken);
            }
            else
            {
                var version = await GetSchemaVersionAsync(connection, cancellationToken);
                if (version < CurrentSchemaVersion)
                {
                    await ExecuteAsync(connection, $"UPDATE SchemaInfo SET value = '{CurrentSchemaVersion}' WHERE name = 'schema_version';", cancellationToken);
                }
            }
            _initialized = true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveImportedPackageAsync(PackageManifest manifest, string source, DateTimeOffset? importedAt = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        await InitializeAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO PackageVersions (
                    package_id, package_version, manifest_format_version, package_name, manifest_json, source, imported_at_utc)
                VALUES ($id, $version, $formatVersion, $name, $manifest, $source, $importedAt)
                ON CONFLICT(package_id, package_version) DO UPDATE SET
                    manifest_format_version = excluded.manifest_format_version,
                    package_name = excluded.package_name,
                    manifest_json = excluded.manifest_json,
                    source = excluded.source,
                    imported_at_utc = excluded.imported_at_utc;
                """;
            command.Parameters.AddWithValue("$id", manifest.Id);
            command.Parameters.AddWithValue("$version", manifest.Version);
            command.Parameters.AddWithValue("$formatVersion", manifest.FormatVersion);
            command.Parameters.AddWithValue("$name", manifest.Name);
            command.Parameters.AddWithValue("$manifest", JsonSerializer.Serialize(manifest, SerializerOptions));
            command.Parameters.AddWithValue("$source", source);
            command.Parameters.AddWithValue("$importedAt", (importedAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<StoredPackageManifest>> LoadImportedPackagesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT manifest_json, source, imported_at_utc
            FROM PackageVersions
            ORDER BY imported_at_utc DESC, package_id COLLATE NOCASE, package_version COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var packages = new List<StoredPackageManifest>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var manifest = JsonSerializer.Deserialize<PackageManifest>(reader.GetString(0), SerializerOptions)
                ?? throw new InvalidDataException("资源库包含无法读取的资源包记录。");
            packages.Add(new StoredPackageManifest(manifest, reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return packages;
    }

    public async Task SaveCandidateInstanceAsync(
        InstanceFingerprint instance,
        DateTimeOffset? selectedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        await InitializeAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO CandidateInstances (
                    instance_key, instance_path, python_path, user_directory, server_address, inspected_at_utc, selected_at_utc)
                VALUES ($key, $instancePath, $pythonPath, $userDirectory, $serverAddress, $inspectedAt, $selectedAt)
                ON CONFLICT(instance_key) DO UPDATE SET
                    instance_path = excluded.instance_path,
                    python_path = excluded.python_path,
                    user_directory = excluded.user_directory,
                    server_address = excluded.server_address,
                    inspected_at_utc = excluded.inspected_at_utc,
                    selected_at_utc = excluded.selected_at_utc;
                """;
            command.Parameters.AddWithValue("$key", CreateInstanceKey(instance.InstancePath));
            command.Parameters.AddWithValue("$instancePath", instance.InstancePath);
            command.Parameters.AddWithValue("$pythonPath", instance.PythonPath);
            command.Parameters.AddWithValue("$userDirectory", instance.UserDirectory);
            command.Parameters.AddWithValue("$serverAddress", (object?)instance.ServerAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("$inspectedAt", instance.InspectedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$selectedAt", (selectedAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<StoredCandidateInstance>> LoadCandidateInstancesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT instance_path, python_path, user_directory, server_address, inspected_at_utc, selected_at_utc
            FROM CandidateInstances
            ORDER BY selected_at_utc DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var instances = new List<StoredCandidateInstance>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var instance = new InstanceFingerprint(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind));
            instances.Add(new StoredCandidateInstance(
                instance,
                DateTimeOffset.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return instances;
    }

    public async Task SaveWorkflowAsync(
        WorkflowDocument workflow,
        string source,
        DateTimeOffset? importedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        await InitializeAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Workflows (workflow_id, display_name, format, raw_json, source, imported_at_utc)
                VALUES ($id, $displayName, $format, $rawJson, $source, $importedAt)
                ON CONFLICT(workflow_id) DO UPDATE SET
                    display_name = excluded.display_name,
                    format = excluded.format,
                    raw_json = excluded.raw_json,
                    source = excluded.source,
                    imported_at_utc = excluded.imported_at_utc;
                """;
            command.Parameters.AddWithValue("$id", workflow.Id);
            command.Parameters.AddWithValue("$displayName", workflow.DisplayName);
            command.Parameters.AddWithValue("$format", workflow.Format.ToString());
            command.Parameters.AddWithValue("$rawJson", workflow.RawJson);
            command.Parameters.AddWithValue("$source", source);
            command.Parameters.AddWithValue("$importedAt", (importedAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<StoredWorkflow>> LoadWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT workflow_id, display_name, format, raw_json, source, imported_at_utc
            FROM Workflows
            ORDER BY imported_at_utc DESC, display_name COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var workflows = new List<StoredWorkflow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<WorkflowFormat>(reader.GetString(2), out var format))
            {
                throw new InvalidDataException("资源库包含无法识别的工作流格式。");
            }
            workflows.Add(new StoredWorkflow(
                new WorkflowDocument(reader.GetString(0), reader.GetString(1), format, reader.GetString(3)),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return workflows;
    }

    public async Task SaveDraftAsync(PackageDraft draft, DateTimeOffset? updatedAt = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.CurrentStep is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(draft), "草稿步骤必须在 1 到 4 之间。");
        await InitializeAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO PackageDrafts (
                    draft_id, workflow_id, name, version, description, author_name, author_url, source, distribution, current_step, updated_at_utc)
                VALUES ($id, $workflowId, $name, $version, $description, $authorName, $authorUrl, $source, $distribution, $currentStep, $updatedAt)
                ON CONFLICT(draft_id) DO UPDATE SET
                    workflow_id = excluded.workflow_id,
                    name = excluded.name,
                    version = excluded.version,
                    description = excluded.description,
                    author_name = excluded.author_name,
                    author_url = excluded.author_url,
                    source = excluded.source,
                    distribution = excluded.distribution,
                    current_step = excluded.current_step,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$id", draft.Id);
            command.Parameters.AddWithValue("$workflowId", (object?)draft.WorkflowId ?? DBNull.Value);
            command.Parameters.AddWithValue("$name", draft.Name);
            command.Parameters.AddWithValue("$version", draft.Version);
            command.Parameters.AddWithValue("$description", draft.Description);
            command.Parameters.AddWithValue("$authorName", draft.AuthorName);
            command.Parameters.AddWithValue("$authorUrl", (object?)draft.AuthorUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$source", draft.Source);
            command.Parameters.AddWithValue("$distribution", draft.Distribution.ToString());
            command.Parameters.AddWithValue("$currentStep", draft.CurrentStep);
            command.Parameters.AddWithValue("$updatedAt", (updatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<StoredPackageDraft>> LoadDraftsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT draft_id, workflow_id, name, version, description, author_name, author_url, source, distribution, current_step, updated_at_utc
            FROM PackageDrafts
            ORDER BY updated_at_utc DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var drafts = new List<StoredPackageDraft>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<DistributionDeclaration>(reader.GetString(8), out var distribution))
            {
                throw new InvalidDataException("资源库包含无法识别的草稿分发方式。");
            }
            drafts.Add(new StoredPackageDraft(
                new PackageDraft(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7),
                    distribution,
                    reader.GetInt32(9)),
                DateTimeOffset.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return drafts;
    }

    public async Task SaveTaskAsync(WorkerTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(task.Id) || string.IsNullOrWhiteSpace(task.Summary) || string.IsNullOrWhiteSpace(task.Stage))
        {
            throw new ArgumentException("任务必须包含 ID、摘要和阶段。", nameof(task));
        }
        if (task.CompletedBytes is < 0 || task.TotalBytes is < 0 ||
            (task.CompletedBytes is not null && task.TotalBytes is not null && task.CompletedBytes > task.TotalBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(task), "任务字节进度无效。");
        }

        await InitializeAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Tasks (task_id, kind, state, summary, stage, completed_bytes, total_bytes, error_message, created_at_utc, updated_at_utc)
                VALUES ($id, $kind, $state, $summary, $stage, $completed, $total, $error, $createdAt, $updatedAt)
                ON CONFLICT(task_id) DO UPDATE SET
                    kind = excluded.kind,
                    state = excluded.state,
                    summary = excluded.summary,
                    stage = excluded.stage,
                    completed_bytes = excluded.completed_bytes,
                    total_bytes = excluded.total_bytes,
                    error_message = excluded.error_message,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$id", task.Id);
            command.Parameters.AddWithValue("$kind", task.Kind.ToString());
            command.Parameters.AddWithValue("$state", task.State.ToString());
            command.Parameters.AddWithValue("$summary", task.Summary);
            command.Parameters.AddWithValue("$stage", task.Stage);
            command.Parameters.AddWithValue("$completed", (object?)task.CompletedBytes ?? DBNull.Value);
            command.Parameters.AddWithValue("$total", (object?)task.TotalBytes ?? DBNull.Value);
            command.Parameters.AddWithValue("$error", (object?)task.ErrorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", task.UpdatedAt.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<WorkerTask>> LoadTasksAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task_id, kind, state, summary, stage, completed_bytes, total_bytes, error_message, created_at_utc, updated_at_utc
            FROM Tasks
            ORDER BY updated_at_utc DESC, task_id COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tasks = new List<WorkerTask>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<WorkerTaskKind>(reader.GetString(1), out var kind) ||
                !Enum.TryParse<WorkerTaskState>(reader.GetString(2), out var state))
            {
                throw new InvalidDataException("资源库包含无法识别的任务类型或状态。");
            }
            tasks.Add(new WorkerTask(
                reader.GetString(0), kind, state, reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                DateTimeOffset.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse(reader.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return tasks;
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task BackupBeforeMigrationAsync(SqliteConnection connection, int previousVersion, CancellationToken cancellationToken)
    {
        var backupDirectory = Path.Combine(LibraryPath, "state", "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"flowpack-v{previousVersion}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.db");
        await using var backupCommand = connection.CreateCommand();
        backupCommand.CommandText = "VACUUM INTO $backupPath;";
        backupCommand.Parameters.AddWithValue("$backupPath", backupPath);
        await backupCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM SchemaInfo WHERE name = 'schema_version';";
        var versionValue = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (!int.TryParse(versionValue, out var version) || version < 1)
        {
            throw new InvalidDataException("资源库的架构版本无效，不能写入。");
        }
        return version;
    }

    private static string CreateInstanceKey(string instancePath)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(instancePath).ToUpperInvariant()));
        return Convert.ToHexString(bytes);
    }
}

public sealed record StoredPackageManifest(PackageManifest Manifest, string Source, DateTimeOffset ImportedAt);
public sealed record StoredCandidateInstance(InstanceFingerprint Instance, DateTimeOffset SelectedAt);
public sealed record StoredWorkflow(WorkflowDocument Workflow, string Source, DateTimeOffset ImportedAt);
public sealed record StoredPackageDraft(PackageDraft Draft, DateTimeOffset UpdatedAt);
