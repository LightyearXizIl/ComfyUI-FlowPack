using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace FlowPack.ComfyUI;

public sealed record ResourceSearchPath(string Category, string Path, bool IsDefault = false);

public sealed record InstanceDescriptor(
    string Id, string Name, string Generation, string? DesktopExecutable,
    string InstallRoot, string CoreDirectory, string DataDirectory, string UserDirectory,
    string WorkflowsDirectory, string CustomNodesDirectory, string? PythonPath,
    IReadOnlyList<string> ModelRoots, string ModelsWriteDirectory,
    IReadOnlyList<ResourceSearchPath> ExtraPaths, string ConfigurationFingerprint,
    IReadOnlyList<string> Issues)
{
    public string? InputDirectory { get; init; }
    public string? ConfigurationRoot { get; init; }
    public string? DesktopLayout { get; init; }
    public bool IsModern => Generation == "desktop-2";
    public string DisplayName => Name + (Issues.Count == 0 ? "" : "（需要检查）");
    public ComfyDesktopLocation ToLegacyLocation() => new(
        Generation, DataDirectory, CoreDirectory, ModelsWriteDirectory,
        CustomNodesDirectory, WorkflowsDirectory, PythonPath, DateTimeOffset.UtcNow)
    { Instance = this };
}

/// <summary>Only reads launcher registration and configuration. Discovery never creates resource directories.</summary>
public sealed class DesktopInstanceDiscovery(IDesktopPathProvider? paths = null, string? configurationRoot = null)
{
    private readonly IDesktopPathProvider _paths = paths ?? new EnvironmentDesktopPathProvider();
    private string ConfigurationRoot => configurationRoot is null ? Path.Combine(_paths.RoamingApplicationData, "Comfy Desktop") : Path.GetFullPath(configurationRoot);

