using System.Text.Json;
using System.Security.Cryptography;
using FlowPack.Core;
using FlowPack.Infrastructure;

var arguments = ParseArguments(args);
if (!arguments.TryGetValue("pipe", out var pipeName) || !arguments.TryGetValue("secret", out var sessionSecret))
{
    Console.Error.WriteLine("Usage: ComfyUI.FlowPack.Worker --pipe <pipe-name> --secret <session-secret> [--library <resource-library-path>]");
    return 2;
}

ResourceLibraryDatabase? libraryDatabase = null;
if (arguments.TryGetValue("library", out var libraryPath))
{
    try
    {
        libraryDatabase = new ResourceLibraryDatabase(libraryPath);
        await libraryDatabase.InitializeAsync();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Unable to open resource library: {exception.Message}");
        return 3;
    }
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var startedAt = DateTimeOffset.UtcNow;
var server = new NamedPipeWorkerServer(pipeName, sessionSecret);
Console.WriteLine("FlowPack Worker IPC is ready.");

try
{
    while (!cancellation.IsCancellationRequested)
    {
        await server.ServeOnceAsync((request, token) => HandleRequestAsync(request, startedAt, libraryDatabase, token), cancellation.Token);
    }
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    return 0;
}
finally
{
    if (libraryDatabase is not null) await libraryDatabase.DisposeAsync();
}

static async Task<WorkerResponse> HandleRequestAsync(
    WorkerRequest request,
    DateTimeOffset startedAt,
    ResourceLibraryDatabase? libraryDatabase,
    CancellationToken cancellationToken)
{
    if (request.Command == WorkerProtocol.PingCommand)
    {
        return new WorkerResponse(request.RequestId, true, JsonSerializer.SerializeToElement(new { protocolVersion = WorkerProtocol.Version, ready = true }));
    }

    if (request.Command == WorkerProtocol.StatusCommand)
    {
        var taskCount = libraryDatabase is null ? 0 : (await libraryDatabase.LoadTasksAsync(cancellationToken)).Count;
        return new WorkerResponse(request.RequestId, true, JsonSerializer.SerializeToElement(new
        {
            protocolVersion = WorkerProtocol.Version,
            state = "idle",
            startedAtUtc = startedAt,
            stagingWritesEnabled = libraryDatabase is not null,
            deploymentWritesEnabled = false,
            installPlanningEnabled = libraryDatabase is not null,
            verificationWritesEnabled = false,
            taskStoreAttached = libraryDatabase is not null,
            taskCount
        }));
    }

    if (request.Command == WorkerProtocol.ListTasksCommand)
    {
        if (libraryDatabase is null)
        {
            return new WorkerResponse(request.RequestId, false, null, new WorkerError("library-not-attached", "Worker 未关联资源库，不能读取任务。"));
        }
        var tasks = await libraryDatabase.LoadTasksAsync(cancellationToken);
        return new WorkerResponse(request.RequestId, true, JsonSerializer.SerializeToElement(tasks));
    }

    if (request.Command == WorkerProtocol.DownloadCommand)
    {
        return await DownloadAsync(request, libraryDatabase, cancellationToken);
    }

    if (request.Command == WorkerProtocol.CreateInstallPlanCommand || request.Command == WorkerProtocol.VerifyCommand)
    {
        return new WorkerResponse(request.RequestId, false, null, new WorkerError(
            "safety-gate-not-met",
            "该命令已保留在 Worker 边界内，但隔离 Desktop 实机验收和 journal 恢复验证尚未完成，拒绝执行。"));
    }

    return new WorkerResponse(request.RequestId, false, null, new WorkerError("unsupported-command", "当前 Worker 尚未实现该命令。"));
}

static async Task<WorkerResponse> DownloadAsync(WorkerRequest request, ResourceLibraryDatabase? libraryDatabase, CancellationToken cancellationToken)
{
    if (libraryDatabase is null)
    {
        return new WorkerResponse(request.RequestId, false, null, new WorkerError("library-not-attached", "Worker 未关联资源库，不能下载。"));
    }
    if (request.Payload is null)
    {
        return new WorkerResponse(request.RequestId, false, null, new WorkerError("invalid-request", "下载请求缺少载荷。"));
    }

    DownloadTaskPayload? payload;
    try
    {
        payload = request.Payload.Value.Deserialize<DownloadTaskPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (JsonException)
    {
        return new WorkerResponse(request.RequestId, false, null, new WorkerError("invalid-request", "下载请求不是有效载荷。"));
    }
    if (payload is null || string.IsNullOrWhiteSpace(payload.FileName) ||
        !string.Equals(payload.FileName, Path.GetFileName(payload.FileName), StringComparison.Ordinal) ||
        payload.FileName is "." or "..")
    {
        return new WorkerResponse(request.RequestId, false, null, new WorkerError("invalid-request", "下载文件名必须是单个安全文件名。"));
    }

    var existing = (await libraryDatabase.LoadTasksAsync(cancellationToken)).FirstOrDefault(task => task.Id == request.RequestId);
    if (existing is not null)
    {
        return new WorkerResponse(request.RequestId, true, JsonSerializer.SerializeToElement(existing));
    }

    var now = DateTimeOffset.UtcNow;
    var task = new WorkerTask(request.RequestId, WorkerTaskKind.Download, WorkerTaskState.Queued, $"下载 {payload.FileName}", "等待下载", null, null, null, now, now);
    await libraryDatabase.SaveTaskAsync(task, cancellationToken);
    try
    {
        task = task with { State = WorkerTaskState.Running, Stage = "正在下载", UpdatedAt = DateTimeOffset.UtcNow };
        await libraryDatabase.SaveTaskAsync(task, cancellationToken);
        var taskDirectory = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.RequestId)));
        var stagingPath = Path.Combine(libraryDatabase.LibraryPath, "staging", "downloads", taskDirectory, payload.FileName);
        using var httpClient = new HttpClient();
        var result = await new VerifiedDownloadService(httpClient).DownloadAsync(
            new DownloadRequest(new Uri(payload.SourceUrl, UriKind.Absolute), stagingPath, payload.ExpectedSha256), cancellationToken);
        task = task with { State = WorkerTaskState.Completed, Stage = "已校验，留在 staging", CompletedBytes = result.BytesWritten, TotalBytes = result.BytesWritten, UpdatedAt = DateTimeOffset.UtcNow };
        await libraryDatabase.SaveTaskAsync(task, cancellationToken);
        return new WorkerResponse(request.RequestId, true, JsonSerializer.SerializeToElement(task));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        task = task with { State = WorkerTaskState.Paused, Stage = "Worker 已停止，下载可从 staging 续传", UpdatedAt = DateTimeOffset.UtcNow };
        await libraryDatabase.SaveTaskAsync(task, CancellationToken.None);
        throw;
    }
    catch (Exception exception) when (exception is ArgumentException or HttpRequestException or IOException or InvalidDataException)
    {
        task = task with { State = WorkerTaskState.Failed, Stage = "下载失败", ErrorMessage = exception.Message, UpdatedAt = DateTimeOffset.UtcNow };
        await libraryDatabase.SaveTaskAsync(task, cancellationToken);
        return new WorkerResponse(request.RequestId, false, null, new WorkerError("download-failed", exception.Message));
    }
}

static Dictionary<string, string> ParseArguments(string[] values)
{
    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index + 1 < values.Length; index += 2)
    {
        if (!values[index].StartsWith("--", StringComparison.Ordinal)) return [];
        parsed[values[index][2..]] = values[index + 1];
    }
    return parsed;
}
