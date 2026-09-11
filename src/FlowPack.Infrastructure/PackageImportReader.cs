using System.IO.Compression;
using System.Security.Cryptography;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public sealed record ImportedPackage(PackageManifest Manifest, PackageImportKind Kind, string Source)
{
    public IReadOnlyList<WorkflowDocument> Workflows { get; init; } = [];
}
public enum PackageImportKind { OnlineManifest, OfflineArchive, ExpandedDirectory }

/// <summary>
/// Reads package inputs without extracting or deploying them. Offline payload files are
/// verified in place before a caller records the import.
/// </summary>
public sealed class PackageImportReader
{
    private readonly PackageManifestReader _manifestReader = new();

    public async Task<ImportedPackage> ReadAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Directory.Exists(source)) return await ReadDirectoryAsync(source, cancellationToken);
        if (!File.Exists(source)) throw new FileNotFoundException("找不到资源包输入。", source);
        if (source.EndsWith(".cpack", StringComparison.OrdinalIgnoreCase)) return await ReadArchiveAsync(source, cancellationToken);
        return await ReadOnlineManifestAsync(source, cancellationToken);
    }

    private async Task<ImportedPackage> ReadOnlineManifestAsync(string source, CancellationToken cancellationToken)
    {
        var manifest = await _manifestReader.ReadAsync(source, cancellationToken);
        return new ImportedPackage(manifest, PackageImportKind.OnlineManifest, source);
    }

    private async Task<ImportedPackage> ReadArchiveAsync(string source, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(source);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > 10_000) throw new InvalidDataException("离线包条目数量超过安全限制。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            EnsureSafeArchivePath(entry.FullName);
            if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException($"离线包包含重复路径：{entry.FullName}");
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry) || manifestEntry.Length == 0)
        {
            throw new InvalidDataException("离线包根目录必须包含 manifest.json。");
        }
        await using var manifestStream = manifestEntry.Open();
        using var document = await System.Text.Json.JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
        var manifest = _manifestReader.Read(document.RootElement);
        EnsureEntryWorkflowsPresent(manifest, path => entries.TryGetValue(path, out var entry) && !entry.FullName.EndsWith("/", StringComparison.Ordinal));
        foreach (var resource in manifest.Resources.Where(resource => resource.PackagePath is not null))
        {
            if (!entries.TryGetValue(resource.PackagePath!, out var payloadEntry) || payloadEntry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"离线包缺少资源载荷：{resource.PackagePath}");
            }
            await VerifyPayloadAsync(payloadEntry.Length, payloadEntry.Open, resource, cancellationToken);
        }
        var workflows = await ReadArchiveWorkflowsAsync(manifest, entries, cancellationToken);
        return new ImportedPackage(manifest, PackageImportKind.OfflineArchive, source) { Workflows = workflows };
    }

    private async Task<ImportedPackage> ReadDirectoryAsync(string source, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(source);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath)) throw new InvalidDataException("展开目录根目录必须包含 manifest.json。");
        var manifest = await _manifestReader.ReadAsync(manifestPath, cancellationToken);
        EnsureEntryWorkflowsPresent(manifest, path =>
        {
            var workflowPath = Path.GetFullPath(Path.Combine(root, path));
            return IsSameOrDescendant(workflowPath, root) && File.Exists(workflowPath);
        });
        foreach (var resource in manifest.Resources.Where(resource => resource.PackagePath is not null))
        {
            var payloadPath = Path.GetFullPath(Path.Combine(root, resource.PackagePath!));
            if (!IsSameOrDescendant(payloadPath, root) || !File.Exists(payloadPath))
            {
                throw new InvalidDataException($"展开目录缺少资源载荷：{resource.PackagePath}");
            }
            var info = new FileInfo(payloadPath);
            await VerifyPayloadAsync(info.Length, () => File.OpenRead(payloadPath), resource, cancellationToken);
        }
        var workflows = await ReadDirectoryWorkflowsAsync(manifest, root, cancellationToken);
        return new ImportedPackage(manifest, PackageImportKind.ExpandedDirectory, root) { Workflows = workflows };
    }

    private static async Task VerifyPayloadAsync(
        long actualSize,
        Func<Stream> openStream,
        ResourceEntry resource,
        CancellationToken cancellationToken)
    {
        if (actualSize != resource.SizeBytes)
        {
            throw new InvalidDataException($"资源 {resource.Id} 的大小与清单不一致。");
        }
        if (resource.Sha256 is null) return;
        await using var stream = openStream();
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!actualHash.Equals(resource.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"资源 {resource.Id} 的 SHA-256 与清单不一致。");
        }
    }

    private static void EnsureEntryWorkflowsPresent(PackageManifest manifest, Func<string, bool> exists)
    {
        foreach (var workflow in manifest.EntryWorkflows)
        {
            if (!exists(workflow.RelativePath))
            {
                throw new InvalidDataException($"离线包缺少入口工作流：{workflow.RelativePath}");
            }
        }
    }

    private static async Task<IReadOnlyList<WorkflowDocument>> ReadArchiveWorkflowsAsync(
        PackageManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        var workflows = new List<WorkflowDocument>();
        foreach (var entry in manifest.EntryWorkflows)
        {
            await using var stream = entries[entry.RelativePath].Open();
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var rawJson = await reader.ReadToEndAsync(cancellationToken);
            workflows.Add(WorkflowDocumentFactory.Create(entry.Id, Path.GetFileNameWithoutExtension(entry.RelativePath), rawJson));
        }
        return workflows;
    }

    private static async Task<IReadOnlyList<WorkflowDocument>> ReadDirectoryWorkflowsAsync(
        PackageManifest manifest,
        string root,
        CancellationToken cancellationToken)
    {
        var workflows = new List<WorkflowDocument>();
        foreach (var entry in manifest.EntryWorkflows)
        {
            var path = Path.Combine(root, entry.RelativePath);
            var rawJson = await File.ReadAllTextAsync(path, cancellationToken);
            workflows.Add(WorkflowDocumentFactory.Create(entry.Id, Path.GetFileNameWithoutExtension(entry.RelativePath), rawJson));
        }
        return workflows;
    }

    private static void EnsureSafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.StartsWith('\\') ||
            path.Contains(':') || path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"离线包包含不安全路径：{path}");
        }
    }

    private static bool IsSameOrDescendant(string candidate, string parent) =>
        candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
