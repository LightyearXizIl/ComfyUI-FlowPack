using System.IO;
using System.Text.Json;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class WorkerLibraryOptionalResponseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_wire_response_requires_explicit_optional_contract(bool optional)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-optional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "state"));
        var pipe = "flowpack-optional-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(root, "state", "worker-session.json"), JsonSerializer.Serialize(new { Pipe = pipe, Secret = "fixture" }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server = new NamedPipeWorkerServer(pipe, "fixture");
        var serving = Task.Run(async () =>
        {
            for (var index = 0; index < 2; index++)
                await server.ServeOnceAsync((request, _) => Task.FromResult(new WorkerResponse(request.RequestId, true,
                    request.Command == WorkerProtocol.PingCommand ? JsonSerializer.SerializeToElement(true) : null)), timeout.Token);
        });
        try
        {
            await using var client = new WorkerLibraryClient(root, allowWorkerLaunch: false);
            if (optional) Assert.Null(await client.CallAsync<RestoredImportSession?>("import.session.load", new { }, token: timeout.Token, allowEmptyResponse: true));
            else await Assert.ThrowsAsync<InvalidDataException>(() => client.CallAsync<RestoredImportSession?>("import.session.load", new { }, token: timeout.Token));
            await serving;
        }
        finally { timeout.Cancel(); try { await serving; } catch (OperationCanceledException) { } Directory.Delete(root, true); }
    }
}
