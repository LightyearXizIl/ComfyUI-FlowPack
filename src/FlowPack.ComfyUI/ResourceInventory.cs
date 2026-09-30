using System.Text.RegularExpressions;
using System.Text.Json;
using System.Security.Cryptography;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

public sealed record LocalResource(string Id, ResourceKind Kind, string Name, string SourcePath, string RelativePath,
    string? Category = null, IReadOnlyList<string>? NodeTypes = null, string? Version = null)
{
    public string? PackageIdentity { get; init; }
    public bool IsStaged { get; init; }
    public bool RuntimeChecked { get; init; }
    public IReadOnlyList<string> LoadedNodeTypes { get; init; } = [];
    public string? ModelDirectoryRoot { get; init; }
    public IReadOnlyList<string> IntegrityIssues { get; init; } = [];
}
public sealed record ResourceInventory(InstanceDescriptor Instance, IReadOnlyList<LocalResource> Resources, IReadOnlyList<string> CoreNodeTypes, IReadOnlyList<string> Issues)
{
    public string? RuntimeFingerprint { get; init; }
    public string? RuntimeNotice { get; init; }
}

public static class ResourceFiles
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".hg", ".svn", ".venv", "venv", "__pycache__", ".pytest_cache", "node_modules", ".cache" };
    public static IEnumerable<string> Enumerate(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var child in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var attr = File.GetAttributes(child);
                if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                if ((attr & FileAttributes.Directory) != 0)
                { if (!Excluded.Contains(Path.GetFileName(child))) pending.Push(child); }
                else if (!IsPrivateFile(child)) yield return child;
            }
        }
    }
    public static bool IsPrivateFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals(".env", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(".envrc", StringComparison.OrdinalIgnoreCase) || name.StartsWith("credentials.", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("auth.json", StringComparison.OrdinalIgnoreCase) || name.Equals("tokens.json", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
            (name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) &&
                (new FileInfo(path).Length > 1024 * 1024 || File.ReadAllText(path).Contains("PRIVATE KEY", StringComparison.Ordinal)));
    }
    public static bool IsModel(string file) => Path.GetExtension(file).ToLowerInvariant() is ".safetensors" or ".sft" or ".ckpt" or ".pt" or ".pth" or ".bin" or ".gguf" or ".onnx";
    public static string NormalizeCategory(string category) => category.ToLowerInvariant() switch { "clip" => "text_encoders", "unet" => "diffusion_models", _ => category };
    public static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
}

