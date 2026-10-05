using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlowPack.App;
using FlowPack.App.Services;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Smoke;

/// <summary>Automates the real WPF commands and IPC service within an allowlisted disposable scope.
/// This host records installation evidence; it never claims Desktop runtime/inference qualification.</summary>
internal static class TransferAcceptanceHost
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Run(string scopeFile, string runId, string? productionWorker = null, string? targetId = null)
    {
        var scope = AcceptanceScope.Load(scopeFile);
        using var configuration = JsonDocument.Parse(File.ReadAllText(scopeFile));
        var config = configuration.RootElement;
        var run = Path.Combine(scope.FixtureRoot, "transfer-runs", Guid.ParseExact(runId, "N").ToString("N"));
        ResourceInstallationService.EnsureNoLinks(run);
        if (Directory.Exists(run)) throw new IOException("验收运行目录已存在；请使用新的 runId，保留已有证据。");
        var library = Path.Combine(run, "library");
        var state = Path.Combine(library, "state"); Directory.CreateDirectory(state);
        var profile = scope.DesktopProfile;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        using var lease = productionWorker is null ? new FileStream(Path.Combine(state, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None) : null;
        async Task<IReadOnlyList<InstanceDescriptor>> Discover(CancellationToken token)
        {
            var instances = await new DesktopInstanceDiscovery(configurationRoot: profile).DiscoverAsync(token);
            foreach (var instance in instances) scope.ValidateInstance(instance);
            if (instances.Count != scope.InstanceIds.Count) throw new IOException("发现实例数与隔离白名单不一致。");
            return instances;
        }
        PersistentWorkerService? worker = null;
        var pipe = "flowpack-transfer-" + Guid.NewGuid().ToString("N");
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(Path.Combine(state, "worker-session.json"), JsonSerializer.Serialize(new { Pipe = pipe, Secret = secret, DesktopProfile = profile }));
        Process? productionProcess = null;
        Task<string>? stdout = null, stderr = null;
        Task serving;
        if (productionWorker is null)
        {
            worker = new PersistentWorkerService(new ResourceLibraryDatabase(library), Discover, scope);
            worker.InitializeAsync(lifetime.Token).GetAwaiter().GetResult();
            var server = new NamedPipeWorkerServer(pipe, secret);
            serving = Task.Run(async () =>
            {
                while (!lifetime.IsCancellationRequested)
                {
                    try { await server.ServeOnceAsync(worker.HandleAsync, lifetime.Token); }
                    catch (IOException) when (!lifetime.IsCancellationRequested) { }
                }
            });
        }
        else
        {
            if (!Path.IsPathFullyQualified(productionWorker) || !File.Exists(productionWorker) || !productionWorker.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("生产Worker必须是现有的绝对EXE路径。");
            ResourceInstallationService.EnsureNoLinks(productionWorker);
            // Validate the allowed discovery set before launching the unmodified production
            // Worker. Its own shipped capability provider remains the authority for writes.
            Discover(lifetime.Token).GetAwaiter().GetResult();
            var start = new ProcessStartInfo(productionWorker) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--pipe", pipe, "--secret", secret, "--library", library, "--desktop-profile", profile }) start.ArgumentList.Add(argument);
            productionProcess = Process.Start(start) ?? throw new IOException("无法启动指定生产Worker。");
            stdout = productionProcess.StandardOutput.ReadToEndAsync(); stderr = productionProcess.StandardError.ReadToEndAsync();
            var connected = false;
            for (var attempt = 0; attempt < 50 && !connected; attempt++)
            {
                if (productionProcess.HasExited) throw new IOException("生产Worker提前退出：" + productionProcess.ExitCode);
                try
                {
                    connected = new NamedPipeWorkerClient().SendAsync(pipe,
                        new(WorkerProtocol.Version, Guid.NewGuid().ToString("N"), secret, WorkerProtocol.PingCommand), TimeSpan.FromMilliseconds(300), lifetime.Token)
                        .GetAwaiter().GetResult().Succeeded;
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { }
            }
            if (!connected) throw new IOException("生产Worker连接超时。");
            Save(Path.Combine(run, "production-worker.json"), new { pid = productionProcess.Id, executable = productionWorker,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(productionWorker))), library, desktopProfile = profile, testCapabilityInjected = false });
            serving = Task.CompletedTask;
        }
        var preferences = Path.Combine(run, "preferences");
        var binding = new LibraryBindingStore(Path.Combine(preferences, "binding.json"));
        binding.SaveAsync(new(library, DateTimeOffset.UtcNow)).GetAwaiter().GetResult();
        var theme = new ThemePreferenceStore(Path.Combine(preferences, "theme.json"));
        theme.SaveAsync(ThemeDefaults.Create(ThemeBase.Dark)).GetAwaiter().GetResult();
        var client = new WorkerLibraryClient(library, allowWorkerLaunch: false, desktopProfile: profile);
        var app = new FlowPack.App.App { SuppressAutomaticWindow = true, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var vm = new ShellViewModel(theme, binding, localization: new LocalizationService(Path.Combine(preferences, "language.json")),
            updateService: new NoUpdates(), libraryClientFactory: path => Same(path, library) ? client : throw new IOException("资源库越出隔离范围。"),
            desktopProfile: profile);
        var window = new MainWindow(vm) { Width = 1280, Height = 800, Title = "FlowPack · 资源转移隔离验收" };
        var exitCode = 1;
        var exerciseStarted = false;
        var frame = new DispatcherFrame();
        window.Loaded += async (_, _) =>
        {
            if (exerciseStarted) return;
            exerciseStarted = true;
            try
            {
                await ExerciseAsync(scope, config, run, library, vm, window, client, productionWorker is not null, targetId, lifetime.Token);
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Save(Path.Combine(run, "failure.json"), new { error = ex.ToString(), vm.CoreNotice, vm.ExportState, vm.ImportState, vm.ImportDetails });
                try { Capture(window, Path.Combine(run, "failure.png")); } catch { }
                Console.Error.WriteLine(ex);
            }
            finally
            {
                vm.DetachWindow();
                _ = window.Dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
            }
        };
        // This source-only host pumps its own bounded frame. Product App startup and exit
        // are verified independently by the portable UI process; do not reenter Shutdown
        // while a dispatcher continuation is closing this acceptance window.
        window.Show();
        Dispatcher.PushFrame(frame);
        window.Close();
        if (productionProcess is not null)
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            client.StopWhenIdleAsync(shutdown.Token).GetAwaiter().GetResult();
            if (!productionProcess.WaitForExit(10_000)) throw new IOException("生产Worker尚未安全退出，保留进程与安装证据。");
            File.WriteAllText(Path.Combine(run, "production-worker-stdout.log"), stdout!.GetAwaiter().GetResult());
            File.WriteAllText(Path.Combine(run, "production-worker-stderr.log"), stderr!.GetAwaiter().GetResult());
            productionProcess.Dispose();
        }
        lifetime.Cancel();
        try { serving.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        client.DisposeAsync().GetAwaiter().GetResult();
        if (worker is not null) worker.DisposeAsync().GetAwaiter().GetResult();
        Console.WriteLine(JsonSerializer.Serialize(new { passed = exitCode == 0, evidenceDirectory = run, scope = "WPF commands + persistent Worker IPC + installation; Desktop inference remains separate" }));
        return exitCode;
    }

    private static async Task ExerciseAsync(AcceptanceScope scope, JsonElement config, string run, string library,
        ShellViewModel vm, MainWindow window, WorkerLibraryClient client, bool usesProductionWorker, string? targetId, CancellationToken token)
    {
        string Read(string name) => config.GetProperty(name).GetString() ?? throw new InvalidDataException(name + "为空。");
        foreach (var field in new[] { "WorkflowPath", "ModelPath", "NodePackageSource" }) RequireScoped(Read(field), scope.FixtureRoot);
        await vm.InitializeWorkspaceAsync();
        await IdleAsync(vm, token);
        Require(vm.DesktopInstances.Count == scope.InstanceIds.Count, "页面实例白名单数量不一致。");
        vm.SelectedInstance = vm.DesktopInstances.Single(x => x.Id == Read("SourceInstanceId"));
        await IdleAsync(vm, token);
        Require(vm.LocalWorkflows.Count > 0 && vm.LocalModels.Count > 0 && vm.LocalNodes.Count > 0, "源实例资源列表未完成扫描：" + vm.CoreNotice);
        vm.CurrentPage = FlowPage.Library;
        vm.LibraryTabIndex = 0;
        Capture(window, Path.Combine(run, "source-library-dark.png"));
        var workflow = vm.LocalWorkflows.Single(x => Same(x.Path, Read("WorkflowPath")));
        workflow.IsSelected = true;
        vm.BeginExportCommand.Execute(null);
        await IdleAsync(vm, token);
        Require(vm.ExportState is TransferState.Ready or TransferState.NeedsAttention, "导出自动检查未完成：" + vm.ExportStatus);
        var model = vm.LocalModels.Single(x => Same(x.Path, Read("ModelPath")));
        var node = vm.LocalNodes.Single(x => Same(x.Path, Read("NodePackageSource")));
        Require(model.IsSelected && node.IsSelected, "自动依赖没有加入模型和节点包。");
        var automatic = vm.ExportFiles.ToArray();
        Save(Path.Combine(run, "automatic-export.json"), new { vm.ExportContentsSummary, vm.ExportStatus, files = automatic });
        Capture(window, Path.Combine(run, "automatic-export-dark.png"));
        model.IsSelected = false;
        await IdleAsync(vm, token);
        vm.PreviewZipCommand.Execute(null);
        await IdleAsync(vm, token);
        Require(!model.IsSelected && !vm.ExportFiles.Any(x => Same(x.SourcePath, model.Path)), "更新导出预览恢复了已取消的依赖。");
        vm.ReturnToResourcesCommand.Execute(null);
        vm.BeginExportCommand.Execute(null);
        await IdleAsync(vm, token);
        Require(!model.IsSelected, "返回导出页面恢复了已取消的依赖。");
        Save(Path.Combine(run, "manual-deselection.json"), new { respected = true, files = vm.ExportFiles.ToArray() });
        vm.ReAddExportDependenciesCommand.Execute(null);
        await IdleAsync(vm, token);
        Require(model.IsSelected && vm.ExportFiles.Any(x => Same(x.SourcePath, model.Path)), "重新添加依赖未恢复模型。");
        var finalExport = vm.ExportFiles.ToArray();
        var zip = Path.Combine(run, "exported-resources.zip");
        await vm.ExportZipToAsync(zip);
        Require(vm.ExportState == TransferState.Completed && File.Exists(zip), "ZIP导出失败：" + vm.CoreNotice);
        Capture(window, Path.Combine(run, "export-completed-dark.png"));
        using (var archive = ZipFile.OpenRead(zip))
            Require(archive.Entries.Count == finalExport.Length + 1, "ZIP内容与页面计划数量不一致。");
        var targets = new List<object>();
        var targetFields = new[] { "NativeTargetInstanceId", "AdoptedTargetInstanceId" };
        if (targetId is not null && !targetFields.Any(field => Read(field) == targetId)) throw new InvalidDataException("只能验收本范围的安装目标。");
        foreach (var targetField in targetFields.Where(field => targetId is null || Read(field) == targetId))
        {
            token.ThrowIfCancellationRequested();
            var target = vm.DesktopInstances.Single(x => x.Id == Read(targetField));
            scope.ValidateInstance(target);
            vm.SelectedInstance = target;
            await IdleAsync(vm, token);
            await vm.ImportSourceAsync(zip);
            await IdleAsync(vm, token);
            Require(vm.ImportState == TransferState.Ready, "导入自动检查失败：" + vm.ImportDetails);
            var plan = Deployment(vm);
            Require(plan is { BlockingReasons.Count: 0, RequiresPythonDependencies: true } && plan.Capability!.Allows(true), "目标安装计划未具备Python依赖安装条件。");
            Require(vm.ImportResources.All(x => x.IsSelected), "已确认资源未默认勾选。");
            Require(plan.Files.Count == finalExport.Length, "安装文件数与导出计划不一致。");
            var targetRun = Path.Combine(run, target.Id); Directory.CreateDirectory(targetRun);
            Save(Path.Combine(targetRun, "initial-plan.json"), plan);
            Capture(window, Path.Combine(targetRun, "automatic-import-dark.png"));
            var before = await PythonDependencyService.SnapshotAsync(target.PythonPath!, token);
            Require(!before.ContainsKey("humanize"), "目标Python预先包含验收依赖，无法证明安装新增能力。");
            Save(Path.Combine(targetRun, "python-before.json"), before);
            Require(vm.ExecuteDeploymentCommand.CanExecute(null), "页面安装按钮没有启用：" + vm.DeploymentGateNotice);
            vm.ExecuteDeploymentCommand.Execute(null);
            await IdleAsync(vm, token);
            Require(vm.ImportState == TransferState.Completed && string.IsNullOrEmpty(vm.ImportError), "页面安装未完成：" + vm.CoreNotice);
            var hashes = new List<object>();
            foreach (var file in plan.Files)
            {
                RequireScoped(file.TargetPath, scope.FixtureRoot, scope.ExternalDataRoot);
                var actual = await ResourceImportService.HashAsync(file.TargetPath, token);
                Require(actual == file.Sha256, "部署文件哈希不一致：" + file.TargetPath);
                hashes.Add(new { file.SourcePath, file.TargetPath, expected = file.Sha256, actual });
            }
            Save(Path.Combine(targetRun, "file-hashes.json"), hashes);
            var journalPath = Path.Combine(library, "state", "journal", plan.Id + ".json");
            var journal = JsonSerializer.Deserialize<DeploymentJournal>(await File.ReadAllTextAsync(journalPath, token))!;
            Require(journal.State == "FilesDeployed" && journal.Error is null, "安装journal没有成功完成。");
            Save(Path.Combine(targetRun, "deployment-journal.json"), journal);
            var after = await PythonDependencyService.SnapshotAsync(target.PythonPath!, token);
            Require(after.TryGetValue("humanize", out var installed) && installed == "4.16.0", "真实依赖安装版本不符。");
            Require(before.All(x => after.TryGetValue(x.Key, out var value) && value == x.Value), "已有Python包版本发生变化。");
            Save(Path.Combine(targetRun, "python-after.json"), after);
            Capture(window, Path.Combine(targetRun, "install-completed-dark.png"));
            await vm.ImportSourceAsync(zip);
            await IdleAsync(vm, token);
            var repeated = Deployment(vm);
            Require(repeated.RequiredBytes == 0 && repeated.Files.Count == plan.Files.Count && repeated.Files.All(x => x.Reuse), "重复导入没有复用全部相同文件。");
            Save(Path.Combine(targetRun, "reuse-plan.json"), repeated);
            Capture(window, Path.Combine(targetRun, "reuse-import-dark.png"));
            var conflictSource = Path.Combine(targetRun, "version-conflict-source", "custom_nodes", "VersionConflictFixture");
            Directory.CreateDirectory(conflictSource);
            await File.WriteAllTextAsync(Path.Combine(conflictSource, "__init__.py"), "NODE_CLASS_MAPPINGS = {'FlowPackVersionConflict': object}\n", token);
            await File.WriteAllTextAsync(Path.Combine(conflictSource, "requirements.txt"), "humanize==4.15.0\n", token);
            await vm.ImportSourceAsync(Path.Combine(targetRun, "version-conflict-source"));
            await IdleAsync(vm, token);
            Require(vm.ExecuteDeploymentCommand.CanExecute(null), "版本冲突夹具未生成可测试计划。");
            var conflictPlan = Deployment(vm);
            Save(Path.Combine(targetRun, "version-conflict-plan.json"), conflictPlan);
            vm.ExecuteDeploymentCommand.Execute(null);
            await IdleAsync(vm, token);
            Require(vm.ImportState == TransferState.Failed && !vm.ExecuteDeploymentCommand.CanExecute(null), "依赖版本冲突后，页面未保持失败状态及禁用安装。");
            Require(vm.ImportDetails.Contains("ResolutionImpossible", StringComparison.OrdinalIgnoreCase) || vm.ImportDetails.Contains("conflicting", StringComparison.OrdinalIgnoreCase),
                "依赖冲突检查未返回可确认的版本冲突原因：" + vm.ImportDetails);
            Require(!Directory.Exists(Path.Combine(target.CustomNodesDirectory, "VersionConflictFixture")), "依赖版本冲突仍写入了节点文件。");
            var afterConflict = await PythonDependencyService.SnapshotAsync(target.PythonPath!, token);
            Require(afterConflict.Count == after.Count && after.All(x => afterConflict.TryGetValue(x.Key, out var value) && value == x.Value), "冲突检查改变了Python环境。");
            Save(Path.Combine(targetRun, "version-conflict-result.json"), new { rejected = true, existingVersionsPreserved = true,
                vm.ImportState, vm.ImportError, vm.ImportDetails, installationDisabled = !vm.ExecuteDeploymentCommand.CanExecute(null), before = after, after = afterConflict });
            Capture(window, Path.Combine(targetRun, "version-conflict-failed-dark.png"));
            targets.Add(new { instanceId = target.Id, target.DesktopLayout, planId = plan.Id, deployedFiles = plan.Files.Count, reusedFiles = repeated.Files.Count,
                installedPythonDependency = installed, preservedExistingPythonVersions = true, incompatibleVersionRejected = true });
        }
        Save(Path.Combine(run, "worker-jobs.json"), await client.CallAsync<IReadOnlyList<WorkerJob>>("job.list", new { }, token: token));
        Save(Path.Combine(run, "result.json"), new { passed = true, generatedAtUtc = DateTimeOffset.UtcNow,
            protocol = WorkerProtocol.Version, qualification = usesProductionWorker ? "production-worker-shipped-provider" : "test-host-only-not-production", desktopInferenceVerified = false,
            exportZip = zip, zipSha256 = await ResourceImportService.HashAsync(zip, token), manualDeselectionPreserved = true,
            exportFiles = finalExport, targets, assemblies = new[] { typeof(ShellViewModel).Assembly, typeof(PersistentWorkerService).Assembly, typeof(WorkerProtocol).Assembly }
                .Select(x => new { path = x.Location, version = x.GetName().Version?.ToString(), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Location))) }).ToArray() });
    }

    private static ResourceInstallPlan Deployment(ShellViewModel vm) =>
        (ResourceInstallPlan?)typeof(ShellViewModel).GetField("_deployment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)
        ?? throw new InvalidDataException("页面没有当前安装计划：" + vm.CoreNotice);
    private static async Task IdleAsync(ShellViewModel vm, CancellationToken token)
    {
        await Task.Delay(250, token);
        while (!vm.CoreReady) await Task.Delay(100, token);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle, token);
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static void RequireScoped(string path, params string[] roots)
    {
        if (!Path.IsPathFullyQualified(path) || !roots.Any(x => ResourceImportService.Inside(x, path) || Same(x, path)))
            throw new IOException("验收路径越出隔离范围：" + path);
        ResourceInstallationService.EnsureNoLinks(path);
    }
    private sealed class NoUpdates : IUpdateService
    {
        public Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default) => Task.FromResult<UpdateInfo?>(null);
    }
}
