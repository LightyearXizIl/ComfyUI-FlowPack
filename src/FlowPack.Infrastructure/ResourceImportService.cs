using System.Security.Cryptography;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public enum RecognitionState { Confirmed, NeedsConfirmation, Unknown }
public sealed record ImportResource(string Id, string SourcePath, string OriginalPath, ResourceKind Kind,
    string TargetRelativePath, long SizeBytes, string Sha256, RecognitionState State, string Evidence)
{
    public IReadOnlyList<string> DeclaredPythonDependencies { get; init; } = [];
}
public sealed record ImportPlan(string Id, string Source, string StagingRoot, IReadOnlyList<ImportResource> Resources,
    IReadOnlyList<WorkflowDocument> Workflows, IReadOnlyList<string> Issues)
{
    // These are declarations, not local payloads. Never feed them directly to installation.
    public PackageManifest? OnlineManifest { get; init; }
    public IReadOnlyList<ResourceEntry> PendingDownloads { get; init; } = [];
}

public sealed class ResourceImportService
{
    public async Task<ImportPlan> ImportAsync(string source, string stagingRoot, CancellationToken token = default, IProgress<OperationProgress>? progress = null)
    {
        source = Path.GetFullPath(source);
        progress?.Report(new("读取导入来源"));
        var onlineManifest = false;
        if (File.Exists(source) && source.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && new FileInfo(source).Length <= 16 * 1024 * 1024)
        {
            try
            {
                using var candidate = JsonDocument.Parse(await File.ReadAllTextAsync(source, token));
                onlineManifest = candidate.RootElement.ValueKind == JsonValueKind.Object && candidate.RootElement.TryGetProperty("formatVersion", out _) &&
                    candidate.RootElement.TryGetProperty("resources", out _) && candidate.RootElement.TryGetProperty("id", out _);
            }
            catch (JsonException) { }
        }
        if (File.Exists(source) && (source.EndsWith(".cpack.json", StringComparison.OrdinalIgnoreCase) || onlineManifest))
        {
            ResourceInstallationService.EnsureNoLinks(source);
            var manifest = (await new PackageImportReader().ReadAsync(source, token)).Manifest;
            var pendingIssues = new List<string>(manifest.CompletenessIssues);
            foreach (var resource in manifest.Resources)
                pendingIssues.Add(resource.Name + (resource.SourceUrl is null ? "：未提供下载来源，需要补全。" : "：尚未下载，不能作为本地安装载荷。"));
            return new(Guid.NewGuid().ToString("N"), source, Path.GetFullPath(stagingRoot), [], [], pendingIssues.Distinct().ToArray())
            { OnlineManifest = manifest, PendingDownloads = manifest.Resources.ToArray() };
        }
        var root = source;
        ImportedPackage? legacy = null;
        if (File.Exists(source) && (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".cpack", StringComparison.OrdinalIgnoreCase)))
        {
            if (source.EndsWith(".cpack", StringComparison.OrdinalIgnoreCase)) legacy = await new PackageImportReader().ReadAsync(source, token);
            root = (await new NativePackageStagingService().StageAsync(source, stagingRoot, cancellationToken: token, progress: progress)).RootPath;
        }
        if (Directory.Exists(source) && File.Exists(Path.Combine(source, "manifest.json")))
        {
            ResourceInstallationService.EnsureNoLinks(Path.Combine(source, "manifest.json"));
            using var candidate = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(source, "manifest.json"), token));
            if (candidate.RootElement.ValueKind == JsonValueKind.Object && candidate.RootElement.TryGetProperty("formatVersion", out _) &&
                candidate.RootElement.TryGetProperty("resources", out _) && candidate.RootElement.TryGetProperty("id", out _))
                legacy = await new PackageImportReader().ReadAsync(source, token);
        }
        var single = File.Exists(root);
        var files = single ? new[] { root } : ResourceFiles.Enumerate(root).ToArray();
        if (single) root = Path.GetDirectoryName(root)!;
        var entries = new List<ImportResource>();
        progress?.Report(new("核对导入内容", 0, files.Length));
        var workflows = new List<WorkflowDocument>();
        var issues = new List<string>();
        var manifestPath = Path.Combine(root, "flowpack-manifest.json");
        if (!single && File.Exists(manifestPath))
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, token));
            if (!manifest.RootElement.TryGetProperty("resources", out var listed) || listed.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("资源清单结构无效。");
            foreach (var item in listed.EnumerateArray())
            {
                var relative = item.GetProperty("path").GetString()!;
                PlannedZipExportService.ValidateRelative(relative);
                var file = Path.GetFullPath(Path.Combine(root, relative));
                if (!Inside(root, file) || !File.Exists(file) || new FileInfo(file).Length != item.GetProperty("size").GetInt64() ||
                    !string.Equals(await HashAsync(file, token, progress), item.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("资源清单校验失败：" + relative);
            }
            if (manifest.RootElement.TryGetProperty("issues", out var missing) && missing.ValueKind == JsonValueKind.Array)
                issues.AddRange(missing.EnumerateArray().Select(x => x.GetString() ?? "导出包存在未解决依赖"));
        }
        var nodeRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(x => Path.GetFileName(x).Equals("__init__.py", StringComparison.OrdinalIgnoreCase)))
        {
            var dir = Path.GetDirectoryName(file)!;
            var relative = ResourceFiles.Relative(root, dir);
            var parts = relative.Split('/');
            var index = Array.FindIndex(parts, x => x.Equals("custom_nodes", StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < parts.Length)
            {
                var nodeRoot = Path.Combine(root, Path.Combine(parts.Take(index + 2).ToArray()));
                nodeRoots.TryAdd(nodeRoot, parts[index + 1]);
            }
            else if (ResourceInventoryService.ReadDeclaredNodeTypes(dir).Count > 0)
                nodeRoots.TryAdd(dir, new DirectoryInfo(dir).Name);
        }
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var relative = single ? Path.GetFileName(file) : ResourceFiles.Relative(root, file);
            if (Path.GetFileName(file) == "flowpack-manifest.json" || legacy is not null && relative == "manifest.json") continue;
            var kind = ResourceKind.Asset;
            var state = RecognitionState.Unknown;
            var target = relative;
            var evidence = "没有足够信息确定用途；不会自动部署";
            var node = nodeRoots.Where(p => Inside(p.Key, file)).OrderBy(p => p.Key.Length).FirstOrDefault();
            if (node.Key is not null)
            {
                kind = ResourceKind.CustomNode; state = RecognitionState.Confirmed;
                target = "custom_nodes/" + node.Value + "/" + ResourceFiles.Relative(node.Key, file);
                evidence = "节点包目录和 Python 节点声明";
            }
            else if (file.EndsWith(".py", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(file) != "__init__.py" &&
                ResourceInventoryService.ReadDeclaredNodeTypesFile(file).Count > 0)
            {
                kind = ResourceKind.CustomNode; state = RecognitionState.Confirmed;
                target = "custom_nodes/" + Path.GetFileName(file);
                evidence = "单文件 Python 节点声明；仅静态识别，尚未验证运行时加载";
            }
            else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && new FileInfo(file).Length <= 16 * 1024 * 1024)
            {
                try
                {
                    var raw = await File.ReadAllTextAsync(file, token);
                    var document = WorkflowDocumentFactory.Create(await HashAsync(file, token, progress), Path.GetFileNameWithoutExtension(file), raw);
                    if (document.Format != WorkflowFormat.Unknown)
                    {
                        kind = ResourceKind.Workflow; state = RecognitionState.Confirmed;
                        var segments = relative.Split('/');
                        var marker = Array.FindIndex(segments, x => x.Equals("workflows", StringComparison.OrdinalIgnoreCase));
                        target = "workflows/" + (marker >= 0 ? string.Join('/', segments.Skip(marker + 1)) : Path.GetFileName(file));
                        evidence = document.Format == WorkflowFormat.Api
                            ? "已识别 API 格式工作流，原始 JSON 保留。它是执行图，不是带画布布局的 UI 工作流；若从 Desktop 侧栏打开为空，可尝试文件打开入口，或向提供者索取 UI 格式工作流。"
                            : "已识别 ComfyUI UI 格式工作流，原始 JSON 与画布数据保留。";
                        workflows.Add(document);
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException) { /* Non-workflow JSON remains visible as unknown. */ }
            }
            if (state == RecognitionState.Unknown && ResourceFiles.IsModel(file))
            {
                kind = ResourceKind.Model;
                var segments = relative.Split('/');
                var marker = Array.FindIndex(segments, x => x.Equals("models", StringComparison.OrdinalIgnoreCase));
                if (marker >= 0 && segments.Length >= marker + 3)
                {
                    target = string.Join('/', segments.Skip(marker)); state = RecognitionState.Confirmed;
                    evidence = "模型文件扩展名与 models/类别 目录一致";
                }
                else
                {
                    target = "models/" + Path.GetFileName(file); state = RecognitionState.NeedsConfirmation;
                    evidence = "模型文件缺少明确类别，请选择目标类别";
                }
            }
            if (state == RecognitionState.Unknown)
            {
                var segments = relative.Split('/');
                var modelMarker = Array.FindIndex(segments, x => x.Equals("models", StringComparison.OrdinalIgnoreCase));
                var directory = Path.GetDirectoryName(file)!;
                var modelBoundary = modelMarker >= 0 && segments.Length > modelMarker + 2
                    ? Path.Combine(root, Path.Combine(segments.Take(modelMarker + 2).ToArray())) : null;
                if (modelBoundary is not null && ModelDirectory.IsMember(file) && ModelDirectory.FindRoot(file, modelBoundary) is not null)
                {
                    kind = ResourceKind.Model; target = string.Join('/', segments.Skip(modelMarker));
                    state = RecognitionState.Confirmed; evidence = "已识别模型目录的配置、词表或分片配套文件";
                }
                if (modelMarker >= 0 && segments.Length > modelMarker + 2 &&
                    Path.GetExtension(file).ToLowerInvariant() is ".json" or ".txt" or ".yaml" or ".yml" or ".model" or ".vocab" or ".merges" or ".tiktoken" or ".spm" &&
                    (File.Exists(Path.Combine(directory, "config.json")) ||
                     Directory.EnumerateFiles(directory).Any(x => ResourceFiles.IsModel(x) && Path.GetFileNameWithoutExtension(x) == Path.GetFileNameWithoutExtension(file))))
                {
                    kind = ResourceKind.Model; target = string.Join('/', segments.Skip(modelMarker));
                    state = RecognitionState.Confirmed; evidence = "模型目录中的配置、分片或配套文件";
                }
                var inputMarker = Array.FindIndex(segments, x => x.Equals("input", StringComparison.OrdinalIgnoreCase));
                if (inputMarker >= 0 && segments.Length > inputMarker + 1)
                {
                    target = string.Join('/', segments.Skip(inputMarker));
                    state = RecognitionState.Confirmed;
                    evidence = "明确的 input 素材目录；安装前仍需核对目标实例输入目录";
                }
            }
            entries.Add(new(Guid.NewGuid().ToString("N"), file, relative, kind, target, new FileInfo(file).Length,
                await HashAsync(file, token, progress), state, evidence));
            progress?.Report(new("核对导入内容", entries.Count, files.Length));
        }
        if (legacy is not null)
            entries = LegacyImportMapping.Apply(legacy.Manifest, entries, issues);
        foreach (var group in entries.Where(x => x.State == RecognitionState.Confirmed).GroupBy(x => x.TargetRelativePath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add("多个文件映射到同一目标：" + group.Key);
        return new(Guid.NewGuid().ToString("N"), source, root, entries, workflows, issues);
    }

    public static async Task<string> HashAsync(string file, CancellationToken token = default, IProgress<OperationProgress>? progress = null)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072]; long completed = 0; int read;
        var stage = "校验文件 · " + Path.GetFileName(file);
        progress?.Report(new(stage, 0, stream.Length, "字节"));
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            hash.AppendData(buffer, 0, read); completed += read;
            progress?.Report(new(stage, completed, stream.Length, "字节"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static bool Inside(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