    public async Task<IReadOnlyList<InstanceDescriptor>> DiscoverAsync(CancellationToken token = default)
    {
        var result = new List<InstanceDescriptor>();
        var configurationRoot = ConfigurationRoot;
        var registry = Path.Combine(configurationRoot, "installations.json");
        var settingsPath = Path.Combine(configurationRoot, "settings.json");
        if (File.Exists(registry))
        {
            var registryText = await File.ReadAllTextAsync(registry, token);
            var settingsText = File.Exists(settingsPath) ? await File.ReadAllTextAsync(settingsPath, token) : "{}";
            using var settings = JsonDocument.Parse(settingsText);
            using var records = JsonDocument.Parse(registryText);
            if (records.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("新版 Desktop 的实例登记不是有效数组，不能回退到旧实例进行安装。");
            foreach (var record in records.RootElement.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (record.ValueKind != JsonValueKind.Object || Text(record, "sourceId") == "cloud" ||
                    !string.IsNullOrEmpty(Text(record, "workspaceId"))) continue;
                var root = Text(record, "installPath");
                var id = Text(record, "id");
                if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(id) || !Path.IsPathFullyQualified(root)) continue;
                try { result.Add(ReadRecord(record, settings.RootElement, root, id, settingsText)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
                {
                    var core = Path.Combine(root, "ComfyUI");
                    result.Add(new(id, Text(record, "name") ?? id, "desktop-2", FindExecutable(), root, core, core,
                        Path.Combine(core, "user"), Path.Combine(core, "user", "default", "workflows"), Path.Combine(core, "custom_nodes"),
                        null, [], Path.Combine(core, "models"), [], Hash(record.GetRawText() + settingsText),
                        ["实例配置无法解析，路径仅供检查，不允许安装：" + ex.Message]));
                }
            }
        }

        // A legacy config can coexist with an adopted modern registration. Do not duplicate that data root.
        var legacyConfig = Path.Combine(_paths.RoamingApplicationData, "ComfyUI", "config.json");
        // An explicit launcher profile must never absorb the user's default legacy environment.
        if (configurationRoot.Equals(Path.Combine(_paths.RoamingApplicationData, "Comfy Desktop"), StringComparison.OrdinalIgnoreCase) && File.Exists(legacyConfig))
        {
            var raw = await File.ReadAllTextAsync(legacyConfig, token);
            using var doc = JsonDocument.Parse(raw);
            var data = Text(doc.RootElement, "basePath");
            if (data is not null && Path.IsPathFullyQualified(data) && Directory.Exists(data) &&
                !result.Any(x => SamePath(x.DataDirectory, data)))
            {
                var models = Path.Combine(data, "models");
                var extra = new List<ResourceSearchPath>();
                var issues = new List<string>();
                var evidence = new StringBuilder(raw);
                ReadExtraYaml(Path.Combine(_paths.RoamingApplicationData, "ComfyUI", "extra_models_config.yaml"), extra, evidence, issues);
                result.Add(new(Hash(data), "旧版 ComfyUI", "legacy", FindExecutable(), data, data, data,
                    Path.Combine(data, "user"), Path.Combine(data, "user", "default", "workflows"),
                    Path.Combine(data, "custom_nodes"), Existing(Path.Combine(data, ".venv", "Scripts", "python.exe")),
                    [models], models, extra, Hash(evidence.ToString()), issues));
            }
        }
        return result;
    }

    private InstanceDescriptor ReadRecord(JsonElement record, JsonElement settings, string root, string id, string settingsText)
    {
        var issues = new List<string>();
        if (Text(record, "sourceId") is not (null or "standalone")) issues.Add("此实例来源尚未适配自动安装。");
        var core = Path.Combine(root, "ComfyUI");
        if (!File.Exists(Path.Combine(core, "main.py")) && File.Exists(Path.Combine(root, "main.py"))) core = root;
        if (!File.Exists(Path.Combine(core, "main.py"))) issues.Add("找不到登记实例的 ComfyUI 核心。");
        var marker = Path.Combine(root, ".comfyui-desktop-2");
        if (!File.Exists(marker) || !string.Equals(File.ReadAllText(marker).Trim(), id, StringComparison.Ordinal))
            issues.Add("实例标识文件缺失或与 Desktop 登记不一致。");
        if (Text(record, "status") is "installing" or "updating" or "failed") issues.Add("Desktop 实例正在安装、更新或处于失败状态。");
        var adopted = Boolean(record, "adopted", false);
        var data = adopted ? Text(record, "adoptedBaseDir") : core;
        if (string.IsNullOrEmpty(data) || !Path.IsPathFullyQualified(data))
        {
            issues.Add("实例数据目录没有可靠的绝对路径。");
            data = core;
        }
        var user = Path.Combine(data, "user");
        string? explicitInput = null;
        var extraFiles = new List<string>();
        var args = ParseArguments(Text(record, "launchArgs") ?? "");
        for (var i = 0; i < args.Count; i++)
        {
            var pair = args[i].Split('=', 2);
            if (pair[0] == "--multi-user") issues.Add("多用户模式需要指定工作流所属用户，当前不自动选择 default。");
            if (pair[0] is not ("--base-directory" or "--user-directory" or "--extra-model-paths-config" or "--input-directory")) continue;
            var value = pair.Length == 2 ? pair[1] : i + 1 < args.Count ? args[++i] : "";
            if (!Path.IsPathFullyQualified(value)) { issues.Add("启动参数包含无法安全确定的相对资源路径。"); continue; }
            if (pair[0] == "--base-directory") data = Path.GetFullPath(value);
            if (pair[0] == "--user-directory") user = Path.GetFullPath(value);
            if (pair[0] == "--input-directory") explicitInput = Path.GetFullPath(value);
            if (pair[0] == "--extra-model-paths-config")
            {
                extraFiles.Add(value);
                while (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    var next = args[++i];
                    if (Path.IsPathFullyQualified(next)) extraFiles.Add(next);
                    else issues.Add("额外模型配置包含相对路径。");
                }
            }
        }
        if (!adopted && !args.Any(x => x == "--user-directory" || x.StartsWith("--user-directory="))) user = Path.Combine(data, "user");
        var python = adopted ? Existing(Text(record, "adoptedPythonPath")) :
            Existing(Path.Combine(root, "ComfyUI", ".venv", "Scripts", "python.exe")) ??
            Existing(Path.Combine(root, "envs", "default", "Scripts", "python.exe"));
        if (python is null) issues.Add("登记实例的 Python 不存在；不会使用系统或其他实例的 Python。");
        var models = new List<string> { Path.Combine(data, "models") };
        var defaultRoot = ResolveDefaultDataRoot(issues);
        var shared = Boolean(record, "useSharedModels", true) ? Strings(settings, "modelsDirs") : [];
        if (Boolean(record, "useSharedModels", true) && !settings.TryGetProperty("modelsDirs", out _) && defaultRoot is not null)
            shared.Add(Path.Combine(defaultRoot, "ComfyUI-Shared", "models"));
        models.AddRange(shared);
        models.AddRange(Strings(record, "modelDirs"));
        models = models.Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var primary = Text(record, "modelDirsPrimary");
        if (primary is null) primary = shared.FirstOrDefault() ?? models[0];
        if (!models.Any(x => SamePath(x, primary))) { issues.Add("默认模型写入路径不属于有效搜索路径。"); primary = models[0]; }
        var extras = new List<ResourceSearchPath>();
        var evidence = new StringBuilder(ConfigurationRoot).Append(record.GetRawText()).Append(settingsText);
        ReadExtraYaml(Path.Combine(core, "extra_model_paths.yaml"), extras, evidence, issues);
        foreach (var file in extraFiles) ReadExtraYaml(file, extras, evidence, issues, required: true);
        if (python is not null) evidence.Append(python).Append(new FileInfo(python).Length).Append(File.GetLastWriteTimeUtc(python).Ticks);
        evidence.Append(File.Exists(marker) ? File.ReadAllText(marker) : "missing-marker");
        evidence.Append(defaultRoot);
        var inputDirectory = Boolean(record, "useSharedInput", true) ? Text(settings, "inputDir") is { Length: > 0 } configuredInput ? configuredInput :
            defaultRoot is null ? null : Path.Combine(defaultRoot, "ComfyUI-Shared", "input") :
            Text(record, "inputDir") ?? explicitInput ?? Path.Combine(data, "input");
        return new(id, Text(record, "name") ?? id, "desktop-2", FindExecutable(), root, core, data, user,
            Path.Combine(user, "default", "workflows"), Path.Combine(data, "custom_nodes"), python,
            models, primary, extras, Hash(evidence.ToString()), issues)
        { ConfigurationRoot = ConfigurationRoot, DesktopLayout = adopted ? "standalone-adopted" : "standalone-native",
            InputDirectory = inputDirectory is not null && Path.IsPathFullyQualified(inputDirectory) ? Path.GetFullPath(inputDirectory) : null };
    }

    private string? FindExecutable() => _paths.GetProcessModulePaths("Comfy Desktop").FirstOrDefault() ??
        _paths.GetRegisteredDesktopExecutables().FirstOrDefault() ??
        Existing(Path.Combine(_paths.LocalApplicationData, "Programs", "Comfy Desktop", "Comfy Desktop.exe"));

    // v1.0.47 paths.ts: redirected install drive, otherwise persisted system-drive mode.
    // Read-only: never create Desktop's marker or default resource directories.
    private string? ResolveDefaultDataRoot(List<string> issues)
    {
        var executable = FindExecutable();
        if (executable is null) { issues.Add("无法确认 Desktop 程序位置，默认共享路径需要检查。"); return null; }
        var drive = Path.GetPathRoot(executable);
        if (!string.Equals(drive, _paths.SystemDrive, StringComparison.OrdinalIgnoreCase)) return Path.Combine(drive!, "Comfy-Desktop");
        var marker = Path.Combine(ConfigurationRoot, "data-location.json");
        string? mode = null;
        if (File.Exists(marker))
        {
            try { using var doc = JsonDocument.Parse(File.ReadAllText(marker)); mode = Text(doc.RootElement, "mode"); }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException) { /* Official launcher also falls back to the footprint. */ }
        }
        var legacy = mode == "legacy-home" || mode != "local-appdata" &&
            (Directory.Exists(Path.Combine(_paths.UserProfile, "ComfyUI-Installs")) || Directory.Exists(Path.Combine(_paths.UserProfile, "ComfyUI-Shared")));
        return legacy ? _paths.UserProfile : Path.Combine(_paths.LocalApplicationData, "Comfy-Desktop");
    }

    public async Task<InstanceDescriptor> AssociateAsync(string path, CancellationToken token = default)
    {
        var root = Path.GetFullPath(path);
        var registered = (await DiscoverAsync(token)).FirstOrDefault(x => SamePath(x.InstallRoot, root) || SamePath(x.DataDirectory, root));
        if (registered is not null) return registered;
        var core = File.Exists(Path.Combine(root, "main.py")) ? root : Path.Combine(root, "ComfyUI");
        if (!File.Exists(Path.Combine(core, "main.py"))) throw new InvalidDataException("所选目录没有 ComfyUI 核心，无法建立只读资源关联。");
        var models = Path.Combine(core, "models"); var user = Path.Combine(core, "user");
        return new(Hash(root), Path.GetFileName(root) + "（手动只读）", "manual-readonly", FindExecutable(), root, core, core, user,
            Path.Combine(user, "default", "workflows"), Path.Combine(core, "custom_nodes"), Existing(Path.Combine(core, ".venv", "Scripts", "python.exe")),
            [models], models, [], Hash(root + File.GetLastWriteTimeUtc(Path.Combine(core, "main.py")).Ticks),
            ["未找到新版 Desktop 登记；允许读取和导出，不授予安装权限。"])
            { InputDirectory = Path.Combine(core, "input") };
    }

    public static IReadOnlyList<string> ParseArguments(string value)
    {
        // Desktop stores a shell-like string; quotes group paths, not commands to execute.
        var result = new List<string>();
        var word = new StringBuilder();
        char quote = '\0';
        foreach (var c in value)
        {
            if (c == quote) { quote = '\0'; continue; }
            if (quote == '\0' && c is '\'' or '"') { quote = c; continue; }
            if (quote == '\0' && char.IsWhiteSpace(c))
            { if (word.Length > 0) { result.Add(word.ToString()); word.Clear(); } }
            else word.Append(c);
        }
        if (quote != '\0') throw new InvalidDataException("Desktop 启动参数的引号没有闭合。");
        if (word.Length > 0) result.Add(word.ToString());
        return result;
    }

    private static void ReadExtraYaml(string file, List<ResourceSearchPath> result, StringBuilder evidence, List<string> issues, bool required = false)
    {
        if (!File.Exists(file)) { if (required) issues.Add("额外模型配置文件不存在。"); return; }
        try
        {
            var raw = File.ReadAllText(file);
            evidence.Append(file).Append(raw);
            var yaml = new YamlStream();
            yaml.Load(new StringReader(raw));
            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode sections) return;
            foreach (var item in sections.Children.Values.OfType<YamlMappingNode>())
            {
                var basePath = item.Children.TryGetValue(new YamlScalarNode("base_path"), out var basis) ? ((YamlScalarNode)basis).Value : null;
                var defaultPath = item.Children.TryGetValue(new YamlScalarNode("is_default"), out var flag) && flag.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
                foreach (var entry in item.Children)
                {
                    var category = entry.Key.ToString();
                    if (category is "base_path" or "is_default") continue;
                    if (entry.Value is not YamlScalarNode scalar) { issues.Add("额外资源路径不是字符串。"); continue; }
                    foreach (var line in (scalar.Value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var baseDir = basePath is null ? Path.GetDirectoryName(file)! : Path.GetFullPath(basePath, Path.GetDirectoryName(file)!);
                        var resolved = Path.GetFullPath(Environment.ExpandEnvironmentVariables(line), baseDir);
                        result.Add(new(category, resolved, defaultPath));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or IOException or ArgumentException or InvalidCastException)
        { issues.Add("额外资源配置无法解析：" + Path.GetFileName(file)); }
    }

    private static string? Existing(string? path) => path is not null && Path.IsPathFullyQualified(path) && File.Exists(path) ? Path.GetFullPath(path) : null;
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static bool Boolean(JsonElement e, string name, bool fallback) => e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : fallback;
    private static List<string> Strings(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
    private static bool SamePath(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
