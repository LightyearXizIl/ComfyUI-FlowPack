using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FlowPack.ComfyUI;

/// <summary>
/// Read-only discovery of a ComfyUI Desktop installation on Windows. It locates the user
/// data root (models / custom_nodes / workflows) without writing anything or starting the
/// target process. Detection is conservative: a directory is only reported when at least one
/// asset directory is present. Paths follow the official ComfyUI Desktop layout
/// (%LOCALAPPDATA%\Comfy-Desktop\ComfyUI-Installs, %APPDATA%\ComfyUI\config.json basePath,
/// ComfyUI-Shared, legacy comfyui-electron, and the running process module).
/// </summary>
public sealed record ComfyDesktopLocation(
    string DetectedVia,
    string BasePath,
    string? InstallRoot,
    string? ModelsDirectory,
    string? CustomNodesDirectory,
    string? WorkflowsDirectory,
    string? PythonPath,
    DateTimeOffset InspectedAt)
{
    public InstanceDescriptor? Instance { get; init; }
    public bool HasAnyAssetDirectory =>
        ModelsDirectory is not null || CustomNodesDirectory is not null || WorkflowsDirectory is not null;
}

public interface IComfyDesktopDetector
{
    Task<ComfyDesktopLocation?> DetectAsync(CancellationToken cancellationToken = default);
}

public interface IDesktopPathProvider
{
    string LocalApplicationData { get; }
    string RoamingApplicationData { get; }
    string MyDocuments { get; }
    string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory)!;
    IReadOnlyList<string> GetProcessModulePaths(string processName);
    IReadOnlyList<string> GetRegisteredDesktopExecutables() => [];
    /// <summary>Optional process command-line evidence. Implementations may return an empty list when unavailable.</summary>
    IReadOnlyList<string> GetProcessCommandLines(string processName) => [];
    /// <summary>Optional user-data roots supplied by the host or launcher.</summary>
    IReadOnlyList<string> GetCustomUserDataDirectories() => [];
}

public sealed class EnvironmentDesktopPathProvider : IDesktopPathProvider
{
    public IReadOnlyList<string> GetProcessCommandLines(string processName)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var result = new List<string>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                var handle = OpenProcess(0x1000, false, process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
                if (handle == IntPtr.Zero) continue;
                try
                {
                    // ProcessCommandLineInformation reads the same bounded command-line
                    // evidence as Task Manager without spawning a visible shell or WMI.
                    NtQueryInformationProcess(handle, 60, IntPtr.Zero, 0, out var size);
                    if (size <= Marshal.SizeOf<UnicodeString>() || size > 1024 * 1024) continue;
                    var buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (NtQueryInformationProcess(handle, 60, buffer, size, out _) != 0) continue;
                        var text = Marshal.PtrToStructure<UnicodeString>(buffer);
                        if (text.Length > 0 && text.Buffer != IntPtr.Zero)
                        {
                            var value = Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
                            if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { }
                finally { CloseHandle(handle); }
            }
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int informationLength, out int returnLength);

    public IReadOnlyList<string> GetRegisteredDesktopExecutables()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var result = new List<string>();
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
        {
            try
            {
                using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = key.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(name);
                    if (app?.GetValue("DisplayName") is not string display || !display.StartsWith("Comfy Desktop", StringComparison.OrdinalIgnoreCase)) continue;
                    if (app.GetValue("InstallLocation") is string root && File.Exists(Path.Combine(root, "Comfy Desktop.exe"))) result.Add(Path.Combine(root, "Comfy Desktop.exe"));
                    if (app.GetValue("DisplayIcon") is string icon)
                    {
                        var path = icon.Split(',')[0].Trim('"');
                        if (File.Exists(path) && Path.GetFileName(path).Equals("Comfy Desktop.exe", StringComparison.OrdinalIgnoreCase)) result.Add(path);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public string LocalApplicationData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string RoamingApplicationData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string MyDocuments => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public IReadOnlyList<string> GetProcessModulePaths(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName)
                .Select(p => { try { return p.MainModule?.FileName; } catch { return null; } })
                .Where(f => !string.IsNullOrWhiteSpace(f))!
                .ToList()!;
        }
        catch
        {
            return [];
        }
    }

}

public sealed class ComfyDesktopDetector : IComfyDesktopDetector
{
    private readonly IDesktopPathProvider _provider;

    public ComfyDesktopDetector(IDesktopPathProvider? provider = null)
    {
        _provider = provider ?? new EnvironmentDesktopPathProvider();
    }

    public Task<ComfyDesktopLocation?> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult<ComfyDesktopLocation?>(null);
        }

        ComfyDesktopLocation? best = null;
        var bestScore = 0;
        foreach (var root in CollectCandidateRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var located = LocateDirectories(root.Path);
            if (located is null) continue;
            var score = (located.Value.Models is not null ? 1 : 0)
                        + (located.Value.CustomNodes is not null ? 1 : 0)
                        + (located.Value.Workflows is not null ? 1 : 0);
            if (score == 0) continue;
            if (score <= bestScore) continue;
            best = new ComfyDesktopLocation(
                root.Source, root.Path, root.InstallRoot,
                located.Value.Models, located.Value.CustomNodes, located.Value.Workflows,
                located.Value.Python, DateTimeOffset.UtcNow);
            bestScore = score;
        }

        return Task.FromResult(best);
    }

