using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>
/// Local worker IPC transport. CurrentUserOnly prevents other Windows users from
/// connecting; the per-launch secret prevents unrelated local clients from issuing commands.
/// </summary>
public sealed class NamedPipeWorkerServer
{
    private const int MaximumMessageCharacters = 1_048_576;
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly byte[] _sessionSecret;

    public NamedPipeWorkerServer(string pipeName, string sessionSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionSecret);
        PipeName = pipeName;
        _sessionSecret = Encoding.UTF8.GetBytes(sessionSecret);
    }

    public string PipeName { get; }

    public async Task ServeOnceAsync(
        Func<WorkerRequest, CancellationToken, Task<WorkerResponse>> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        await using var server = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        var message = await reader.ReadLineAsync(cancellationToken);
        var response = await ProcessMessageAsync(message, handler, cancellationToken);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SerializerOptions));
    }

    private async Task<WorkerResponse> ProcessMessageAsync(
        string? message,
        Func<WorkerRequest, CancellationToken, Task<WorkerResponse>> handler,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaximumMessageCharacters)
        {
            return Failure(string.Empty, "invalid-request", "IPC 请求为空或超过大小限制。");
        }

        WorkerRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<WorkerRequest>(message, SerializerOptions);
        }
        catch (JsonException)
        {
            return Failure(string.Empty, "invalid-json", "IPC 请求不是有效 JSON。");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.Command))
        {
            return Failure(request?.RequestId ?? string.Empty, "invalid-request", "IPC 请求缺少必要字段。");
        }
        if (request.ProtocolVersion != WorkerProtocol.Version)
        {
            return Failure(request.RequestId, "unsupported-protocol", "Worker 协议版本不匹配。");
        }
        if (!SecretsMatch(request.SessionSecret))
        {
            return Failure(request.RequestId, "unauthorized", "Worker 会话凭证无效。");
        }

        try
        {
            var response = await handler(request, cancellationToken);
            return response with { RequestId = request.RequestId };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failure(request.RequestId, "internal-error", "Worker 未能处理该请求。");
        }
    }

    private bool SecretsMatch(string suppliedSecret)
    {
        if (string.IsNullOrWhiteSpace(suppliedSecret)) return false;
        var supplied = Encoding.UTF8.GetBytes(suppliedSecret);
        return supplied.Length == _sessionSecret.Length && CryptographicOperations.FixedTimeEquals(supplied, _sessionSecret);
    }

    private static WorkerResponse Failure(string requestId, string code, string message) =>
        new(requestId, false, null, new WorkerError(code, message));
}

public sealed class NamedPipeWorkerClient
{
    private const int MaximumMessageCharacters = 1_048_576;
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<WorkerResponse> SendAsync(
        string pipeName,
        WorkerRequest request,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(request);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(connectTimeout);
        await client.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, SerializerOptions));
        var message = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaximumMessageCharacters)
        {
            throw new InvalidDataException("Worker 返回了空响应或超过大小限制的响应。");
        }
        try
        {
            return JsonSerializer.Deserialize<WorkerResponse>(message, SerializerOptions)
                ?? throw new InvalidDataException("Worker 返回了无效响应。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Worker 返回的不是有效 JSON。", exception);
        }
    }
}
