using System.IO;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PersistentWorkerServiceTests : IDisposable
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_requires_unchanged_worker_preview(bool tampered)
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "workflow.json");
        await File.WriteAllTextAsync(source, "{\"version\":1,\"nodes\":[]}");
        await using var worker = new PersistentWorkerService(new ResourceLibraryDatabase(Path.Combine(_root, "library")));
        await worker.InitializeAsync();
        var planning = Request("export.plan", new ExportPlanningInput(
            [new("w", ResourceKind.Workflow, "workflow", source, "workflows/workflow.json")], []));
        await worker.HandleAsync(planning, default);
        var planJob = await WaitAsync(worker, planning.RequestId);
        Assert.Equal("Completed", planJob.State);
        var plan = planJob.Result!.Value.Deserialize<ExportPlan>()!;
        if (tampered) plan = plan with { Files = [plan.Files[0] with { ArchivePath = "workflows/unpreviewed.json" }] };
        var output = Path.Combine(_root, "export.zip");
        var executing = Request("export.execute", new ExportJobInput(plan, output, false));
        await worker.HandleAsync(executing, default);
        var result = await WaitAsync(worker, executing.RequestId);
        Assert.Equal(tampered ? "Failed" : "Completed", result.State);
        Assert.Equal(!tampered, File.Exists(output));
        if (tampered) Assert.Contains("已保存的预览", result.Error);
    }
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-worker-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Import_history_and_stage_progress_survive_worker_restart()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "workflow.json");
        await File.WriteAllTextAsync(file, "{\"version\":1,\"nodes\":[]}");
        string jobId;
        await using (var worker = new PersistentWorkerService(new ResourceLibraryDatabase(Path.Combine(_root, "library"))))
        {
            await worker.InitializeAsync();
            var request = Request("resource.import", new ImportJobInput(file)); jobId = request.RequestId;
            Assert.True((await worker.HandleAsync(request, default)).Succeeded);
            var job = await WaitAsync(worker, jobId);
            Assert.Equal("Completed", job.State); Assert.Equal(1, job.CompletedUnits); Assert.Equal(1, job.TotalUnits);
        }
        await using var restarted = new PersistentWorkerService(new ResourceLibraryDatabase(Path.Combine(_root, "library")));
        await restarted.InitializeAsync();
        var response = await restarted.HandleAsync(Request("library.imports", new { }), default);
        var history = response.Payload!.Value.Deserialize<ImportHistoryEntry[]>()!;
        Assert.Equal(file, Assert.Single(history).Source); Assert.Equal(1, history[0].ResourceCount);
        var restored = await WaitAsync(restarted, jobId);
        Assert.Equal(1, restored.CompletedUnits); Assert.Equal("项", restored.ProgressUnit);
    }

    private static WorkerRequest Request(string command, object input, string? id = null) =>
        new(WorkerProtocol.Version, id ?? Guid.NewGuid().ToString("N"), "fixture", command, JsonSerializer.SerializeToElement(input));
    private static async Task<WorkerJob> WaitAsync(PersistentWorkerService worker, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var response = await worker.HandleAsync(Request("job.get", id), timeout.Token);
            var job = response.Payload!.Value.Deserialize<WorkerJob>()!;
            if (job.State is not ("Queued" or "Running" or "PauseRequested" or "CancelRequested")) return job;
            await Task.Delay(10, timeout.Token);
        }
    }
    [Fact]
    public async Task Duplicate_requests_are_idempotent_and_changed_payload_is_rejected()
    {
        var calls = 0;
        await using var worker = new PersistentWorkerService(new(_root), ct => { Interlocked.Increment(ref calls); return Task.FromResult<IReadOnlyList<InstanceDescriptor>>([]); });
        await worker.InitializeAsync();
        var request = Request("instance.discover", new { }, "same-request");
        Assert.True((await worker.HandleAsync(request, default)).Succeeded);
        Assert.True((await worker.HandleAsync(request, default)).Succeeded);
        Assert.Equal("Completed", (await WaitAsync(worker, request.RequestId)).State);
        Assert.Equal(1, calls);
        var conflict = await worker.HandleAsync(Request("instance.discover", new { changed = true }, request.RequestId), default);
        Assert.Equal("idempotency-conflict", conflict.Error!.Code);
    }
    [Fact]
    public async Task Pause_cannot_replace_an_accepted_cancel_request()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var worker = new PersistentWorkerService(new(_root), async ct =>
        {
            entered.TrySetResult(); await release.Task;
            ct.ThrowIfCancellationRequested(); return [];
        });
        await worker.InitializeAsync();
        var request = Request("instance.discover", new { });
        await worker.HandleAsync(request, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cancel = await worker.HandleAsync(Request("task.cancel", request.RequestId), default);
            Assert.Equal("CancelRequested", cancel.Payload!.Value.Deserialize<WorkerJob>()!.State);
            var pause = await worker.HandleAsync(Request("task.pause", request.RequestId), default);
            Assert.Equal("invalid-task-state", pause.Error!.Code);
        }
        finally { release.TrySetResult(); }
        Assert.Equal("Cancelled", (await WaitAsync(worker, request.RequestId)).State);
        var resume = await worker.HandleAsync(Request("task.resume", request.RequestId), default);
        Assert.Equal("invalid-task-state", resume.Error!.Code);
    }
    [Fact]
    public async Task Pause_resume_and_reopen_preserve_completed_job()
    {
        var calls = 0;
        await using (var worker = new PersistentWorkerService(new(_root), async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.Infinite, ct);
            return [];
        }))
        {
            await worker.InitializeAsync();
            var request = Request("instance.discover", new { }, "persistent");
            await worker.HandleAsync(request, default);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref calls) == 0) await Task.Delay(10, timeout.Token);
            await worker.HandleAsync(Request("task.pause", request.RequestId), default);
            Assert.Equal("Paused", (await WaitAsync(worker, request.RequestId)).State);
            await worker.HandleAsync(Request("task.resume", request.RequestId), default);
            Assert.Equal("Completed", (await WaitAsync(worker, request.RequestId)).State);
        }
        await using var reopened = new PersistentWorkerService(new(_root), ct => Task.FromResult<IReadOnlyList<InstanceDescriptor>>([]));
        await reopened.InitializeAsync();
        var saved = await WaitAsync(reopened, "persistent");
        Assert.Equal("Completed", saved.State); Assert.Equal(2, saved.Attempt);
    }
    [Fact]
    public async Task Production_default_does_not_grant_installation_from_a_legacy_record()
    {
        await using var worker = new PersistentWorkerService(new(_root)); await worker.InitializeAsync();
        var status = await worker.HandleAsync(Request(WorkerProtocol.StatusCommand, new { }), default);
        Assert.True(status.Payload!.Value.GetProperty("deploymentCapabilitiesPerPlan").GetBoolean());
        var request = Request("install.execute", new { }); await worker.HandleAsync(request, default);
        var result = await WaitAsync(worker, request.RequestId);
        Assert.Equal("Failed", result.State); Assert.Contains("隔离实例", result.Error);
    }
    [Fact]
    public async Task Legacy_running_task_requires_recheck_without_acquiring_execution_authority()
    {
        var database = new ResourceLibraryDatabase(_root); await database.InitializeAsync();
        await database.SaveTaskAsync(new("old-install", WorkerTaskKind.Install, WorkerTaskState.Running, "old", "old", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await using var worker = new PersistentWorkerService(database); await worker.InitializeAsync();
        var old = Assert.Single(await database.LoadTasksAsync());
        Assert.Equal(WorkerTaskState.Failed, old.State); Assert.Equal("legacy-plan-recheck-required", old.ErrorMessage);
        var response = await worker.HandleAsync(Request("job.list", new { }), default);
        Assert.Empty(response.Payload!.Value.Deserialize<WorkerJob[]>()!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Installation_rechecks_current_capability_and_rejects_tampered_plan(bool alternateJsonFormat)
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "Desktop.exe"); await File.WriteAllTextAsync(executable, "fixture");
        var source = Path.Combine(_root, "source.json"); await File.WriteAllTextAsync(source, "{}");
        var data = Path.Combine(_root, "target");
        var instance = new InstanceDescriptor("a", "A", "desktop-2", executable, data, data, data, Path.Combine(data, "user"),
            Path.Combine(data, "user", "default", "workflows"), Path.Combine(data, "custom_nodes"), null, [], Path.Combine(data, "models"), [], "fingerprint", [])
            { ConfigurationRoot = _root, DesktopLayout = "standalone-native" };
        var version = "1.0.47";
        var capabilities = new DeploymentCapabilityProvider([new("1.0.47", "standalone-native", false, "unit-test-only")], _ => version);
        await using var worker = new PersistentWorkerService(new(Path.Combine(_root, "library")), _ => Task.FromResult<IReadOnlyList<InstanceDescriptor>>([instance]), capabilities);
        await worker.InitializeAsync();
        var resource = new ImportResource("w", source, "workflow.json", ResourceKind.Workflow, "workflows/workflow.json", 2,
            await ResourceImportService.HashAsync(source), RecognitionState.Confirmed, "fixture");
        var preview = Request("install.plan", new InstallPlanningInput(instance, [resource]));
        await worker.HandleAsync(preview, default);
        var planned = await WaitAsync(worker, preview.RequestId);
        Assert.Equal("Completed", planned.State);
        var plan = planned.Result!.Value.Deserialize<ResourceInstallPlan>()!;
        Assert.True(plan.Capability!.Allows(false));
        var forged = Request("install.execute", plan with { Capability = plan.Capability with { EvidenceId = "forged" } });
        await worker.HandleAsync(forged, default);
        Assert.Contains("原始计划", (await WaitAsync(worker, forged.RequestId)).Error);
        version = "1.0.48";
        var execute = Request("install.execute", plan);
        if (alternateJsonFormat)
        {
            using var formatted = JsonDocument.Parse(JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
            Assert.NotEqual(planned.Result.Value.GetRawText(), formatted.RootElement.GetRawText());
            Assert.True(JsonElement.DeepEquals(planned.Result.Value, formatted.RootElement));
            execute = execute with { Payload = formatted.RootElement.Clone() };
        }
        await worker.HandleAsync(execute, default);
        var failed = await WaitAsync(worker, execute.RequestId);
        Assert.Equal("Failed", failed.State); Assert.Contains("1.0.48", failed.Error);
        Assert.False(File.Exists(Path.Combine(instance.WorkflowsDirectory, "workflow.json")));
    }

    [Fact]
    public async Task Imported_workflow_snapshot_survives_source_removal()
    {
        Directory.CreateDirectory(_root); var source = Path.Combine(_root, "source.json");
        const string raw = "{\"version\":1,\"nodes\":[]}"; await File.WriteAllTextAsync(source, raw);
        await using var worker = new PersistentWorkerService(new(Path.Combine(_root, "library"))); await worker.InitializeAsync();
        var request = Request("resource.import", new ImportJobInput(source)); await worker.HandleAsync(request, default);
        Assert.Equal("Completed", (await WaitAsync(worker, request.RequestId)).State);
        File.Delete(source);
        var response = await worker.HandleAsync(Request("library.workflows", new { }), default);
        var stored = Assert.Single(response.Payload!.Value.Deserialize<StoredWorkflow[]>()!);
        Assert.Equal(raw, await File.ReadAllTextAsync(stored.Source));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Online_materialization_requires_retained_download_with_matching_source(bool matchingSource)
    {
        var library = Path.Combine(_root, "library");
        var payload = Path.Combine(library, "staging", "downloads", "fixture", "workflow.json");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        const string raw = "{\"version\":1,\"nodes\":[]}"; await File.WriteAllTextAsync(payload, raw);
        var hash = await ResourceImportService.HashAsync(payload);
        var declaration = Path.Combine(_root, "bundle.cpack.json");
        await File.WriteAllTextAsync(declaration, JsonSerializer.Serialize(new { formatVersion = "1", id = "bundle", name = "Bundle", version = "1",
            resources = new[] { new { id = "workflow", name = "workflow.json", kind = "Workflow", sizeBytes = new FileInfo(payload).Length,
                sha256 = hash, sourceUrl = "https://example.invalid/workflow.json", deploymentPurpose = "workflows/workflow.json" } } }));
        var db = new ResourceLibraryDatabase(library); await db.InitializeAsync();
        var seed = new WorkerJob("completed-download", "task.download", JsonSerializer.SerializeToElement(new DownloadTaskPayload(
            matchingSource ? "https://example.invalid/workflow.json" : "https://other.invalid/workflow.json", hash, "workflow.json")),
            "Completed", "fixture download result", JsonSerializer.SerializeToElement(new DownloadResult(payload, new FileInfo(payload).Length, hash, false)), null, 1, DateTimeOffset.UtcNow);
        await db.SaveJobAsync(seed.Id, JsonSerializer.Serialize(seed));
        await using var worker = new PersistentWorkerService(db); await worker.InitializeAsync();
        var import = Request("resource.import", new ImportJobInput(declaration)); await worker.HandleAsync(import, default);
        var imported = (await WaitAsync(worker, import.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
        var request = Request("resource.materialize", new OnlineMaterializeInput(imported.Id, "workflow", seed.Id));
        await worker.HandleAsync(request, default);
        var result = await WaitAsync(worker, request.RequestId);
        if (matchingSource)
        {
            Assert.Equal("Completed", result.State);
            Assert.Empty(result.Result!.Value.Deserialize<ImportPlan>()!.PendingDownloads);
            var stored = Assert.Single((await worker.HandleAsync(Request("library.workflows", new { }), default)).Payload!.Value.Deserialize<StoredWorkflow[]>()!);
            Assert.Equal(raw, await File.ReadAllTextAsync(stored.Source));
        }
        else { Assert.Equal("Failed", result.State); Assert.Contains("来源与原在线清单不一致", result.Error); }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task Import_session_reopens_with_retained_content_but_no_installation_plan()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "workflow.json"); await File.WriteAllTextAsync(source, "{\"version\":1,\"nodes\":[]}");
        var library = Path.Combine(_root, "library"); string planId;
        await using (var worker = new PersistentWorkerService(new(library)))
        {
            await worker.InitializeAsync();
            var request = Request("resource.import", new ImportJobInput(source)); await worker.HandleAsync(request, default);
            var plan = (await WaitAsync(worker, request.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
            planId = plan.Id;
            var save = await worker.HandleAsync(Request("import.session.save", new ImportSessionState(planId, new Dictionary<string, string>())), default);
            Assert.True(save.Succeeded);
        }
        await using var reopened = new PersistentWorkerService(new(library)); await reopened.InitializeAsync();
        var loaded = await reopened.HandleAsync(Request("import.session.load", new { }), default);
        Assert.True(loaded.Succeeded);
        var session = loaded.Payload!.Value.Deserialize<RestoredImportSession>()!;
        Assert.Equal(planId, session.Plan.Id); Assert.Single(session.Plan.Workflows); Assert.Empty(session.State.DownloadJobs);
        Assert.DoesNotContain("Capability", loaded.Payload.Value.GetRawText());
        Assert.DoesNotContain("ResourceInstallPlan", loaded.Payload.Value.GetRawText());
    }

    [Fact]
    public async Task Invalid_session_references_do_not_replace_previously_saved_session()
    {
        Directory.CreateDirectory(_root); var source = Path.Combine(_root, "bundle.cpack.json");
        await File.WriteAllTextAsync(source, """{"formatVersion":"1","id":"a","name":"A","version":"1","resources":[{"id":"r","name":"r.pth","kind":"Model","sizeBytes":1,"sourceUrl":"https://example.invalid/r.pth"}]}""");
        await using var worker = new PersistentWorkerService(new(Path.Combine(_root, "library"))); await worker.InitializeAsync();
        var import = Request("resource.import", new ImportJobInput(source)); await worker.HandleAsync(import, default);
        var plan = (await WaitAsync(worker, import.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
        Assert.True((await worker.HandleAsync(Request("import.session.save", new ImportSessionState(plan.Id, new Dictionary<string, string>())), default)).Succeeded);
        Assert.False((await worker.HandleAsync(Request("import.session.save", new ImportSessionState(plan.Id, new Dictionary<string, string> { ["r"] = "missing-download" })), default)).Succeeded);
        Assert.False((await worker.HandleAsync(Request("import.session.save", new ImportSessionState("missing-plan", new Dictionary<string, string>())), default)).Succeeded);
        Assert.False((await worker.HandleAsync(Request("import.session.save", new ImportSessionState(plan.Id, new Dictionary<string, string>()) { Version = 2 }), default)).Succeeded);
        var restored = (await worker.HandleAsync(Request("import.session.load", new { }), default)).Payload!.Value.Deserialize<RestoredImportSession>()!;
        Assert.Equal(plan.Id, restored.Plan.Id); Assert.Empty(restored.State.DownloadJobs);
    }

    [Fact]
    public async Task Concurrent_session_saves_accept_only_one_revision_and_preserve_download_links()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "bundle.cpack.json");
        await File.WriteAllTextAsync(source, """{"formatVersion":"1","id":"a","name":"A","version":"1","resources":[{"id":"r","name":"r.pth","kind":"Model","sizeBytes":1,"sourceUrl":"https://example.invalid/r.pth"}]}""");
        var db = new ResourceLibraryDatabase(Path.Combine(_root, "library"));
        await db.InitializeAsync();
        var seed = new WorkerJob("saved-download", "task.download", JsonSerializer.SerializeToElement(
            new DownloadTaskPayload("https://example.invalid/r.pth", null, "r.pth")), "Completed", "fixture", null, null, 1, DateTimeOffset.UtcNow);
        await db.SaveJobAsync(seed.Id, JsonSerializer.Serialize(seed));
        await using var worker = new PersistentWorkerService(db); await worker.InitializeAsync();
        var import = Request("resource.import", new ImportJobInput(source)); await worker.HandleAsync(import, default);
        var plan = (await WaitAsync(worker, import.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
        var initial = new ImportSessionState(plan.Id, new Dictionary<string, string>());
        var saved = (await worker.HandleAsync(Request("import.session.save", initial), default)).Payload!.Value.Deserialize<ImportSessionState>()!;
        Assert.Equal(1, saved.Revision);
        // Simulate the committed association of online.download after a window read revision 1.
        await db.SaveImportSessionAsync(saved with { DownloadJobs = new Dictionary<string, string> { ["r"] = seed.Id } });
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => worker.HandleAsync(Request("import.session.save", saved), default)));
        Assert.Single(responses, x => x.Succeeded);
        Assert.Equal("import-session-conflict", Assert.Single(responses, x => !x.Succeeded).Error!.Code);
        var restored = (await worker.HandleAsync(Request("import.session.load", new { }), default)).Payload!.Value.Deserialize<RestoredImportSession>()!;
        Assert.Equal(2, restored.State.Revision);
        Assert.Equal(seed.Id, restored.State.DownloadJobs["r"]);
    }

    [Fact]
    public async Task Source_edit_is_atomic_immutable_idempotent_and_rejects_stale_window()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.cpack.json");
        await File.WriteAllTextAsync(source, """{"formatVersion":"1","id":"a","name":"A","version":"1","resources":[{"id":"r","name":"r.pth","kind":"Model","sizeBytes":1}]}""");
        var db = new ResourceLibraryDatabase(Path.Combine(_root, "library"));
        await using var worker = new PersistentWorkerService(db); await worker.InitializeAsync();
        var import = Request("resource.import", new ImportJobInput(source)); await worker.HandleAsync(import, default);
        var original = (await WaitAsync(worker, import.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
        var state = (await worker.HandleAsync(Request("import.session.save", new ImportSessionState(original.Id, new Dictionary<string, string>())), default))
            .Payload!.Value.Deserialize<ImportSessionState>()!;
        var edit = Request("online.source", new OnlineSourceInput(original.Id, "r", "https://example.invalid/r.pth", state.Revision));
        var response = await worker.HandleAsync(edit, default); Assert.True(response.Succeeded, response.Error?.Message);
        var changed = response.Payload!.Value.Deserialize<RestoredImportSession>()!;
        Assert.NotEqual(original.Id, changed.Plan.Id);
        Assert.Null(original.PendingDownloads.Single().SourceUrl);
        Assert.Equal("https://example.invalid/r.pth", changed.Plan.PendingDownloads.Single().SourceUrl);
        Assert.Equal(state.Revision + 1, changed.State.Revision);
        Assert.Equal(changed.Plan.Id, (await db.LoadImportSessionAsync())!.PlanId);
        Assert.True((await worker.HandleAsync(edit, default)).Succeeded);
        var stale = await worker.HandleAsync(Request("online.source", new OnlineSourceInput(original.Id, "r", "https://example.invalid/b.pth", state.Revision)), default);
        Assert.Equal("import-session-conflict", stale.Error!.Code);
        var jobs = (await worker.HandleAsync(Request("job.list", new { }), default)).Payload!.Value.Deserialize<WorkerJob[]>()!;
        Assert.Single(jobs, x => x.Operation == "resource.source");
        Assert.DoesNotContain(jobs, x => x.Operation is "task.download" or "install.execute");
        var restored = (await worker.HandleAsync(Request("import.session.load", new { }), default)).Payload!.Value.Deserialize<RestoredImportSession>()!;
        Assert.Equal(changed.Plan.Id, restored.Plan.Id);
        var localMatchRequest = Request("resource.match-local", new OnlineLocalMatchInput(changed.Plan.Id, "r", [source]));
        Assert.True((await worker.HandleAsync(localMatchRequest, default)).Succeeded);
        var localMatchJob = await WaitAsync(worker, localMatchRequest.RequestId);
        Assert.Equal("Completed", localMatchJob.State);
        Assert.Null(localMatchJob.Result!.Value.Deserialize<OnlineLocalMatchResult>()!.SourcePath);
        Assert.Equal(changed.Plan.Id, (await db.LoadImportSessionAsync())!.PlanId);
        var foreignChoice = changed.State with { ResourceChoices = [new("foreign", "C:/not-in-plan/file.pth", true, "models/file.pth")] };
        Assert.False((await worker.HandleAsync(Request("import.session.save", foreignChoice), default)).Succeeded);
        Assert.Equal(changed.State.Revision, (await db.LoadImportSessionAsync())!.Revision);
        // A retry from the old window must not return an unrelated import as its source-edit result.
        var anotherImport = Request("resource.import", new ImportJobInput(source));
        await worker.HandleAsync(anotherImport, default);
        var anotherPlan = (await WaitAsync(worker, anotherImport.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
        var anotherState = new ImportSessionState(anotherPlan.Id, new Dictionary<string, string>()) { Revision = changed.State.Revision };
        Assert.True((await worker.HandleAsync(Request("import.session.save", anotherState), default)).Succeeded);
        var oldRetry = await worker.HandleAsync(edit, default);
        Assert.Equal("import-session-conflict", oldRetry.Error!.Code);
        Assert.Equal(anotherPlan.Id, (await db.LoadImportSessionAsync())!.PlanId);
    }

    [Fact]
    public async Task Edited_source_and_other_download_links_survive_worker_restart()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "restart.cpack.json");
        await File.WriteAllTextAsync(source, """{"formatVersion":"1","id":"a","name":"A","version":"1","resources":[{"id":"r","name":"r.pth","kind":"Model","sizeBytes":1},{"id":"other","name":"other.pth","kind":"Model","sizeBytes":1,"sourceUrl":"https://example.invalid/other.pth"}]}""");
        var library = Path.Combine(_root, "library");
        var db = new ResourceLibraryDatabase(library); await db.InitializeAsync();
        var download = new WorkerJob("existing-download", "task.download", JsonSerializer.SerializeToElement(
            new DownloadTaskPayload("https://example.invalid/other.pth", null, "other.pth")), "Completed", "fixture", null, null, 1, DateTimeOffset.UtcNow);
        await db.SaveJobAsync(download.Id, JsonSerializer.Serialize(download));
        RestoredImportSession changed;
        WorkerRequest edit;
        await using (var worker = new PersistentWorkerService(db))
        {
            await worker.InitializeAsync();
            var import = Request("resource.import", new ImportJobInput(source)); await worker.HandleAsync(import, default);
            var plan = (await WaitAsync(worker, import.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
            var state = (await worker.HandleAsync(Request("import.session.save", new ImportSessionState(plan.Id,
                new Dictionary<string, string> { ["other"] = download.Id })), default)).Payload!.Value.Deserialize<ImportSessionState>()!;
            edit = Request("online.source", new OnlineSourceInput(plan.Id, "r", "https://example.invalid/new.pth", state.Revision));
            var response = await worker.HandleAsync(edit, default); Assert.True(response.Succeeded, response.Error?.Message);
            changed = response.Payload!.Value.Deserialize<RestoredImportSession>()!;
            Assert.Equal(download.Id, changed.State.DownloadJobs["other"]);
        }
        await using var restarted = new PersistentWorkerService(new ResourceLibraryDatabase(library));
        await restarted.InitializeAsync();
        var restored = (await restarted.HandleAsync(Request("import.session.load", new { }), default)).Payload!.Value.Deserialize<RestoredImportSession>()!;
        Assert.Equal(changed.Plan.Id, restored.Plan.Id);
        Assert.Equal("https://example.invalid/new.pth", restored.Plan.PendingDownloads.Single(x => x.Id == "r").SourceUrl);
        Assert.Equal(download.Id, restored.State.DownloadJobs["other"]);
        Assert.True((await restarted.HandleAsync(edit, default)).Succeeded);
        var linkedEdit = await restarted.HandleAsync(Request("online.source", new OnlineSourceInput(restored.Plan.Id, "other",
            "https://example.invalid/replacement.pth", restored.State.Revision)), default);
        Assert.False(linkedEdit.Succeeded);
        Assert.Contains("已关联下载", linkedEdit.Error!.Message);
        var final = (await restarted.HandleAsync(Request("import.session.load", new { }), default)).Payload!.Value.Deserialize<RestoredImportSession>()!;
        Assert.Equal(restored.State.Revision, final.State.Revision);
        Assert.Equal("https://example.invalid/other.pth", final.Plan.PendingDownloads.Single(x => x.Id == "other").SourceUrl);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Derived_plan_keeps_unseen_download_but_new_import_does_not_inherit_it(bool derived)
    {
        Directory.CreateDirectory(_root);
        var local = Path.Combine(_root, "workflow.json");
        await File.WriteAllTextAsync(local, "{\"version\":1,\"nodes\":[]}");
        var source = Path.Combine(_root, "bundle.cpack.json");
        await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new { formatVersion = "1", id = "a", name = "A", version = "1",
            resources = new[] {
                new { id = "r", name = "r.pth", kind = "Model", sizeBytes = 1L, sourceUrl = "https://example.invalid/r.pth" },
                new { id = "w", name = "workflow.json", kind = "Workflow", sizeBytes = new FileInfo(local).Length, sourceUrl = "https://example.invalid/workflow.json" }
            } }));
        var db = new ResourceLibraryDatabase(Path.Combine(_root, "library")); await db.InitializeAsync();
        var seed = new WorkerJob("saved-download", "task.download", JsonSerializer.SerializeToElement(
            new DownloadTaskPayload("https://example.invalid/r.pth", null, "r.pth")), "Completed", "fixture", null, null, 1, DateTimeOffset.UtcNow);
        await db.SaveJobAsync(seed.Id, JsonSerializer.Serialize(seed));
        await using var worker = new PersistentWorkerService(db); await worker.InitializeAsync();
        var import = Request("resource.import", new ImportJobInput(source)); await worker.HandleAsync(import, default);
        var original = (await WaitAsync(worker, import.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
        var state = new ImportSessionState(original.Id, new Dictionary<string, string> { ["r"] = seed.Id });
        var saved = (await worker.HandleAsync(Request("import.session.save", state), default)).Payload!.Value.Deserialize<ImportSessionState>()!;
        var next = derived ? Request("resource.materialize-local", new OnlineLocalInput(original.Id, "w", local))
            : Request("resource.import", new ImportJobInput(source));
        await worker.HandleAsync(next, default);
        var completed = await WaitAsync(worker, next.RequestId); Assert.Equal("Completed", completed.State);
        var plan = completed.Result!.Value.Deserialize<ImportPlan>()!;
        var response = await worker.HandleAsync(Request("import.session.save", new ImportSessionState(plan.Id, new Dictionary<string, string>())
            { Revision = saved.Revision }), default);
        Assert.True(response.Succeeded, response.Error?.Message);
        var final = response.Payload!.Value.Deserialize<ImportSessionState>()!;
        if (derived) Assert.Equal(seed.Id, final.DownloadJobs["r"]);
        else Assert.Empty(final.DownloadJobs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manifest_without_source_can_materialize_local_payload_only_after_hash_check(bool matchingHash)
    {
        Directory.CreateDirectory(_root);
        var local = Path.Combine(_root, "workflow.json");
        await File.WriteAllTextAsync(local, "{\"version\":1,\"nodes\":[]}");
        var hash = await ResourceImportService.HashAsync(local);
        var manifest = Path.Combine(_root, "local-only.cpack.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new { formatVersion = "1", id = "local", name = "Local", version = "1",
            resources = new[] { new { id = "w", name = "workflow.json", kind = "Workflow", sizeBytes = new FileInfo(local).Length,
                sha256 = matchingHash ? hash : new string('0', 64), deploymentPurpose = "workflows/workflow.json" } } }));
        await using var worker = new PersistentWorkerService(new(Path.Combine(_root, "library"))); await worker.InitializeAsync();
        var import = Request("resource.import", new ImportJobInput(manifest)); await worker.HandleAsync(import, default);
        var plan = (await WaitAsync(worker, import.RequestId)).Result!.Value.Deserialize<ImportPlan>()!;
        Assert.Null(Assert.Single(plan.PendingDownloads).SourceUrl);
        var request = Request("resource.materialize-local", new OnlineLocalInput(plan.Id, "w", local)); await worker.HandleAsync(request, default);
        var result = await WaitAsync(worker, request.RequestId);
        Assert.Equal(matchingHash ? "Completed" : "Failed", result.State);
        if (matchingHash)
        {
            var materialized = result.Result!.Value.Deserialize<ImportPlan>()!;
            Assert.Empty(materialized.PendingDownloads); Assert.Single(materialized.Workflows);
            Assert.Equal(hash, Assert.Single(materialized.Resources).Sha256);
        }
        else Assert.Contains("SHA-256", result.Error);
        var jobs = (await worker.HandleAsync(Request("job.list", new { }), default)).Payload!.Value.Deserialize<WorkerJob[]>()!;
        Assert.DoesNotContain(jobs, x => x.Operation == "task.download");
    }
}
