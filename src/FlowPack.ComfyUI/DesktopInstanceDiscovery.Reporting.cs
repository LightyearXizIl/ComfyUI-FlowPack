using System.Text;
using System.Text.Json;

namespace FlowPack.ComfyUI;

public sealed partial class DesktopInstanceDiscovery
{
    public async Task<InstanceDiscoveryReport> DiscoverReportAsync(CancellationToken token = default)
    {
        var instances = new List<InstanceDescriptor>();
        var checkedSources = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();
        await ReadProfileAsync(ConfigurationRoot, instances, checkedSources, skipped, errors, token);
        // Explicit profiles are isolation boundaries. Never mix in another user's or
        // launcher's default resources when the caller selected one profile.
        if (!_explicitConfigurationRoot)
        {
            await ReadLegacyAsync(instances, checkedSources, skipped, errors, token);
            var profileRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ConfigurationRoot };
            foreach (var custom in CustomProfiles(errors))
            {
                token.ThrowIfCancellationRequested();
                if (profileRoots.Add(custom)) await ReadProfileAsync(custom, instances, checkedSources, skipped, errors, token);
            }
            foreach (var candidate in FallbackCandidates(checkedSources, skipped, errors))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (instances.Any(x => SamePath(x.InstallRoot, candidate.Path) || SamePath(x.DataDirectory, candidate.Path))) continue;
                    var descriptor = CreateReadOnlyCandidate(candidate.Path, candidate.Source);
                    if (descriptor is not null && !instances.Any(x => SamePath(x.CoreDirectory, descriptor.CoreDirectory) && SamePath(x.DataDirectory, descriptor.DataDirectory)))
                        instances.Add(descriptor);
                    else if (descriptor is null) skipped.Add(candidate.Path + "：没有可识别的 ComfyUI 核心或资源目录。");
                }
                catch (Exception ex) when (IsDiscoveryError(ex)) { errors.Add(candidate.Path + "：" + ex.Message); }
            }
        }
        return new(instances, ConfigurationRoot, checkedSources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), skipped, errors);
    }

    private async Task ReadProfileAsync(string profile, List<InstanceDescriptor> instances, List<string> checkedSources,
        List<string> skipped, List<string> errors, CancellationToken token)
    {
        var registry = Path.Combine(profile, "installations.json");
        var settingsPath = Path.Combine(profile, "settings.json");
        checkedSources.Add(registry);
        checkedSources.Add(settingsPath);
        if (!File.Exists(registry)) { skipped.Add(registry + "：不存在。"); return; }
        string settingsText = "{}";
        var settingsIssues = new List<string>();
        try
        {
            if (File.Exists(settingsPath)) settingsText = await File.ReadAllTextAsync(settingsPath, token);
            using var validation = JsonDocument.Parse(settingsText);
            if (validation.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("设置必须是 JSON 对象。");
        }
        catch (Exception ex) when (IsDiscoveryError(ex))
        {
            settingsIssues.Add("Desktop 共享设置无法读取，默认共享资源路径需要检查：" + ex.Message);
            errors.Add(settingsPath + "：" + ex.Message);
            settingsText = "{}";
        }
        using var settings = JsonDocument.Parse(settingsText);
        try
        {
            using var records = JsonDocument.Parse(await File.ReadAllTextAsync(registry, token));
            if (records.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("实例登记必须是 JSON 数组。");
            var index = 0;
            foreach (var record in records.RootElement.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                var label = registry + $" [登记 {++index}]";
                if (record.ValueKind != JsonValueKind.Object) { skipped.Add(label + "：登记不是对象。"); continue; }
                var id = Text(record, "id");
                var root = Text(record, "installPath");
                if (Text(record, "sourceId") == "cloud") { skipped.Add(label + "：cloud 实例不属于本机资源。"); continue; }
                if (!string.IsNullOrEmpty(Text(record, "workspaceId"))) { skipped.Add(label + "：workspaceId 非空，工作区实例尚未适配。"); continue; }
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(root)) { skipped.Add(label + "：缺少 id 或 installPath。"); continue; }
                if (!Path.IsPathFullyQualified(root)) { skipped.Add(label + "：installPath 不是绝对路径。"); continue; }
                try
                {
                    var prior = instances.FirstOrDefault(x => x.Id == id && !SamePath(x.InstallRoot, root));
                    if (prior is not null)
                    {
                        var conflict = label + $"：实例 id {id} 冲突；保留先发现的目录 {prior.InstallRoot}，已跳过目录 {root}。";
                        skipped.Add(conflict);
                        errors.Add(conflict);
                        continue;
                    }
                    // Use the same profile for path resolution and fingerprint evidence.
                    var reader = SamePath(profile, ConfigurationRoot) ? this : new DesktopInstanceDiscovery(_paths, profile);
                    var instance = reader.ReadRecord(record, settings.RootElement, root, id, settingsText);
                    if (settingsIssues.Count > 0) instance = instance with { Issues = instance.Issues.Concat(settingsIssues).ToArray() };
                    if (!instances.Any(x => x.Id == instance.Id && SamePath(x.InstallRoot, instance.InstallRoot))) instances.Add(instance);
                }
                catch (Exception ex) when (IsDiscoveryError(ex))
                {
                    errors.Add(label + "：" + ex.Message);
                    var core = Path.Combine(root, "ComfyUI");
                    instances.Add(new(id, Text(record, "name") ?? id, "desktop-2", FindExecutable(), root, core, core,
                        Path.Combine(core, "user"), Path.Combine(core, "user", "default", "workflows"), Path.Combine(core, "custom_nodes"),
                        null, [], Path.Combine(core, "models"), [], Hash(profile + record.GetRawText() + settingsText),
                        ["实例配置无法解析，路径仅供检查，不允许安装：" + ex.Message])
                        { ConfigurationRoot = profile });
                }
            }
        }
        catch (Exception ex) when (IsDiscoveryError(ex)) { errors.Add(registry + "：" + ex.Message); }
    }

    private async Task ReadLegacyAsync(List<InstanceDescriptor> instances, List<string> checkedSources, List<string> skipped,
        List<string> errors, CancellationToken token)
    {
        var file = Path.Combine(_paths.RoamingApplicationData, "ComfyUI", "config.json");
        checkedSources.Add(file);
        if (!File.Exists(file)) { skipped.Add(file + "：不存在。"); return; }
        try
        {
            var raw = await File.ReadAllTextAsync(file, token);
            using var doc = JsonDocument.Parse(raw);
            var data = Text(doc.RootElement, "basePath");
            if (data is null || !Path.IsPathFullyQualified(data) || !Directory.Exists(data))
            { skipped.Add(file + "：basePath 不是存在的绝对目录。"); return; }
            if (instances.Any(x => SamePath(x.DataDirectory, data))) return;
            var models = Path.Combine(data, "models");
            var extra = new List<ResourceSearchPath>();
            var issues = new List<string>();
            var evidence = new StringBuilder(raw);
            ReadExtraYaml(Path.Combine(_paths.RoamingApplicationData, "ComfyUI", "extra_models_config.yaml"), extra, evidence, issues);
            instances.Add(new(Hash(data), "旧版 ComfyUI", "legacy", FindExecutable(), data, data, data,
                Path.Combine(data, "user"), Path.Combine(data, "user", "default", "workflows"),
                Path.Combine(data, "custom_nodes"), Existing(Path.Combine(data, ".venv", "Scripts", "python.exe")),
                [models], models, extra, Hash(evidence.ToString()), issues)
                { ConfigurationRoot = Path.GetDirectoryName(file), DesktopLayout = "legacy-config" });
        }
        catch (Exception ex) when (IsDiscoveryError(ex)) { errors.Add(file + "：" + ex.Message); }
    }

    private IReadOnlyList<string> CustomProfiles(List<string> errors)
    {
        var roots = new List<string>();
        foreach (var root in _paths.GetCustomUserDataDirectories())
            if (Path.IsPathFullyQualified(root)) roots.Add(Path.GetFullPath(root));
        foreach (var name in new[] { "Comfy Desktop", "ComfyUI-Desktop", "comfyui-electron" })
        foreach (var command in _paths.GetProcessCommandLines(name))
        {
            try
            {
                var args = ParseArguments(command);
                for (var i = 0; i < args.Count; i++)
                {
                    var pair = args[i].Split('=', 2);
                    if (pair[0] != "--user-data-dir") continue;
                    var value = pair.Length == 2 ? pair[1] : i + 1 < args.Count ? args[++i] : "";
                    if (Path.IsPathFullyQualified(value)) roots.Add(Path.GetFullPath(value));
                    else errors.Add("进程 --user-data-dir 不是绝对路径，已跳过。");
                }
            }
            catch (Exception ex) when (IsDiscoveryError(ex)) { errors.Add("无法读取 Desktop 进程用户数据目录：" + ex.Message); }
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<(string Path, string Source)> FallbackCandidates(List<string> checkedSources, List<string> skipped, List<string> errors)
    {
        var candidates = new List<(string Path, string Source)>();
        void Add(string path, string source, bool children = false)
        {
            if (!Path.IsPathFullyQualified(path)) return;
            path = Path.GetFullPath(path);
            checkedSources.Add(path);
            if (!Directory.Exists(path)) { skipped.Add(path + "：不存在。"); return; }
            candidates.Add((path, source));
            if (!children) return;
            try { candidates.AddRange(Directory.EnumerateDirectories(path).Select(x => (x, source))); }
            catch (Exception ex) when (IsDiscoveryError(ex)) { errors.Add(path + "：" + ex.Message); }
        }
        var dataRoots = new List<string> { Path.Combine(_paths.LocalApplicationData, "Comfy-Desktop"), _paths.UserProfile };
        foreach (var executable in _paths.GetRegisteredDesktopExecutables().Concat(_paths.GetProcessModulePaths("Comfy Desktop")))
        {
            if (!Path.IsPathFullyQualified(executable)) continue;
            var drive = Path.GetPathRoot(executable);
            if (drive is not null && !SamePath(drive, _paths.SystemDrive)) dataRoots.Add(Path.Combine(drive, "Comfy-Desktop"));
        }
        foreach (var root in dataRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Add(Path.Combine(root, "ComfyUI-Installs"), "本地 ComfyUI-Installs", children: true);
            Add(Path.Combine(root, "ComfyUI-Shared"), "本地 ComfyUI-Shared");
        }
        Add(Path.Combine(_paths.MyDocuments, "ComfyUI"), "Documents");
        Add(Path.Combine(_paths.LocalApplicationData, "Programs", "comfyui-electron"), "comfyui-electron");
        foreach (var name in new[] { "Comfy Desktop", "ComfyUI-Desktop", "comfyui-electron" })
        foreach (var module in _paths.GetProcessModulePaths(name))
        {
            if (!Path.IsPathFullyQualified(module)) continue;
            var directory = Path.GetDirectoryName(module)!;
            Add(directory, "运行进程：" + name);
            Add(Path.Combine(directory, "resources"), "运行进程资源：" + name);
            Add(Path.Combine(directory, "resources", "ComfyUI"), "运行进程核心：" + name);
        }
        return candidates.DistinctBy(x => Path.TrimEndingDirectorySeparator(x.Path), StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private InstanceDescriptor? CreateReadOnlyCandidate(string root, string source)
    {
        var core = File.Exists(Path.Combine(root, "main.py")) ? root : Path.Combine(root, "ComfyUI");
        var hasCore = File.Exists(Path.Combine(core, "main.py"));
        var data = HasAssets(root) ? root : core;
        // Shared stores contain resources but no main.py. They remain useful for
        // reading/export and must never acquire installation capability.
        if (!hasCore && !HasAssets(data)) return null;
        var models = Path.Combine(data, "models");
        var user = Path.Combine(data, "user");
        var issues = new List<string> { "未找到新版 Desktop 登记；允许读取和导出，不授予安装权限。", "发现来源：" + source };
        if (!hasCore) issues.Add("资源目录没有 ComfyUI 核心，仅提供只读资源访问。");
        var python = Existing(Path.Combine(core, ".venv", "Scripts", "python.exe")) ?? Existing(Path.Combine(root, "envs", "default", "Scripts", "python.exe"));
        return new(Hash("manual|" + root), Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) + "（手动只读）", "manual-readonly", FindExecutable(), root, core, data, user,
            Path.Combine(user, "default", "workflows"), Path.Combine(data, "custom_nodes"), python, [models], models, [],
            Hash(root + source + (hasCore ? File.GetLastWriteTimeUtc(Path.Combine(core, "main.py")).Ticks : 0)), issues)
            { ConfigurationRoot = ConfigurationRoot, DesktopLayout = "manual-readonly", InputDirectory = Path.Combine(data, "input") };
    }

    private static bool HasAssets(string root) => Directory.Exists(Path.Combine(root, "models")) ||
        Directory.Exists(Path.Combine(root, "custom_nodes")) || Directory.Exists(Path.Combine(root, "user"));
    private static bool IsDiscoveryError(Exception ex) => ex is JsonException or IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException;
}
