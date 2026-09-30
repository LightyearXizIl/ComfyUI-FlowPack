using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Smoke;

/// <summary>Source-only cross-process probe. Installer execution is deliberately substituted.</summary>
internal static class UpdateCoordinationAcceptance
{
    public static async Task<int> RunAsync()
    {
        foreach (var cancelUpdate in new[] { false, true })
        {
            var root = Path.Combine(Path.GetTempPath(), "FlowPack-update-process-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var children = new List<Process>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var token = timeout.Token;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            var installerExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<int>? updating = null;
            try
            {
                foreach (var role in new[] { "one", "two" })
                {
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var arg in new[] { "--acceptance-update-child", root, role }) start.ArgumentList.Add(arg);
                    children.Add(Process.Start(start) ?? throw new IOException("Cannot start worker probe."));
                }
                async Task WaitMarker(string name)
                {
                    while (!File.Exists(Path.Combine(root, name)))
                    {
                        if (children.Any(x => x.HasExited && x.ExitCode != 0)) throw new IOException("Probe child failed before " + name);
                        await Task.Delay(20, token);
                    }
                }
                await WaitMarker("one.ready"); await WaitMarker("two.ready");
                var coordinator = new GlobalWorkCoordinator(Path.Combine(root, "coordination"));
                var installer = Path.Combine(root, "ComfyUI-FlowPack-0.0.4-Setup.exe");
                await File.WriteAllTextAsync(installer, "not an executable; installer callback substituted", token);
                var requestPath = Path.Combine(root, "update-request.json");
                await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new UpdateBootstrapRequest(
                    Guid.NewGuid().ToString("N"), installer, await ResourceImportService.HashAsync(installer, token), Environment.ProcessId,
                    Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks)), token);
                var launching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                updating = new UpdateBootstrap(coordinator).RunAsync(requestPath, cancel.Token,
                    waitForExit: (_, _, _) => Task.CompletedTask,
                    launchInstaller: async (_, ct) => { launching.TrySetResult(); await installerExit.Task.WaitAsync(ct); return 0; });
                // Observe the real admission barrier, not a status file written before acquisition.
                while (true)
                {
                    try { using var admitted = await coordinator.EnterWorkAsync(token); }
                    catch (IOException) { break; }
                    if (updating.IsCompleted) throw new IOException("Update finished before barrier observation.");
                    await Task.Delay(20, token);
                }
                await File.WriteAllTextAsync(Path.Combine(root, "probe"), "probe", token);
                await WaitMarker("one.blocked"); await WaitMarker("two.blocked");
                if (launching.Task.IsCompleted || updating.IsCompleted) throw new IOException("Update bypassed active worker jobs.");
                if (cancelUpdate)
                {
                    cancel.Cancel();
                    if (await updating != 1 || launching.Task.IsCompleted) throw new IOException("Cancelled update launched installer.");
                    using var resumed = await coordinator.EnterWorkAsync(token);
                    if (children.Any(x => x.HasExited)) throw new IOException("Cancelling update ended existing workers.");
                    await File.WriteAllTextAsync(Path.Combine(root, "one.finish"), "finish", token);
                    await File.WriteAllTextAsync(Path.Combine(root, "two.finish"), "finish", token);
                }
                else
                {
                    await File.WriteAllTextAsync(Path.Combine(root, "one.finish"), "finish", token);
                    await children[0].WaitForExitAsync(token);
                    if (launching.Task.IsCompleted || updating.IsCompleted) throw new IOException("Update ignored second worker.");
                    await File.WriteAllTextAsync(Path.Combine(root, "two.finish"), "finish", token);
                    await launching.Task.WaitAsync(token);
                    try { using var unexpected = await coordinator.EnterWorkAsync(token); throw new InvalidOperationException("Installer lost barrier."); }
                    catch (IOException) { }
                    try { File.WriteAllText(installer, "tampered"); throw new InvalidOperationException("Verified installer not locked."); }
                    catch (IOException) { }
                    installerExit.TrySetResult();
                    if (await updating != 0) throw new IOException("Update probe failed.");
                    using var resumed = await coordinator.EnterWorkAsync(token);
                }
                foreach (var child in children)
                {
                    await child.WaitForExitAsync(token);
                    if (child.ExitCode != 0) throw new IOException("Worker probe failed: " + child.ExitCode);
                }
                var status = JsonSerializer.Deserialize<UpdateBootstrapStatus>(await File.ReadAllTextAsync(Path.Combine(root, "update-status.json"), token))!;
                if (status.State != (cancelUpdate ? "Cancelled" : "Completed")) throw new IOException("Unexpected update state.");
                var result = JsonSerializer.Serialize(new { passed = true, root, cancelUpdate, workerPids = children.Select(x => x.Id).ToArray(),
                    status, realPersistentWorkerProcesses = 2, newJobsBlocked = true, admissionRestored = true,
                    actualInstallerLaunched = false, scope = "cross-process worker barrier; installer callback substituted; no App UI or upgrade lifecycle" });
                await File.WriteAllTextAsync(Path.Combine(root, "result.json"), result, token);
                Console.WriteLine(result);
            }
            finally
            {
                cancel.Cancel(); installerExit.TrySetResult();
                if (updating is not null) await updating;
                foreach (var child in children)
                {
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
                    child.Dispose();
                }
            }
        }
        return 0;
    }

    public static async Task<int> ChildAsync(string root, string role)
    {
        var absolute = Path.GetFullPath(root);
        const string prefix = "FlowPack-update-process-";
        var name = Path.GetFileName(absolute);
        if (Path.GetDirectoryName(absolute) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) ||
            !name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _) || role is not ("one" or "two"))
            throw new InvalidDataException("Probe only accepts its own generated temporary roots.");
        ResourceInstallationService.EnsureNoLinks(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = timeout.Token;
        var coordinator = new GlobalWorkCoordinator(Path.Combine(root, "coordination"));
        await using var worker = new PersistentWorkerService(new(Path.Combine(root, "library-" + role)), async ct =>
        {
            await File.WriteAllTextAsync(Path.Combine(root, role + ".ready"), "ready", ct);
            while (!File.Exists(Path.Combine(root, role + ".finish"))) await Task.Delay(20, ct);
            return Array.Empty<InstanceDescriptor>();
        }, coordinator: coordinator);
        await worker.InitializeAsync();
        WorkerRequest Request(string command, object data) => new(WorkerProtocol.Version, Guid.NewGuid().ToString("N"), "probe", command, JsonSerializer.SerializeToElement(data));
        var job = Request("instance.discover", new { });
        if (!(await worker.HandleAsync(job, token)).Succeeded) throw new IOException("Could not admit test work.");
        while (!File.Exists(Path.Combine(root, "probe"))) await Task.Delay(20, token);
        var blocked = await worker.HandleAsync(Request("instance.discover", new { }), token);
        if (blocked.Succeeded || blocked.Error?.Message.Contains("更新") != true) throw new IOException("New worker job was not blocked.");
        await File.WriteAllTextAsync(Path.Combine(root, role + ".blocked"), JsonSerializer.Serialize(blocked), token);
        while (true)
        {
            var response = await worker.HandleAsync(Request("job.get", job.RequestId), token);
            var current = response.Payload!.Value.Deserialize<WorkerJob>()!;
            if (current.State == "Completed") return 0;
            if (current.State is "Failed" or "Cancelled") throw new IOException("Existing work was interrupted.");
            await Task.Delay(20, token);
        }
    }
}
