using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

public sealed record RuntimeEndpoint(int ProcessId, int Port, DateTimeOffset StartedAt)
{
    public int? ListenerProcessId { get; init; }
    public DateTimeOffset? ListenerStartedAt { get; init; }
}
public interface IRuntimeEndpointResolver
{
    Task<RuntimeEndpoint?> ResolveAsync(InstanceDescriptor instance, IReadOnlyList<InstanceDescriptor> peers, CancellationToken token);
}

/// <summary>Reads official Desktop port locks; never probes arbitrary network hosts or starts an instance.</summary>
public sealed class DesktopRuntimeEndpointResolver(string? configurationRoot = null) : IRuntimeEndpointResolver
{
    public async Task<RuntimeEndpoint?> ResolveAsync(InstanceDescriptor instance, IReadOnlyList<InstanceDescriptor> peers, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || instance.PythonPath is null) return null;
        // Lock files identify only the display name. Shared Python + duplicate names remain ambiguous.
        if (peers.Count(x => x.Name == instance.Name && SamePath(x.PythonPath, instance.PythonPath)) != 1) return null;
        var root = Path.Combine(configurationRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Comfy Desktop"), "port-locks");
        if (!Directory.Exists(root)) return null;
        var candidates = new List<RuntimeEndpoint>();
        foreach (var file in Directory.EnumerateFiles(root, "port-*.json"))
        {
            token.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file).AsSpan(5), out var port) || port is < 1 or > 65535) continue;
            try
            {
                if (new FileInfo(file).Length > 8192) continue;
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file, token));
                var data = json.RootElement;
                if (data.GetProperty("installationName").GetString() != instance.Name) continue;
                using var process = Process.GetProcessById(data.GetProperty("pid").GetInt32());
                var started = new DateTimeOffset(process.StartTime.ToUniversalTime());
                if (process.HasExited || !SamePath(process.MainModule?.FileName, instance.PythonPath) ||
                    started > DateTimeOffset.FromUnixTimeMilliseconds(data.GetProperty("timestamp").GetInt64())) continue;
                candidates.Add(new(process.Id, port, started));
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or System.ComponentModel.Win32Exception) { }
        }
        if (candidates.Count != 1) return null;
        var candidate = candidates[0];
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netstat.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-ano"); start.ArgumentList.Add("-p"); start.ArgumentList.Add("tcp");
        using var network = Process.Start(start) ?? throw new IOException("无法核对本地监听端口。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var output = network.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = network.StandardError.ReadToEndAsync(timeout.Token);
        try { await network.WaitForExitAsync(timeout.Token); }
        catch { if (!network.HasExited) network.Kill(); throw; }
        await error;
        if (network.ExitCode != 0) return null;
        foreach (var line in (await output).Split('\n'))
        {
            var fields = Regex.Split(line.Trim(), @"\s+");
            if (fields.Length != 5 || fields[0] != "TCP" || fields[3] != "LISTENING" ||
                !int.TryParse(fields[4], out var listenerPid) ||
                (fields[1] != "127.0.0.1:" + candidate.Port && fields[1] != "0.0.0.0:" + candidate.Port)) continue;
            if (listenerPid == candidate.ProcessId) return candidate;
            // CPython's Windows venv launcher stays alive while its base interpreter owns the socket.
            // Only accept its immediate child, with the base executable pinned by this venv's config.
            try
            {
                using var child = Process.GetProcessById(listenerPid);
                var childStarted = new DateTimeOffset(child.StartTime.ToUniversalTime());
                if (child.HasExited || childStarted < candidate.StartedAt ||
                    ProcessParent(listenerPid) != candidate.ProcessId || !MatchesVenvBase(instance.PythonPath, child.MainModule?.FileName)) continue;
                return candidate with { ListenerProcessId = listenerPid, ListenerStartedAt = childStarted };
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
        }
        return null;
    }
    internal static bool SamePath(string? first, string? second) => first is not null && second is not null &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    public static bool MatchesVenvBase(string launcher, string? executable)
    {
        if (executable is null || !Path.GetFileName(launcher).Equals("python.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var scripts = Path.GetDirectoryName(launcher);
        if (!string.Equals(Path.GetFileName(scripts), "Scripts", StringComparison.OrdinalIgnoreCase)) return false;
        var config = Path.Combine(Path.GetDirectoryName(scripts)!, "pyvenv.cfg");
        if (!File.Exists(config) || new FileInfo(config).Length > 16384) return false;
        var homes = File.ReadAllLines(config).Select(x => x.Split('=', 2)).Where(x => x.Length == 2 && x[0].Trim() == "home").Select(x => x[1].Trim()).ToArray();
        return homes.Length == 1 && Path.IsPathFullyQualified(homes[0]) && SamePath(Path.Combine(homes[0], "python.exe"), executable);
    }

    private static int? ProcessParent(int pid)
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return null;
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), ExeFile = "" };
        if (!Process32First(snapshot, ref entry)) return null;
        do { if (entry.ProcessId == (uint)pid) return checked((int)entry.ParentProcessId); }
        while (Process32Next(snapshot, ref entry));
        return null;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}

