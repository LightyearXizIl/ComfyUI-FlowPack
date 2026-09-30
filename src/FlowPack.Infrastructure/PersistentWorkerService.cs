using System.Collections.Concurrent;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public sealed record WorkerJob(string Id, string Operation, JsonElement Input, string State, string Stage,
    JsonElement? Result, string? Error, int Attempt, DateTimeOffset CreatedAt)
{
    public long? CompletedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public OnlineDownloadInput? OnlineOrigin { get; init; }
    public IReadOnlyList<string> AvailableActions => State switch
    {
        "Queued" or "Running" => ["task.pause", "task.cancel"],
        "PauseRequested" => ["task.cancel"],
        "Paused" => Operation == "install.execute" ? ["task.cancel"] : ["task.resume", "task.cancel"],
        "Failed" or "Cancelled" => Operation == "install.execute" ? [] : ["task.retry"],
        _ => []
    };
    public bool CanControl(string command) => AvailableActions.Contains(command);
}
public sealed record ImportJobInput(string Source);
public sealed record OnlineMaterializeInput(string PlanId, string ResourceId, string DownloadJobId);
public sealed record OnlineLocalInput(string PlanId, string ResourceId, string SourcePath);
public sealed record ExportJobInput(ExportPlan Plan, string Output, bool AllowPartial);
public sealed record ExportPlanningInput(IReadOnlyList<LocalResource> Resources, IReadOnlyList<string> Issues);
public sealed record InstallPlanningInput(InstanceDescriptor Instance, IReadOnlyList<ImportResource> Resources, IReadOnlyList<WorkflowDocument>? Workflows = null);
public sealed record DependencyAnalysisInput(IReadOnlyList<WorkflowDocument> Workflows, ResourceInventory Inventory, IReadOnlyList<ImportResource>? StagedResources = null);
public sealed record LibrarySaveInput(PackageManifest? Manifest = null, WorkflowDocument? Workflow = null,
    PackageDraft? Draft = null, InstanceFingerprint? Instance = null, string Source = "");

public sealed class PersistentWorkerService : IAsyncDisposable
{
    private readonly ResourceLibraryDatabase _database;
    private readonly Func<CancellationToken, Task<IReadOnlyList<InstanceDescriptor>>> _discover;
    private readonly ConcurrentDictionary<string, WorkerJob> _jobs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new();
    private readonly ConcurrentDictionary<string, Task> _runners = new();
    private readonly SemaphoreSlim _downloads = new(3);
    private readonly SemaphoreSlim _control = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IDeploymentCapabilityProvider _capabilities;
    private readonly GlobalWorkCoordinator _coordinator;
    public bool ShutdownRequested { get; private set; }

