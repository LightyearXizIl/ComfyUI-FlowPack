using System.IO;
using System.Text.Json;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class GlobalWorkCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-coordination-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Update_waits_for_all_admitted_work_and_blocks_new_work_across_coordinator_instances()
    {
        var one = new GlobalWorkCoordinator(_root); var two = new GlobalWorkCoordinator(_root);
        var work = await one.EnterWorkAsync();
        var retained = work.Retain();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var update = two.BeginUpdateAsync(token: timeout.Token);
        Assert.False(update.IsCompleted);
        await Assert.ThrowsAsync<IOException>(() => one.EnterWorkAsync(timeout.Token));
        work.Dispose(); Assert.False(update.IsCompleted);
        retained.Dispose();
        using (await update)
        {
            await Assert.ThrowsAsync<IOException>(() => one.EnterWorkAsync(timeout.Token));
            await Assert.ThrowsAsync<IOException>(() => one.BeginUpdateAsync(token: timeout.Token));
        }
        using var next = await one.EnterWorkAsync(timeout.Token);
    }

    [Fact]
    public async Task Cancelled_update_reopens_admission_without_cancelling_existing_work()
    {
        var coordinator = new GlobalWorkCoordinator(_root);
        using var work = await coordinator.EnterWorkAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.BeginUpdateAsync(token: cancellation.Token));
        using var another = await coordinator.EnterWorkAsync();
    }

    [Fact]
    public async Task Other_library_cannot_submit_jobs_during_update_but_can_cancel_an_existing_job()
    {
        var coordinator = new GlobalWorkCoordinator(Path.Combine(_root, "global"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = new PersistentWorkerService(new(Path.Combine(_root, "library-one")), async ct =>
            { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return []; }, coordinator: coordinator);
        await using var second = new PersistentWorkerService(new(Path.Combine(_root, "library-two")), ct => Task.FromResult<IReadOnlyList<FlowPack.ComfyUI.InstanceDescriptor>>([]), coordinator: coordinator);
        await first.InitializeAsync(); await second.InitializeAsync();
        WorkerRequest Request(string command, object data, string? id = null) => new(WorkerProtocol.Version, id ?? Guid.NewGuid().ToString("N"), "test", command, JsonSerializer.SerializeToElement(data));
        var request = Request("instance.discover", new { });
        Assert.True((await first.HandleAsync(request, default)).Succeeded);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await entered.Task.WaitAsync(timeout.Token);
        var pending = coordinator.BeginUpdateAsync(token: timeout.Token);
        var denied = await second.HandleAsync(Request("instance.discover", new { }), timeout.Token);
        Assert.False(denied.Succeeded);
        Assert.Contains("更新", denied.Error!.Message);
        Assert.True((await first.HandleAsync(Request("task.cancel", request.RequestId), timeout.Token)).Succeeded);
        using var update = await pending;
        var existing = await first.HandleAsync(Request("job.get", request.RequestId), timeout.Token);
        Assert.Equal("Cancelled", existing.Payload!.Value.Deserialize<WorkerJob>()!.State);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
