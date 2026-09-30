using System.IO;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Infrastructure;

namespace FlowPack.Smoke;

// Test-host only. This assembly is not included in the installer or production Worker.
internal sealed record AcceptanceScope(string FixtureRoot, string DesktopProfile, string ExternalDataRoot,
    IReadOnlyList<string> InstanceIds) : IDeploymentCapabilityProvider
{
    public static AcceptanceScope Load(string path)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FlowPack.sln"))) repository = repository.Parent;
        if (repository is null) throw new IOException("隔离验收入口只能从源码工作区运行。");
        var scope = JsonSerializer.Deserialize<AcceptanceScope>(File.ReadAllText(path)) ?? throw new InvalidDataException("验收范围为空。");
        scope.ValidateRoots(Path.Combine(repository.FullName, "artifacts", "acceptance"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyUI FlowPack", "Acceptance"));
        if (!Inside(path, scope.FixtureRoot)) throw new IOException("验收说明必须位于测试夹具目录。");
        ResourceInstallationService.EnsureNoLinks(path);
        return scope;
    }

    internal void ValidateRoots(string fixturesParent, string externalParent)
    {
        if (!Inside(FixtureRoot, fixturesParent) || Same(FixtureRoot, fixturesParent) ||
            !Inside(ExternalDataRoot, externalParent) || Same(ExternalDataRoot, externalParent) ||
            !Inside(DesktopProfile, FixtureRoot) || Same(DesktopProfile, FixtureRoot) ||
            InstanceIds.Count == 0 || InstanceIds.Any(string.IsNullOrWhiteSpace) || InstanceIds.Distinct().Count() != InstanceIds.Count)
            throw new InvalidDataException("验收根目录、配置目录或实例白名单无效。");
        foreach (var root in new[] { FixtureRoot, ExternalDataRoot, DesktopProfile })
        {
            if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            ResourceInstallationService.EnsureNoLinks(root);
        }
    }

    public DeploymentCapability Evaluate(InstanceDescriptor instance, bool requiresPython)
    {
        var reasons = new List<string>();
        try { ValidateInstance(instance); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { reasons.Add(ex.Message); }
        // This is permission to perform a bounded test, never evidence that qualification passed.
        var candidate = new DeploymentCapabilityProvider([
            new("1.0.47", "standalone-native", true, "acceptance-in-progress-not-production-qualified"),
            new("1.0.47", "standalone-adopted", true, "acceptance-in-progress-not-production-qualified")
        ]).Evaluate(instance, requiresPython);
        reasons.AddRange(candidate.Reasons);
        return candidate with { CanInstallFiles = reasons.Count == 0, CanInstallPython = reasons.Count == 0,
            Reasons = reasons.Distinct().ToArray() };
    }

    internal void ValidateInstance(InstanceDescriptor instance)
    {
        if (!InstanceIds.Contains(instance.Id) || instance.ConfigurationRoot is null || !Same(instance.ConfigurationRoot, DesktopProfile))
            throw new IOException("实例不属于指定的隔离 Desktop 配置。");
        var paths = new[] { instance.InstallRoot, instance.CoreDirectory, instance.DataDirectory, instance.UserDirectory,
            instance.WorkflowsDirectory, instance.CustomNodesDirectory, instance.PythonPath, instance.ModelsWriteDirectory,
            instance.InputDirectory }.Concat(instance.ModelRoots).Concat(instance.ExtraPaths.Select(x => x.Path));
        foreach (var path in paths)
        {
            if (path is null || !(Inside(path, FixtureRoot) || Inside(path, ExternalDataRoot)))
                throw new IOException("验收实例包含测试范围以外的有效路径：" + path);
            ResourceInstallationService.EnsureNoLinks(path);
        }
        if (!Inside(instance.PythonPath!, Path.Combine(instance.DataDirectory, ".venv")))
            throw new IOException("验收只允许使用隔离数据目录内的 Python 虚拟环境。");
        ResourceInstallationService.EnsureNoLinks(DesktopProfile);
        // Output isn't a deployment target, but GUI inference must not write into a user's shared output.
        var registrationPath = Path.Combine(DesktopProfile, "installations.json");
        ResourceInstallationService.EnsureNoLinks(registrationPath);
        using var registry = JsonDocument.Parse(File.ReadAllText(registrationPath));
        var record = registry.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetString() == instance.Id);
        if (!record.TryGetProperty("useSharedOutput", out var sharedOutput) || sharedOutput.ValueKind != JsonValueKind.False)
            throw new IOException("隔离运行验收必须显式关闭共享输出。");
        if (record.TryGetProperty("outputDir", out var output) && output.ValueKind == JsonValueKind.String)
            RequireOutputScope(output.GetString()!);
        var arguments = DesktopInstanceDiscovery.ParseArguments(record.TryGetProperty("launchArgs", out var launch) ? launch.GetString() ?? "" : "");
        for (var i = 0; i < arguments.Count; i++)
        {
            var pair = arguments[i].Split('=', 2);
            if (pair[0] != "--output-directory") continue;
            RequireOutputScope(pair.Length == 2 ? pair[1] : ++i < arguments.Count ? arguments[i] : "");
        }
        void RequireOutputScope(string path)
        {
            if (!(Inside(path, FixtureRoot) || Inside(path, ExternalDataRoot))) throw new IOException("验收输出目录越出隔离范围。");
            ResourceInstallationService.EnsureNoLinks(path);
        }
    }

    private static bool Same(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static bool Inside(string path, string root) => Path.IsPathFullyQualified(path) && (Same(path, root) ||
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
}
