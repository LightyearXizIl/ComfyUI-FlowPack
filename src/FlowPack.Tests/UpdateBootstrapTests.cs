using System.IO;
using System.Text.Json;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class UpdateBootstrapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-update-" + Guid.NewGuid().ToString("N"));
    private async Task<string> RequestAsync(bool correctHash = true)
    {
        Directory.CreateDirectory(_root);
        var installer = Path.Combine(_root, "ComfyUI-FlowPack-0.0.4-Setup.exe");
        await File.WriteAllTextAsync(installer, "fixture-not-an-executable");
        var request = new UpdateBootstrapRequest(Guid.NewGuid().ToString("N"), installer,
            correctHash ? await ResourceImportService.HashAsync(installer) : new string('0', 64), 1, 0);
        var path = Path.Combine(_root, "update-request.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request)); return path;
    }

    [Fact]
    public async Task Hash_failure_never_calls_the_installer_or_exit_handoff()
    {
        var called = false;
        var result = await new UpdateBootstrap(new(Path.Combine(_root, "global"))).RunAsync(await RequestAsync(false),
            waitForExit: (request, root, ct) => { called = true; return Task.CompletedTask; },
            launchInstaller: (path, ct) => { called = true; return Task.FromResult(0); });
        Assert.Equal(1, result); Assert.False(called);
        var status = JsonSerializer.Deserialize<UpdateBootstrapStatus>(await File.ReadAllTextAsync(Path.Combine(_root, "update-status.json")))!;
        Assert.Equal("Failed", status.State); Assert.Contains("SHA-256", status.Message);
    }

    [Fact]
    public async Task Missing_parent_cancels_wait_and_releases_update_intent_without_launching()
    {
        var requestPath = await RequestAsync();
        var request = JsonSerializer.Deserialize<UpdateBootstrapRequest>(await File.ReadAllTextAsync(requestPath))!;
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request with { ParentProcessId = int.MaxValue }));
        var coordinator = new GlobalWorkCoordinator(Path.Combine(_root, "global"));
        using var admitted = await coordinator.EnterWorkAsync();
        var launched = false;
        var result = await new UpdateBootstrap(coordinator).RunAsync(requestPath,
            launchInstaller: (path, token) => { launched = true; return Task.FromResult(0); });
        Assert.Equal(1, result); Assert.False(launched);
        using var next = await coordinator.EnterWorkAsync();
        var status = JsonSerializer.Deserialize<UpdateBootstrapStatus>(await File.ReadAllTextAsync(Path.Combine(_root, "update-status.json")))!;
        Assert.Equal("Cancelled", status.State);
    }

    [Theory]
    [InlineData(0, "Completed")]
    [InlineData(5, "Failed")]
    public async Task Barrier_and_verified_file_remain_locked_until_installer_exits(int exitCode, string expectedState)
    {
        var request = await RequestAsync();
        var coordinator = new GlobalWorkCoordinator(Path.Combine(_root, "global"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var operation = new UpdateBootstrap(coordinator).RunAsync(request, timeout.Token,
            waitForExit: async (req, root, ct) =>
            {
                await Assert.ThrowsAsync<IOException>(() => coordinator.EnterWorkAsync(ct));
                Assert.Throws<IOException>(() => File.WriteAllText(req.InstallerPath, "changed"));
            },
            launchInstaller: async (path, ct) => { entered.SetResult(); await exited.Task.WaitAsync(ct); return exitCode; });
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            await Assert.ThrowsAsync<IOException>(() => coordinator.EnterWorkAsync(timeout.Token));
        }
        finally { exited.TrySetResult(); }
        Assert.Equal(exitCode == 0 ? 0 : 1, await operation);
        using var next = await coordinator.EnterWorkAsync(timeout.Token);
        var status = JsonSerializer.Deserialize<UpdateBootstrapStatus>(await File.ReadAllTextAsync(Path.Combine(_root, "update-status.json")))!;
        Assert.Equal(expectedState, status.State); Assert.Equal(exitCode, status.ExitCode);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
