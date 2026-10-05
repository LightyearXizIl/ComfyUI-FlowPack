using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.App;

public sealed class ResourceTreeNode(string name, string path, ResourceSelection? selection = null)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public ResourceSelection? Selection { get; } = selection;
    public bool IsResource => Selection is not null;
    public List<ResourceTreeNode> Children { get; } = [];
}
public sealed record ImportRecordRow(string Name, string Source, string Detail);
public sealed record ResourceDependencyGroup(string Name, IReadOnlyList<LibraryDependencyRow> Rows);
public sealed record LibraryDependencyRow(string Name, string State, string Path, string RequiredBy, ResourceSelection? Selection = null);

public sealed partial class ShellViewModel
{
    public ObservableCollection<ImportRecordRow> ImportHistory { get; } = [];
    public ObservableCollection<ResourceTreeNode> WorkflowTree { get; } = [];
    public ObservableCollection<ResourceTreeNode> ModelTree { get; } = [];
    public ObservableCollection<ResourceTreeNode> NodeTree { get; } = [];
    public ObservableCollection<ResourceSelection> DiskOnlyWorkflows { get; } = [];
    public ObservableCollection<ResourceSelection> ReferenceWorkflows { get; } = [];
    public ObservableCollection<ResourceDependencyGroup> LibraryDependencyGroups { get; } = [];
    private ResourceSelection? _librarySelectedResource;
    private string? _librarySelectionCaption;
    public bool LibraryAnalysisBusy { get; private set; }
    
    private long _librarySelectionRevision;
    private bool _exportWorkflows = true, _exportModels = true, _exportNodes = true;
    public bool ExportWorkflows { get => _exportWorkflows; set { _exportWorkflows = value; InvalidateExport(); } }
    public bool ExportModels { get => _exportModels; set { _exportModels = value; InvalidateExport(); } }
    public bool ExportNodes { get => _exportNodes; set { _exportNodes = value; InvalidateExport(); } }
    public string InstanceVersions => $"ComfyUI Core  {_inventory?.RunningCoreVersion ?? _inventory?.CoreVersion ?? "未确认"}     Desktop  {_inventory?.DesktopVersion ?? "未确认"}";
    public string InstanceVersionDetails => _inventory?.RunningCoreVersion is { } running && _inventory.CoreVersion is { } disk && running != disk ?
        $"磁盘 Core：{disk}；运行 Core：{running}。\n" : "";
    public string FrontendListNotice => _inventory?.FrontendNotice ?? "尚未核对前端列表";
    public string LibrarySelectionTitle => _librarySelectionCaption ?? _librarySelectedResource?.Name ?? "选择资源查看详情";
    public string LibrarySelectionPath => _librarySelectedResource?.Path ?? "";
    public string LibrarySelectionNotice { get; private set; } = "选择工作流查看模型和节点包依赖；勾选决定实际导出内容。";
    public bool LibraryHasSelection => _librarySelectedResource is not null;
    public bool HasDiskOnlyWorkflows => DiskOnlyWorkflows.Count > 0;
    public string ImportSourceLabel => _activeImport?.Source ?? "尚未选择来源";
    public bool HasImportSource => _activeImport is not null || ImportResources.Count > 0;
    public ICommand SelectReferenceCommand => new RelayCommand(p => _ = ExecuteCoreAsync(async () =>
    {
        if (p is not ResourceSelection row) return;
        row.IsReference = true;
        await ShowLibraryResourceAsync(row);
    }), _ => CoreReady);
    public ICommand ReopenImportRecordCommand => new RelayCommand(p => _ = ExecuteCoreAsync(async () =>
    {
        if (p is not ImportRecordRow row) return;
        if (!File.Exists(row.Source) && !Directory.Exists(row.Source))
            throw new IOException("历史来源已移走，请通过“选择文件”或“选择目录”重新指定。");
        await ImportNativeAsync(row.Source!);
    }), _ => CoreReady);