public sealed class ResourceInventoryService
{
    public Task<ResourceInventory> ScanAsync(InstanceDescriptor instance, CancellationToken token = default) => Task.Run(() =>
    {
        var resources = new List<LocalResource>();
        var issues = new List<string>(instance.Issues);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void ScanModels(string root, string? category)
        {
            foreach (var file in ResourceFiles.Enumerate(root).Where(ResourceFiles.IsModel))
            {
                token.ThrowIfCancellationRequested();
                if (!seen.Add(file)) continue;
                var relative = ResourceFiles.Relative(root, file);
                var parts = relative.Split('/');
                var kind = category ?? (parts.Length > 1 ? parts[0] : null);
                var boundary = category is null && parts.Length > 1 ? Path.Combine(root, parts[0]) : root;
                var bundle = ModelDirectory.FindRoot(file, boundary);
                if (bundle is not null && seen.Add(bundle))
                {
                    var description = ModelDirectory.Read(bundle);
                    var bundleRelative = ResourceFiles.Relative(root, bundle);
                    resources.Add(new(bundle, ResourceKind.Model, Path.GetFileName(bundle), bundle,
                        category is null ? bundleRelative : category + "/" + bundleRelative, kind)
                        { ModelDirectoryRoot = bundle, IntegrityIssues = description.Issues });
                }
                resources.Add(new(file, ResourceKind.Model, Path.GetFileName(file), file,
                    category is null ? relative : category + "/" + relative, kind) { ModelDirectoryRoot = bundle });
            }
        }
        foreach (var root in instance.ModelRoots) ScanModels(root, null);
        foreach (var extra in instance.ExtraPaths.Where(p => p.Category != "custom_nodes")) ScanModels(extra.Path, extra.Category);
        foreach (var root in new[] { instance.CustomNodesDirectory }.Concat(instance.ExtraPaths.Where(x => x.Category == "custom_nodes").Select(x => x.Path)))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.py"))
            {
                token.ThrowIfCancellationRequested();
                if (Path.GetFileName(file) == "__init__.py" || ResourceFiles.IsPrivateFile(file) ||
                    (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || !seen.Add(file)) continue;
                var types = ReadDeclaredNodeTypesFile(file);
                resources.Add(new(file, ResourceKind.CustomNode, Path.GetFileName(file), file,
                    "custom_nodes/" + Path.GetFileName(file), NodeTypes: types));
                if (types.Count == 0) issues.Add("单文件节点需要运行信息补充类型：" + Path.GetFileName(file));
            }
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                token.ThrowIfCancellationRequested();
                if (Path.GetFileName(directory).EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 || !seen.Add(directory)) continue;
                var types = ReadDeclaredNodeTypes(directory);
                var identity = ReadPackageIdentity(directory);
                if (types.Count == 0) issues.Add("节点包需要运行实例信息补充类型：" + Path.GetFileName(directory));
                resources.Add(new(directory, ResourceKind.CustomNode, Path.GetFileName(directory), directory,
                    "custom_nodes/" + Path.GetFileName(directory), NodeTypes: types, Version: identity.Version) { PackageIdentity = identity.Name });
            }
        }
        foreach (var file in ResourceFiles.Enumerate(instance.WorkflowsDirectory).Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            resources.Add(new(file, ResourceKind.Workflow, Path.GetFileNameWithoutExtension(file), file,
                "workflows/" + ResourceFiles.Relative(instance.WorkflowsDirectory, file)));
        }
        var core = new List<string>();
        if (instance.InputDirectory is not null)
            foreach (var file in ResourceFiles.Enumerate(instance.InputDirectory))
            {
                token.ThrowIfCancellationRequested();
                resources.Add(new(file, ResourceKind.Asset, Path.GetFileName(file), file, "input/" + ResourceFiles.Relative(instance.InputDirectory, file)));
            }
        var nodesFile = Path.Combine(instance.CoreDirectory, "nodes.py");
        if (File.Exists(nodesFile)) core.AddRange(ReadDeclaredNodeTypesFile(nodesFile));
        var extrasRoot = Path.Combine(instance.CoreDirectory, "comfy_extras");
        foreach (var file in ResourceFiles.Enumerate(extrasRoot).Where(f => f.EndsWith(".py"))) core.AddRange(ReadDeclaredNodeTypesFile(file));
        return new ResourceInventory(instance, resources, core.Distinct(StringComparer.Ordinal).ToArray(), issues);
    }, token);

    public static IReadOnlyList<string> ReadDeclaredNodeTypes(string directory) => ResourceFiles.Enumerate(directory)
        .Where(p => !ResourceFiles.Relative(directory, p).Split('/').SkipLast(1).Any(part =>
            part.Equals("tests", StringComparison.OrdinalIgnoreCase) || part.Equals("test", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("examples", StringComparison.OrdinalIgnoreCase) || part.Equals("docs", StringComparison.OrdinalIgnoreCase)))
        .Where(p => p.EndsWith(".py", StringComparison.OrdinalIgnoreCase)).SelectMany(ReadDeclaredNodeTypesFile).Distinct(StringComparer.Ordinal).ToArray();
    public static (string? Name, string? Version) ReadPackageIdentity(string directory)
    {
        var path = Path.Combine(directory, "pyproject.toml");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return (null, null);
        var project = Regex.Match(File.ReadAllText(path), @"(?ms)^\[project\][^\r\n]*\r?\n(.*?)(?=^\[|\z)", RegexOptions.None, TimeSpan.FromSeconds(1));
        string? Field(string name)
        {
            var match = Regex.Match(project.Groups[1].Value, "(?m)^" + name + "\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.None, TimeSpan.FromSeconds(1));
            return match.Success ? match.Groups[1].Value : null;
        }
        return (Field("name"), Field("version"));
    }
    public static IReadOnlyList<string> ReadDeclaredNodeTypesFile(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) return [];
        var text = File.ReadAllText(path);
        var matches = Regex.Matches(text, @"(?m)^NODE_CLASS_MAPPINGS\s*(?::[^=\r\n]+)?=\s*\{([^}]+)\}", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var declared = matches.SelectMany(m => Regex.Matches(m.Groups[1].Value, "[\"']([^\"']+)[\"']\\s*:", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(x => x.Groups[1].Value));
        var schema = Regex.Matches(text, "\\bnode_id\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(x => x.Groups[1].Value);
        return declared.Concat(schema).Distinct().ToArray();
    }
}

public enum DependencyState { Present, Missing, Ambiguous, Unresolved }
public sealed record AnalyzedDependency(string Id, ResourceKind Kind, string Reference, string? Category,
    DependencyState State, IReadOnlyList<LocalResource> Candidates, IReadOnlyList<string> RequiredBy, string Evidence)
{
    public string? RequiredPackageIdentity { get; init; }
    public string? RequiredVersion { get; init; }
    public string? RequiredSha256 { get; init; }
}
public sealed record DependencyAnalysis(IReadOnlyList<AnalyzedDependency> Dependencies, IReadOnlyList<string> Issues);

public sealed class InventoryDependencyAnalyzer
{
    public async Task<DependencyAnalysis> AnalyzeAsync(IReadOnlyList<WorkflowDocument> workflows, ResourceInventory inventory, CancellationToken token = default)
    {
        var analysis = Analyze(workflows, inventory);
        var result = new List<AnalyzedDependency>();
        foreach (var dependency in analysis.Dependencies)
        {
            var current = dependency;
            if (dependency.Kind == ResourceKind.Model)
            {
                var sources = workflows.Select(w => FindHashes(w.RawJson, dependency.Reference, dependency.Category)).ToArray();
                var hashes = sources.SelectMany(x => x.Hashes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (sources.Any(x => x.Ambiguous) || hashes.Length > 1) current = dependency with { State = DependencyState.Ambiguous, Evidence = "模型哈希要求冲突，或同名模型的哈希没有明确类别" };
                else if (hashes.Length == 1)
                {
                    var matching = new List<LocalResource>();
                    foreach (var candidate in dependency.Candidates)
                    {
                        if (Directory.Exists(candidate.SourcePath)) continue; // A directory has no single file SHA-256.
                        await using var stream = File.OpenRead(candidate.SourcePath);
                        if (Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(hashes[0], StringComparison.OrdinalIgnoreCase)) matching.Add(candidate);
                    }
                    current = dependency with { RequiredSha256 = hashes[0], Candidates = matching, State = dependency.Candidates.Any(x => Directory.Exists(x.SourcePath)) ? DependencyState.Unresolved : matching.Count > 0 ? DependencyState.Present : DependencyState.Missing,
                        Evidence = dependency.Candidates.Any(x => Directory.Exists(x.SourcePath)) ? "目录模型需要逐文件校验，不能套用单文件 SHA-256" : "按工作流明确指定的 SHA-256 核对" };
                }
            }
            result.Add(current);
        }
        return analysis with { Dependencies = result };
    }
    private static (IReadOnlyList<string> Hashes, bool Ambiguous) FindHashes(string raw, string reference, string? category)
    {
        using var doc = JsonDocument.Parse(raw);
        var references = WorkflowRequirementParser.Analyze(raw).ModelReferences;
        var hashes = new List<string>(); var ambiguous = false;
        Visit(doc.RootElement, null); return (hashes, ambiguous);
        void Visit(JsonElement value, string? nodeType)
        {
            if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Visit(item, nodeType);
            if (value.ValueKind != JsonValueKind.Object) return;
            if (value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String) nodeType = type.GetString();
            if (value.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            foreach (var model in models.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object || !model.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String ||
                    !string.Equals(n.GetString()!.Replace('\\', '/'), reference, StringComparison.OrdinalIgnoreCase) ||
                    !model.TryGetProperty("sha256", out var hash) || hash.ValueKind != JsonValueKind.String || hash.GetString() is not { Length: 64 } text || !text.All(Uri.IsHexDigit)) continue;
                var explicitCategory = model.TryGetProperty("directory", out var dir) && dir.ValueKind == JsonValueKind.String ? dir.GetString() :
                    model.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.String ? cat.GetString() : null;
                var matchingReferences = references.Where(x => x.Reference.Replace('\\', '/').Equals(reference, StringComparison.OrdinalIgnoreCase));
                if (nodeType is not null) matchingReferences = matchingReferences.Where(x => x.NodeType == nodeType);
                var categories = matchingReferences.Select(x => ResourceFiles.NormalizeCategory(x.CategoryHint ?? x.SemanticCategory ?? "")).Distinct().ToArray();
                var expectedCategory = ResourceFiles.NormalizeCategory(category ?? "");
                if (!categories.Contains(expectedCategory)) continue;
                if (explicitCategory is not null)
                { if (ResourceFiles.NormalizeCategory(explicitCategory) == expectedCategory) hashes.Add(text); }
                else if (categories.Length == 1) hashes.Add(text);
                else ambiguous = true;
            }
            foreach (var property in value.EnumerateObject()) if (property.Name != "models") Visit(property.Value, nodeType);
        }
    }
    public DependencyAnalysis Analyze(IReadOnlyList<WorkflowDocument> workflows, ResourceInventory inventory)
    {
        var result = new Dictionary<string, AnalyzedDependency>(StringComparer.Ordinal);
        var issues = new List<string>();
        foreach (var workflow in workflows)
        {
            var requirements = WorkflowRequirementParser.Analyze(workflow.RawJson);
            foreach (var type in requirements.NodeTypes)
            {
                if (inventory.CoreNodeTypes.Contains(type, StringComparer.Ordinal) || type is "Note" or "Reroute" or "PrimitiveNode") continue;
                var candidates = inventory.Resources.Where(x => x.Kind == ResourceKind.CustomNode && x.NodeTypes?.Contains(type, StringComparer.Ordinal) == true).ToArray();
                if (candidates.Any(x => x.IsStaged)) candidates = candidates.Where(x => x.IsStaged).ToArray();
                var requirement = FindNodeIdentity(workflow.RawJson, type);
                var originalCount = candidates.Length;
                if (requirement.Name is not null) candidates = candidates.Where(x => x.PackageIdentity == requirement.Name).ToArray();
                if (requirement.Version is not null) candidates = candidates.Where(x => x.Version == requirement.Version).ToArray();
                var loaded = candidates.Length == 1 && (candidates[0].IsStaged ||
                    inventory.RuntimeFingerprint == inventory.Instance.ConfigurationFingerprint && candidates[0].RuntimeChecked && candidates[0].LoadedNodeTypes.Contains(type, StringComparer.Ordinal));
                Add(new("node:" + type, ResourceKind.CustomNode, type, null,
                    requirement.Conflict ? DependencyState.Ambiguous : loaded ? DependencyState.Present : candidates.Length > 1 ? DependencyState.Ambiguous : originalCount > 0 ? DependencyState.Unresolved : DependencyState.Missing,
                    candidates, [workflow.DisplayName], requirement.Conflict ? "同一工作流对节点包身份或版本的要求冲突" :
                    originalCount > 0 && candidates.Length == 0 ? "存在同类型节点，但包身份或要求版本未匹配，不能自动复用" :
                    loaded ? candidates[0].IsStaged ? "完整节点源码已暂存；部署后仍需启动验证" : "所选实例运行时已加载所需节点类型" :
                    candidates.Any(x => x.RuntimeChecked) ? "源码存在，但运行实例没有加载所需类型；需要检查节点启动错误" : "源码存在，加载状态待验证；请启动所选实例并重新扫描")
                    { RequiredPackageIdentity = requirement.Name, RequiredVersion = requirement.Version });
            }
            foreach (var model in requirements.ModelReferences)
            {
                var reference = model.Reference.Replace('\\', '/');
                var category = model.CategoryHint ?? model.SemanticCategory;
                var candidates = inventory.Resources.Where(x => x.Kind == ResourceKind.Model &&
                    (category is null || ResourceFiles.NormalizeCategory(x.Category ?? "") == ResourceFiles.NormalizeCategory(category)) &&
                    (x.RelativePath.Equals(reference, StringComparison.OrdinalIgnoreCase) ||
                     x.RelativePath.Equals((x.Category ?? "") + "/" + reference, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (candidates.Any(x => x.IsStaged)) candidates = candidates.Where(x => x.IsStaged).ToArray();
                Add(new("model:" + category + ":" + reference, ResourceKind.Model, reference, category,
                    candidates.Any(x => x.IntegrityIssues.Count > 0) ? DependencyState.Unresolved : candidates.Length == 1 ? DependencyState.Present : candidates.Length > 1 ? DependencyState.Ambiguous : DependencyState.Missing,
                    candidates, [workflow.DisplayName], candidates.Any(x => x.IntegrityIssues.Count > 0) ? string.Join("；", candidates.SelectMany(x => x.IntegrityIssues)) : model.Evidence ?? "工作流引用；来源版本未指定"));
            }
            issues.AddRange(requirements.Issues.Select(x => workflow.DisplayName + "：" + x));
            foreach (var input in requirements.InputReferences)
            {
                var reference = input.Replace('\\', '/');
                if (reference.EndsWith(" [input]", StringComparison.Ordinal)) reference = reference[..^8];
                var candidates = inventory.Resources.Where(x => x.Kind == ResourceKind.Asset && x.RelativePath.Equals("input/" + reference, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Any(x => x.IsStaged)) candidates = candidates.Where(x => x.IsStaged).ToArray();
                Add(new("input:" + reference, ResourceKind.Asset, reference, "input", candidates.Length == 1 ? DependencyState.Present : candidates.Length > 1 ? DependencyState.Ambiguous : DependencyState.Unresolved,
                    candidates, [workflow.DisplayName], "已知输入节点的明确文件引用；临时和输出目录素材需要手动补全"));
            }
        }
        return new(result.Values.ToArray(), issues.Distinct().ToArray());
        void Add(AnalyzedDependency dependency)
        {
            if (result.TryGetValue(dependency.Id, out var existing))
            {
                dependency = dependency with { RequiredBy = existing.RequiredBy.Concat(dependency.RequiredBy).Distinct().ToArray() };
                if (existing.State == DependencyState.Ambiguous || existing.RequiredVersion != dependency.RequiredVersion || existing.RequiredPackageIdentity != dependency.RequiredPackageIdentity)
                    dependency = dependency with { State = DependencyState.Ambiguous, Evidence = "所选工作流对同一节点要求不同包身份或版本" };
            }
            result[dependency.Id] = dependency;
        }
    }
    private static (string? Name, string? Version, bool Conflict) FindNodeIdentity(string raw, string nodeType)
    {
        using var doc = JsonDocument.Parse(raw); var names = new HashSet<string>(); var versions = new HashSet<string>();
        Visit(doc.RootElement); return (names.FirstOrDefault(), versions.FirstOrDefault(), names.Count > 1 || versions.Count > 1);
        void Visit(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Visit(child);
            if (value.ValueKind != JsonValueKind.Object) return;
            if (value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == nodeType &&
                value.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                if (props.TryGetProperty("cnr_id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString())) names.Add(id.GetString()!);
                if (props.TryGetProperty("ver", out var ver) && ver.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ver.GetString())) versions.Add(ver.GetString()!);
            }
            foreach (var property in value.EnumerateObject()) Visit(property.Value);
        }
    }
}
