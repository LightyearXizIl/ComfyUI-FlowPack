using System.IO;
using System.IO.Compression;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>
/// Builds portable archives in the native ComfyUI layout (workflows/*.json, models/&lt;category&gt;/&lt;file&gt;,
/// custom_nodes/&lt;name&gt;/). This is the universal "zip" the user asked for (requirement 2: "package installed
/// workflows/models/nodes as a zip"; requirement 7: export each asset kind individually) and it round-trips
/// through <see cref="ComfyuiPackageRecognizer"/> (phase B) — so a package we export is recognized by the very
/// same detector that must accept third-party zips (requirement 3: "must not only recognize our own package").
///
/// It deliberately does NOT emit the .cpack manifest format; <see cref="WorkflowPackageExportService"/> keeps
/// owning that richer self-describing export. Keeping the two formats separate avoids entangling manifest
/// metadata with native file copies.
/// </summary>
public sealed class ResourcePackageExportService
{
    private const string WorkflowDir = "workflows";
    private const string ModelsDir = "models";
    private const string CustomNodesDir = "custom_nodes";

    // ---- Requirement 7: individual exports ------------------------------------------------

    /// <summary>Exports the selected workflows as workflows/&lt;name&gt;.json entries only (no dependencies).</summary>
    public async Task<ResourcePackageResult> ExportWorkflowsAsync(
        string outputPath,
        IReadOnlyList<WorkflowDocument> workflows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        ValidateOut(outputPath: ref outputPath, extension: ".zip");

        return await BuildArchiveAsync(outputPath, async archive =>
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bundled = new List<BundledModel>();
            var bundledNodes = new List<BundledNode>();
            for (var i = 0; i < workflows.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var doc = workflows[i];
                var entryPath = WorkflowDir + "/" + UniqueName(used, SanitizeWorkflowName(doc.DisplayName, i));
                await WriteTextEntryAsync(archive, entryPath, doc.RawJson, cancellationToken);
            }
            return new ResourcePackageResult(outputPath, workflows.Count, bundled, bundledNodes, [], []);
        }, cancellationToken);
    }

    /// <summary>Exports individual model files into models/&lt;category&gt;/&lt;file&gt; (requirement 7).</summary>
    public async Task<ResourcePackageResult> ExportModelsAsync(
        string outputPath,
        IReadOnlyList<ModelFileRef> models,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(models);
        ValidateOut(outputPath: ref outputPath, extension: ".zip");

        return await BuildArchiveAsync(outputPath, async archive =>
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bundled = new List<BundledModel>();
            for (var i = 0; i < models.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = models[i];
                var fileName = SanitizeFileName(Path.GetFileName(model.SourcePath), i);
                var entryPath = ModelsDir + "/" + model.Category + "/" + UniqueName(used, fileName);
                await WriteFileFromDiskAsync(archive, entryPath, model.SourcePath, CompressionLevel.NoCompression, cancellationToken);
                bundled.Add(new BundledModel(fileName, model.Category, model.SourcePath, entryPath));
            }
            return new ResourcePackageResult(outputPath, 0, bundled, [], [], []);
        }, cancellationToken);
    }

    /// <summary>Exports individual custom-node directories into custom_nodes/&lt;name&gt;/ (requirement 7).</summary>
    public async Task<ResourcePackageResult> ExportNodesAsync(
        string outputPath,
        IReadOnlyList<NodeDirRef> nodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ValidateOut(outputPath: ref outputPath, extension: ".zip");

        return await BuildArchiveAsync(outputPath, async archive =>
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bundled = new List<BundledNode>();
            for (var i = 0; i < nodes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = nodes[i];
                var name = UniqueName(used, SanitizeFileName(new DirectoryInfo(node.SourcePath).Name, i));
                var entryDir = CustomNodesDir + "/" + name;
                await CopyDirToArchiveAsync(archive, entryDir, node.SourcePath, cancellationToken);
                bundled.Add(new BundledNode(name, node.SourcePath, entryDir));
            }
            return new ResourcePackageResult(outputPath, 0, [], bundled, [], []);
        }, cancellationToken);
    }

    // ---- Requirements 2 + 6: bundle workflows with their resolved dependencies -----------

    /// <summary>
    /// Packages the selected workflows, and — when requested — copies the resolved model/node files that are
    /// actually present in the local ComfyUI Desktop instance. Missing dependencies (not on disk) are reported
    /// in <see cref="ResourcePackageResult.MissingModels"/> / <see cref="ResourcePackageResult.MissingNodes"/>
    /// rather than fabricated, so the UI can offer a download (requirement 8 continuity).
    /// </summary>
    public async Task<ResourcePackageResult> ExportBundleAsync(
        string outputPath,
        ExportBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Workflows);
        ValidateOut(outputPath: ref outputPath, extension: ".zip");

        var analyzer = new WorkflowDependencyAnalyzer();
        var dependencies = request.Workflows
            .Select(w => analyzer.AnalyzeRaw(w.RawJson, w.DisplayName))
            .ToList();

        var resolution = await new DependencyResolver(request.Registry).ResolveAsync(request.LocalSource, dependencies, cancellationToken);

        return await BuildArchiveAsync(outputPath, async archive =>
        {
            var usedWorkflow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryPath = WorkflowDir + "/" + UniqueName(usedWorkflow, SanitizeWorkflowName(dependency.DisplayName, 0));
                await WriteTextEntryAsync(archive, entryPath, dependency.Document.RawJson, cancellationToken);
            }

            var bundledModels = new List<BundledModel>();
            var missingModels = new List<string>();
            var usedModel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (request.IncludeModels && request.LocalSource?.ModelsDirectory is { } modelsRoot)
            {
                foreach (var model in resolution.Models)
                {
                    if (model.LocalPath is null) { missingModels.Add(model.Reference); continue; }
                    var source = Path.Combine(modelsRoot, model.LocalPath);
                    if (!File.Exists(source)) { missingModels.Add(model.Reference); continue; }
                    var entryPath = ModelsDir + "/" + UniqueName(usedModel, model.LocalPath);
                    await WriteFileFromDiskAsync(archive, entryPath, source, CompressionLevel.NoCompression, cancellationToken);
                    bundledModels.Add(new BundledModel(model.FileName, model.CategoryHint ?? "", source, entryPath));
                }
            }
            else if (request.IncludeModels)
            {
                missingModels.AddRange(resolution.Models.Select(m => m.Reference));
            }

            var bundledNodes = new List<BundledNode>();
            var missingNodes = new List<string>();
            var usedNode = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (request.IncludeNodes && request.LocalSource?.CustomNodesDirectory is { } nodesRoot)
            {
                foreach (var node in resolution.Nodes)
                {
                    if (!node.PresentLocally || node.LocalPackage is null) { missingNodes.Add(node.NodeType); continue; }
                    var source = Path.Combine(nodesRoot, node.LocalPackage);
                    if (!Directory.Exists(source)) { missingNodes.Add(node.NodeType); continue; }
                    var entryPath = CustomNodesDir + "/" + UniqueName(usedNode, node.LocalPackage);
                    await CopyDirToArchiveAsync(archive, entryPath, source, cancellationToken);
                    bundledNodes.Add(new BundledNode(node.LocalPackage, source, entryPath));
                }
            }
            else if (request.IncludeNodes)
            {
                missingNodes.AddRange(resolution.Nodes.Select(n => n.NodeType));
            }

            return new ResourcePackageResult(outputPath, dependencies.Count, bundledModels, bundledNodes, missingModels, missingNodes);
        }, cancellationToken);
    }

    // ---- Shared helpers ----------------------------------------------------------------

    private static async Task<ResourcePackageResult> BuildArchiveAsync(
        string outputPath,
        Func<ZipArchive, Task<ResourcePackageResult>> write,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var temporaryPath = outputPath + ".tmp";
        ResourcePackageResult result;
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                result = await write(archive);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        return result;
    }

    private static async Task WriteTextEntryAsync(ZipArchive archive, string entryPath, string text, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(NormalizeSlashes(entryPath), CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream, leaveOpen: true);
        await writer.WriteAsync(text);
    }

    private static async Task WriteFileFromDiskAsync(
        ZipArchive archive,
        string entryPath,
        string sourcePath,
        CompressionLevel compression,
        CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(sourcePath);
        var entry = archive.CreateEntry(NormalizeSlashes(entryPath), compression);
        await using var entryStream = entry.Open();
        await source.CopyToAsync(entryStream, cancellationToken);
    }

    private static async Task CopyDirToArchiveAsync(ZipArchive archive, string zipDir, string sourceDir, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeSlashes(Path.GetRelativePath(sourceDir, file));
            var entryPath = zipDir.TrimEnd('/') + "/" + relative;
            await WriteFileFromDiskAsync(archive, entryPath, file, CompressionLevel.Optimal, cancellationToken);
        }
    }

    private static void ValidateOut(ref string outputPath, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        outputPath = Path.GetFullPath(outputPath);
        if (!outputPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) outputPath += extension;
    }

    private static string SanitizeWorkflowName(string displayName, int index)
    {
        var baseName = string.IsNullOrWhiteSpace(displayName) ? $"workflow-{index + 1}" : displayName.Trim();
        var cleaned = new string(baseName.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = $"workflow-{index + 1}";
        if (!cleaned.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) cleaned += ".json";
        return cleaned;
    }

    private static string SanitizeFileName(string name, int index)
    {
        var cleaned = new string(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? $"asset-{index + 1}" : cleaned;
    }

    private static string UniqueName(HashSet<string> used, string candidate)
    {
        if (used.Add(candidate)) return candidate;
        var dotted = candidate.Contains('.') ? candidate[..candidate.LastIndexOf('.')] : candidate;
        var ext = candidate.Contains('.') ? candidate[candidate.LastIndexOf('.')..] : "";
        var counter = 1;
        string next;
        while (!used.Add(next = dotted + "-" + counter + ext)) counter++;
        return next;
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/');
}

// ---- Input / output records ------------------------------------------------------------

/// <summary>A model file on disk to include in an individual export. Category is the ComfyUI models/ subfolder.</summary>
public sealed record ModelFileRef(string SourcePath, string Category);

/// <summary>A custom-node directory on disk to include in an individual export.</summary>
public sealed record NodeDirRef(string SourcePath);

/// <summary>Bundle request: selected workflows + options for pulling resolved deps from a local Desktop instance.</summary>
public sealed record ExportBundleRequest(
    IReadOnlyList<WorkflowDocument> Workflows,
    ComfyDesktopLocation? LocalSource,
    bool IncludeModels,
    bool IncludeNodes,
    ICustomNodeRegistry? Registry = null);

public sealed record BundledModel(string FileName, string Category, string SourcePath, string ZipPath);
public sealed record BundledNode(string Name, string SourcePath, string ZipPath);

/// <summary>Outcome of any export: what was bundled and what could not be (so the UI can prompt a download).</summary>
public sealed record ResourcePackageResult(
    string OutputPath,
    int WorkflowCount,
    IReadOnlyList<BundledModel> BundledModels,
    IReadOnlyList<BundledNode> BundledNodes,
    IReadOnlyList<string> MissingModels,
    IReadOnlyList<string> MissingNodes);