    public ICommand AnalyzeReferencesCommand => new RelayCommand(_ => _ = ExecuteCoreAsync(async () =>
    {
        if (_inventory is null) throw new InvalidDataException("请先在首页扫描实例。");
        var inventory = _inventory;
        var references = ReferenceWorkflows.Where(x => x.IsReference).ToArray();
        if (references.Length == 0) throw new InvalidDataException("请勾选用于分析的工作流。");
        var docs = await Task.Run(async () =>
        {
            var result = new List<WorkflowDocument>();
            foreach (var row in references) result.Add(await new WorkflowReader().ReadAsync(row.Path));
            return result;
        });
        var analysis = await _libraryDatabase!.RunAsync<DependencyAnalysis>("dependency.analyze", new DependencyAnalysisInput(docs, _inventory, []), JobProgress());
        if (!ReferenceEquals(inventory, _inventory)) return;
        foreach (var dependency in analysis.Dependencies.Where(x => IncludedInExport(x.Kind) && x.Candidates.Count == 1 &&
            (x.State == DependencyState.Present || x.Kind == ResourceKind.CustomNode && x.State == DependencyState.Unresolved)))
        {
            var payload = LocalModels.Concat(LocalNodes).SingleOrDefault(x => x.Path == dependency.Candidates[0].SourcePath);
            if (payload is not null) payload.IsSelected = true;
        }
        ++_librarySelectionRevision; _librarySelectedResource = references[0]; LibraryAnalysisBusy = false;
        _librarySelectionCaption = references.Length == 1 ? null : $"{references.Length} 个参考工作流的依赖";
        PopulateLibraryDependencyGroups(analysis.Dependencies);
        LibrarySelectionNotice = "已勾选可唯一确认的依赖。可取消不需要的项目；缺失或存在多个来源的项目需要核对。";
        NotifyLibraryDetails();
        CoreNotice = "已勾选可唯一确认的依赖，可取消不需要的项目后生成预览。";
    }), _ => CoreReady && ReferenceWorkflows.Any(x => x.IsReference));

    private async Task RefreshImportHistoryAsync()
    {
        if (_libraryDatabase is null) return;
        var history = await _libraryDatabase.CallAsync<IReadOnlyList<ImportHistoryEntry>>("library.imports", new { });
        var legacy = await _libraryDatabase.LoadImportedPackagesAsync();
        ImportHistory.Clear();
        foreach (var item in history) ImportHistory.Add(new(Path.GetFileName(item.Source), item.Source, $"{item.ResourceCount} 项资源 · {item.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}"));
        foreach (var item in legacy.Where(x => !history.Any(h => h.Source == x.Source)))
            ImportHistory.Add(new(item.Manifest.Name, item.Source, "旧资源包记录 · 重新核对后进入同一安装流程"));
    }

    private void InvalidateExport()
    {
        ++_exportInputRevision;
        _zipExport = null;
        foreach (var name in new[] { nameof(ExportWorkflows), nameof(ExportModels), nameof(ExportNodes), nameof(ExportSummary), nameof(ExportFiles) }) OnPropertyChanged(name);
        NotifyTransferState();
        if (!_changingExportSelection && CurrentPage == FlowPage.Packaging)
        {
            _exportRefreshPending = true;
            if (!_coreBusy) _ = QueueExportRefreshAsync();
        }
        CommandManager.InvalidateRequerySuggested();
    }
    private bool IncludedInExport(ResourceKind kind) => kind switch
    { ResourceKind.Workflow => ExportWorkflows, ResourceKind.Model => ExportModels, ResourceKind.CustomNode => ExportNodes, ResourceKind.Asset => true, _ => false };

