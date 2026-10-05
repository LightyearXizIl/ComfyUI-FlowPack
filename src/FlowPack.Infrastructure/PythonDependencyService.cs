using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using FlowPack.ComfyUI;

namespace FlowPack.Infrastructure;

public sealed record PythonWheelFile(string Name, string Version, string Path, string Sha256);
public sealed record PythonDependencyPlan(string PythonPath, IReadOnlyDictionary<string, string> Baseline, IReadOnlyList<PythonWheelFile> Wheels);
public sealed record PythonRecoveryIssue(string PlanId, string? PythonPath, string Message);
public sealed record PythonDependencyInputs(IReadOnlyList<PlannedDeployment> NodeFiles,
    IReadOnlyList<PlannedDeployment> RequirementFiles, IReadOnlyList<PlannedDeployment> ProjectFiles,
    IReadOnlyList<string> DeclaredRequirements)
{
    // Metadata presence is deliberately conservative: empty files and projects that only
    // constrain Python still need a qualified Python path before parsing/preparing them.
    public bool RequiresPythonDependencies => RequirementFiles.Count > 0 || ProjectFiles.Count > 0 || DeclaredRequirements.Count > 0;
}

/// <summary>Resolves compatible wheels against pinned installed versions; never executes node installation scripts.</summary>
public sealed class PythonDependencyService(string libraryPath)
{
    public static PythonDependencyInputs Inspect(ResourceInstallPlan plan)
    {
        var nodeRoot = Path.GetFullPath(plan.Instance.CustomNodesDirectory);
        var nodes = plan.Files.Where(x => ResourceImportService.Inside(nodeRoot, Path.GetFullPath(x.TargetPath)) ||
            x.TargetPath.Replace('\\', '/').Contains("/custom_nodes/", StringComparison.OrdinalIgnoreCase)).ToArray();
        // Use target names, not staging file names: staging may rename a source while the
        // installed metadata still governs the node's dependency preparation.
        return new(nodes, nodes.Where(x => Path.GetFileName(x.TargetPath).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)).ToArray(),
            nodes.Where(x => Path.GetFileName(x.TargetPath).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)).ToArray(), plan.PythonRequirements);
    }

    public async Task<PythonDependencyPlan?> PrepareAsync(ResourceInstallPlan plan, CancellationToken token = default)
    {
        var sources = Inspect(plan);
        if (sources.NodeFiles.Count > 0 && plan.Instance.PythonPath is { } targetPython) await EnsureNoPendingRepairAsync(targetPython, token);
        if (sources.NodeFiles.Any(x => new[] { "install.py", "install.bat", "install.sh" }.Contains(Path.GetFileName(x.TargetPath), StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("节点包含特殊安装脚本，需要专门适配；不会自动执行脚本。");
        if (!sources.RequiresPythonDependencies) return null;
        var python = plan.Instance.PythonPath ?? throw new InvalidDataException("缺少目标 Python。");
        var directory = Path.Combine(libraryPath, "staging", "python", plan.Id); Directory.CreateDirectory(directory);
        var input = new List<string>();
        foreach (var requirement in sources.DeclaredRequirements) AddRequirement(requirement);
        foreach (var projectFile in sources.ProjectFiles)
        {
            var output = await RunAsync(python, ["-I", "-c", "import tomllib,json,sys; p=tomllib.load(open(sys.argv[1],'rb')).get('project',{}); print(json.dumps({'dependencies':p.get('dependencies',[]),'dynamic':p.get('dynamic',[]),'python':p.get('requires-python')}))", projectFile.SourcePath], token);
            using var project = JsonDocument.Parse(output);
            if (project.RootElement.GetProperty("dynamic").EnumerateArray().Any(x => x.GetString() == "dependencies"))
                throw new InvalidDataException("节点动态计算 Python 依赖，需要专门适配。");
            foreach (var requirement in project.RootElement.GetProperty("dependencies").EnumerateArray()) AddRequirement(requirement.GetString()!);
            if (project.RootElement.GetProperty("python").ValueKind == JsonValueKind.String)
                await RunAsync(python, ["-I", "-c", "import sys; from packaging.specifiers import SpecifierSet; assert SpecifierSet(sys.argv[1]).contains('.'.join(map(str,sys.version_info[:3]))), 'Node requires a different Python version'", project.RootElement.GetProperty("python").GetString()!], token);
        }
        foreach (var file in sources.RequirementFiles)
        {
            foreach (var raw in await File.ReadAllLinesAsync(file.SourcePath, token))
            {
                AddRequirement(raw);
            }
        }
        if (input.Count == 0) return null;
        var baseline = await SnapshotAsync(python, token);
        var constraints = Path.Combine(directory, "installed.txt");
        var requested = Path.Combine(directory, "requirements.txt");
        var reportPath = Path.Combine(directory, "resolution.json");
        await File.WriteAllLinesAsync(constraints, baseline.Select(p => p.Key + "==" + p.Value), token);
        await File.WriteAllLinesAsync(requested, input.Distinct(), token);
        await RunAsync(python, ["-I", "-m", "pip", "--isolated", "install", "--dry-run", "--report", reportPath,
            "--only-binary=:all:", "--disable-pip-version-check", "--index-url", "https://pypi.org/simple", "-c", constraints, "-r", requested], token);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, token));
        var wheels = new List<PythonWheelFile>();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var install in report.RootElement.GetProperty("install").EnumerateArray())
        {
            var metadata = install.GetProperty("metadata");
            var name = Normalize(metadata.GetProperty("name").GetString()!);
            var version = metadata.GetProperty("version").GetString()!;
            if (baseline.TryGetValue(name, out var prior))
            { if (prior != version) throw new InvalidDataException("Python 依赖冲突：" + name + " " + prior + " → " + version); continue; }
            var info = install.GetProperty("download_info");
            var uri = new Uri(info.GetProperty("url").GetString()!);
            var fileName = Path.GetFileName(uri.AbsolutePath);
            if (!fileName.EndsWith(".whl", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("只允许已构建 wheel，不能从源码安装。");
            var hash = info.GetProperty("archive_info").GetProperty("hashes").GetProperty("sha256").GetString()!;
            var target = Path.Combine(directory, fileName);
            var downloaded = await new VerifiedDownloadService(http).DownloadAsync(new(uri, target, hash), token);
            wheels.Add(new(name, version, target, downloaded.Sha256));
        }
        return new(python, baseline, wheels);
        void AddRequirement(string raw)
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#')) return;
            if (line.StartsWith('-') || line.Contains("http:") || line.Contains("https:") || line.Contains("git+") || line.Contains('@') || line.Contains('\\'))
                throw new InvalidDataException("节点依赖包含选项、源码或直接地址，需要专门处理：" + line);
            if (!Regex.IsMatch(line, "^[A-Za-z0-9][A-Za-z0-9._-]*", RegexOptions.None, TimeSpan.FromSeconds(1)))
                throw new InvalidDataException("节点依赖不是可解析的包声明。");
            input.Add(line);
        }
    }

    public async Task InstallAsync(string installationId, PythonDependencyPlan plan, CancellationToken token = default)
    {
        if (plan.Wheels.Count == 0) return;
        await EnsureNoPendingRepairAsync(plan.PythonPath, token);
        var now = await SnapshotAsync(plan.PythonPath, token);
        if (now.Count != plan.Baseline.Count || plan.Baseline.Any(p => !now.TryGetValue(p.Key, out var v) || v != p.Value))
            throw new InvalidDataException("Python 环境在解析后已改变，需要重新检查。");
        foreach (var wheel in plan.Wheels)
            if (await ResourceImportService.HashAsync(wheel.Path, token) != wheel.Sha256) throw new InvalidDataException("Python wheel 校验失败。");
        var path = Path.Combine(libraryPath, "state", "python-attempts", installationId + ".json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteAsync("Installing", null);
        try
        {
            await RunAsync(plan.PythonPath, new[] { "-I", "-m", "pip", "--isolated", "install", "--no-index", "--no-deps", "--no-compile", "--disable-pip-version-check" }.Concat(plan.Wheels.Select(x => x.Path)).ToArray(), token);
            var after = await SnapshotAsync(plan.PythonPath, token);
            if (plan.Baseline.Any(p => !after.TryGetValue(p.Key, out var v) || v != p.Value) || plan.Wheels.Any(p => !after.TryGetValue(p.Name, out var v) || v != p.Version))
                throw new InvalidDataException("Python 安装后环境与计划不一致。");
            await WriteAsync("Completed", after);
        }
        catch
        {
            IReadOnlyDictionary<string, string>? after = null;
            try { after = await SnapshotAsync(plan.PythonPath, CancellationToken.None); } catch { }
            await WriteAsync("NeedsRepair", after);
            throw new IOException("Python 依赖安装未完整完成，已保存前后清单；需要修复，不会自动卸载原有包。");
        }
        async Task WriteAsync(string state, IReadOnlyDictionary<string, string>? after)
        {
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { state, plan.PythonPath, before = plan.Baseline, after, planned = plan.Wheels }), CancellationToken.None);
            File.Move(temporary, path, true);
        }
    }

    public async Task<IReadOnlyList<PythonRecoveryIssue>> RecoverAsync(CancellationToken token = default)
    {
        var root = Path.Combine(libraryPath, "state", "python-attempts");
        var issues = new List<PythonRecoveryIssue>();
        if (!Directory.Exists(root)) return issues;
        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
            ResourceInstallationService.EnsureNoLinks(file);
            var record = JsonNode.Parse(await File.ReadAllTextAsync(file, token))?.AsObject()
                ?? throw new InvalidDataException("Python 安装日志无法读取，请检查：" + file);
            var state = record["state"]?.GetValue<string>();
            if (state is not ("Installing" or "NeedsRepair" or "Completed"))
                throw new InvalidDataException("Python 安装日志状态未知，不能确认环境安全。");
            if (state == "Completed") continue;
            if (state == "Installing")
            {
                record["state"] = "NeedsRepair";
                record["recoveryNotice"] = "Python 安装被中断；原清单与计划保留，尚未验证环境，不能视为已回滚。";
                var temporary = file + ".tmp";
                await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, record, cancellationToken: token);
                    await stream.FlushAsync(token); stream.Flush(true);
                }
                File.Move(temporary, file, true);
            }
            issues.Add(new(Path.GetFileNameWithoutExtension(file), record["PythonPath"]?.GetValue<string>(),
                "Python 环境需要修复；保留了安装前清单，不会自动卸载或恢复原有包。日志：" + file));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
            {
                // Unknown ownership must block Python writes, but not unrelated read-only Worker use.
                issues.Add(new(Path.GetFileNameWithoutExtension(file), null,
                    "Python 恢复日志无法核验，需要人工修复；原文件已保留，不会继续修改 Python 环境。日志：" + file + "；" + ex.Message));
            }
        }
        return issues;
    }

    private async Task EnsureNoPendingRepairAsync(string python, CancellationToken token)
    {
        var pending = (await RecoverAsync(token)).FirstOrDefault(x => x.PythonPath is null ||
            string.Equals(Path.GetFullPath(x.PythonPath), Path.GetFullPath(python), StringComparison.OrdinalIgnoreCase));
        if (pending is not null) throw new InvalidDataException(pending.Message);
    }

    public static async Task<IReadOnlyDictionary<string, string>> SnapshotAsync(string python, CancellationToken token) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(await RunAsync(python, ["-I", "-c", "import importlib.metadata as m,json,re; print(json.dumps({re.sub(r'[-_.]+','-',d.metadata['Name']).lower():d.version for d in m.distributions() if d.metadata['Name']}))"], token)) ?? throw new InvalidDataException("Python 包清单无效。");

    private static string Normalize(string name) => Regex.Replace(name.ToLowerInvariant(), "[-_.]+", "-");
    private static async Task<string> RunAsync(string python, IReadOnlyList<string> args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["PYTHONUTF8"] = "1";
        using var process = Process.Start(start) ?? throw new IOException("无法启动目标 Python。");
        // Keep draining the pipes during cancellation until the owned process exits.
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        var output = await stdout; var error = await stderr;
        if (process.ExitCode != 0) throw new IOException("Python 依赖检查或安装失败：" + (error.Length > 3000 ? error[^3000..] : error));
        return output;
    }
}
