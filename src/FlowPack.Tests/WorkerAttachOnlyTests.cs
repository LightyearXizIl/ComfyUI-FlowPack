using System.IO;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class WorkerAttachOnlyTests
{
    [Fact]
    public async Task Missing_session_in_attach_only_mode_does_not_launch_production_worker()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-attach-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var client = new WorkerLibraryClient(root, allowWorkerLaunch: false);
            var error = await Assert.ThrowsAsync<IOException>(() => client.InitializeAsync());
            Assert.Contains("不允许启动其他 Worker", error.Message);
            Assert.False(File.Exists(Path.Combine(root, "state", "worker-session.json")));
            Assert.False(File.Exists(Path.Combine(root, "state", "worker.lock")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
