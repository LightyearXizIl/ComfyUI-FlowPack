using System.Text.Json;

namespace FlowPack.Core;

public static class WorkerProtocol
{
    public const string Version = "1";
    public const string PingCommand = "ping";
    public const string StatusCommand = "worker.status";
    public const string ListTasksCommand = "task.list";
    public const string DownloadCommand = "task.download";
}

public sealed record WorkerRequest(
    string ProtocolVersion,
    string RequestId,
    string SessionSecret,
    string Command,
    JsonElement? Payload = null);

public sealed record WorkerResponse(
    string RequestId,
    bool Succeeded,
    JsonElement? Payload = null,
    WorkerError? Error = null);

public sealed record WorkerError(string Code, string Message);

public sealed record DownloadTaskPayload(string SourceUrl, string ExpectedSha256, string FileName);
