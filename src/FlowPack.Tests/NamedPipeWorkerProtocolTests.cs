using System.Text.Json;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class NamedPipeWorkerProtocolTests
{
    [Fact]
    public async Task Current_session_can_send_a_versioned_ping()
    {
        var pipeName = $"flowpack-test-{Guid.NewGuid():N}";
        var server = new NamedPipeWorkerServer(pipeName, "test-session-secret");
        var serverTask = server.ServeOnceAsync((request, _) =>
        {
            Assert.Equal(WorkerProtocol.PingCommand, request.Command);
            return Task.FromResult(new WorkerResponse(request.RequestId, true, JsonSerializer.SerializeToElement(new { ready = true })));
        });

        var response = await new NamedPipeWorkerClient().SendAsync(
            pipeName,
            new WorkerRequest(WorkerProtocol.Version, "request-1", "test-session-secret", WorkerProtocol.PingCommand),
            TimeSpan.FromSeconds(5));
        await serverTask;

        Assert.True(response.Succeeded);
        Assert.Equal("request-1", response.RequestId);
        Assert.True(response.Payload!.Value.GetProperty("ready").GetBoolean());
    }

    [Fact]
    public async Task Invalid_session_secret_is_rejected_before_the_handler_runs()
    {
        var pipeName = $"flowpack-test-{Guid.NewGuid():N}";
        var server = new NamedPipeWorkerServer(pipeName, "correct-secret");
        var handlerWasCalled = false;
        var serverTask = server.ServeOnceAsync((_, _) =>
        {
            handlerWasCalled = true;
            return Task.FromResult(new WorkerResponse("unexpected", true));
        });

        var response = await new NamedPipeWorkerClient().SendAsync(
            pipeName,
            new WorkerRequest(WorkerProtocol.Version, "request-2", "incorrect-secret", WorkerProtocol.PingCommand),
            TimeSpan.FromSeconds(5));
        await serverTask;

        Assert.False(response.Succeeded);
        Assert.Equal("unauthorized", response.Error!.Code);
        Assert.False(handlerWasCalled);
    }

    [Fact]
    public async Task Protocol_mismatch_is_rejected_before_the_handler_runs()
    {
        var pipeName = $"flowpack-test-{Guid.NewGuid():N}";
        var server = new NamedPipeWorkerServer(pipeName, "test-session-secret");
        var handlerWasCalled = false;
        var serverTask = server.ServeOnceAsync((_, _) =>
        {
            handlerWasCalled = true;
            return Task.FromResult(new WorkerResponse("unexpected", true));
        });

        var response = await new NamedPipeWorkerClient().SendAsync(
            pipeName,
            new WorkerRequest("999", "request-3", "test-session-secret", WorkerProtocol.PingCommand),
            TimeSpan.FromSeconds(5));
        await serverTask;

        Assert.False(response.Succeeded);
        Assert.Equal("unsupported-protocol", response.Error!.Code);
        Assert.False(handlerWasCalled);
    }
}
