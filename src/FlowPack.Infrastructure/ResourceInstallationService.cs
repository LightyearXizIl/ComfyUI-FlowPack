using System.Diagnostics;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public sealed record PlannedDeployment(string SourcePath, string TargetPath, long SizeBytes, string Sha256, bool Reuse);
public sealed record ResourceInstallPlan(string Id, InstanceDescriptor Instance, IReadOnlyList<PlannedDeployment> Files,
    IReadOnlyList<string> BlockingReasons, long RequiredBytes, DateTimeOffset CreatedAt)
{
    public IReadOnlyList<string> PythonRequirements { get; init; } = [];
    public DeploymentCapability? Capability { get; init; }
}
public sealed record DeploymentJournal(string PlanId, string State, IReadOnlyList<JournalFile> Files, string? Error = null);
public sealed record JournalFile(string TargetPath, string Sha256, string State)
{
    public string? TemporaryPath { get; init; }
    public string? RecoveryPath { get; init; }
}

/// <summary>Creates and applies immutable file plans. Caller must hold the Worker library lease.</summary>
public sealed class ResourceInstallationService(string libraryPath)
{
    public async Task<ResourceInstallPlan> PlanAsync(InstanceDescriptor instance, IReadOnlyList<ImportResource> resources, CancellationToken token = default)
    {
        var blockers = new List<string>(instance.Issues);
        if (!instance.IsModern) blockers.Add("旧版只支持检测和导出。");
        var stagedInventory = ImportInventoryService.Merge(new(instance, [], [], []), resources);
        blockers.AddRange(stagedInventory.Resources.Where(x => x.ModelDirectoryRoot is not null).SelectMany(x => x.IntegrityIssues));
        var files = new List<PlannedDeployment>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources)
        {
            token.ThrowIfCancellationRequested();
            if (resource.State != RecognitionState.Confirmed) { blockers.Add("资源用途尚未确认：" + resource.OriginalPath); continue; }
            var target = ResolveTarget(instance, resource.TargetRelativePath);
            if (resource.Kind == ResourceKind.Model && !File.Exists(target))
            {
                var parts = resource.TargetRelativePath.Replace('\\', '/').Split('/');
                if (parts.Length >= 3)
                {
                    var alternate = instance.ModelRoots.Select(root => Path.Combine(root, Path.Combine(parts.Skip(1).ToArray())))
                        .Concat(instance.ExtraPaths.Where(x => ResourceFiles.NormalizeCategory(x.Category) == ResourceFiles.NormalizeCategory(parts[1]))
                            .Select(x => Path.Combine(x.Path, Path.Combine(parts.Skip(2).ToArray()))));
                    foreach (var candidate in alternate.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        EnsureNoLinks(candidate);
                        if (File.Exists(candidate) && string.Equals(await ResourceImportService.HashAsync(candidate, token), resource.Sha256, StringComparison.OrdinalIgnoreCase))
                        { target = candidate; break; }
                    }
                }
            }
            EnsureNoLinks(target);
            if (!targets.Add(target)) { blockers.Add("重复部署目标：" + resource.TargetRelativePath); continue; }
            var sourceHash = await ResourceImportService.HashAsync(resource.SourcePath, token);
            if (sourceHash != resource.Sha256) { blockers.Add("导入后源文件发生变化：" + resource.OriginalPath); continue; }
            var exists = File.Exists(target);
            var reuse = exists && (await ResourceImportService.HashAsync(target, token)).Equals(resource.Sha256, StringComparison.OrdinalIgnoreCase);
            if (exists && !reuse) blockers.Add("同名异内容，不能覆盖：" + resource.TargetRelativePath);
            if (Directory.Exists(target)) blockers.Add("目标被目录占用：" + resource.TargetRelativePath);
            files.Add(new(resource.SourcePath, target, new FileInfo(resource.SourcePath).Length, resource.Sha256, reuse));
        }
        if (files.Count == 0) blockers.Add("没有可安装的资源。");
        foreach (var package in resources.Where(x => x.TargetRelativePath.Replace('\\', '/').StartsWith("custom_nodes/", StringComparison.Ordinal))
                     .GroupBy(x => x.TargetRelativePath.Replace('\\', '/').Split('/')[1], StringComparer.OrdinalIgnoreCase))
        {
            var root = Path.Combine(instance.CustomNodesDirectory, package.Key);
            var expected = package.Select(x => ResolveTarget(instance, x.TargetRelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (ResourceFiles.Enumerate(root).Any(x => !expected.Contains(x)))
                blockers.Add("已有节点包含计划外源码，不能合并覆盖：" + package.Key);
        }
        CheckSpace(files, blockers);
        return new(Guid.NewGuid().ToString("N"), instance, files, blockers.Distinct().ToArray(), files.Where(x => !x.Reuse).Sum(x => x.SizeBytes), DateTimeOffset.UtcNow)
            { PythonRequirements = resources.SelectMany(x => x.DeclaredPythonDependencies).Distinct().ToArray() };
    }

    public async Task ExecuteAsync(ResourceInstallPlan plan, Func<InstanceDescriptor, CancellationToken, Task<InstanceDescriptor>> refresh,
        IProgress<string>? progress = null, CancellationToken token = default,
        Func<CancellationToken, Task>? prepareEnvironment = null, Func<CancellationToken, Task>? installEnvironment = null)
    {
        if (!Guid.TryParseExact(plan.Id, "N", out _)) throw new InvalidDataException("安装计划标识无效。");
        if (plan.BlockingReasons.Count > 0) throw new InvalidDataException(string.Join("\n", plan.BlockingReasons));
        using var instanceLease = await AcquireLeaseAsync("instance:" + plan.Instance.Id + ":" + plan.Instance.DataDirectory, progress, token);
        var leases = new List<FileStream>();
        try
        {
            if (plan.Instance.PythonPath is not null)
                leases.Add(await AcquireLeaseAsync("python:" + Path.GetFullPath(plan.Instance.PythonPath), progress, token));
            foreach (var target in plan.Files.Select(x => x.TargetPath.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal))
                leases.Add(await AcquireLeaseAsync("target:" + target, progress, token));
            // Unknown recovery evidence cannot authorize another write, even with a fresh plan.
            var journalDirectory = Path.Combine(libraryPath, "state", "journal");
            if (Directory.Exists(journalDirectory))
                foreach (var journalFile in Directory.EnumerateFiles(journalDirectory, "*.json"))
                    try { await ReadJournalAsync(journalFile, token); }
                    catch (Exception ex) when (IsRecoveryError(ex))
                    { throw new InvalidDataException("安装恢复日志需要人工修复，尚未写入目标。日志：" + journalFile, ex); }
            async Task VerifyEnvironmentAsync()
            {
                var current = await refresh(plan.Instance, token);
                if (current.ConfigurationFingerprint != plan.Instance.ConfigurationFingerprint || current.Issues.Count > 0)
                    throw new InvalidDataException("Desktop 配置或 Python 已改变，请重新生成安装计划。");
                EnsurePythonStopped(current);
                var nodeRoot = Path.GetFullPath(plan.Instance.CustomNodesDirectory);
                var expected = plan.Files.Select(x => Path.GetFullPath(x.TargetPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var package in expected.Where(x => ResourceImportService.Inside(nodeRoot, x))
                             .Select(x => Path.GetRelativePath(nodeRoot, x).Split(Path.DirectorySeparatorChar)[0]).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (ResourceFiles.Enumerate(Path.Combine(nodeRoot, package)).Any(x => !expected.Contains(x) && !x.EndsWith(".flowpack-" + plan.Id + ".tmp", StringComparison.Ordinal)))
                        throw new IOException("已有节点出现计划外源码，请重新检查：" + package);
            }
            await VerifyEnvironmentAsync();
            var spaceIssues = new List<string>(); CheckSpace(plan.Files, spaceIssues);
            if (spaceIssues.Count > 0) throw new IOException(string.Join("\n", spaceIssues));
            var journal = new DeploymentJournal(plan.Id, "Running", []);
            await SaveJournalAsync(journal, token);
            try
            {
                if (prepareEnvironment is not null) await prepareEnvironment(token);
                await VerifyEnvironmentAsync();
                foreach (var file in plan.Files)
                {
                    token.ThrowIfCancellationRequested();
                    await VerifyEnvironmentAsync();
                    EnsureNoLinks(file.SourcePath);
                    EnsureNoLinks(file.TargetPath);
                    if (await ResourceImportService.HashAsync(file.SourcePath, token) != file.Sha256)
                        throw new IOException("安装源文件已改变。");
                    if (File.Exists(file.TargetPath))
                    {
                        if (await ResourceImportService.HashAsync(file.TargetPath, token) == file.Sha256) continue;
                        throw new IOException("目标文件在安装前发生冲突。");
                    }
                    if (file.Reuse) throw new IOException("计划复用的目标文件已被移走，请重新检查。");
                    progress?.Report("正在部署 " + Path.GetFileName(file.TargetPath));
                    var temporary = file.TargetPath + ".flowpack-" + plan.Id + ".tmp";
                    journal = journal with { Files = journal.Files.Append(new JournalFile(file.TargetPath, file.Sha256, "Intent") { TemporaryPath = temporary }).ToArray() };
                    await SaveJournalAsync(journal, token);
                    Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);
                    try
                    {
                        await using (var input = File.OpenRead(file.SourcePath))
                        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                        { await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(flushToDisk: true); }
                        if (await ResourceImportService.HashAsync(temporary, token) != file.Sha256) throw new IOException("部署文件校验失败。");
                        await VerifyEnvironmentAsync();
                        EnsureNoLinks(file.TargetPath);
                        File.Move(temporary, file.TargetPath, overwrite: false);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    journal = journal with { Files = journal.Files.Select(x => x.TargetPath == file.TargetPath ? x with { State = "Committed" } : x).ToArray() };
                    await SaveJournalAsync(journal, token);
                }
                if (installEnvironment is not null)
                {
                    await VerifyEnvironmentAsync();
                    await SaveJournalAsync(journal with { State = "InstallingPython" }, token);
                    await installEnvironment(token);
                }
                await SaveJournalAsync(journal with { State = "FilesDeployed" }, token);
            }
            catch (Exception ex)
            {
                await SaveJournalAsync(journal with { State = "NeedsReview", Error = ex.Message }, CancellationToken.None);
                throw;
            }
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    public async Task<IReadOnlyList<DeploymentJournal>> RecoverAsync(CancellationToken token = default)
    {
        var path = Path.Combine(libraryPath, "state", "journal");
        if (!Directory.Exists(path)) return [];
        var reports = new List<DeploymentJournal>();
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
            var journal = await ReadJournalAsync(file, token);
            if (journal.State == "FilesDeployed") continue;
            var items = new List<JournalFile>();
            foreach (var item in journal.Files)
            {
                EnsureNoLinks(item.TargetPath);
                var recovered = item;
                // Only a temporary name recorded before copying is eligible. Never sweep the target directory.
                if (item.TemporaryPath is { } temporary && File.Exists(temporary))
                {
                    if (!string.Equals(Path.GetFullPath(temporary), Path.GetFullPath(item.TargetPath) + ".flowpack-" + journal.PlanId + ".tmp", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("恢复暂存路径与安装日志不一致。");
                    EnsureNoLinks(temporary);
                    var quarantine = Path.Combine(libraryPath, "state", "recovered-files", journal.PlanId, items.Count + ".tmp");
                    EnsureNoLinks(quarantine);
                    Directory.CreateDirectory(Path.GetDirectoryName(quarantine)!);
                    recovered = item with { RecoveryPath = quarantine };
                    journal = journal with { Files = journal.Files.Select(x => x.TargetPath == item.TargetPath ? recovered : x).ToArray() };
                    await SaveJournalAsync(journal, token);
                    // Retain partial bytes for inspection. If a backup already exists, do not overwrite either file.
                    if (!File.Exists(quarantine) && !Directory.Exists(quarantine))
                    {
                        try { File.Move(temporary, quarantine, overwrite: false); }
                        catch (IOException) when (File.Exists(quarantine) || Directory.Exists(quarantine))
                        { /* A backup appeared after the check. Preserve both and report below. */ }
                    }
                    if (File.Exists(temporary))
                    {
                        var conflict = "恢复备份已存在，原暂存与备份均保留，需人工核对：" + temporary + "；" + quarantine;
                        if (journal.Error?.Contains(conflict, StringComparison.Ordinal) != true)
                            journal = journal with { Error = string.IsNullOrEmpty(journal.Error) ? conflict : journal.Error + "\n" + conflict };
                    }
                }
                var state = !File.Exists(item.TargetPath) ? "NotPresent" : await ResourceImportService.HashAsync(item.TargetPath, token) == item.Sha256 ? "VerifiedPresent" : "ExternallyModified";
                items.Add(recovered with { State = state });
            }
            var report = journal with { State = "NeedsReview", Files = items, Error = journal.Error ?? "上次部署中断；已核对文件并保留暂存，需重新预览；Python 状态须单独检查。" };
            await SaveJournalAsync(report, token); reports.Add(report);
            }
            catch (Exception ex) when (IsRecoveryError(ex))
            {
                // Do not rewrite untrusted or unreadable evidence, or infer that Python was rolled back.
                reports.Add(new(Path.GetFileNameWithoutExtension(file), "NeedsRepair", [],
                    "安装恢复日志无法核验，需要人工修复；原日志保留。日志：" + file + "；" + ex.Message));
            }
        }
        return reports;
    }

    private static bool IsRecoveryError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException
        or JsonException or InvalidOperationException or ArgumentException;

    private static async Task<DeploymentJournal> ReadJournalAsync(string file, CancellationToken token)
    {
        EnsureNoLinks(file);
        var journal = JsonSerializer.Deserialize<DeploymentJournal>(await File.ReadAllTextAsync(file, token));
        if (journal is null || !Guid.TryParseExact(journal.PlanId, "N", out _) ||
            !string.Equals(journal.PlanId, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("恢复日志的计划标识与文件名不符。");
        if (journal.State is not ("Running" or "InstallingPython" or "FilesDeployed" or "NeedsReview") || journal.Files is null)
            throw new InvalidDataException("恢复日志状态或文件列表无效。");
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Validate the entire record before moving any recorded temporary file.
        foreach (var item in journal.Files)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.TargetPath) || !Path.IsPathFullyQualified(item.TargetPath) ||
                string.IsNullOrWhiteSpace(item.Sha256) || item.State is not
                    ("Intent" or "Committed" or "NotPresent" or "VerifiedPresent" or "ExternallyModified"))
                throw new InvalidDataException("恢复日志包含无效文件记录。");
            var target = Path.GetFullPath(item.TargetPath);
            if (!targets.Add(target)) throw new InvalidDataException("恢复日志包含重复目标。");
            EnsureNoLinks(target);
            if (item.TemporaryPath is { } temporary)
            {
                if (!Path.IsPathFullyQualified(temporary) || !string.Equals(Path.GetFullPath(temporary),
                    target + ".flowpack-" + journal.PlanId + ".tmp", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("恢复暂存路径与安装日志不一致。");
                EnsureNoLinks(temporary);
            }
        }
        return journal;
    }

    public static string ResolveTarget(InstanceDescriptor instance, string relative)
    {
        PlannedZipExportService.ValidateRelative(relative);
        var parts = relative.Replace('\\', '/').Split('/');
        var root = parts[0] switch
        {
            "workflows" => instance.WorkflowsDirectory,
            "models" when parts.Length >= 3 => instance.ModelsWriteDirectory,
            "custom_nodes" when parts.Length >= 3 || parts.Length == 2 && parts[1].EndsWith(".py", StringComparison.OrdinalIgnoreCase) => instance.CustomNodesDirectory,
            "input" when parts.Length >= 2 => instance.InputDirectory ?? throw new InvalidDataException("实例输入目录尚未确定。"),
            _ => throw new InvalidDataException("不支持的资源部署用途：" + relative)
        };
        var target = Path.GetFullPath(Path.Combine(root, Path.Combine(parts.Skip(1).ToArray())));
        if (parts[0] == "models")
        {
            var categoryDefault = instance.ExtraPaths.LastOrDefault(x => x.IsDefault && ResourceFiles.NormalizeCategory(x.Category) == ResourceFiles.NormalizeCategory(parts[1]));
            if (categoryDefault is not null)
            {
                root = categoryDefault.Path;
                target = Path.GetFullPath(Path.Combine(root, Path.Combine(parts.Skip(2).ToArray())));
            }
        }
        if (!ResourceImportService.Inside(root, target)) throw new InvalidDataException("资源目标越界。");
        if (target.Replace('\\', '/').Contains("/resources/ComfyUI/", StringComparison.OrdinalIgnoreCase) ||
            target.Replace('\\', '/').Contains("/resource/ComfyUI/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("不能写入 Desktop 程序内置核心目录。");
        return target;
    }

    private async Task SaveJournalAsync(DeploymentJournal journal, CancellationToken token)
    {
        var dir = Path.Combine(libraryPath, "state", "journal"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, journal.PlanId + ".json");
        var temporary = file + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(stream, journal, cancellationToken: token);
            await stream.FlushAsync(token);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, file, true);
    }
    private static async Task<FileStream> AcquireLeaseAsync(string identity, IProgress<string>? progress, CancellationToken token)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyUI FlowPack", "locks");
        Directory.CreateDirectory(root);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity.ToUpperInvariant())));
        var announced = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(root, hash + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                if (!announced) { progress?.Report("等待其他任务释放实例或共享资源，可暂停或取消"); announced = true; }
                await Task.Delay(250, token);
            }
        }
    }
    public static void EnsureNoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("自动部署不允许经过符号链接或目录联接：" + current);
            current = Path.GetDirectoryName(current);
        }
    }
    private static void EnsurePythonStopped(InstanceDescriptor instance)
    {
        foreach (var process in Process.GetProcessesByName("python"))
        {
            using (process)
            {
                string? path;
                try { path = process.MainModule?.FileName; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                { throw new IOException("无法确认正在运行的 Python 所属实例，请关闭目标实例后重试。", ex); }
                if (string.Equals(path, instance.PythonPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("目标实例的 Python 正在运行，请在 Desktop 中停止该实例后再安装。");
            }
        }
    }
    private static void CheckSpace(IEnumerable<PlannedDeployment> files, List<string> issues)
    {
        foreach (var group in files.Where(x => !x.Reuse).GroupBy(x => Path.GetPathRoot(x.TargetPath)!, StringComparer.OrdinalIgnoreCase))
            if (new DriveInfo(group.Key).AvailableFreeSpace < group.Sum(x => x.SizeBytes) + 64L * 1024 * 1024)
                issues.Add("目标磁盘空间不足：" + group.Key);
    }
}
