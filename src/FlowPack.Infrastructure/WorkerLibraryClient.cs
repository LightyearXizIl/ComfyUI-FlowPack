using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>UI-facing library access. Database writes run only in the persistent Worker.</summary>
public sealed class WorkerLibraryClient(string libraryPath, bool allowWorkerLaunch = true) : IAsyncDisposable
{
    public string LibraryPath { get; } = Path.GetFullPath(libraryPath);
    private string? _pipe;
    private string? _secret;
    private readonly SemaphoreSlim _connection = new(1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task InitializeAsync(CancellationToken token = default) => _ = await CallAsync<bool>("library.initialize", new { }, token: token).ConfigureAwait(false);
    public Task<IReadOnlyList<StoredPackageManifest>> LoadImportedPackagesAsync(CancellationToken token = default) => CallAsync<IReadOnlyList<StoredPackageManifest>>("library.packages", new { }, token: token);
    public Task<IReadOnlyList<StoredWorkflow>> LoadWorkflowsAsync(CancellationToken token = default) => CallAsync<IReadOnlyList<StoredWorkflow>>("library.workflows", new { }, token: token);
    public Task<IReadOnlyList<StoredCandidateInstance>> LoadCandidateInstancesAsync(CancellationToken token = default) => CallAsync<IReadOnlyList<StoredCandidateInstance>>("library.instances", new { }, token: token);
    public Task<IReadOnlyList<StoredPackageDraft>> LoadDraftsAsync(CancellationToken token = default) => CallAsync<IReadOnlyList<StoredPackageDraft>>("library.drafts", new { }, token: token);
    public Task<IReadOnlyList<WorkerTask>> LoadTasksAsync(CancellationToken token = default) => CallAsync<IReadOnlyList<WorkerTask>>("library.tasks", new { }, token: token);
    public async Task SaveImportedPackageAsync(PackageManifest manifest, string source, CancellationToken token = default) => _ = await CallAsync<bool>("library.save-package", new LibrarySaveInput(Manifest: manifest, Source: source), token: token).ConfigureAwait(false);
    public async Task SaveWorkflowAsync(WorkflowDocument workflow, string source, CancellationToken token = default) => _ = await CallAsync<bool>("library.save-workflow", new LibrarySaveInput(Workflow: workflow, Source: source), token: token).ConfigureAwait(false);
    public async Task SaveCandidateInstanceAsync(InstanceFingerprint instance, CancellationToken token = default) => _ = await CallAsync<bool>("library.save-instance", new LibrarySaveInput(Instance: instance), token: token).ConfigureAwait(false);
    public async Task SaveDraftAsync(PackageDraft draft, CancellationToken token = default) => _ = await CallAsync<bool>("library.save-draft", new LibrarySaveInput(Draft: draft), token: token).ConfigureAwait(false);

    public async Task<T> RunAsync<T>(string operation, object input, IProgress<WorkerJob>? progress = null, CancellationToken token = default)
    {
        var job = await CallAsync<WorkerJob>(operation, input, token: token).ConfigureAwait(false);
        job = await WaitForJobAsync(job.Id, progress, token).ConfigureAwait(false);
        return job.Result is { } result ? result.Deserialize<T>(JsonOptions)! : throw new InvalidDataException("Worker 结果为空。");
    }

    public async Task<WorkerJob> WaitForJobAsync(string id, IProgress<WorkerJob>? progress = null, CancellationToken token = default)
    {
        var job = await CallAsync<WorkerJob>("job.get", id, token: token).ConfigureAwait(false);
        while (job.State is "Queued" or "Running" or "PauseRequested" or "CancelRequested")
        {
            progress?.Report(job);
            await Task.Delay(350, token).ConfigureAwait(false);
            job = await CallAsync<WorkerJob>("job.get", job.Id, token: token).ConfigureAwait(false);
        }
        progress?.Report(job);
        if (job.State != "Completed") throw new IOException(job.Error ?? job.Stage);
        return job;
    }

    public async Task<T> CallAsync<T>(string operation, object input, string? requestId = null, CancellationToken token = default, bool allowEmptyResponse = false)
    {
        var id = requestId ?? Guid.NewGuid().ToString("N");
        WorkerResponse response;
        try { response = await SendAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException && !token.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
            await _connection.WaitAsync(token).ConfigureAwait(false);
            try { _pipe = null; _secret = null; } finally { _connection.Release(); }
            response = await SendAsync().ConfigureAwait(false);
        }
        if (!response.Succeeded) throw new IOException(response.Error?.Message ?? "Worker 请求失败。");
        if (response.Payload is null && allowEmptyResponse && default(T) is null) return default!;
        return response.Payload is { } value ? value.Deserialize<T>(JsonOptions)! : throw new InvalidDataException("Worker 响应为空。");
        async Task<WorkerResponse> SendAsync()
        {
            await ConnectAsync(token).ConfigureAwait(false);
            return await new NamedPipeWorkerClient().SendAsync(_pipe!, new(WorkerProtocol.Version, id, _secret!, operation,
                JsonSerializer.SerializeToElement(input)), TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        }
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        if (_pipe is not null) return;
        await _connection.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_pipe is not null) return;
            var state = Path.Combine(LibraryPath, "state"); Directory.CreateDirectory(state);
            FileStream? lease = null;
            for (var i = 0; i < 100 && lease is null; i++)
            {
                try { lease = new FileStream(Path.Combine(state, "worker-launch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { await Task.Delay(100, token).ConfigureAwait(false); }
            }
            using var launchLease = lease ?? throw new IOException("另一个窗口正在启动 Worker，请稍后重试。");
            var sessionFile = Path.Combine(state, "worker-session.json");
            if (File.Exists(sessionFile))
            {
                try
                {
                    var session = JsonSerializer.Deserialize<WorkerSession>(await File.ReadAllTextAsync(sessionFile, token).ConfigureAwait(false));
                    if (session is not null && await PingAsync(session, token).ConfigureAwait(false)) { _pipe = session.Pipe; _secret = session.Secret; return; }
                }
                catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException) { token.ThrowIfCancellationRequested(); }
            }
            if (!allowWorkerLaunch) throw new IOException("指定的 Worker 会话不可用；此连接不允许启动其他 Worker，请重新启动验收宿主后重试。");
            var created = new WorkerSession("flowpack-" + Guid.NewGuid().ToString("N"), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            var worker = FindWorker();
            var start = new ProcessStartInfo(worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : worker) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(worker);
            foreach (var item in new[] { "--pipe", created.Pipe, "--secret", created.Secret, "--library", LibraryPath }) start.ArgumentList.Add(item);
            using var process = Process.Start(start) ?? throw new IOException("无法启动 Worker。");
            for (var i = 0; i < 30; i++)
            {
                if (process.HasExited) throw new IOException("Worker 启动失败，退出代码 " + process.ExitCode);
                if (await PingAsync(created, token).ConfigureAwait(false))
                {
                    await File.WriteAllTextAsync(sessionFile, JsonSerializer.Serialize(created), token).ConfigureAwait(false);
                    _pipe = created.Pipe; _secret = created.Secret; return;
                }
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            throw new IOException("Worker 启动超时。");
        }
        finally { _connection.Release(); }
    }
    private static async Task<bool> PingAsync(WorkerSession session, CancellationToken token)
    {
        try { return (await new NamedPipeWorkerClient().SendAsync(session.Pipe, new(WorkerProtocol.Version, Guid.NewGuid().ToString("N"), session.Secret, WorkerProtocol.PingCommand), TimeSpan.FromMilliseconds(300), token).ConfigureAwait(false)).Succeeded; }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { token.ThrowIfCancellationRequested(); return false; }
    }
    public async Task StopWhenIdleAsync(CancellationToken token = default)
    {
        while (!await CallAsync<bool>("worker.prepare-update", new { }, token: token).ConfigureAwait(false))
            await Task.Delay(500, token).ConfigureAwait(false);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using var lease = new FileStream(Path.Combine(LibraryPath, "state", "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                _pipe = null; _secret = null; return;
            }
            catch (IOException) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
        throw new IOException("后台进程尚未安全退出，不能启动更新安装器。");
    }
    private static string FindWorker()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "worker", "ComfyUI.FlowPack.Worker.exe");
        if (File.Exists(installed)) return installed;
        var parent = new DirectoryInfo(AppContext.BaseDirectory);
        while (parent is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(parent.FullName, "src", "FlowPack.Worker", "bin", configuration, "net10.0-windows", "ComfyUI.FlowPack.Worker.dll");
                if (File.Exists(candidate)) return candidate;
            }
            parent = parent.Parent;
        }
        throw new FileNotFoundException("未找到同版本 Worker，请重新安装 FlowPack。");
    }
    public ValueTask DisposeAsync() { _connection.Dispose(); return ValueTask.CompletedTask; }
    private sealed record WorkerSession(string Pipe, string Secret);
}
