using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;
using Microsoft.Win32;

namespace FlowPack.App;

public sealed class ResourceSelection(LocalResource resource) : INotifyPropertyChanged
{
    public LocalResource Resource { get; } = resource;
    public string Name => Resource.Name;
    public string Path => Resource.SourcePath;
    public string? Category => Resource.Category;
    private bool _selected;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); CommandManager.InvalidateRequerySuggested(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class ImportSelection(ImportResource resource) : INotifyPropertyChanged
{
    public ImportResource Resource { get; private set; } = resource;
    private bool _selected = resource.State == RecognitionState.Confirmed;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public string Name => System.IO.Path.GetFileName(Resource.OriginalPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    public string OriginalPath => Resource.OriginalPath;
    private string _targetRelativePath = resource.TargetRelativePath;
    public string TargetRelativePath
    {
        get => _targetRelativePath;
        set
        {
            if (_targetRelativePath == value) return;
            _targetRelativePath = value;
            Resource = Resource with { State = RecognitionState.NeedsConfirmation, Evidence = "路径已修改，需要再次确认" };
            PropertyChanged?.Invoke(this, new(null));
        }
    }
    public string Evidence => Resource.Evidence;
    public string State => Resource.State switch { RecognitionState.Confirmed => "已识别", RecognitionState.NeedsConfirmation => "需要确认", _ => "未知用途" };
    public void Confirm(InstanceDescriptor instance)
    {
        ResourceInstallationService.ResolveTarget(instance, TargetRelativePath);
        Resource = Resource with { TargetRelativePath = TargetRelativePath, State = RecognitionState.Confirmed, Evidence = "用户确认的资源用途" };
        PropertyChanged?.Invoke(this, new(nameof(State))); PropertyChanged?.Invoke(this, new(nameof(Evidence)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class DependencySelection(AnalyzedDependency dependency) : INotifyPropertyChanged
{
    public AnalyzedDependency Dependency { get; } = dependency;
    public string Name => Dependency.Reference;
    public bool IsStaged { get; private set; } = dependency.Candidates.Any(x => x.IsStaged);
    public string State => IsStaged ? "已暂存，待安装" : Dependency.State switch
    { DependencyState.Present => "本地可复用", DependencyState.Missing => "缺失", DependencyState.Ambiguous => "存在冲突", _ => "需要检查" };
    public void MarkStaged() { IsStaged = true; PropertyChanged?.Invoke(this, new(null)); }
    public string RequiredBy => string.Join("、", Dependency.RequiredBy);
    public string Category { get; set; } = dependency.Category ?? "";
    public string DownloadUrl { get; set; } = "";
    public string ExpectedSha256 { get; set; } = "";
    public string SourceNotice { get; private set; } = dependency.Evidence;
    public ResolvedDependencySource? ResolvedSource { get; private set; }
    public void SetSource(ResolvedDependencySource source)
    {
        ResolvedSource = source;
        DownloadUrl = source.DownloadUrl ?? ""; ExpectedSha256 = source.ExpectedSha256 ?? ""; SourceNotice = source.Description;
        PropertyChanged?.Invoke(this, new(null));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class ShellViewModel
{
    private ResourceInventory? _inventory;
    private ImportPlan? _activeImport;
    private ExportPlan? _zipExport;
    private ResourceInstallPlan? _deployment;
    private string? _dependencySourceContext;
    private bool _coreBusy;
    private bool _initializedCore;
    private bool _settingInstance;
    private ResourceKind? _exportKind;
    private InstanceDescriptor? _selectedInstance;
    private string _coreNotice = "等待扫描 Desktop 实例。";
    private readonly List<WorkflowDocument> _analyzedWorkflows = [];
    public ObservableCollection<InstanceDescriptor> DesktopInstances { get; } = [];
    public ObservableCollection<ResourceSelection> LocalWorkflows { get; } = [];
    public ObservableCollection<ResourceSelection> LocalModels { get; } = [];
    public ObservableCollection<ResourceSelection> LocalNodes { get; } = [];
    public ICollectionView LocalWorkflowsView => CollectionViewSource.GetDefaultView(LocalWorkflows);
    public ICollectionView LocalModelsView => CollectionViewSource.GetDefaultView(LocalModels);
    public ICollectionView LocalNodesView => CollectionViewSource.GetDefaultView(LocalNodes);
    private string _resourceFilter = "";
    public string ResourceFilter
    {
        get => _resourceFilter;
        set
        {
            _resourceFilter = value; OnPropertyChanged();
            foreach (var view in new[] { LocalWorkflowsView, LocalModelsView, LocalNodesView })
                view.Filter = item => item is ResourceSelection row && (row.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                    row.Path.Contains(value, StringComparison.OrdinalIgnoreCase) || (row.Category?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false));
        }
    }
    public ObservableCollection<ImportSelection> ImportResources { get; } = [];
    public ObservableCollection<DependencySelection> DependencyRows { get; } = [];
    public ObservableCollection<WorkerJob> CoreTasks { get; } = [];
    private bool _includeDependencies = true;
    public bool IncludeDependencies
    {
        get => _includeDependencies;
        set
        {
            if (_includeDependencies == value) return;
            _includeDependencies = value; _zipExport = null;
            OnPropertyChanged(); OnPropertyChanged(nameof(ExportSummary)); OnPropertyChanged(nameof(ExportFiles));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public event EventHandler? ExportPreviewRequested;
    public bool AllowPartialExport { get; set; }
    public bool CoreReady => !_coreBusy && !_updating;
    public string DeploymentGateNotice => _deployment?.Capability is not { } capability ? "选择资源后按 Desktop 版本及实例布局检查安装能力。" :
        capability.Allows(_deployment.PythonRequirements.Count > 0) ? "此计划具备安装能力；安装前请停止所选实例。" : string.Join("\n", capability.Reasons);
    public string CoreNotice { get => _coreNotice; private set { _coreNotice = value; OnPropertyChanged(); } }
    public string InstancePaths => SelectedInstance is null ? "请选择目标实例。" :
        $"Desktop：{SelectedInstance.DesktopExecutable ?? "程序位置尚未确认"}\n核心：{SelectedInstance.CoreDirectory}\n工作流：{SelectedInstance.WorkflowsDirectory}\n节点：{SelectedInstance.CustomNodesDirectory}\n模型搜索：{string.Join("；", SelectedInstance.ModelRoots.Concat(SelectedInstance.ExtraPaths.Where(x => x.Category != "custom_nodes").Select(x => x.Path)))}\n模型默认写入：{SelectedInstance.ModelsWriteDirectory}\nPython：{SelectedInstance.PythonPath ?? "未找到"}";
    public string ExportSummary => _zipExport is null ? "选择资源后生成导出预览。" :
        $"将导出 {_zipExport.Files.Count} 个文件，{FormatBytes(_zipExport.TotalBytes)}。\n{string.Join("\n", _zipExport.Issues)}";
    public string DeploymentSummary
    {
        get
        {
            if (_deployment is null) return "选择导入资源并生成安装预览。";
            var files = $"安装文件：新增 {_deployment.Files.Count(x => !x.Reuse)} 个，已有同内容 {_deployment.Files.Count(x => x.Reuse)} 个，需要 {FormatBytes(_deployment.RequiredBytes)}。";
            var dependencies = DependencyRows.Count == 0 ? "" :
                $"\n工作流依赖：本地可复用 {DependencyRows.Count(x => !x.IsStaged && x.Dependency.State == DependencyState.Present)} 项，已暂存待安装 {DependencyRows.Count(x => x.IsStaged)} 项，待解决 {DependencyRows.Count(x => !x.IsStaged && x.Dependency.State != DependencyState.Present)} 项。";
            return files + dependencies + (_deployment.BlockingReasons.Count == 0 ? "" : "\n" + string.Join("\n", _deployment.BlockingReasons));
        }
    }
    public IReadOnlyList<ExportFile> ExportFiles => _zipExport?.Files ?? [];
    public IReadOnlyList<PlannedDeployment> DeploymentFiles => _deployment?.Files ?? [];
    public InstanceDescriptor? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (_selectedInstance?.Id == value?.Id) return;
            _selectedInstance = value; _deployment = null; _zipExport = null; _inventory = null;
            DependencyRows.Clear();
            OnPropertyChanged(); OnPropertyChanged(nameof(InstancePaths)); OnPropertyChanged(nameof(DeploymentSummary)); OnPropertyChanged(nameof(DeploymentGateNotice));
            if (!_settingInstance && value is not null) _ = ExecuteCoreAsync(ScanSelectedInstanceAsync);
        }
    }
    public ICommand RefreshInstancesCommand { get; private set; } = null!;
    public ICommand AssociateInstanceCommand { get; private set; } = null!;
    public ICommand ImportNativeCommand { get; private set; } = null!;
    public ICommand PreviewZipCommand { get; private set; } = null!;
    public ICommand SaveZipCommand { get; private set; } = null!;
    public ICommand PreviewDeploymentCommand { get; private set; } = null!;
    public ICommand ExecuteDeploymentCommand { get; private set; } = null!;
    public ICommand ConfirmMappingCommand { get; private set; } = null!;
    public ICommand ResolveSourcesCommand { get; private set; } = null!;
    public ICommand DownloadMissingCommand { get; private set; } = null!;
    public ICommand ChooseLocalDependencyCommand { get; private set; } = null!;
    public ICommand RefreshCoreTasksCommand { get; private set; } = null!;
    public ICommand PauseCoreTaskCommand { get; private set; } = null!;
    public ICommand ResumeCoreTaskCommand { get; private set; } = null!;
    public ICommand CancelCoreTaskCommand { get; private set; } = null!;
    public ICommand RetryCoreTaskCommand { get; private set; } = null!;

    private void InitializeCoreCommands()
    {
        InitializeOnlineCommands();
        ICommand Command(Func<Task> action, Func<bool>? available = null) => new RelayCommand(_ => _ = ExecuteCoreAsync(action), _ => CoreReady && (available?.Invoke() ?? true));
        RefreshInstancesCommand = Command(RefreshInstancesAsync);
        AssociateInstanceCommand = Command(async () =>
        {
            var dialog = new OpenFolderDialog { Title = "关联 Desktop 登记目录或只读 ComfyUI 资源目录" };
            if (dialog.ShowDialog() != true) return;
            await EnsureCoreLibraryAsync();
            var instance = await _libraryDatabase!.RunAsync<InstanceDescriptor>("instance.associate", dialog.FolderName, JobProgress());
            if (!instance.IsModern)
            {
                var associated = await LoadManualAssociationsAsync();
                Directory.CreateDirectory(Path.GetDirectoryName(ManualAssociationsPath)!);
                var temporary = ManualAssociationsPath + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(associated.Append(instance.InstallRoot).Distinct(StringComparer.OrdinalIgnoreCase)));
                File.Move(temporary, ManualAssociationsPath, true);
            }
            if (!DesktopInstances.Any(x => x.Id == instance.Id)) DesktopInstances.Add(instance);
            _settingInstance = true; try { SelectedInstance = instance; } finally { _settingInstance = false; }
            await ScanSelectedInstanceAsync();
        });
        ImportNativeCommand = Command(async () =>
        {
            var dialog = new OpenFileDialog { Filter = "工作流和资源包|*.zip;*.json;*.cpack|所有文件|*.*" };
            if (dialog.ShowDialog() == true) await ImportNativeAsync(dialog.FileName);
        });
        PreviewZipCommand = new RelayCommand(p => _ = ExecuteCoreAsync(async () =>
        {
            if (p is string name) _exportKind = Enum.TryParse<ResourceKind>(name, out var kind) ? kind : null;
            await PrepareZipAsync();
        }), p => CoreReady && HasExportSelection(p));
        SaveZipCommand = Command(async () =>
        {
            if (_zipExport is null) throw new InvalidDataException("请先生成导出预览。");
            var dialog = new SaveFileDialog { Filter = "ZIP 资源包|*.zip", DefaultExt = ".zip", FileName = "ComfyUI-resources.zip" };
            if (dialog.ShowDialog() != true) return;
            await _libraryDatabase!.RunAsync<string>("export.execute", new ExportJobInput(_zipExport, dialog.FileName, AllowPartialExport), JobProgress());
            CoreNotice = "ZIP 已导出：" + dialog.FileName;
        }, () => _zipExport is { Files.Count: > 0 });
        PreviewDeploymentCommand = Command(PrepareDeploymentAsync);
        ExecuteDeploymentCommand = new RelayCommand(_ => _ = ExecuteCoreAsync(ExecuteDeploymentAsync),
            _ => CoreReady && _deployment is { BlockingReasons.Count: 0, Capability: { } capability } && capability.Allows(_deployment.PythonRequirements.Count > 0));
        ConfirmMappingCommand = new RelayCommand(p => _ = ExecuteCoreAsync(async () =>
        {
            if (p is ImportSelection row && SelectedInstance is not null) row.Confirm(SelectedInstance);
            _deployment = null; OnPropertyChanged(nameof(DeploymentSummary)); await PrepareDeploymentAsync();
        }));
        ResolveSourcesCommand = Command(ResolveSourcesAsync);
        DownloadMissingCommand = new RelayCommand(p => _ = ExecuteCoreAsync(() => DownloadMissingAsync(p as DependencySelection)), _ => CoreReady);
        ChooseLocalDependencyCommand = new RelayCommand(p => _ = ExecuteCoreAsync(() => ChooseLocalDependencyAsync(p as DependencySelection)), _ => CoreReady);
        RefreshCoreTasksCommand = Command(RefreshCoreTasksAsync);
        PauseCoreTaskCommand = ControlTask("task.pause"); ResumeCoreTaskCommand = ControlTask("task.resume");
        CancelCoreTaskCommand = ControlTask("task.cancel"); RetryCoreTaskCommand = ControlTask("task.retry");
        ICommand ControlTask(string action) => new RelayCommand(p => _ = ExecuteTaskControlAsync(action, p as WorkerJob),
            p => p is WorkerJob job && job.CanControl(action));
        ImportResources.CollectionChanged += (_, args) =>
        {
            if (args.OldItems is not null) foreach (ImportSelection row in args.OldItems) row.PropertyChanged -= ImportSelectionChanged;
            if (args.NewItems is not null) foreach (ImportSelection row in args.NewItems) row.PropertyChanged += ImportSelectionChanged;
            InvalidateDeployment();
        };
    }

    private void InvalidateDeployment()
    {
        _deploymentInputRevision++;
        _deployment = null;
        OnPropertyChanged(nameof(DeploymentGateNotice));
        OnPropertyChanged(nameof(DeploymentSummary)); OnPropertyChanged(nameof(DeploymentFiles));
        CommandManager.InvalidateRequerySuggested();
    }
    private long _deploymentInputRevision;
    private void ImportSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        InvalidateDeployment();
        if (args.PropertyName is null or nameof(ImportSelection.IsSelected) or nameof(ImportSelection.TargetRelativePath))
        {
            _importChoicesDirty = true;
            if (!_coreBusy) _ = ExecuteCoreAsync(async () =>
            {
                await SaveImportSessionAsync();
                if (SelectedInstance is not null && args.PropertyName == nameof(ImportSelection.IsSelected)) await PrepareDeploymentAsync();
            });
        }
    }
    public Task ImportSourceAsync(string path) => ExecuteCoreAsync(() => ImportNativeAsync(path));

    public async Task InitializeWorkspaceAsync()
    {
        if (_initializedCore) return; _initializedCore = true;
        await ExecuteCoreAsync(async () => { await EnsureCoreLibraryAsync(); await RefreshInstancesAsync(); await RefreshCoreTasksAsync(); await RestoreImportSessionAsync(); });
        if (!_taskMonitorLifetime.IsCancellationRequested) _ = MonitorCoreTasksAsync();
        await CheckForUpdatesAutomaticallyAsync();
    }
    private async Task EnsureCoreLibraryAsync()
    {
        if (_libraryDatabase is not null)
        {
            await _libraryDatabase.InitializeAsync();
            return;
        }
        var binding = await _libraryBindingStore.LoadAsync();
        if (binding is not null && !Directory.Exists(binding.LibraryPath)) throw new IOException("原资源库不可用，请在设置中重新关联；不会建立空库替代。");
        var path = binding?.LibraryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyUI FlowPack", "Data");
        var database = _libraryClientFactory(path); await database.InitializeAsync();
        _libraryDatabase = database; ResourceLibraryLocation = path;
        if (binding is null) await _libraryBindingStore.SaveAsync(new(path, DateTimeOffset.UtcNow));
    }
    private async Task RefreshInstancesAsync()
    {
        await EnsureCoreLibraryAsync();
        var instances = (await _libraryDatabase!.RunAsync<IReadOnlyList<InstanceDescriptor>>("instance.discover", new { }, JobProgress())).ToList();
        foreach (var path in await LoadManualAssociationsAsync())
        {
            if (!Directory.Exists(path) || instances.Any(x => string.Equals(x.InstallRoot, path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var associated = await _libraryDatabase.RunAsync<InstanceDescriptor>("instance.associate", path, JobProgress());
                if (instances.All(x => x.Id != associated.Id)) instances.Add(associated);
            }
            catch (InvalidOperationException) { /* Keep the association for a later rescan; never grant stale write permission. */ }
        }
        var saved = await LoadWorkspacePreferenceAsync();
        var selectedId = SelectedInstance?.Id ?? saved;
        _settingInstance = true;
        try
        {
            DesktopInstances.Clear(); foreach (var instance in instances) DesktopInstances.Add(instance);
            _selectedInstance = null;
            SelectedInstance = instances.FirstOrDefault(x => x.Id == selectedId) ?? (instances.Count(x => x.IsModern) == 1 ? instances.Single(x => x.IsModern) : instances.Count == 1 ? instances[0] : null);
        }
        finally { _settingInstance = false; }
        if (SelectedInstance is not null) await ScanSelectedInstanceAsync();
        else CoreNotice = instances.Count == 0 ? "未发现 Desktop 实例，请安装或在 Desktop 中登记实例后重新扫描。" : "发现多个实例，请选择目标。";
    }
    private async Task ScanSelectedInstanceAsync()
    {
        if (SelectedInstance is not { } instance) return;
        await EnsureCoreLibraryAsync();
        var inventory = await _libraryDatabase!.RunAsync<ResourceInventory>("inventory.scan", instance, JobProgress());
        if (!ReferenceEquals(instance, SelectedInstance)) { await ScanSelectedInstanceAsync(); return; }
        _inventory = inventory;
        _detectedDesktop = instance.ToLegacyLocation();
        ComfyUiLocation = instance.DataDirectory; StatusNotice = "●  已检测 " + instance.Name;
        var previouslySelected = LocalWorkflows.Concat(LocalModels).Concat(LocalNodes).Where(x => x.IsSelected).Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var collection in new[] { LocalWorkflows, LocalModels, LocalNodes }) collection.Clear();
        foreach (var resource in _inventory.Resources)
        {
            if (resource.Kind == ResourceKind.Asset) continue;
            var collection = resource.Kind switch { ResourceKind.Workflow => LocalWorkflows, ResourceKind.Model => LocalModels, _ => LocalNodes };
            collection.Add(new(resource) { IsSelected = previouslySelected.Contains(resource.SourcePath) });
        }
        foreach (var stored in await _libraryDatabase.LoadWorkflowsAsync())
            if (File.Exists(stored.Source) && !LocalWorkflows.Any(x => x.Path == stored.Source))
                LocalWorkflows.Add(new(new(stored.Workflow.Id, ResourceKind.Workflow, stored.Workflow.DisplayName, stored.Source, "workflows/" + stored.Workflow.DisplayName + ".json")));
        var latestDraft = (await _libraryDatabase.LoadDraftsAsync()).FirstOrDefault();
        if (latestDraft is not null)
        {
            _packageDraft = latestDraft.Draft;
            foreach (var row in LocalWorkflows.Where(x => _packageDraft.WorkflowIds.Contains(x.Resource.Id))) row.IsSelected = true;
        }
        if (!ReferenceEquals(instance, SelectedInstance)) { await ScanSelectedInstanceAsync(); return; }
        await SaveWorkspacePreferenceAsync(instance.Id);
        await RefreshImportedDependenciesForInstanceAsync();
        if (!ReferenceEquals(instance, SelectedInstance)) { await ScanSelectedInstanceAsync(); return; }
        CoreNotice = $"已扫描 {LocalWorkflows.Count} 个工作流、{LocalModels.Count} 个模型、{LocalNodes.Count} 个节点包。" +
            (inventory.Issues.Count > 0 ? "\n" + string.Join("\n", inventory.Issues.Take(8)) : "");
        OnPropertyChanged(nameof(DetectedDesktop)); OnPropertyChanged(nameof(DesktopSummary)); OnPropertyChanged(nameof(InstancePaths));
    }
    private async Task ImportNativeAsync(string path)
    {
        await EnsureCoreLibraryAsync();
        _activeImport = await _libraryDatabase!.RunAsync<ImportPlan>("resource.import", new ImportJobInput(path), JobProgress());
        RefreshOnlineRows(reset: true);
        ImportResources.Clear(); foreach (var item in _activeImport.Resources) ImportResources.Add(new(item));
        await SaveImportSessionAsync();
        _deployment = null; CurrentPage = FlowPage.Install;
        // Reuse is independent of whether the manifest provides a downloadable URL.
        foreach (var row in OnlineResources.ToArray()) await TryReuseOnlineLocalAsync(row);
        _analyzedWorkflows.Clear(); _analyzedWorkflows.AddRange(_activeImport.Workflows);
        await PopulateDependenciesAsync();
        CoreNotice = $"已识别 {_activeImport.Resources.Count} 项，{_activeImport.Resources.Count(x => x.State != RecognitionState.Confirmed)} 项需要确认。";
        OnPropertyChanged(nameof(DeploymentSummary));
        if (SelectedInstance is not null && ImportResources.Any(x => x.IsSelected)) await PrepareDeploymentAsync();
        // Planning rebuilds dependency rows. Resolve sources on the final rows shown to the user.
        if (DependencyRows.Any(x => x.Dependency.State == DependencyState.Missing)) await ResolveSourcesAsync();
    }
    private async Task<DependencyAnalysis> PopulateDependenciesAsync(bool includeImported = true)
    {
        if (_inventory is null || _analyzedWorkflows.Count == 0)
        {
            DependencyRows.Clear(); _dependencySourceContext = null;
            return new([], []);
        }
        var context = JsonSerializer.Serialize(new
        {
            ImportId = _activeImport?.Id, _inventory.Instance.Id, _inventory.Instance.ConfigurationFingerprint,
            includeImported, Workflows = _analyzedWorkflows.Select(x => new { x.Id, x.RawJson }).OrderBy(x => x.Id).ToArray()
        });
        var previous = context == _dependencySourceContext ? DependencyRows.ToArray() : [];
        var analysis = await _libraryDatabase!.RunAsync<DependencyAnalysis>("dependency.analyze", new DependencyAnalysisInput(_analyzedWorkflows, _inventory,
            includeImported ? ImportResources.Where(x => x.IsSelected).Select(x => x.Resource).ToArray() : []), JobProgress());
        DependencyRows.Clear(); _dependencySourceContext = context;
        foreach (var item in analysis.Dependencies)
        {
            var row = new DependencySelection(item);
            var prior = previous.SingleOrDefault(x => x.Dependency.Id == item.Id && x.Dependency.Kind == item.Kind &&
                x.Dependency.Reference == item.Reference && x.Dependency.Category == item.Category &&
                x.Dependency.RequiredPackageIdentity == item.RequiredPackageIdentity && x.Dependency.RequiredVersion == item.RequiredVersion &&
                x.Dependency.RequiredSha256 == item.RequiredSha256);
            if (prior is not null && item.State != DependencyState.Ambiguous)
            {
                if (prior.ResolvedSource is { } source && source.DownloadUrl == prior.DownloadUrl)
                    row.SetSource(source);
                row.Category = prior.Category;
                row.DownloadUrl = prior.DownloadUrl;
                row.ExpectedSha256 = prior.ExpectedSha256;
            }
            DependencyRows.Add(row);
        }
        return analysis;
    }
    private async Task PrepareZipAsync()
    {
        if (!HasExportSelection(null)) throw new InvalidDataException("请先勾选要导出的资源。");
        await EnsureCoreLibraryAsync();
        if (_inventory is null) throw new InvalidDataException("请先选择并扫描 Desktop 实例。");
        var selection = LocalWorkflows.Concat(LocalModels).Concat(LocalNodes).Where(x => x.IsSelected && (_exportKind is null || x.Resource.Kind == _exportKind)).Select(x => x.Resource).ToList();
        _analyzedWorkflows.Clear();
        foreach (var workflow in selection.Where(x => x.Kind == ResourceKind.Workflow))
            _analyzedWorkflows.Add(await new WorkflowReader().ReadAsync(workflow.SourcePath));
        var analysis = await PopulateDependenciesAsync(includeImported: false);
        var issues = new List<string>();
        if (IncludeDependencies && _analyzedWorkflows.Count > 0)
        {
            selection.AddRange(analysis.Dependencies.Where(x => x.State == DependencyState.Present ||
                x.Kind == ResourceKind.CustomNode && x.State == DependencyState.Unresolved && x.Candidates.Count == 1).SelectMany(x => x.Candidates));
            issues.AddRange(analysis.Issues);
            issues.AddRange(analysis.Dependencies.Where(x => x.State != DependencyState.Present).Select(x => x.Reference + "：" + x.State));
        }
        _zipExport = await _libraryDatabase!.RunAsync<ExportPlan>("export.plan", new ExportPlanningInput(selection, issues), JobProgress());
        var workflowIds = selection.Where(x => x.Kind == ResourceKind.Workflow).Select(x => x.Id).Distinct().ToArray();
        _packageDraft = _packageDraft with { WorkflowId = workflowIds.FirstOrDefault(), WorkflowSelectionJson = JsonSerializer.Serialize(workflowIds) };
        await _libraryDatabase.SaveDraftAsync(_packageDraft);
        CoreNotice = "导出预览已生成。";
        OnPropertyChanged(nameof(ExportSummary)); OnPropertyChanged(nameof(ExportFiles));
        ExportPreviewRequested?.Invoke(this, EventArgs.Empty);
    }
    private bool HasExportSelection(object? parameter)
    {
        var kind = parameter is string name ? Enum.TryParse<ResourceKind>(name, out var parsed) ? parsed : (ResourceKind?)null : _exportKind;
        return LocalWorkflows.Concat(LocalModels).Concat(LocalNodes).Any(x => x.IsSelected && (kind is null || x.Resource.Kind == kind));
    }
    private async Task ExecuteDeploymentAsync()
    {
        var plan = _deployment ?? throw new InvalidDataException("请先生成安装预览。");
        var instance = SelectedInstance;
        var importId = _activeImport?.Id;
        await _libraryDatabase!.RunAsync<string>("install.execute", plan, JobProgress());
        // A finished install must not leave its old writable preview or staged dependency labels active.
        if (!ReferenceEquals(instance, SelectedInstance) || importId != _activeImport?.Id) return;
        InvalidateDeployment();
        try
        {
            await ScanSelectedInstanceAsync();
            CoreNotice = "文件已部署，已重新检查本地资源；节点加载状态以目标 Desktop 实际运行结果为准。";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OperationCanceledException)
        {
            InvalidateDeployment();
            DependencyRows.Clear();
            CoreNotice = "文件已部署，但重新检查失败，请重新扫描实例：" + ex.Message;
        }
    }

    private async Task PrepareDeploymentAsync()
    {
        if (SelectedInstance is null) throw new InvalidDataException("请选择目标实例。");
        await EnsureCoreLibraryAsync();
        var resources = ImportResources.Where(x => x.IsSelected).Select(x => x.Resource with { TargetRelativePath = x.TargetRelativePath }).ToArray();
        var instance = SelectedInstance;
        var revision = _deploymentInputRevision;
        var importId = _activeImport?.Id;
        bool IsCurrent() => revision == _deploymentInputRevision && ReferenceEquals(instance, SelectedInstance) && importId == _activeImport?.Id;
        var selectedWorkflows = _activeImport?.Workflows.Where(w => resources.Any(r => r.Kind == ResourceKind.Workflow && r.Sha256.Equals(w.Id, StringComparison.OrdinalIgnoreCase))).ToArray();
        _analyzedWorkflows.Clear();
        if (selectedWorkflows is not null) _analyzedWorkflows.AddRange(selectedWorkflows);
        await PopulateDependenciesAsync();
        if (!IsCurrent()) { DependencyRows.Clear(); return; }
        var planned = await _libraryDatabase!.RunAsync<ResourceInstallPlan>("install.plan", new InstallPlanningInput(instance!, resources, selectedWorkflows), JobProgress());
        if (!IsCurrent()) { DependencyRows.Clear(); return; }
        _deployment = planned;
        OnPropertyChanged(nameof(DeploymentGateNotice));
        OnPropertyChanged(nameof(DeploymentSummary)); OnPropertyChanged(nameof(DeploymentFiles)); CoreNotice = DeploymentSummary;
    }
    private async Task ResolveSourcesAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var resolver = new DependencySourceResolver(http);
        foreach (var row in DependencyRows.Where(x => x.Dependency.State == DependencyState.Missing))
        {
            try
            {
                row.SetSource(row.Dependency.RequiredPackageIdentity is { } id ? await resolver.ResolveRegistryAsync(id, row.Dependency.RequiredVersion) :
                    row.Dependency.RequiredVersion is not null ? new(null, null, null, "请提供工作流要求的固定版本节点归档，不自动改用最新版本。") :
                    row.Dependency.Kind == ResourceKind.Model ? DependencySourceResolver.ResolveModel(row.Name, _analyzedWorkflows.Select(x => x.RawJson)) : await resolver.ResolveNodeAsync(row.Name));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
            { row.SetSource(new(null, null, null, "来源查询失败，可手动填写直链或选择本地文件。")); }
        }
        CoreNotice = "缺失依赖来源已检查；只下载有明确地址的项目。";
    }
    private async Task DownloadMissingAsync(DependencySelection? selected)
    {
        await EnsureCoreLibraryAsync();
        var rows = (selected is null ? DependencyRows.ToArray() : [selected]).Where(x => !x.IsStaged && x.Dependency.State != DependencyState.Present && !string.IsNullOrWhiteSpace(x.DownloadUrl)).ToArray();
        if (rows.Length == 0) throw new InvalidDataException("没有已确认来源的缺失依赖。");
        var downloads = new Dictionary<string, DownloadResult>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Dependency.Kind == ResourceKind.Model && string.IsNullOrWhiteSpace(row.Category)) throw new InvalidDataException("请先填写模型类别：" + row.Name);
            var url = row.DownloadUrl;
            if (row.Dependency.Kind == ResourceKind.CustomNode && Uri.TryCreate(url, UriKind.Absolute, out var nodeUri) && nodeUri.Host == "github.com")
            { using var http = new HttpClient(); url = (await new DependencySourceResolver(http).PinGitHubAsync(url)).DownloadUrl ?? throw new InvalidDataException("无法固定节点来源版本。"); }
            var fileName = row.Dependency.Kind == ResourceKind.CustomNode ? "node-" + Guid.NewGuid().ToString("N") + ".zip" : Path.GetFileName(row.Name.Replace('\\', '/'));
            var downloadKey = url + "|" + row.ExpectedSha256;
            if (!downloads.TryGetValue(downloadKey, out var downloaded))
            {
                downloaded = await _libraryDatabase!.RunAsync<DownloadResult>("task.download", new DownloadTaskPayload(url, string.IsNullOrWhiteSpace(row.ExpectedSha256) ? null : row.ExpectedSha256, fileName), JobProgress());
                downloads.Add(downloadKey, downloaded);
            }
            await AppendDependencyAsync(row, downloaded.StagingPath);
        }
        await PrepareDeploymentAsync(); CurrentPage = FlowPage.Install;
        CoreNotice = "缺失资源已下载并加入安装预览，已有资源不会重复下载。";
    }
    private async Task ChooseLocalDependencyAsync(DependencySelection? row)
    {
        if (row is null) return;
        var dialog = new OpenFileDialog { Filter = row.Dependency.Kind == ResourceKind.CustomNode ? "节点 ZIP|*.zip" : "模型文件|*.safetensors;*.ckpt;*.pt;*.pth;*.bin;*.gguf;*.onnx;*.sft|所有文件|*.*" };
        if (dialog.ShowDialog() == true) { await EnsureCoreLibraryAsync(); await AppendDependencyAsync(row, dialog.FileName); CoreNotice = "本地文件已加入安装预览。"; }
    }
    private async Task AppendDependencyAsync(DependencySelection row, string path)
    {
        if (row.Dependency.State == DependencyState.Present || row.IsStaged) return;
        var imported = await _libraryDatabase!.RunAsync<ImportPlan>("resource.import", new ImportJobInput(path), JobProgress());
        IReadOnlyList<ImportResource> selectedResources = imported.Resources;
        if (row.Dependency.Kind == ResourceKind.Model)
            selectedResources = DependencyImportSelection.Model(imported, row.Category, row.Name, row.Dependency.RequiredSha256);
        if (row.Dependency.Kind == ResourceKind.CustomNode)
        {
            if (SelectedInstance is null) throw new InvalidDataException("请选择目标实例。");
            var nodes = ImportInventoryService.Merge(new(SelectedInstance, [], [], []), imported.Resources);
            var matches = nodes.Resources.Where(x => x.Kind == ResourceKind.CustomNode && x.NodeTypes?.Contains(row.Name) == true).Select(x => x.SourcePath).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("所选节点包未声明唯一的所需节点类型，不能作为此依赖自动安装。");
            var identity = ResourceInventoryService.ReadPackageIdentity(matches[0]);
            if ((row.Dependency.RequiredPackageIdentity is not null && identity.Name != row.Dependency.RequiredPackageIdentity) ||
                (row.Dependency.RequiredVersion is not null && identity.Version != row.Dependency.RequiredVersion))
                throw new InvalidDataException("节点归档的包身份或版本与工作流要求不符。");
            selectedResources = imported.Resources.Where(x => x.Kind == ResourceKind.CustomNode &&
                (Path.GetFullPath(x.SourcePath).Equals(Path.GetFullPath(matches[0]), StringComparison.OrdinalIgnoreCase) || ResourceImportService.Inside(matches[0], x.SourcePath))).ToArray();
        }
        foreach (var resource in selectedResources)
        {
            if (row.Dependency.Kind == ResourceKind.CustomNode && resource.Kind != ResourceKind.CustomNode) continue;
            var item = resource;
            if (row.Dependency.Kind == ResourceKind.Model)
            {
                item = resource;
            }
            else if (row.Dependency.Kind == ResourceKind.CustomNode && row.Dependency.RequiredPackageIdentity is { } identity)
                item = resource with { TargetRelativePath = "custom_nodes/" + identity + "/" + string.Join('/', resource.TargetRelativePath.Split('/').Skip(2)),
                    DeclaredPythonDependencies = row.ResolvedSource?.DownloadUrl == row.DownloadUrl ? row.ResolvedSource.PythonDependencies : [] };
            if (!ImportResources.Any(x => x.Resource.Sha256 == item.Sha256 && x.TargetRelativePath == item.TargetRelativePath)) ImportResources.Add(new(item));
        }
        row.MarkStaged();
    }
    private async Task RefreshCoreTasksAsync()
    {
        await EnsureCoreLibraryAsync(); var tasks = await _libraryDatabase!.CallAsync<IReadOnlyList<WorkerJob>>("job.list", new { });
        TaskSnapshotReconciler.Apply(CoreTasks, tasks); _taskSnapshotGeneration++;
        CommandManager.InvalidateRequerySuggested();
        await RestoreTasksAsync();
    }
    private async Task ExecuteTaskControlAsync(string operation, WorkerJob? job)
    {
        if (job is null || _libraryDatabase is null) return;
        try { await _libraryDatabase.CallAsync<WorkerJob>(operation, job.Id); await RefreshCoreTasksAsync(); }
        catch (Exception ex) { CoreNotice = ex.Message; }
    }
    private IProgress<WorkerJob> JobProgress() => new Progress<WorkerJob>(job =>
    {
        _taskSnapshotGeneration++;
        CoreNotice = job.Operation + "：" + job.Stage;
        var existing = CoreTasks.FirstOrDefault(x => x.Id == job.Id);
        if (existing is null) CoreTasks.Insert(0, job);
        else CoreTasks[CoreTasks.IndexOf(existing)] = job;
        CommandManager.InvalidateRequerySuggested();
    });
    private readonly CancellationTokenSource _taskMonitorLifetime = new();
    private long _taskSnapshotGeneration;
    private async Task MonitorCoreTasksAsync()
    {
        var token = _taskMonitorLifetime.Token;
        string? previousError = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                var client = _libraryDatabase;
                if (client is null || _updating) continue;
                var generation = _taskSnapshotGeneration;
                try
                {
                    var tasks = await client.CallAsync<IReadOnlyList<WorkerJob>>("job.list", new { }, token: token);
                    if (token.IsCancellationRequested) break;
                    // A direct progress/control response received meanwhile is newer than this snapshot.
                    if (generation != _taskSnapshotGeneration || !ReferenceEquals(client, _libraryDatabase)) continue;
                    TaskSnapshotReconciler.Apply(CoreTasks, tasks); _taskSnapshotGeneration++;
                    CommandManager.InvalidateRequerySuggested();
                    if (previousError is not null && CoreNotice == previousError) CoreNotice = "后台任务连接已恢复。";
                    previousError = null;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    var message = "后台任务状态暂时无法刷新，已保留现有记录：" + ex.Message;
                    if (message != previousError) CoreNotice = message;
                    previousError = message;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task ExecuteCoreAsync(Func<Task> action)
    {
        if (_coreBusy) return;
        _coreBusy = true; OnPropertyChanged(nameof(CoreReady)); CommandManager.InvalidateRequerySuggested();
        try
        {
            await action();
            while (_importChoicesDirty && _activeImport is not null && _libraryDatabase is not null)
            {
                await SaveImportSessionAsync();
                if (SelectedInstance is not null) await PrepareDeploymentAsync();
            }
        }
        catch (Exception ex) { CoreNotice = ex.Message; }
        finally { _coreBusy = false; OnPropertyChanged(nameof(CoreReady)); CommandManager.InvalidateRequerySuggested(); }
    }
    private string WorkspacePreferencePath => Path.Combine(Path.GetDirectoryName(_themeStore.FilePath)!, "workspace-selection.json");
    private string ManualAssociationsPath => Path.Combine(Path.GetDirectoryName(_themeStore.FilePath)!, "manual-associations.json");
    private async Task<IReadOnlyList<string>> LoadManualAssociationsAsync()
    {
        if (!File.Exists(ManualAssociationsPath)) return [];
        try { return JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(ManualAssociationsPath)) ?? []; }
        catch (JsonException) { return []; }
    }
    private async Task<string?> LoadWorkspacePreferenceAsync()
    {
        if (!File.Exists(WorkspacePreferencePath)) return null;
        try { return JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(WorkspacePreferencePath)); }
        catch (JsonException) { return null; }
    }
    private async Task SaveWorkspacePreferenceAsync(string id)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(WorkspacePreferencePath)!);
        var temporary = WorkspacePreferencePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(id)); File.Move(temporary, WorkspacePreferencePath, true);
    }
}