    public PersistentWorkerService(ResourceLibraryDatabase database,
        Func<CancellationToken, Task<IReadOnlyList<InstanceDescriptor>>>? discover = null, IDeploymentCapabilityProvider? capabilities = null, GlobalWorkCoordinator? coordinator = null)
    {
        _database = database; _discover = discover ?? (ct => new DesktopInstanceDiscovery().DiscoverAsync(ct));
        _capabilities = capabilities ?? new DeploymentCapabilityProvider();
        _coordinator = coordinator ?? new GlobalWorkCoordinator();
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        using var admission = await _coordinator.EnterWorkAsync(token);
        await _database.InitializeAsync(token);
        foreach (var json in await _database.LoadJobsAsync(token))
        {
            var job = JsonSerializer.Deserialize<WorkerJob>(json);
            if (job is null) continue;
            if (job.State is "Queued" or "Running" or "PauseRequested" or "CancelRequested")
                job = job with { State = "Paused", Stage = "Worker 已重启，请继续或重新检查安装计划" };
            _jobs[job.Id] = job; await SaveAsync(job);
        }
        foreach (var legacy in await _database.LoadTasksAsync(token))
            if (!_jobs.ContainsKey(legacy.Id) && legacy.State is WorkerTaskState.Queued or WorkerTaskState.Running or WorkerTaskState.Paused)
                await _database.SaveTaskAsync(legacy with { State = WorkerTaskState.Failed,
                    Stage = "旧任务缺少可恢复的执行计划，请重新检查并创建任务", ErrorMessage = "legacy-plan-recheck-required", UpdatedAt = DateTimeOffset.UtcNow }, token);
        var recoveries = await new ResourceInstallationService(_database.LibraryPath).RecoverAsync(token);
        var pythonRecoveries = await new PythonDependencyService(_database.LibraryPath).RecoverAsync(token);
        var repairIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var report in recoveries.Where(x => x.State == "NeedsRepair"))
        {
            var id = "deployment-recovery-" + report.PlanId;
            repairIds.Add(id);
            var repair = new WorkerJob(id, "install.recovery", JsonSerializer.SerializeToElement(new { report.PlanId }),
                "NeedsReview", "安装恢复日志需要人工修复", null, report.Error, 0,
                _jobs.TryGetValue(id, out var previous) ? previous.CreatedAt : DateTimeOffset.UtcNow);
            _jobs[id] = repair; await SaveAsync(repair);
        }
        foreach (var report in pythonRecoveries)
        {
            // Recovery evidence can outlive (or predate) its original job database row.
            // Keep it visible even when no install.execute job can be associated with it.
            var id = "python-recovery-" + report.PlanId;
            repairIds.Add(id);
            var repair = new WorkerJob(id, "install.recovery", JsonSerializer.SerializeToElement(new { report.PlanId, report.PythonPath }),
                "NeedsReview", "Python 环境需要检查与修复", null, report.Message, 0,
                _jobs.TryGetValue(id, out var previous) ? previous.CreatedAt : DateTimeOffset.UtcNow);
            _jobs[id] = repair; await SaveAsync(repair);
        }
        foreach (var job in _jobs.Values.Where(x => x.Operation == "install.recovery" && x.State == "NeedsReview" && !repairIds.Contains(x.Id)))
        {
            var resolved = job with { State = "Completed", Stage = "未再检测到该恢复问题；不代表安装或 Python 环境已修复" };
            _jobs[job.Id] = resolved; await SaveAsync(resolved);
        }
        foreach (var report in recoveries)
        {
            foreach (var job in _jobs.Values.Where(x => x.Operation == "install.execute" && x.Input.Deserialize<ResourceInstallPlan>()?.Id == report.PlanId))
            {
                var interrupted = job with { State = "NeedsReview", Stage = "安装中断，需要重新检查", Error = report.Error };
                _jobs[job.Id] = interrupted; await SaveAsync(interrupted);
            }
        }
        foreach (var report in pythonRecoveries)
        foreach (var job in _jobs.Values.Where(x => x.Operation == "install.execute" && x.Input.Deserialize<ResourceInstallPlan>()?.Id == report.PlanId))
        {
            var interrupted = job with { State = "NeedsReview", Stage = "Python 依赖安装中断，需要修复", Error = report.Message };
            _jobs[job.Id] = interrupted; await SaveAsync(interrupted);
        }
    }

    public async Task<WorkerResponse> HandleAsync(WorkerRequest request, CancellationToken token)
    {
        try
        {
            var readOnly = request.Command is "worker.prepare-update" or "job.list" or "job.get" or "library.packages" or "library.workflows" or "library.instances" or "library.drafts" or "library.tasks" ||
                request.Command == WorkerProtocol.PingCommand || request.Command == WorkerProtocol.StatusCommand || request.Command == WorkerProtocol.ListTasksCommand;
            using var admission = readOnly ? null : request.Command is "task.pause" or "task.cancel"
                ? await _coordinator.EnterTaskControlAsync(token) : await _coordinator.EnterWorkAsync(token);
            if (request.Command == "worker.prepare-update")
            {
                await _control.WaitAsync(token);
                try
                {
                    if (!_tokens.IsEmpty) return Ok(request, false);
                    ShutdownRequested = true; return Ok(request, true);
                }
                finally { _control.Release(); }
            }
            if (request.Command == WorkerProtocol.PingCommand) return Ok(request, new { ready = true, protocolVersion = WorkerProtocol.Version });
            if (request.Command == WorkerProtocol.StatusCommand) return Ok(request, new { activeTasks = _jobs.Values.Count(x => x.State is "Running" or "Queued"), deploymentCapabilitiesPerPlan = true,
                taskStoreAttached = true, taskCount = (await _database.LoadTasksAsync(token)).Count });
            if (request.Command == "job.list") return Ok(request, _jobs.Values.OrderByDescending(x => x.CreatedAt)
                .Select(x => x with { Input = JsonSerializer.SerializeToElement(new { }), Result = null }).ToArray());
            if (request.Command == "job.get")
                return _jobs.TryGetValue(request.Payload!.Value.GetString()!, out var found) ? Ok(request, found) : Fail(request, "not-found", "任务不存在。");
            if (request.Command == "online.source")
            {
                await _control.WaitAsync(token);
                try
                {
                    var input = Read<OnlineSourceInput>(request.Payload!.Value);
                    if (_jobs.TryGetValue(request.RequestId, out var prior))
                    {
                        if (prior.Operation != "resource.source" || prior.Input.Deserialize<OnlineSourceInput>() != input)
                            return Fail(request, "idempotency-conflict", "同一请求不能更改来源内容。");
                        var latest = await _database.LoadImportSessionAsync(token) ?? throw new InvalidDataException("导入会话已丢失。");
                        var savedPlan = prior.Result?.Deserialize<ImportPlan>() ?? throw new InvalidDataException("来源修改记录缺少计划，请重新导入。");
                        if (!ContinuesImportPlan(latest.PlanId, savedPlan.Id))
                            return Fail(request, "import-session-conflict", "该来源修改已完成，但当前已切换到其他导入会话；未替换当前内容。");
                        return Ok(request, new RestoredImportSession(ValidateImportSession(latest), latest));
                    }
                    var current = await _database.LoadImportSessionAsync(token);
                    if (current?.PlanId != input.PlanId || current.Revision != input.Revision)
                        return Fail(request, "import-session-conflict", "导入会话已改变，请重新打开窗口读取最新内容。");
                    if (current.DownloadJobs.ContainsKey(input.ResourceId))
                        throw new InvalidDataException("此资源已关联下载任务。请重新导入后修改来源，避免混用已有下载。");
                    var changed = OnlineSourceEditing.Apply(ValidateImportSession(current), input);
                    var state = current with { PlanId = changed.Id, Revision = checked(current.Revision + 1) };
                    var job = new WorkerJob(request.RequestId, "resource.source", request.Payload.Value, "Completed", "来源已保存，尚未下载",
                        JsonSerializer.SerializeToElement(changed), null, 1, DateTimeOffset.UtcNow);
                    await _database.SaveDownloadAndImportSessionAsync(job, state, token);
                    _jobs[job.Id] = job;
                    return Ok(request, new RestoredImportSession(changed, state));
                }
                finally { _control.Release(); }
            }
            if (request.Command == "import.session.save")
            {
                await _control.WaitAsync(token);
                try
                {
                    var session = Read<ImportSessionState>(request.Payload!.Value);
                    var plan = ValidateImportSession(session);
                    var current = await _database.LoadImportSessionAsync(token);
                    if (session.Revision != (current?.Revision ?? 0))
                        return Fail(request, "import-session-conflict", "另一个窗口已更改导入会话，请重新打开窗口恢复最新内容后再操作；未覆盖已有下载。");
                    var links = session.DownloadJobs.ToDictionary(x => x.Key, x => x.Value);
                    var continuesCurrent = current is not null && ContinuesImportPlan(session.PlanId, current.PlanId);
                    // Downloads enter under this same lock. Keep associations added after the
                    // caller read this plan's session. A new import must not inherit unrelated links.
                    foreach (var link in current?.DownloadJobs ?? new Dictionary<string, string>())
                    {
                        if (!plan.PendingDownloads.Any(x => x.Id == link.Key)) continue;
                        if (links.TryGetValue(link.Key, out var replacement) && replacement != link.Value)
                            return Fail(request, "import-session-conflict", "资源已有其他下载任务，不能覆盖其关联。");
                        if (continuesCurrent || links.ContainsKey(link.Key)) links[link.Key] = link.Value;
                    }
                    session = session with { DownloadJobs = links, Revision = checked(session.Revision + 1) };
                    ValidateImportSession(session);
                    await _database.SaveImportSessionAsync(session, token);
                    return Ok(request, session);
                }
                finally { _control.Release(); }
            }
            if (request.Command == "import.session.load")
            {
                var session = await _database.LoadImportSessionAsync(token);
                if (session is null) return Ok(request, null);
                return Ok(request, new RestoredImportSession(ValidateImportSession(session), session));
            }
            if (request.Command.StartsWith("library.", StringComparison.Ordinal)) return Ok(request, await LibraryAsync(request.Command, request.Payload, token));
            if (request.Command == WorkerProtocol.ListTasksCommand) return Ok(request, await _database.LoadTasksAsync(token));
            if (request.Command is "task.pause" or "task.cancel" or "task.resume" or "task.retry")
            {
                await _control.WaitAsync(token);
                try
                {
                    var id = request.Payload!.Value.GetString()!;
                    if (!_jobs.TryGetValue(id, out var job)) return Fail(request, "not-found", "任务不存在。");
                    if (request.Command == "task.pause" && job.State == "PauseRequested" || request.Command == "task.cancel" && job.State == "CancelRequested")
                        return Ok(request, job);
                    if (!job.CanControl(request.Command))
                        return job.Operation == "install.execute" && request.Command is "task.resume" or "task.retry"
                            ? Fail(request, "replan-required", "安装恢复需要重新预览；已部署相同文件会自动复用。")
                            : Fail(request, "invalid-task-state", "当前任务状态不能执行此操作：" + job.State);
                    if (request.Command is "task.pause" or "task.cancel")
                    {
                        var active = _tokens.TryGetValue(id, out var cancellation);
                        job = job with { State = active ? request.Command == "task.pause" ? "PauseRequested" : "CancelRequested" : request.Command == "task.pause" ? "Paused" : "Cancelled" };
                        _jobs[id] = job; await SaveAsync(job); cancellation?.Cancel();
                    }
                    else if (!_tokens.ContainsKey(id) && job.State is "Paused" or "Failed" or "Cancelled" or "NeedsReview")
                    {
                        if (job.Operation == "install.execute") return Fail(request, "replan-required", "安装恢复需要重新预览；已部署相同文件会自动复用。");
                        job = job with { State = "Queued", Error = null, Attempt = job.Attempt + 1 };
                        _jobs[id] = job; await SaveAsync(job); Start(job, admission!.Retain());
                    }
                    return Ok(request, job);
                }
                finally { _control.Release(); }
            }
            if (request.Command == "online.download")
            {
                await _control.WaitAsync(token);
                try
                {
                    if (ShutdownRequested) return Fail(request, "worker-stopping", "Worker 正在安全退出。");
                    var input = Read<OnlineDownloadInput>(request.Payload!.Value);
                    if (_jobs.TryGetValue(request.RequestId, out var prior))
                        return prior.Operation == "task.download" && prior.OnlineOrigin == input ? Ok(request, prior)
                            : Fail(request, "idempotency-conflict", "同一请求不能改变在线下载内容。");
                    PlannedZipExportService.ValidateRelative(input.FileName);
                    if (input.FileName != Path.GetFileName(input.FileName)) throw new InvalidDataException("下载文件名不能包含目录。");
                    var plan = FindOnlinePlan(input.PlanId);
                    var item = plan.PendingDownloads.SingleOrDefault(x => x.Id == input.ResourceId)
                        ?? throw new InvalidDataException("在线资源不属于原计划。");
                    if (item.SourceUrl is null) throw new InvalidDataException("清单没有下载来源，请选择本地文件。");
                    if (!Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != "https" || source.UserInfo.Length > 0 || source.Fragment.Length > 0)
                        throw new InvalidDataException("在线来源必须是不含凭据或片段的 HTTPS 地址。");
                    var session = await _database.LoadImportSessionAsync(token);
                    if (session?.PlanId != plan.Id) throw new InvalidDataException("当前导入会话已改变，请重新检查后下载。");
                    if (session.DownloadJobs.TryGetValue(item.Id, out var savedId) && _jobs.TryGetValue(savedId, out var savedJob))
                    { ValidateImportSession(session); return Ok(request, savedJob); }
                    var queued = new WorkerJob(request.RequestId, "task.download", JsonSerializer.SerializeToElement(new DownloadTaskPayload(item.SourceUrl, item.Sha256, input.FileName)),
                        "Queued", "等待执行", null, null, 1, DateTimeOffset.UtcNow) { OnlineOrigin = input };
                    var links = session.DownloadJobs.ToDictionary(x => x.Key, x => x.Value); links[item.Id] = queued.Id;
                    await _database.SaveDownloadAndImportSessionAsync(queued, session with { DownloadJobs = links }, token);
                    _jobs[queued.Id] = queued;
                    Start(queued, admission!.Retain());
                    return Ok(request, queued);
                }
                finally { _control.Release(); }
            }
            var operations = new[] { "instance.discover", "instance.associate", "inventory.scan", "dependency.analyze", "resource.import", "resource.materialize", "resource.materialize-local", "resource.match-local", "export.plan", "export.execute", "install.plan", "install.execute", "task.download", "verify.run" };
            if (!operations.Contains(request.Command)) return Fail(request, "unsupported-command", "未知 Worker 操作。");
            await _control.WaitAsync(token);
            try
            {
                if (ShutdownRequested) return Fail(request, "worker-stopping", "Worker 正在为软件更新安全退出。");
                if (_jobs.TryGetValue(request.RequestId, out var existing))
                {
                    if (existing.Operation != request.Command || existing.Input.GetRawText() != (request.Payload ?? JsonSerializer.SerializeToElement(new { })).GetRawText())
                        return Fail(request, "idempotency-conflict", "同一请求 ID 对应的内容已改变。");
                    return Ok(request, existing);
                }
                var job = new WorkerJob(request.RequestId, request.Command, request.Payload ?? JsonSerializer.SerializeToElement(new { }), "Queued", "等待执行", null, null, 1, DateTimeOffset.UtcNow);
                _jobs[job.Id] = job; await SaveAsync(job); Start(job, admission!.Retain()); return Ok(request, job);
            }
            finally { _control.Release(); }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or ArgumentException or InvalidOperationException)
        { return Fail(request, "operation-failed", ex.Message); }
    }

    private void Start(WorkerJob job, GlobalWorkCoordinator.WorkLease admission)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        if (!_tokens.TryAdd(job.Id, cancellation)) { cancellation.Dispose(); admission.Dispose(); return; }
        _runners[job.Id] = Task.Run(async () =>
        {
            using var admittedWork = admission;
            var token = cancellation.Token;
            try
            {
                await _control.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    job = job with { State = "Running", Stage = "正在执行" }; _jobs[job.Id] = job; await SaveAsync(job);
                }
                finally { _control.Release(); }
                var result = await ExecuteAsync(job, token);
                job = job with { State = "Completed", Stage = job.Operation == "install.execute" ? "文件已部署；请启动目标实例检查节点与模型" : "已完成", Result = JsonSerializer.SerializeToElement(result), Error = null };
            }
            catch (OperationCanceledException)
            {
                var cancel = _jobs[job.Id].State == "CancelRequested";
                job = job with { State = cancel ? "Cancelled" : "Paused", Stage = job.Operation == "install.execute" ? "安装已中断，需要重新检查" : cancel ? "已取消" : "已暂停" };
            }
            catch (Exception ex) { job = job with { State = "Failed", Stage = job.Operation == "install.execute" ? "安装未完成，请重新预览并检查失败原因" : "操作失败", Error = ex.Message }; }
            await _control.WaitAsync();
            try
            {
                var latest = _jobs[job.Id]; job = job with { CompletedBytes = latest.CompletedBytes, TotalBytes = latest.TotalBytes };
                _jobs[job.Id] = job; await SaveAsync(job);
            }
            finally { _tokens.TryRemove(job.Id, out _); cancellation.Dispose(); _control.Release(); }
        });
    }

    private async Task<object?> ExecuteAsync(WorkerJob job, CancellationToken token)
    {
        switch (job.Operation)
        {
            case "instance.discover": return await _discover(token);
            case "instance.associate": return await new DesktopInstanceDiscovery().AssociateAsync(job.Input.GetString()!, token);
            case "inventory.scan": return await ScanRuntimeInventoryAsync(Read<InstanceDescriptor>(job.Input), token);
            case "dependency.analyze":
                var analysisInput = Read<DependencyAnalysisInput>(job.Input);
                return await new InventoryDependencyAnalyzer().AnalyzeAsync(analysisInput.Workflows,
                    await ImportInventoryService.MergeVerifiedAsync(await ScanRuntimeInventoryAsync(analysisInput.Inventory.Instance, token), analysisInput.StagedResources ?? [], token), token);
            case "resource.import":
                var imported = await new ResourceImportService().ImportAsync(Read<ImportJobInput>(job.Input).Source, Path.Combine(_database.LibraryPath, "staging", "imports"), token);
                await SaveImportedWorkflowsAsync(imported, token);
                return imported;
            case "resource.materialize":
                var materialize = Read<OnlineMaterializeInput>(job.Input);
                var onlinePlan = FindOnlinePlan(materialize.PlanId);
                var remote = onlinePlan.PendingDownloads.SingleOrDefault(x => x.Id == materialize.ResourceId)
                    ?? throw new InvalidDataException("资源不在原计划的待下载列表中。");
                if (!_jobs.TryGetValue(materialize.DownloadJobId, out var downloadJob) || downloadJob.State != "Completed" || downloadJob.Operation != "task.download")
                    throw new InvalidDataException("必须引用本 Worker 已完成的下载任务。");
                var downloadInput = Read<DownloadTaskPayload>(downloadJob.Input);
                if (remote.SourceUrl is null || !string.Equals(remote.SourceUrl, downloadInput.SourceUrl, StringComparison.Ordinal))
                    throw new InvalidDataException("下载任务来源与原在线清单不一致，请重新确认来源。");
                var payload = downloadJob.Result?.Deserialize<DownloadResult>() ?? throw new InvalidDataException("下载任务没有可用的结果。");
                var downloadRoot = Path.GetFullPath(Path.Combine(_database.LibraryPath, "staging", "downloads")) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(payload.StagingPath).StartsWith(downloadRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("在线载荷不属于本资源库下载暂存区。");
                var materialized = await new OnlineImportMaterializer().MaterializeAsync(onlinePlan, remote.Id, payload.StagingPath, token);
                await SaveImportedWorkflowsAsync(materialized, token);
                return materialized;
            case "resource.materialize-local":
                var local = Read<OnlineLocalInput>(job.Input);
                var localPlan = await new OnlineImportMaterializer().MaterializeAsync(FindOnlinePlan(local.PlanId), local.ResourceId, local.SourcePath, token);
                await SaveImportedWorkflowsAsync(localPlan, token);
                return localPlan;
            case "resource.match-local":
                var matching = Read<OnlineLocalMatchInput>(job.Input);
                var declaration = FindOnlinePlan(matching.PlanId).PendingDownloads.SingleOrDefault(x => x.Id == matching.ResourceId)
                    ?? throw new InvalidDataException("资源不属于原计划的待补全项。");
                var lastHashProgress = DateTimeOffset.MinValue;
                var hashProgress = new InlineProgress<LocalHashProgress>(value =>
                {
                    var now = DateTimeOffset.UtcNow;
                    if (value.CompletedBytes != 0 && value.CompletedBytes != value.TotalBytes && now - lastHashProgress < TimeSpan.FromMilliseconds(400)) return;
                    lastHashProgress = now;
                    if (_jobs.TryGetValue(job.Id, out var prior) && prior.State == "Running")
                        _jobs.TryUpdate(job.Id, prior with { CompletedBytes = value.CompletedBytes, TotalBytes = value.TotalBytes,
                            Stage = $"核对本地候选 {value.CandidateIndex}/{value.CandidateCount}：{Path.GetFileName(value.SourcePath)}（当前文件字节进度）" }, prior);
                });
                return await OnlineLocalHashMatcher.MatchAsync(declaration, matching.CandidatePaths, token, hashProgress);
            case "export.plan":
                var selection = Read<ExportPlanningInput>(job.Input);
                return await new PlannedZipExportService().PlanAsync(selection.Resources, selection.Issues, token);
            case "export.execute":
                var export = Read<ExportJobInput>(job.Input);
                if (!_jobs.Values.Any(x => x.Operation == "export.plan" && x.State == "Completed" &&
                    x.Result?.Deserialize<ExportPlan>()?.Id == export.Plan.Id &&
                    JsonElement.DeepEquals(x.Result.Value, JsonSerializer.SerializeToElement(export.Plan))))
                    throw new InvalidDataException("导出计划不是本 Worker 已保存的预览，请重新检查导出内容。");
                await new PlannedZipExportService().ExportAsync(export.Plan, export.Output, export.AllowPartial, token); return export.Output;
            case "install.plan":
                var input = Read<InstallPlanningInput>(job.Input);
                var proposed = await new ResourceInstallationService(_database.LibraryPath).PlanAsync(input.Instance, input.Resources, token);
                if (input.Workflows is { Count: > 0 })
                {
                    var inventory = await ImportInventoryService.MergeVerifiedAsync(await ScanRuntimeInventoryAsync(input.Instance, token), input.Resources, token);
                    var dependencies = await new InventoryDependencyAnalyzer().AnalyzeAsync(input.Workflows, inventory, token);
                    proposed = proposed with { BlockingReasons = proposed.BlockingReasons.Concat(dependencies.Dependencies.Where(x => x.State != DependencyState.Present)
                          .Select(x => "工作流依赖尚未解决：" + x.Reference + "（" + x.State + "）")).Concat(dependencies.Issues).ToArray() };
                }
                return proposed with { Capability = _capabilities.Evaluate(proposed.Instance, proposed.PythonRequirements.Count > 0) };
            case "install.execute":
                var install = Read<ResourceInstallPlan>(job.Input);
                if (install.Capability is null) throw new InvalidDataException("旧计划缺少版本及布局安装能力依据，请重新检查；隔离实例验收资格不会从旧任务继承。");
                // Execute only a plan previously generated and retained by this Worker.
                if (!_jobs.Values.Any(x => x.Operation == "install.plan" && x.State == "Completed" && x.Result?.Deserialize<ResourceInstallPlan>()?.Id == install.Id && JsonElement.DeepEquals(x.Result.Value, job.Input)))
                    throw new InvalidDataException("安装计划不是本 Worker 生成的原始计划。");
                var pythonService = new PythonDependencyService(_database.LibraryPath);
                PythonDependencyPlan? pythonPlan = null;
                var executionInstance = (await _discover(token)).SingleOrDefault(x => x.Id == install.Instance.Id) ?? throw new InvalidDataException("目标实例不再存在。");
                RequireCapability(executionInstance, install.PythonRequirements.Count > 0);
                await new ResourceInstallationService(_database.LibraryPath).ExecuteAsync(install, async (prior, ct) =>
                {
                    var current = (await _discover(ct)).SingleOrDefault(x => x.Id == prior.Id) ?? throw new InvalidDataException("目标实例不再存在。");
                    RequireCapability(current, install.PythonRequirements.Count > 0);
                    return current;
                },
                    progress: new InlineProgress<string>(stage =>
                    {
                        if (_jobs.TryGetValue(job.Id, out var prior)) _jobs.TryUpdate(job.Id, prior with { Stage = stage }, prior);
                    }), token: token,
                    prepareEnvironment: async ct => { pythonPlan = await pythonService.PrepareAsync(install, ct); },
                    installEnvironment: async ct => { if (pythonPlan is not null) await pythonService.InstallAsync(install.Id, pythonPlan, ct); });
                return install.Id;
            case "task.download":
                var download = Read<DownloadTaskPayload>(job.Input);
                PlannedZipExportService.ValidateRelative(download.FileName);
                if (download.FileName.Contains('/') || download.FileName.Contains('\\')) throw new InvalidDataException("下载只接受文件名。");
                await _downloads.WaitAsync(token);
                try
                {
                    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
                    var lastProgress = DateTimeOffset.MinValue;
                    var progress = new InlineProgress<DownloadProgress>(value =>
                    {
                        var now = DateTimeOffset.UtcNow;
                        if (now - lastProgress < TimeSpan.FromMilliseconds(400) && value.CompletedBytes != value.TotalBytes) return;
                        lastProgress = now;
                        if (_jobs.TryGetValue(job.Id, out var prior) && prior.State == "Running")
                            _jobs.TryUpdate(job.Id, prior with { CompletedBytes = value.CompletedBytes, TotalBytes = value.TotalBytes,
                                Stage = $"已下载 {value.CompletedBytes:N0} / {(value.TotalBytes is { } total ? total.ToString("N0") : "未知")} 字节" }, prior);
                    });
                    return await new VerifiedDownloadService(http).DownloadAsync(new(new Uri(download.SourceUrl),
                        Path.Combine(_database.LibraryPath, "staging", "downloads", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(job.Id))), download.FileName), download.ExpectedSha256), token, progress);
                }
                finally { _downloads.Release(); }
            case "verify.run":
                var plan = Read<ResourceInstallPlan>(job.Input);
                var errors = new List<string>();
                foreach (var file in plan.Files)
                    if (!File.Exists(file.TargetPath) || await ResourceImportService.HashAsync(file.TargetPath, token) != file.Sha256) errors.Add("文件缺失或内容改变：" + file.TargetPath);
                return new VerificationReport(VerificationLevel.FileIntegrity, errors.Count == 0, errors);
            default: throw new InvalidDataException("不支持的操作。");
        }
    }

    private ImportPlan ValidateImportSession(ImportSessionState state)
    {
        if (state.Version != 1) throw new InvalidDataException("导入会话版本不受支持。");
        var plan = FindOnlinePlan(state.PlanId);
        var seenChoices = new HashSet<(string, string)>();
        foreach (var choice in state.ResourceChoices ?? [])
        {
            if (choice is null || !seenChoices.Add((choice.ResourceId, choice.SourcePath)) ||
                !plan.Resources.Any(x => x.Id == choice.ResourceId && x.SourcePath == choice.SourcePath))
                throw new InvalidDataException("资源选择不属于原导入计划或存在重复。");
            // A partially typed mapping may be saved, but must be validated and confirmed before installation.
            if (choice.TargetRelativePath is null || choice.TargetRelativePath.Length > 32768)
                throw new InvalidDataException("目标路径草稿无效或过长。");
        }
        foreach (var pair in state.DownloadJobs)
        {
            var declaration = plan.PendingDownloads.SingleOrDefault(x => x.Id == pair.Key)
                ?? throw new InvalidDataException("会话下载不属于原计划的待补全资源。");
            if (!_jobs.TryGetValue(pair.Value, out var job) || job.Operation != "task.download")
                throw new InvalidDataException("会话引用的下载任务不存在。");
            var input = Read<DownloadTaskPayload>(job.Input);
            if (declaration.SourceUrl != input.SourceUrl || !string.Equals(declaration.Sha256, input.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("会话下载来源或哈希与原清单不一致。");
        }
        return plan;
    }

    private ImportPlan FindOnlinePlan(string id) => _jobs.Values
        .Where(x => x.State == "Completed" && x.Operation is "resource.import" or "resource.materialize" or "resource.materialize-local" or "resource.source")
        .Select(x => x.Result?.Deserialize<ImportPlan>()).SingleOrDefault(x => x?.Id == id)
        ?? throw new InvalidDataException("找不到本 Worker 保存的在线导入计划。");

    private bool ContinuesImportPlan(string candidate, string ancestor)
    {
        // Trust retained Worker operations, not caller-supplied ancestry or matching manifest IDs.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(candidate))
        {
            if (candidate == ancestor) return true;
            var producer = _jobs.Values.SingleOrDefault(x => x.State == "Completed" &&
                x.Operation is "resource.materialize" or "resource.materialize-local" or "resource.source" &&
                x.Result?.Deserialize<ImportPlan>()?.Id == candidate);
            if (producer is null) return false;
            candidate = producer.Operation == "resource.source" ? Read<OnlineSourceInput>(producer.Input).PlanId : producer.Operation == "resource.materialize"
                ? Read<OnlineMaterializeInput>(producer.Input).PlanId
                : Read<OnlineLocalInput>(producer.Input).PlanId;
        }
        return false;
    }

    private async Task SaveImportedWorkflowsAsync(ImportPlan plan, CancellationToken token)
    {
        foreach (var workflow in plan.Workflows)
        {
            var snapshotRoot = Path.Combine(_database.LibraryPath, "workflows");
            Directory.CreateDirectory(snapshotRoot);
            var snapshot = Path.Combine(snapshotRoot, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(workflow.RawJson))) + ".json");
            if (!File.Exists(snapshot)) await File.WriteAllTextAsync(snapshot, workflow.RawJson, token);
            await _database.SaveWorkflowAsync(workflow, snapshot, cancellationToken: token);
        }
    }

    private async Task<ResourceInventory> ScanRuntimeInventoryAsync(InstanceDescriptor instance, CancellationToken token)
    {
        var peers = await _discover(token);
        var current = peers.SingleOrDefault(x => x.Id == instance.Id) ?? instance;
        var inventory = await new ResourceInventoryService().ScanAsync(current, token);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
        return await new RuntimeNodeInspector(http, new DesktopRuntimeEndpointResolver(current.ConfigurationRoot)).InspectAsync(inventory, peers, token);
    }

    private void RequireCapability(InstanceDescriptor instance, bool requiresPython)
    {
        var capability = _capabilities.Evaluate(instance, requiresPython);
        if (!capability.Allows(requiresPython)) throw new InvalidDataException(string.Join("\n", capability.Reasons));
    }

    private async Task<object?> LibraryAsync(string command, JsonElement? payload, CancellationToken token)
    {
        if (command == "library.initialize") { await _database.InitializeAsync(token); return true; }
        if (command == "library.packages") return await _database.LoadImportedPackagesAsync(token);
        if (command == "library.workflows") return await _database.LoadWorkflowsAsync(token);
        if (command == "library.instances") return await _database.LoadCandidateInstancesAsync(token);
        if (command == "library.drafts") return await _database.LoadDraftsAsync(token);
        if (command == "library.tasks") return await _database.LoadTasksAsync(token);
        var value = Read<LibrarySaveInput>(payload!.Value);
        switch (command)
        {
            case "library.save-package": await _database.SaveImportedPackageAsync(value.Manifest!, value.Source, cancellationToken: token); break;
            case "library.save-workflow": await _database.SaveWorkflowAsync(value.Workflow!, value.Source, cancellationToken: token); break;
            case "library.save-instance": await _database.SaveCandidateInstanceAsync(value.Instance!, cancellationToken: token); break;
            case "library.save-draft": await _database.SaveDraftAsync(value.Draft!, cancellationToken: token); break;
            default: throw new InvalidDataException("未知资源库命令。");
        }
        return true;
    }
    private async Task SaveAsync(WorkerJob job)
    {
        await _database.SaveJobAsync(job.Id, JsonSerializer.Serialize(job));
        var state = job.State switch { "Completed" => WorkerTaskState.Completed, "Failed" or "NeedsReview" => WorkerTaskState.Failed, "Cancelled" => WorkerTaskState.Cancelled, "Paused" => WorkerTaskState.Paused, "Queued" => WorkerTaskState.Queued, _ => WorkerTaskState.Running };
        var kind = job.Operation.StartsWith("export") ? WorkerTaskKind.PackageExport : job.Operation.StartsWith("install") ? WorkerTaskKind.Install : job.Operation == "task.download" ? WorkerTaskKind.Download : WorkerTaskKind.Verification;
        await _database.SaveTaskAsync(new(job.Id, kind, state, job.Operation, job.Stage, job.CompletedBytes, job.TotalBytes, job.Error, job.CreatedAt, DateTimeOffset.UtcNow));
    }
    private static T Read<T>(JsonElement element) => element.Deserialize<T>() ?? throw new InvalidDataException("请求内容无效。");
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private static WorkerResponse Ok(WorkerRequest request, object? data) => new(request.RequestId, true, JsonSerializer.SerializeToElement(data));
    private static WorkerResponse Fail(WorkerRequest request, string code, string message) => new(request.RequestId, false, Error: new(code, message));
    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel(); await Task.WhenAll(_runners.Values); _shutdown.Dispose();
        _downloads.Dispose(); _control.Dispose(); await _database.DisposeAsync();
    }
}
