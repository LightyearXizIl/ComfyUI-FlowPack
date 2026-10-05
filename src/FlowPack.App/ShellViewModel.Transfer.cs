using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.App;

public enum TransferState { Empty, Checking, Ready, NeedsAttention, Executing, Completed, Failed }

public sealed partial class ShellViewModel
{
    private long _exportInputRevision;
    private bool _exportRefreshPending, _changingExportSelection, _pendingInstanceScan;
    private readonly HashSet<string> _analyzedExportWorkflows = new(StringComparer.OrdinalIgnoreCase);
    private TransferState _exportState, _importState;
    private string _exportNotice = "选择资源后，软件会自动整理导出清单。";
    private string? _importError;
    public ObservableCollection<ResourceSelection> ExportSelectionRows { get; } = [];
    public IReadOnlyList<ResourceSelection> SelectedExportResources => LocalWorkflows.Concat(LocalModels).Concat(LocalNodes).Concat(LocalExportAssets).Where(x => x.IsSelected).ToArray();
    public string ExportSelectionSummary => $"已选 {SelectedExportResources.Count} 项";
    public string ExportContentsSummary => $"工作流 {SelectedExportResources.Count(x => x.Resource.Kind == ResourceKind.Workflow)} · 模型 {SelectedExportResources.Count(x => x.Resource.Kind == ResourceKind.Model)} · 节点包 {SelectedExportResources.Count(x => x.Resource.Kind == ResourceKind.CustomNode)} · 输入素材 {SelectedExportResources.Count(x => x.Resource.Kind == ResourceKind.Asset)}\n" +
        (_zipExport is null ? "调整选择后自动检查大小。" : $"共 {_zipExport.Files.Count} 个文件 · {FormatBytes(_zipExport.TotalBytes)}");
    public string ExportStatus => _exportNotice;
    public TransferState ExportState => _exportState;
    public TransferState ImportState => _importState;
    public string ImportError => _importError is null ? "" : "操作未完成，请展开详情查看原因。";
    public bool HasSourceImportError => _importError is not null && !HasImportSource;
    public bool HasImportProblems => UnresolvedDependencies.Any() || HasOnlineResources || HasUnconfirmedImports || (_deployment?.BlockingReasons.Count > 0);
    public bool HasUnconfirmedImports => ImportResources.Any(x => x.Resource.State != RecognitionState.Confirmed);
    public IEnumerable<DependencySelection> UnresolvedDependencies => DependencyRows.Where(x => !x.IsStaged && x.Dependency.State != DependencyState.Present);
    public bool HasUnresolvedDependencies => UnresolvedDependencies.Any();
    public string ImportContentSummary => $"工作流 {ImportResources.Count(x => x.Resource.Kind == ResourceKind.Workflow)} · 模型 {ImportResources.Count(x => x.Resource.Kind == ResourceKind.Model)} · 节点包 {ImportResources.Where(x => x.Resource.Kind == ResourceKind.CustomNode).Select(x => x.TargetRelativePath.Replace('\\', '/').Split('/').ElementAtOrDefault(1)).Distinct().Count()} · {FormatBytes(ImportResources.Sum(x => x.Resource.SizeBytes))}";
    public string ImportCountsSummary => _deployment is null ? "检查完成后显示安装内容" : $"新增 {_deployment.Files.Count(x => !x.Reuse)} 个文件\n复用 {_deployment.Files.Count(x => x.Reuse)} 个文件\n所需空间 {FormatBytes(_deployment.RequiredBytes)}";
    public string ImportStatus => _importState == TransferState.Completed ? (_importError is null ? "安装完成，已重新检查资源。" : "安装完成，复查失败，请重新检查。") :
        _importError is not null ? "操作未完成，请查看详情。" :
        _importState == TransferState.Executing ? "正在安装…" : _importState == TransferState.Checking ? "正在自动检查资源与安装条件…" :
        !HasImportSource ? "选择文件后自动分析。" : SelectedInstance is null ? "请选择安装到的 ComfyUI。" :
        HasImportProblems ? "有需要处理的项目，请查看左侧。" : _deployment?.Capability is { } c && !c.Allows(_deployment.RequiresPythonDependencies == true) ? "当前版本或布局尚不支持此安装。" :
        _deployment is null ? "请勾选要安装的资源。" : "资源已准备好，可以安装。";
    public string ImportProblemNotice => _deployment is null ? "核对下面标出的资源用途或缺失依赖。" : string.Join("\n", _deployment.BlockingReasons.Distinct());
    public string ImportDetails => string.Join("\n", new[] { _importError, DeploymentGateNotice, ImportProblemNotice }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public ICommand BeginExportCommand { get; private set; } = null!;
    public ICommand ClearExportSelectionCommand { get; private set; } = null!;
    public ICommand ReAddExportDependenciesCommand { get; private set; } = null!;
    public ICommand ReturnToResourcesCommand { get; private set; } = null!;

    private void InitializeTransferCommands()
    {
        BeginExportCommand = new RelayCommand(_ =>
        {
            if (SelectedExportResources.Count == 0) { CurrentPage = FlowPage.Library; LibraryTabIndex = 0; return; }
            CurrentPage = FlowPage.Packaging;
            _exportKind = null;
            _exportWorkflows = _exportModels = _exportNodes = true;
            SyncExportRows();
            if (_inventory is not null) _ = ExecuteCoreAsync(async () => { await AddExportDependenciesAsync(false); await PrepareZipAsync(); });
            else SetExportState(TransferState.NeedsAttention, "请先在首页扫描当前实例。");
        }, p => CoreReady && (!Equals(p, "Selected") || SelectedExportResources.Count > 0));
        ClearExportSelectionCommand = new RelayCommand(_ =>
        {
            _changingExportSelection = true;
            try { foreach (var row in SelectedExportResources) row.IsSelected = false; }
            finally { _changingExportSelection = false; }
            ExportSelectionRows.Clear(); _analyzedExportWorkflows.Clear(); InvalidateExport();
            SetExportState(TransferState.Empty, "请在资源库选择要导出的资源。");
        }, _ => CoreReady && SelectedExportResources.Count > 0);
        ReAddExportDependenciesCommand = new RelayCommand(_ => _ = ExecuteCoreAsync(async () => { await AddExportDependenciesAsync(true); await PrepareZipAsync(); }),
            _ => CoreReady && LocalWorkflows.Any(x => x.IsSelected));
        ReturnToResourcesCommand = new RelayCommand(_ => CurrentPage = FlowPage.Library);
        foreach (var rows in new[] { LocalWorkflows, LocalModels, LocalNodes, LocalExportAssets }) rows.CollectionChanged += ExportResourcesChanged;
        DependencyRows.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (DependencySelection row in e.OldItems) row.PropertyChanged -= ImportDependencyChanged;
            if (e.NewItems is not null) foreach (DependencySelection row in e.NewItems) row.PropertyChanged += ImportDependencyChanged;
            NotifyTransferState();
        };
        OnlineResources.CollectionChanged += (_, _) => NotifyTransferState();
    }
    private void ImportDependencyChanged(object? sender, PropertyChangedEventArgs e) => NotifyTransferState();
    private void ExportResourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (ResourceSelection row in e.OldItems) row.PropertyChanged -= ResourceExportChoiceChanged;
        if (e.NewItems is not null) foreach (ResourceSelection row in e.NewItems) { row.PropertyChanged -= ResourceExportChoiceChanged; row.PropertyChanged += ResourceExportChoiceChanged; }
        InvalidateExport();
    }
    private void SyncExportRows()
    {
        foreach (var row in SelectedExportResources)
            if (!ExportSelectionRows.Contains(row)) ExportSelectionRows.Add(row);
        var local = LocalWorkflows.Concat(LocalModels).Concat(LocalNodes).Concat(LocalExportAssets).ToHashSet();
        foreach (var row in ExportSelectionRows.Where(x => !local.Contains(x)).ToArray()) ExportSelectionRows.Remove(row);
        NotifyTransferState();
    }
    private async Task AddExportDependenciesAsync(bool force)
    {
        if (_inventory is not { } inventory) return;
        var workflows = LocalWorkflows.Where(x => x.IsSelected && (force || !_analyzedExportWorkflows.Contains(x.Path))).ToArray();
        if (workflows.Length == 0) return;
        var revision = _exportInputRevision;
        SetExportState(TransferState.Checking, "正在整理工作流依赖…");
        await EnsureCoreLibraryAsync();
        var docs = await Task.Run(async () =>
        {
            var result = new List<WorkflowDocument>();
            foreach (var row in workflows) result.Add(await new WorkflowReader().ReadAsync(row.Path));
            return result;
        });
        var analysis = await _libraryDatabase!.RunAsync<DependencyAnalysis>("dependency.analyze", new DependencyAnalysisInput(docs, inventory, []), JobProgress());
        if (revision != _exportInputRevision || !ReferenceEquals(inventory, _inventory)) return;
        _changingExportSelection = true;
        try
        {
            foreach (var d in analysis.Dependencies.Where(x => x.Candidates.Count == 1 && (x.State == DependencyState.Present || x.Kind == ResourceKind.CustomNode && x.State == DependencyState.Unresolved)))
            {
                var row = LocalModels.Concat(LocalNodes).Concat(LocalExportAssets).FirstOrDefault(x => string.Equals(x.Path, d.Candidates[0].SourcePath, StringComparison.OrdinalIgnoreCase));
                if (row is not null) row.IsSelected = true;
            }
            foreach (var row in workflows) _analyzedExportWorkflows.Add(row.Path);
        }
        finally { _changingExportSelection = false; }
        PopulateLibraryDependencyGroups(analysis.Dependencies);
        SyncExportRows();
    }
    private void SetExportState(TransferState state, string notice)
    {
        _exportState = state; _exportNotice = notice; NotifyTransferState();
    }
    private void SetImportState(TransferState state, string? error = null)
    {
        _importState = state; _importError = error; NotifyTransferState();
    }
    private void NotifyTransferState()
    {
        foreach (var name in new[] { nameof(ExportSelectionSummary), nameof(ExportContentsSummary), nameof(ExportStatus), nameof(ExportState), nameof(ImportState), nameof(ImportError), nameof(HasSourceImportError), nameof(HasImportSource), nameof(HasImportProblems), nameof(HasUnconfirmedImports), nameof(UnresolvedDependencies), nameof(HasUnresolvedDependencies), nameof(ImportContentSummary), nameof(ImportCountsSummary), nameof(ImportStatus), nameof(ImportProblemNotice), nameof(ImportDetails) }) OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
    private async Task QueueExportRefreshAsync()
    {
        await Task.Delay(180);
        if (!_exportRefreshPending || _coreBusy || CurrentPage != FlowPage.Packaging) return;
        await ExecuteCoreAsync(async () =>
        {
            _exportRefreshPending = false;
            if (SelectedExportResources.Count > 0) await PrepareZipAsync();
            else SetExportState(TransferState.Empty, "请返回资源库选择资源。");
        });
    }
    public Task ExportZipToAsync(string destination) => ExecuteCoreAsync(() => WriteExportZipAsync(destination));
    private async Task WriteExportZipAsync(string destination)
    {
        if (_zipExport is null) await PrepareZipAsync();
        var plan = _zipExport ?? throw new InvalidDataException("选择已改变，请等待重新检查完成。");
        SetExportState(TransferState.Executing, "正在导出 ZIP…");
        await _libraryDatabase!.RunAsync<string>("export.execute", new ExportJobInput(plan, destination, AllowPartialExport), JobProgress());
        CoreNotice = "ZIP 已导出：" + destination;
        SetExportState(TransferState.Completed, "ZIP 已导出。");
    }
}