    private IEnumerable<(string Path, string Source, string? InstallRoot)> CollectCandidateRoots()
    {
        var local = _provider.LocalApplicationData;
        var roaming = _provider.RoamingApplicationData;
        var docs = _provider.MyDocuments;

        var configPath = Path.Combine(roaming, "ComfyUI", "config.json");
        var basePath = File.Exists(configPath) ? ReadBasePath(configPath) : null;
        if (!string.IsNullOrWhiteSpace(basePath) && Directory.Exists(basePath))
        {
            yield return (basePath, "config.json", null);
        }

        var installs = Path.Combine(local, "Comfy-Desktop", "ComfyUI-Installs");
        if (Directory.Exists(installs))
        {
            foreach (var dir in Directory.EnumerateDirectories(installs))
            {
                yield return (dir, "ComfyUI-Installs", dir);
            }
        }

        var shared = Path.Combine(local, "Comfy-Desktop", "ComfyUI-Shared");
        if (Directory.Exists(shared)) yield return (shared, "ComfyUI-Shared", null);

        var legacy = Path.Combine(local, "Programs", "comfyui-electron");
        if (Directory.Exists(legacy)) yield return (legacy, "comfyui-electron", null);

        var docsBase = Path.Combine(docs, "ComfyUI");
        if (Directory.Exists(docsBase)) yield return (docsBase, "Documents", null);

        foreach (var module in _provider.GetProcessModulePaths("ComfyUI-Desktop")
                     .Concat(_provider.GetProcessModulePaths("comfyui-electron")))
        {
            var dir = Path.GetDirectoryName(module);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) yield return (dir, "process", null);
        }
    }

    private static string? ReadBasePath(string configPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("basePath", out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static (string? Models, string? CustomNodes, string? Workflows, string? Python)? LocateDirectories(string root)
    {
        var models = FirstExisting(root, "models", Path.Combine("ComfyUI", "models"));
        var customNodes = FirstExisting(root, "custom_nodes", Path.Combine("ComfyUI", "custom_nodes"));
        var workflows = FirstExisting(
            root,
            Path.Combine("user", "default", "workflows"),
            Path.Combine("ComfyUI", "user", "default", "workflows"));
        var python = FirstExisting(
            root,
            Path.Combine("python_embeded", "python.exe"),
            Path.Combine(".venv", "Scripts", "python.exe"),
            Path.Combine("ComfyUI", "python_embeded", "python.exe"));
        if (models is null && customNodes is null && workflows is null) return null;
        return (models, customNodes, workflows, python);
    }

    private static string? FirstExisting(string root, params string[] relatives)
    {
        foreach (var relative in relatives)
        {
            var full = Path.Combine(root, relative);
            if (Directory.Exists(full) || File.Exists(full)) return full;
        }

        return null;
    }
}