public sealed class RuntimeNodeInspector(HttpClient http, IRuntimeEndpointResolver resolver)
{
    public async Task<ResourceInventory> InspectAsync(ResourceInventory inventory, IReadOnlyList<InstanceDescriptor> peers, CancellationToken token = default)
    {
        inventory = inventory with { RuntimeFingerprint = null, RunningCoreVersion = null, FrontendListVerified = false, FrontendWorkflows = [], FrontendNotice = "尚未核对前端列表", Resources = inventory.Resources.Select(x => x with { RuntimeChecked = false, LoadedNodeTypes = [] }).ToArray() };
        try
        {
            var endpoint = await resolver.ResolveAsync(inventory.Instance, peers, token);
            if (endpoint is null) return inventory with { RuntimeNotice = "未找到可唯一关联的运行实例；节点源码已索引，加载状态待验证。" };
            using var stats = await ReadAsync(endpoint.Port, "system_stats", token);
            var args = stats.RootElement.GetProperty("system").GetProperty("argv").EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new InvalidDataException("运行实例启动参数不是字符串。")).ToArray();
            if (!MatchesPaths(inventory.Instance, args)) return inventory with { RuntimeNotice = "运行服务的启动路径不匹配所选实例，未使用其节点信息。" };
            using var info = await ReadAsync(endpoint.Port, "object_info", token);
            if (info.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("节点信息不是对象。");
            var refreshed = await resolver.ResolveAsync(inventory.Instance, peers, token);
            if (refreshed != endpoint) return inventory with { RuntimeNotice = "读取节点时运行实例发生变化，请重新扫描。" };
            var loaded = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var builtin = new List<string>();
            foreach (var node in info.RootElement.EnumerateObject())
            {
                if (node.Value.ValueKind != JsonValueKind.Object || !node.Value.TryGetProperty("python_module", out var module) || module.ValueKind != JsonValueKind.String) continue;
                var name = module.GetString()!;
                if (name == "nodes" || name.StartsWith("comfy_extras.", StringComparison.Ordinal) || name.StartsWith("comfy_api_nodes.", StringComparison.Ordinal)) builtin.Add(node.Name);
                else if (name.StartsWith("custom_nodes.", StringComparison.Ordinal))
                {
                    var package = name[13..];
                    if (!loaded.TryGetValue(package, out var types)) loaded[package] = types = [];
                    types.Add(node.Name);
                }
            }
            var resources = inventory.Resources.Select(resource =>
            {
                if (resource.Kind != ResourceKind.CustomNode || resource.IsStaged) return resource;
                var package = File.Exists(resource.SourcePath) ? Path.GetFileNameWithoutExtension(resource.SourcePath) : Path.GetFileName(resource.SourcePath);
                var sameNames = inventory.Resources.Count(x => x.Kind == ResourceKind.CustomNode &&
                    (File.Exists(x.SourcePath) ? Path.GetFileNameWithoutExtension(x.SourcePath) : Path.GetFileName(x.SourcePath)) == package);
                var types = sameNames == 1 ? loaded.GetValueOrDefault(package) ?? [] : [];
                return resource with { LoadedNodeTypes = types, NodeTypes = (resource.NodeTypes ?? []).Concat(types).Distinct(StringComparer.Ordinal).ToArray(),
                    RuntimeChecked = true };
            }).ToArray();
            var checkedInventory = inventory with { Resources = resources, CoreNodeTypes = inventory.CoreNodeTypes.Concat(builtin).Distinct(StringComparer.Ordinal).ToArray(),
                RuntimeFingerprint = inventory.Instance.ConfigurationFingerprint, RuntimeNotice = "已核对所选实例的运行时节点信息。",
                RunningCoreVersion = stats.RootElement.GetProperty("system").TryGetProperty("comfyui_version", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString() : null };
            try
            {
                if (args.Contains("--multi-user") || !DesktopRuntimeEndpointResolver.SamePath(inventory.Instance.WorkflowsDirectory,
                    Path.Combine(inventory.Instance.UserDirectory, "default", "workflows")))
                    return checkedInventory with { FrontendNotice = "尚未核对前端列表：当前用户目录未确认。" };
                using var users = await ReadAsync(endpoint.Port, "users", token);
                if (users.RootElement.TryGetProperty("users", out _))
                    return checkedInventory with { FrontendNotice = "尚未核对前端列表：多用户配置需要确认用户。" };
                using var list = await ReadAsync(endpoint.Port, "userdata?dir=workflows&recurse=true&split=false&full_info=true", token);
                var paths = FrontendWorkflowList.Parse(list.RootElement);
                if (await resolver.ResolveAsync(inventory.Instance, peers, token) != endpoint)
                    return checkedInventory with { FrontendNotice = "尚未核对前端列表：运行实例发生变化。" };
                return checkedInventory with { FrontendListVerified = true, FrontendWorkflows = paths, FrontendNotice = "已核对所选实例的前端保存列表" };
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidDataException or OperationCanceledException && !token.IsCancellationRequested)
            { return checkedInventory with { FrontendNotice = "尚未核对前端列表：服务查询失败。" }; }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException && !token.IsCancellationRequested)
        { return inventory with { RuntimeNotice = "运行时节点信息读取失败，保留源码索引：" + ex.Message }; }
    }

    public static bool MatchesPaths(InstanceDescriptor instance, IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Any(x => x is null) || instance.PythonPath is null) return false;
        string? Value(string key)
        {
            string? result = null;
            for (var i = 1; i < args.Count; i++)
                if (args[i].StartsWith(key + "=", StringComparison.Ordinal)) result = args[i][(key.Length + 1)..];
                else if (args[i] == key && i + 1 < args.Count) result = args[++i];
            return result;
        }
        var basis = Value("--base-directory"); var user = Value("--user-directory");
        if (basis is not null && (!Path.IsPathFullyQualified(basis) || !DesktopRuntimeEndpointResolver.SamePath(basis, instance.DataDirectory))) return false;
        if (user is not null && (!Path.IsPathFullyQualified(user) || !DesktopRuntimeEndpointResolver.SamePath(user, instance.UserDirectory))) return false;
        if (Path.IsPathFullyQualified(args[0]))
            return DesktopRuntimeEndpointResolver.SamePath(args[0], Path.Combine(instance.CoreDirectory, "main.py")) &&
                DesktopRuntimeEndpointResolver.SamePath(basis ?? instance.CoreDirectory, instance.DataDirectory) &&
                DesktopRuntimeEndpointResolver.SamePath(user ?? Path.Combine(basis ?? instance.CoreDirectory, "user"), instance.UserDirectory);
        // Desktop's adopted launch uses a relative entrypoint but pins both data and user paths.
        if (args[0].Replace('\\', '/') != "ComfyUI/main.py") return false;
        if (basis is not null && user is not null) return true;
        return instance.PythonPath.StartsWith(Path.TrimEndingDirectorySeparator(instance.InstallRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            DesktopRuntimeEndpointResolver.SamePath(basis ?? instance.CoreDirectory, instance.DataDirectory) &&
            DesktopRuntimeEndpointResolver.SamePath(user ?? Path.Combine(basis ?? instance.CoreDirectory, "user"), instance.UserDirectory);
    }

    private async Task<JsonDocument> ReadAsync(int port, string resource, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8)); token = deadline.Token;
        using var response = await http.GetAsync($"http://127.0.0.1:{port}/{resource}", HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        { if (memory.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("节点信息超过允许大小。"); memory.Write(buffer, 0, count); }
        return JsonDocument.Parse(memory.ToArray());
    }
}
