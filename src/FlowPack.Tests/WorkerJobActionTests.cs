using System.Text.Json;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class WorkerJobActionTests
{
    [Theory]
    [InlineData("Running", "task.pause", true)]
    [InlineData("Running", "task.retry", false)]
    [InlineData("Paused", "task.resume", true)]
    [InlineData("Paused", "task.retry", false)]
    [InlineData("Failed", "task.retry", true)]
    [InlineData("Failed", "task.resume", false)]
    [InlineData("Completed", "task.cancel", false)]
    [InlineData("CancelRequested", "task.pause", false)]
    [InlineData("PauseRequested", "task.cancel", true)]
    public void Actions_match_state_and_cancellation_cannot_be_reversed(string state, string command, bool allowed)
    {
        var job = new WorkerJob("a", "task.download", JsonSerializer.SerializeToElement(new { }), state, "", null, null, 1, DateTimeOffset.UtcNow);
        Assert.Equal(allowed, job.CanControl(command));
    }
    [Fact]
    public void Old_serialized_action_list_cannot_grant_install_resume()
    {
        var job = new WorkerJob("a", "install.execute", JsonSerializer.SerializeToElement(new { }), "Paused", "", null, "failure", 1, DateTimeOffset.UtcNow);
        var raw = JsonSerializer.Serialize(job).Replace("task.cancel", "task.resume");
        var restored = JsonSerializer.Deserialize<WorkerJob>(raw)!;
        Assert.False(restored.CanControl("task.resume"));
        Assert.True(restored.CanControl("task.cancel"));
        Assert.Equal("failure", restored.Error);
    }
}
