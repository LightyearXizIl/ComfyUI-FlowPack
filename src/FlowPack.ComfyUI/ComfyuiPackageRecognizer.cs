using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

/// <summary>
/// Read-only recognition of a ComfyUI native package (zip archive or expanded directory) from its
/// on-disk layout alone. It does NOT require a manifest.json, so it can recognize packages authored
/// by other tools or downloaded from the internet (requirement 3: recognition must not be limited to
/// FlowPack's own .cpack output).
///
/// Recognized layout:
///   workflows/*.json        -> workflow documents (node types + referenced model filenames extracted)
///   custom_nodes/&lt;name&gt;/...   -> custom node directories
///   models/&lt;category&gt;/...      -> model files (category = first sub-directory)
///   manifest.json (optional) -> reported via HasManifest; ignored if absent
///
/// Routing note: .cpack inputs are the self-packaged, manifest-driven format and are handled by
/// FlowPack.Infrastructure.PackageImportReader. This recognizer returns null for .cpack so the caller
/// can fall back to that reader. If the source contains none of the native markers above, recognition
/// returns null (not a ComfyUI native package) so other importers may attempt it.
/// </summary>
public sealed record RecognizedWorkflow(
    WorkflowDocument Document,
    IReadOnlyList<string> NodeTypes,
    IReadOnlyList<string> ReferencedModelFileNames)
{
    public string Id => Document.Id;
    public string DisplayName => Document.DisplayName;
    public WorkflowFormat Format => Document.Format;
}

public sealed record RecognizedCustomNode(string DirectoryName, string RelativePath);

public sealed record RecognizedModelFile(string RelativePath, string Category, long SizeBytes);

public sealed record RecognizedPackage(
    string Source,
    PackageRecognitionKind Kind,
    IReadOnlyList<RecognizedWorkflow> Workflows,
    IReadOnlyList<RecognizedCustomNode> CustomNodes,
    IReadOnlyList<RecognizedModelFile> ModelFiles,
    bool HasManifest,
    DateTimeOffset InspectedAt)
{
    public bool IsEmpty => Workflows.Count == 0 && CustomNodes.Count == 0 && ModelFiles.Count == 0;
}

public enum PackageRecognitionKind { ZipArchive, ExpandedDirectory }

public interface IComfyuiPackageRecognizer
{
    Task<RecognizedPackage?> RecognizeAsync(string source, CancellationToken cancellationToken = default);
}

public sealed class ComfyuiPackageRecognizer : IComfyuiPackageRecognizer
{
    public Task<RecognizedPackage?> RecognizeAsync(string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        // Self-packaged .cpack is manifest-driven and handled by PackageImportReader.
        if (source.EndsWith(".cpack", StringComparison.OrdinalIgnoreCase)) return Task.FromResult<RecognizedPackage?>(null);

        if (Directory.Exists(source)) return RecognizeDirectoryAsync(source, cancellationToken);
        if (!File.Exists(source)) throw new FileNotFoundException("找不到待识别的包。", source);
        return RecognizeZipAsync(source, cancellationToken);
    }

