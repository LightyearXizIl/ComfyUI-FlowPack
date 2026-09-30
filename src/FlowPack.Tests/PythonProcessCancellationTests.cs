using System.Diagnostics;
using System.IO;
using System.Reflection;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PythonProcessCancellationTests
{
    private static Task<string> Run(string executable, string[] args, CancellationToken token) =>
        (Task<string>)typeof(PythonDependencyService).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [executable, args, token])!;

    [Fact]
    public async Task Already_cancelled_does_not_start_an_executable()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Run("nonexistent-flowpack-test-executable.exe", [], new CancellationToken(true)));
    }

    [Fact]
    public async Task Cancellation_returns_only_after_owned_process_exits()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-python-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "pid.txt");
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        using var cancel = new CancellationTokenSource();
        Process? child = null;
        Task<string>? running = null;
        try
        {
            running = Run(shell, ["-NoProfile", "-NonInteractive", "-Command",
                "$taskPidPath = '" + marker.Replace("'", "''") + "'; [IO.File]::WriteAllText($taskPidPath, [string]$PID); [Console]::WriteLine('started'); Start-Sleep -Seconds 60"], cancel.Token);
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(marker) && !running.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(20);
            Assert.True(File.Exists(marker), "The owned test process did not start.");
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(marker)));
            Assert.False(child.HasExited);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.True(child.HasExited);
        }
        finally
        {
            cancel.Cancel();
            if (child is not null)
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
                child.Dispose();
            }
            if (running is not null) { try { await running; } catch (OperationCanceledException) { } }
            Directory.Delete(root, true);
        }
    }
}
