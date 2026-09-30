using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlowPack.Infrastructure;

public sealed record UpdateBootstrapRequest(string Id, string InstallerPath, string Sha256, int ParentProcessId, long ParentStartedUtcTicks);
public sealed record UpdateBootstrapStatus(string State, string Message, int? ExitCode = null);

public sealed class UpdateBootstrap(GlobalWorkCoordinator? coordinator = null)
{
    public async Task<int> RunAsync(string requestPath, CancellationToken token = default,
        Func<UpdateBootstrapRequest, string, CancellationToken, Task>? waitForExit = null,
        Func<string, CancellationToken, Task<int>>? launchInstaller = null)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
        var statusPath = Path.Combine(root, "update-status.json");
        async Task Status(string state, string message, int? code = null)
        {
            var temp = statusPath + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new UpdateBootstrapStatus(state, message, code)), CancellationToken.None);
            File.Move(temp, statusPath, true);
        }
        try
        {
            var request = JsonSerializer.Deserialize<UpdateBootstrapRequest>(await File.ReadAllTextAsync(requestPath, token)) ?? throw new InvalidDataException("更新请求为空。");
            if (!Guid.TryParseExact(request.Id, "N", out _) || request.Sha256.Length != 64 || !request.Sha256.All(Uri.IsHexDigit) ||
                !Regex.IsMatch(Path.GetFileName(request.InstallerPath), @"^ComfyUI-FlowPack-\d+\.\d+\.\d+-Setup\.exe$") ||
                !string.Equals(Path.GetDirectoryName(Path.GetFullPath(request.InstallerPath)), root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新请求或安装器路径无效。");
            ResourceInstallationService.EnsureNoLinks(request.InstallerPath);
            // Keep the verified file open without write/delete sharing through installation.
            await using var installer = new FileStream(request.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
            async Task Verify()
            {
                installer.Position = 0;
                if (!Convert.ToHexString(await SHA256.HashDataAsync(installer, token)).Equals(request.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装器 SHA-256 校验失败，未启动安装器。");
            }
            await Verify();
            if (launchInstaller is null) await EnsureCompatibleProcessesAsync(token);
            await Status("Waiting", "已校验安装器，阻止新任务并等待所有后台任务结束。");
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var watchStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            var watch = waitForExit is null ? WatchParentAsync(request, root, waiting, watchStop.Token) : Task.CompletedTask;
            GlobalWorkCoordinator.UpdateLease exclusive;
            try { exclusive = await (coordinator ?? new GlobalWorkCoordinator()).BeginUpdateAsync(token: waiting.Token); }
            finally { watchStop.Cancel(); await watch; }
            using var update = exclusive;
            await Status("ReadyToExit", "后台任务已安全结束，等待主窗口确认退出。");
            await (waitForExit ?? WaitForParentAsync)(request, root, token);
            if (launchInstaller is null) await EnsureCompatibleProcessesAsync(token);
            await Verify();
            await Status("Installing", "已再次校验安装器，正在安装。协调锁保持到安装器退出。");
            var exit = await (launchInstaller ?? LaunchInstallerAsync)(request.InstallerPath, token);
            await Status(exit == 0 ? "Completed" : "Failed", exit == 0 ? "安装器已正常退出。" : "安装器未成功完成，退出代码：" + exit, exit);
            return exit == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            await Status(ex is OperationCanceledException ? "Cancelled" : "Failed", ex.Message);
            return 1;
        }
    }

    private static async Task WaitForParentAsync(UpdateBootstrapRequest request, string root, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var approval = Path.Combine(root, "proceed.txt");
        while (!File.Exists(approval))
        {
            if (File.Exists(Path.Combine(root, "cancel.txt"))) throw new OperationCanceledException("更新已取消。");
            await Task.Delay(100, deadline.Token);
        }
        if ((await File.ReadAllTextAsync(approval, deadline.Token)).Trim() != request.Id) throw new InvalidDataException("更新退出确认不匹配。");
        try
        {
            using var parent = Process.GetProcessById(request.ParentProcessId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartedUtcTicks) return; // PID was reused; never touch the unrelated process.
            await parent.WaitForExitAsync(deadline.Token);
        }
        catch (ArgumentException) { }
    }

    private static async Task WatchParentAsync(UpdateBootstrapRequest request, string root, CancellationTokenSource waiting, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var parent = Process.GetProcessById(request.ParentProcessId);
                    if (File.Exists(Path.Combine(root, "cancel.txt")) || parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != request.ParentStartedUtcTicks)
                    { waiting.Cancel(); return; }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { waiting.Cancel(); return; }
                await Task.Delay(200, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static async Task<int> LaunchInstallerAsync(string path, CancellationToken token)
    {
        using var installer = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }) ?? throw new IOException("无法启动安装器。");
        // Once launched, never abandon the lease or forcibly terminate an installation on caller cancellation.
        await installer.WaitForExitAsync(CancellationToken.None);
        return installer.ExitCode;
    }

    private static async Task EnsureCompatibleProcessesAsync(CancellationToken token)
    {
        var ownInfrastructure = typeof(UpdateBootstrap).Assembly.Location;
        var expected = await ResourceImportService.HashAsync(ownInfrastructure, token);
        foreach (var name in new[] { "ComfyUI.FlowPack", "ComfyUI.FlowPack.Worker" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    var directory = Path.GetDirectoryName(process.MainModule?.FileName ?? "");
                    var infrastructure = directory is null ? null : Path.Combine(directory, "FlowPack.Infrastructure.dll");
                    if (infrastructure is null || !File.Exists(infrastructure) || await ResourceImportService.HashAsync(infrastructure, token) != expected)
                        throw new IOException("检测到不支持同一更新协调协议的 FlowPack 进程，请先在旧窗口结束任务并退出后重试。进程号：" + process.Id);
                }
                catch (System.ComponentModel.Win32Exception ex) { throw new IOException("无法确认其他 FlowPack 进程的更新兼容性，请关闭其他窗口后重试。", ex); }
            }
        }
    }
}

public static class UpdateBootstrapLauncher
{
    public static async Task<(Process Process, string Root, string Id)> StartAsync(string workerExecutable, string installer, string sha256, CancellationToken token = default)
    {
        if (!File.Exists(workerExecutable)) throw new FileNotFoundException("未找到可独立运行的同版本更新引导程序，请使用完整安装版。", workerExecutable);
        var id = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyUI FlowPack", "updates", id);
        var helper = Path.Combine(root, "helper"); Directory.CreateDirectory(helper);
        var sourceRoot = Path.GetDirectoryName(workerExecutable)!;
        await Task.Run(() =>
        {
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested(); ResourceInstallationService.EnsureNoLinks(file);
                var destination = Path.Combine(helper, Path.GetRelativePath(sourceRoot, file)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, false);
            }
        }, token);
        var stagedInstaller = Path.Combine(root, Path.GetFileName(installer));
        await Task.Run(() => File.Copy(installer, stagedInstaller, false), token);
        using var parent = Process.GetCurrentProcess();
        var request = new UpdateBootstrapRequest(id, stagedInstaller, sha256, parent.Id, parent.StartTime.ToUniversalTime().Ticks);
        var requestPath = Path.Combine(root, "update-request.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), token);
        var start = new ProcessStartInfo(Path.Combine(helper, Path.GetFileName(workerExecutable)))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--install-update"); start.ArgumentList.Add(requestPath);
        return (Process.Start(start) ?? throw new IOException("无法启动独立更新引导程序。"), root, id);
    }
}