    private async Task<RecognizedPackage?> RecognizeDirectoryAsync(string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(source);

        var workflowsDir = Path.Combine(root, "workflows");
        var modelsDir = Path.Combine(root, "models");
        var nodesDir = Path.Combine(root, "custom_nodes");

        var workflows = new List<RecognizedWorkflow>();
        if (Directory.Exists(workflowsDir))
        {
            foreach (var file in Directory.EnumerateFiles(workflowsDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recognized = await TryReadWorkflowFileAsync(file, cancellationToken);
                if (recognized is not null) workflows.Add(recognized);
            }
        }

        var customNodes = Directory.Exists(nodesDir)
            ? Directory.EnumerateDirectories(nodesDir)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => new RecognizedCustomNode(name!, Path.Combine("custom_nodes", name!).Replace('\\', '/')))
                .ToList()
            : new List<RecognizedCustomNode>();

        var modelFiles = new List<RecognizedModelFile>();
        if (Directory.Exists(modelsDir))
        {
            foreach (var file in Directory.EnumerateFiles(modelsDir, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = NormalizeSlashes(Path.GetRelativePath(root, file));
                var category = ModelCategoryFromRelative(relative);
                modelFiles.Add(new RecognizedModelFile(relative, category, new FileInfo(file).Length));
            }
        }

        var hasManifest = File.Exists(Path.Combine(root, "manifest.json"));
        return BuildOrNull(source, PackageRecognitionKind.ExpandedDirectory, workflows, customNodes, modelFiles, hasManifest);
    }

    private async Task<RecognizedPackage?> RecognizeZipAsync(string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = File.OpenRead(source);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > 10_000) throw new InvalidDataException("离线包条目数量超过安全限制。");

        var workflows = new List<RecognizedWorkflow>();
        var customNodeNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var modelFiles = new List<RecognizedModelFile>();
        var hasManifest = false;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeArchivePath(entry.FullName);
            var full = entry.FullName;
            if (full.Length == 0 || full.EndsWith("/", StringComparison.Ordinal) || full.EndsWith("\\", StringComparison.Ordinal))
            {
                continue; // directory placeholder entry
            }

            if (full.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                hasManifest = true;
                continue;
            }

            if (MatchesPrefix(full, "workflows/") && full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                await using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                var rawJson = await reader.ReadToEndAsync(cancellationToken);
                var recognized = TryBuildRecognizedWorkflow(rawJson, Path.GetFileNameWithoutExtension(full));
                if (recognized is not null) workflows.Add(recognized);
            }
            else if (MatchesPrefix(full, "custom_nodes/"))
            {
                var dirName = TopSegmentAfter(full, "custom_nodes/");
                if (dirName is not null) customNodeNames.Add(dirName);
            }
            else if (MatchesPrefix(full, "models/"))
            {
                var category = SegmentAfter(full, "models/", 0);
                modelFiles.Add(new RecognizedModelFile(NormalizeSlashes(full), category ?? "models", entry.Length));
            }
        }

        var customNodes = customNodeNames
            .Select(name => new RecognizedCustomNode(name, "custom_nodes/" + name))
            .ToList();
        return BuildOrNull(source, PackageRecognitionKind.ZipArchive, workflows, customNodes, modelFiles, hasManifest);
    }

    private static RecognizedPackage? BuildOrNull(
        string source,
        PackageRecognitionKind kind,
        List<RecognizedWorkflow> workflows,
        List<RecognizedCustomNode> customNodes,
        List<RecognizedModelFile> modelFiles,
        bool hasManifest)
    {
        if (workflows.Count == 0 && customNodes.Count == 0 && modelFiles.Count == 0) return null;
        return new RecognizedPackage(source, kind, workflows, customNodes, modelFiles, hasManifest, DateTimeOffset.UtcNow);
    }

    private static async Task<RecognizedWorkflow?> TryReadWorkflowFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var rawJson = await File.ReadAllTextAsync(path, cancellationToken);
            return TryBuildRecognizedWorkflow(rawJson, Path.GetFileNameWithoutExtension(path));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static RecognizedWorkflow? TryBuildRecognizedWorkflow(string rawJson, string displayName)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;
        WorkflowDocument document;
        try
        {
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));
            document = WorkflowDocumentFactory.Create(id, displayName, rawJson);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var set = WorkflowRequirementParser.Analyze(rawJson);
        return new RecognizedWorkflow(document, set.NodeTypes, set.ModelReferences.Select(m => m.Reference).ToList());
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/');

    private static bool MatchesPrefix(string full, string prefix) =>
        full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && full.Length > prefix.Length;

    private static string? TopSegmentAfter(string full, string prefix)
    {
        var remainder = full.Substring(prefix.Length);
        var slash = remainder.IndexOf('/');
        if (slash < 0) return null; // file directly under prefix, not a directory
        var segment = remainder.Substring(0, slash);
        return string.IsNullOrEmpty(segment) ? null : segment;
    }

    private static string? SegmentAfter(string full, string prefix, int index)
    {
        var remainder = full.Substring(prefix.Length);
        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return index < segments.Length ? segments[index] : null;
    }

    private static string ModelCategoryFromRelative(string relative)
    {
        // relative looks like "models/<category>/<file>" or "models/<file>"
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3 ? segments[1] : "models";
    }

    private static void EnsureSafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.StartsWith('\\') ||
            path.Contains(':') || path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"离线包包含不安全路径：{path}");
        }
    }
}
