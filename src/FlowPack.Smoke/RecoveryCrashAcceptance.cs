using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Infrastructure;

namespace FlowPack.Smoke;

/// <summary>Source-only crash probe; never included in the product installer.</summary>
internal static class RecoveryCrashAcceptance
{
    public static async Task<int> RunAsync()
    {
        var results = new List<object>();
        foreach (var phase in new[] { "during-copy", "before-commit", "after-commit" })
        {
            var root = Path.Combine(Path.GetTempPath(), "FlowPack-crash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "probe-scope.json"), JsonSerializer.Serialize(new { Root = root, Phase = phase }));
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--acceptance-crash-child"); start.ArgumentList.Add(root); start.ArgumentList.Add(phase);
            using var process = Process.Start(start) ?? throw new IOException("Could not start isolated crash probe.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                bool ReachedCheckpoint()
                {
                    if (phase != "during-copy") return File.Exists(Path.Combine(root, "ready.json"));
                    var targetDirectory = Path.Combine(root, "isolated-target");
                    if (!Directory.Exists(targetDirectory)) return false;
                    var partial = Directory.GetFiles(targetDirectory, "*.tmp").SingleOrDefault();
                    return partial is not null && new FileInfo(partial).Length is > 0 and < 536870912;
                }
                while (!ReachedCheckpoint())
                {
                    if (process.HasExited) throw new IOException("Crash child exited before its checkpoint: " + process.ExitCode);
                    await Task.Delay(phase == "during-copy" ? 1 : 50, deadline.Token);
                }
                var journalFile = Directory.GetFiles(Path.Combine(root, "state", "journal"), "*.json").Single();
                var beforeTermination = JsonSerializer.Deserialize<DeploymentJournal>(await File.ReadAllTextAsync(journalFile))!;
                if (beforeTermination.State != (phase != "after-commit" ? "Running" : "InstallingPython") ||
                    beforeTermination.Files.Single().State != (phase != "after-commit" ? "Intent" : "Committed"))
                    throw new InvalidDataException("Child did not reach the requested journal checkpoint.");
                // Kill only this returned child handle, after it has confirmed the durable checkpoint.
                process.Kill(); await process.WaitForExitAsync(deadline.Token);
                if (process.ExitCode == 0) throw new IOException("The child did not end by forced termination.");
                var partialPath = beforeTermination.Files.Single().TemporaryPath!;
                var partialBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                var partialHash = File.Exists(partialPath) ? await ResourceImportService.HashAsync(partialPath) : null;
                if (phase == "during-copy" && (partialBytes <= 0 || partialBytes >= 536870912))
                    throw new InvalidDataException("Missed the partial-copy window; this run does not prove interrupted copying.");
                var report = (await new ResourceInstallationService(root).RecoverAsync()).Single();
                var item = report.Files.Single();
                var sourceHash = await ResourceImportService.HashAsync(Path.Combine(root, "source.bin"));
                var committed = phase == "after-commit";
                if (report.State != "NeedsReview" || item.State != (committed ? "VerifiedPresent" : "NotPresent"))
                    throw new InvalidDataException("Unexpected recovery state.");
                if (File.Exists(item.TargetPath) != committed) throw new InvalidDataException("Unexpected target presence.");
                var retained = committed ? item.TargetPath : item.RecoveryPath ?? throw new InvalidDataException("Missing retained partial.");
                if (await ResourceImportService.HashAsync(retained) != (phase == "during-copy" ? partialHash : sourceHash))
                    throw new InvalidDataException("Recovered bytes changed.");
                var repeated = (await new ResourceInstallationService(root).RecoverAsync()).Single();
                if (JsonSerializer.Serialize(repeated) != JsonSerializer.Serialize(report)) throw new InvalidDataException("Repeated recovery changed evidence.");
                await using (var worker = new PersistentWorkerService(new(root))) await worker.InitializeAsync();
                var evidence = new { phase, root, childPid = process.Id, exitCode = process.ExitCode, sourceHash, partialBytes, partialHash, beforeTermination, report,
                    repeatedRecovery = true, workerRestart = true, pythonExecuted = false };
                await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(evidence));
                results.Add(evidence);
            }
            finally
            {
                if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(results));
        return 0;
    }

    public static async Task<int> ChildAsync(string root, string phase)
    {
        var absolute = Path.GetFullPath(root);
        var name = Path.GetFileName(absolute);
        if (!string.Equals(Path.GetDirectoryName(absolute), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith("FlowPack-crash-", StringComparison.Ordinal) || !Guid.TryParseExact(name[15..], "N", out _) ||
            phase is not ("during-copy" or "before-commit" or "after-commit"))
            throw new InvalidDataException("Crash probe only accepts its generated temporary directory.");
        ResourceInstallationService.EnsureNoLinks(absolute);
        using var scope = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(absolute, "probe-scope.json")));
        if (scope.RootElement.GetProperty("Root").GetString() != root || scope.RootElement.GetProperty("Phase").GetString() != phase)
            throw new InvalidDataException("Probe scope does not match.");
        var source = Path.Combine(root, "source.bin");
        var block = Enumerable.Range(0, 262144).Select(x => (byte)(x % 251)).ToArray();
        await using (var payload = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None, 262144, true))
            for (var index = 0; index < (phase == "during-copy" ? 2048 : 1); index++) await payload.WriteAsync(block);
        var target = Path.Combine(root, "isolated-target", "model.bin");
        var instance = new InstanceDescriptor("crash-fixture", "crash-fixture", "desktop-2", null, root, root, root,
            root, root, Path.Combine(root, "custom_nodes"), null, [], root, [], "fixture", []);
        var id = Guid.NewGuid().ToString("N");
        var temporary = target + ".flowpack-" + id + ".tmp";
        var plan = new ResourceInstallPlan(id, instance,
            [new(source, target, new FileInfo(source).Length, await ResourceImportService.HashAsync(source), false)], [], new FileInfo(source).Length, DateTimeOffset.UtcNow);
        async Task PauseAsync()
        {
            await using (var marker = new FileStream(Path.Combine(root, "ready.json.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(marker, new { phase, id }); await marker.FlushAsync(); marker.Flush(true);
            }
            File.Move(Path.Combine(root, "ready.json.tmp"), Path.Combine(root, "ready.json"));
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        await new ResourceInstallationService(root).ExecuteAsync(plan, async (descriptor, _) =>
        {
            if (phase == "before-commit" && File.Exists(temporary)) await PauseAsync();
            return descriptor;
        }, installEnvironment: phase == "after-commit" ? _ => PauseAsync() : null);
        throw new InvalidOperationException("Crash probe unexpectedly reached completion.");
    }
}