    private void RebuildResourceTrees()
    {
        foreach (var tree in new[] { WorkflowTree, ModelTree, NodeTree }) tree.Clear();
        DiskOnlyWorkflows.Clear(); ReferenceWorkflows.Clear();
        var frontend = (_inventory?.FrontendWorkflows ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in LocalWorkflows.Concat(LocalModels).Concat(LocalNodes))
        {
            row.PropertyChanged -= ResourceExportChoiceChanged; row.PropertyChanged += ResourceExportChoiceChanged;
            var resource = row.Resource;
            var tree = resource.Kind switch { ResourceKind.Workflow => WorkflowTree, ResourceKind.Model => ModelTree, _ => NodeTree };
            string rootName, rootPath, relative;
            if (resource.Kind == ResourceKind.Workflow)
            {
                ReferenceWorkflows.Add(row);
                rootPath = _inventory?.Instance.WorkflowsDirectory ?? "";
                var belongs = rootPath.Length > 0 && ResourceImportService.Inside(rootPath, row.Path);
                relative = belongs ? ResourceFiles.Relative(rootPath, row.Path) : resource.Name + ".json";
                if (belongs && _inventory?.FrontendListVerified == true && !frontend.Contains(relative)) { DiskOnlyWorkflows.Add(row); continue; }
                rootName = belongs ? "工作流" : "资源库保存的工作流";
            }
            else if (resource.Kind == ResourceKind.Model)
            {
                rootPath = (_inventory?.Instance.ModelRoots ?? []).Concat(_inventory?.Instance.ExtraPaths.Where(x => x.Category != "custom_nodes").Select(x => x.Path) ?? [])
                    .Where(x => ResourceImportService.Inside(x, row.Path) || string.Equals(x, row.Path, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Length).FirstOrDefault() ?? Path.GetDirectoryName(row.Path)!;
                rootName = (_inventory?.Instance.ModelsWriteDirectory == rootPath ? "实例模型" : "共享 / 扩展模型") + " · " + rootPath;
                relative = ResourceFiles.Relative(rootPath, row.Path);
            }
            else
            {
                rootPath = Path.GetDirectoryName(row.Path)!; rootName = "节点包 · " + rootPath; relative = resource.Name;
            }
            var parent = tree.FirstOrDefault(x => x.Path == rootPath && x.Name == rootName);
            if (parent is null) { parent = new(rootName, rootPath); tree.Add(parent); }
            var parts = relative.Replace('\\', '/').Split('/');
            foreach (var part in parts.SkipLast(1))
            {
                var folder = parent.Children.FirstOrDefault(x => x.Name == part && !x.IsResource);
                if (folder is null) { folder = new(part, Path.Combine(parent.Path, part)); parent.Children.Add(folder); }
                parent = folder;
            }
            var leaf = new ResourceTreeNode(resource.Name, row.Path, row);
            foreach (var type in resource.NodeTypes ?? []) leaf.Children.Add(new(type, "节点类型 · " + resource.Name));
            parent.Children.Add(leaf);
        }
        foreach (var name in new[] { nameof(InstanceVersions), nameof(InstanceVersionDetails), nameof(FrontendListNotice), nameof(HasDiskOnlyWorkflows) }) OnPropertyChanged(name);
        ApplyResourceTreeFilter();
    }

    private void ResourceExportChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ResourceSelection.IsSelected) or nameof(ResourceSelection.IsReference)) { SyncExportRows(); InvalidateExport(); }
    }
    private void ApplyResourceTreeFilter()
    {
        // Filtering tree roots uses a separate projection; resource selection objects retain their check state.
        OnPropertyChanged(nameof(FilteredWorkflowTree)); OnPropertyChanged(nameof(FilteredModelTree)); OnPropertyChanged(nameof(FilteredNodeTree));
    }
    public IEnumerable<ResourceTreeNode> FilteredWorkflowTree => FilterTree(WorkflowTree);
    public IEnumerable<ResourceTreeNode> FilteredModelTree => FilterTree(ModelTree);
    public IEnumerable<ResourceTreeNode> FilteredNodeTree => FilterTree(NodeTree);
    private IEnumerable<ResourceTreeNode> FilterTree(IEnumerable<ResourceTreeNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(ResourceFilter)) return nodes;
        return nodes.Select(FilterNode).OfType<ResourceTreeNode>().ToArray();
        ResourceTreeNode? FilterNode(ResourceTreeNode node)
        {
            if (node.Name.Contains(ResourceFilter, StringComparison.OrdinalIgnoreCase) || node.Path.Contains(ResourceFilter, StringComparison.OrdinalIgnoreCase) ||
                node.Selection?.Category?.Contains(ResourceFilter, StringComparison.OrdinalIgnoreCase) == true) return node;
            var children = node.Children.Select(FilterNode).OfType<ResourceTreeNode>().ToArray();
            if (children.Length == 0) return null;
            var copy = new ResourceTreeNode(node.Name, node.Path, node.Selection); copy.Children.AddRange(children); return copy;
        }
    }

    public async Task ShowLibraryResourceAsync(ResourceSelection row)
    {
        var revision = ++_librarySelectionRevision;
        _librarySelectedResource = row; _librarySelectionCaption = null; LibraryDependencyGroups.Clear();
        LibrarySelectionNotice = row.Resource.Kind == ResourceKind.Workflow ? "正在分析工作流依赖…" :
            row.Resource.Kind == ResourceKind.CustomNode ? $"完整节点包 · {row.Resource.NodeTypes?.Count ?? 0} 个已识别类型；加载状态以运行实例核对为准。" : "按原目录保留模型及配套文件。";
        NotifyLibraryDetails();
        if (_inventory is not { } inventory) return;
        LibraryAnalysisBusy = true; NotifyLibraryDetails();
        var paths = row.Resource.Kind == ResourceKind.Workflow ? new[] { row.Path } : LocalWorkflows.Select(x => x.Path).ToArray();
        try
        {
            var docs = await Task.Run(async () =>
            {
                var result = new List<WorkflowDocument>();
                foreach (var path in paths)
                {
                    try { result.Add(await new WorkflowReader().ReadAsync(path)); }
                    catch (Exception ex) when (row.Resource.Kind != ResourceKind.Workflow && ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
                }
                return result;
            });
            var analysis = await Task.Run(() => new InventoryDependencyAnalyzer().AnalyzeAsync(docs, inventory));
            if (revision != _librarySelectionRevision || !ReferenceEquals(inventory, _inventory)) return;

            var dependencies = row.Resource.Kind == ResourceKind.Workflow ? analysis.Dependencies :
                analysis.Dependencies.Where(x => x.Candidates.Any(c => c.SourcePath == row.Path)).ToArray();
            PopulateLibraryDependencyGroups(dependencies);
            if (row.Resource.Kind == ResourceKind.Workflow)
            {
                var builtins = WorkflowRequirementParser.Analyze(docs.FirstOrDefault()?.RawJson ?? "{}").NodeTypes.Where(x => inventory.CoreNodeTypes.Contains(x)).ToArray();
                if (builtins.Length > 0) LibraryDependencyGroups.Add(new("内置节点 · 无需迁移", builtins.Select(x => new LibraryDependencyRow(x, "内置", "ComfyUI Core", row.Name)).ToArray()));
            }
            LibrarySelectionNotice = analysis.Issues.Count > 0 ? string.Join("\n", analysis.Issues) : row.Resource.Kind == ResourceKind.Workflow ? "导出时会自动加入可确认的依赖，之后可取消不需要的资源。" : "下方显示已保存工作流对该资源的引用。";
        }
        catch (Exception ex) { if (revision == _librarySelectionRevision) LibrarySelectionNotice = "分析未完成：" + ex.Message; }
        finally { if (revision == _librarySelectionRevision) { LibraryAnalysisBusy = false; NotifyLibraryDetails(); } }
        NotifyLibraryDetails();
    }
    private void PopulateLibraryDependencyGroups(IEnumerable<AnalyzedDependency> dependencies)
    {
        LibraryDependencyGroups.Clear();
        foreach (var group in dependencies.GroupBy(x => x.Kind))
        {
            var rows = group.GroupBy(d => d.Candidates.Count == 1 ? d.Candidates[0].SourcePath : d.Id, StringComparer.OrdinalIgnoreCase).Select(items =>
            {
                var d = items.First();
                var candidate = d.Candidates.Count == 1 ? d.Candidates[0] : null;
                var payload = candidate is null ? null : LocalModels.Concat(LocalNodes).FirstOrDefault(x => x.Path == candidate.SourcePath);
                var name = candidate?.Name ?? d.Reference;
                if (candidate is not null && d.Kind == ResourceKind.CustomNode) name += " · " + string.Join("、", items.Select(x => x.Reference).Distinct());
                var state = string.Join("、", items.Select(x => x.State switch { DependencyState.Present => "可用", DependencyState.Missing => "缺失", DependencyState.Ambiguous => "需要选择来源", _ => "加载状态待验证" }).Distinct());
                return new LibraryDependencyRow(name, state, candidate?.SourcePath ?? d.Evidence,
                    "用于：" + string.Join("、", items.SelectMany(x => x.RequiredBy).Distinct()), payload);
            }).ToArray();
            LibraryDependencyGroups.Add(new(group.Key == ResourceKind.Model ? "关联模型" : "关联节点包", rows));
        }
    }

    private void NotifyLibraryDetails()
    {
        foreach (var name in new[] { nameof(LibrarySelectionTitle), nameof(LibrarySelectionPath), nameof(LibrarySelectionNotice), nameof(LibraryHasSelection), nameof(LibraryAnalysisBusy) }) OnPropertyChanged(name);
    }
}
