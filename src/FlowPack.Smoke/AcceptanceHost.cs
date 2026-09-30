using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using FlowPack.App;
using FlowPack.App.Services;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Smoke;

internal static class AcceptanceHost
{
    public static int Run(string mode, string scopeFile, string runId)
    {
        var scope = AcceptanceScope.Load(scopeFile);
        var library = Path.Combine(scope.FixtureRoot, "app-runs", Guid.ParseExact(runId, "N").ToString("N"));
        ResourceInstallationService.EnsureNoLinks(library);
        if (mode == "--acceptance-worker") return WorkerAsync(scope, scopeFile, library).GetAwaiter().GetResult();
        if (mode != "--acceptance-ui") throw new ArgumentException("Unknown acceptance mode");

        var state = Path.Combine(library, "state"); Directory.CreateDirectory(state);
        var context = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(scopeFile)));
        var sessionPath = Path.Combine(state, "acceptance-session.json");
        var session = ReadReadySessionAsync(sessionPath, context).GetAwaiter().GetResult();
        if (session is null)
        {
            var executable = Path.ChangeExtension(typeof(AcceptanceHost).Assembly.Location, ".exe");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "--acceptance-worker", Path.GetFullPath(scopeFile), runId }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("无法启动隔离验收 Worker 宿主。");
            var until = DateTime.UtcNow.AddSeconds(15);
            while (session is null && DateTime.UtcNow < until)
            {
                if (process.HasExited) throw new IOException("隔离验收 Worker 宿主退出：" + process.ExitCode);
                Task.Delay(100).GetAwaiter().GetResult();
                session = ReadReadySessionAsync(sessionPath, context).GetAwaiter().GetResult();
            }
            if (session is null) throw new IOException("隔离验收 Worker 连接超时。");
        }
        var preferences = Path.Combine(library, "preferences");
        var binding = new LibraryBindingStore(Path.Combine(preferences, "library-binding.json"));
        binding.SaveAsync(new(library, DateTimeOffset.UtcNow)).GetAwaiter().GetResult();
        var app = new FlowPack.App.App { SuppressAutomaticWindow = true, ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.InitializeComponent();
        var vm = new ShellViewModel(new ThemePreferenceStore(Path.Combine(preferences, "theme.json")), binding,
            localization: new LocalizationService(Path.Combine(preferences, "language.json")), updateService: new NoInstallerUpdates(),
            libraryClientFactory: path => string.Equals(Path.GetFullPath(path), library, StringComparison.OrdinalIgnoreCase)
                ? new WorkerLibraryClient(path, allowWorkerLaunch: false)
                : throw new IOException("隔离验收窗口不能切换到测试范围外的资源库。"));
        var window = new MainWindow(vm, initializeWorkspace: true) { Title = "FlowPack · 隔离验收（非生产安装资格）" };
        return app.Run(window);
    }

    private static async Task<int> WorkerAsync(AcceptanceScope scope, string scopeFile, string library)
    {
        var state = Path.Combine(library, "state"); Directory.CreateDirectory(state);
        using var lease = new FileStream(Path.Combine(state, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        async Task<IReadOnlyList<InstanceDescriptor>> Discover(CancellationToken token)
        {
            var instances = await new DesktopInstanceDiscovery(configurationRoot: scope.DesktopProfile).DiscoverAsync(token);
            foreach (var instance in instances) scope.ValidateInstance(instance);
            return instances;
        }
        await using var worker = new PersistentWorkerService(new ResourceLibraryDatabase(library), Discover, scope);
        await worker.InitializeAsync();
        var session = new Session("flowpack-acceptance-" + Guid.NewGuid().ToString("N"), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(scopeFile))));
        await File.WriteAllTextAsync(Path.Combine(state, "worker-session.json"), JsonSerializer.Serialize(new { session.Pipe, session.Secret }));
        await File.WriteAllTextAsync(Path.Combine(state, "acceptance-session.json"), JsonSerializer.Serialize(session));
        var server = new NamedPipeWorkerServer(session.Pipe, session.Secret);
        while (!worker.ShutdownRequested)
        {
            try { await server.ServeOnceAsync(worker.HandleAsync, CancellationToken.None); }
            catch (IOException) { /* Reconnect without cancelling persisted jobs, like the production host. */ }
        }
        return 0;
    }

    private static async Task<Session?> ReadReadySessionAsync(string path, string context)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var session = JsonSerializer.Deserialize<Session>(await File.ReadAllTextAsync(path));
            if (session is null || session.Context != context) return null;
            var pong = await new NamedPipeWorkerClient().SendAsync(session.Pipe,
                new(WorkerProtocol.Version, Guid.NewGuid().ToString("N"), session.Secret, WorkerProtocol.PingCommand), TimeSpan.FromMilliseconds(300));
            return pong.Succeeded ? session : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException) { return null; }
    }
    private sealed record Session(string Pipe, string Secret, string Context);
    private sealed class NoInstallerUpdates : IUpdateService
    {
        public Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default) =>
            Task.FromException<UpdateInfo?>(new IOException("隔离功能验收宿主不执行产品更新；升级须另做安装器专项。"));
    }
}
