using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

/// <summary>Opt-in real pip cancellation in a new venv; no network or Desktop writes.</summary>
public sealed class PythonInterruptedInstallIntegrationTests
{
    [PythonEnvironmentFact]
    public async Task Partial_real_wheel_install_is_retained_and_worker_restart_requires_repair()
    {
        var parent = Path.GetFullPath(Environment.GetEnvironmentVariable("FLOWPACK_OFFICIAL_TEST_ROOT")
            ?? throw new InvalidOperationException("Output path required"));
        var root = Path.Combine(parent, "python-interrupted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ResourceInstallationService.EnsureNoLinks(root);
        var environment = Path.Combine(root, "environment");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("FLOWPACK_TEST_PYTHON_BASE")!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-I", "-m", "venv", environment }) start.ArgumentList.Add(arg);
        using (var process = Process.Start(start)!)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(deadline.Token); }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            }
            Assert.True(process.ExitCode == 0, await error); await output;
        }

        const int payloadCount = 2048;
        var wheel = Path.Combine(root, "flowpack_interrupt_fixture-1.0-py3-none-any.whl");
        using (var archive = ZipFile.Open(wheel, ZipArchiveMode.Create))
        {
            void Text(string name, string value)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(value);
            }
            Text("flowpack_interrupt_fixture/__init__.py", "# Local acceptance fixture; no executable installation hooks.\n");
            var bytes = new byte[65536];
            for (var index = 0; index < payloadCount; index++)
            {
                using var stream = archive.CreateEntry($"flowpack_interrupt_fixture/payload-{index:D4}.bin").Open();
                stream.Write(bytes);
            }
            Text("flowpack_interrupt_fixture-1.0.dist-info/METADATA", "Metadata-Version: 2.1\nName: flowpack-interrupt-fixture\nVersion: 1.0\n");
            Text("flowpack_interrupt_fixture-1.0.dist-info/WHEEL", "Wheel-Version: 1.0\nGenerator: FlowPack-local-acceptance\nRoot-Is-Purelib: true\nTag: py3-none-any\n");
            Text("flowpack_interrupt_fixture-1.0.dist-info/RECORD", "");
        }
        var python = Path.Combine(environment, "Scripts", "python.exe");
        var library = Path.Combine(root, "library");
        var service = new PythonDependencyService(library);
        var before = await PythonDependencyService.SnapshotAsync(python, deadline.Token);
        var plan = new PythonDependencyPlan(python, before,
            [new("flowpack-interrupt-fixture", "1.0", wheel, await ResourceImportService.HashAsync(wheel, deadline.Token))]);
        var installationId = Guid.NewGuid().ToString("N");
        var package = Path.Combine(environment, "Lib", "site-packages", "flowpack_interrupt_fixture");
        var marker = Path.Combine(package, "__init__.py");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var running = service.InstallAsync(installationId, plan, cancel.Token);
        try
        {
            while (!File.Exists(marker) && !running.IsCompleted)
                await Task.Delay(1, deadline.Token);
            Assert.True(File.Exists(marker), "pip never began extracting the fixture; this run proves no interrupted installation.");
            Assert.False(running.IsCompleted, "Missed the pip extraction window.");
            cancel.Cancel();
            var error = await Assert.ThrowsAsync<IOException>(() => running);
            Assert.Contains("需要修复", error.Message);
        }
        finally
        {
            cancel.Cancel();
            try { await running; } catch (IOException) { }
        }
        var retained = Directory.GetFiles(package);
        Assert.InRange(retained.Length, 1, payloadCount);
        var after = await PythonDependencyService.SnapshotAsync(python, deadline.Token);
        Assert.All(before, entry => Assert.Equal(entry.Value, after[entry.Key]));
        var journalPath = Path.Combine(library, "state", "python-attempts", installationId + ".json");
        using var journal = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath, deadline.Token));
        Assert.Equal("NeedsRepair", journal.RootElement.GetProperty("state").GetString());
        Assert.Equal(python, journal.RootElement.GetProperty("PythonPath").GetString());
        Assert.Equal(before.Count, journal.RootElement.GetProperty("before").EnumerateObject().Count());

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using (var worker = new PersistentWorkerService(new(library))) await worker.InitializeAsync();
            await using var database = new ResourceLibraryDatabase(library);
            var job = Assert.Single((await database.LoadJobsAsync()).Select(x => JsonSerializer.Deserialize<WorkerJob>(x)!),
                x => x.Id == "python-recovery-" + installationId);
            Assert.Equal("NeedsReview", job.State);
            Assert.Empty(job.AvailableActions);
            Assert.Contains("Python", job.Error);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new PythonDependencyService(library)
            .InstallAsync(Guid.NewGuid().ToString("N"), plan, deadline.Token));
        Assert.Equal(retained.Order(), Directory.GetFiles(package).Order());
        await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            passed = true, scope = "real offline pip wheel extraction cancelled in a disposable venv; not forced host termination or Desktop deployment",
            python, installationId, before, after, retainedFiles = retained.Length, completeFileCount = payloadCount + 1,
            journalPath, workerRestarts = 2, retryBlocked = true, originalDesktopModified = false
        }), deadline.Token);
        // Retain this uniquely generated venv and journal as partial-install evidence.
    }
}
