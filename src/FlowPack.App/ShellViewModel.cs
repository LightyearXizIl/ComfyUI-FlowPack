using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;
using Microsoft.Win32;

namespace FlowPack.App;

public enum FlowPage
{
    Home,
    Packages,
    InstallPreview,
    Workflows,
    Models,
    Nodes,
    Tasks,
    PackageWizard,
    Appearance
}

public sealed record PackageRow(
    string Id,
    string Name,
    string Version,
    string Summary,
    string Status,
    IReadOnlyList<ResourceEntry> Resources,
    PackageManifest? Manifest = null);

public sealed record TaskRow(string Name, string Stage, int? Progress, string Detail, bool IsCurrent);
public sealed record WorkflowRow(string Id, string Name, string Format, string Summary);
public sealed record ResourceLibraryRow(
    string Id,
    string Name,
    string PackageName,
    string PackageVersion,
    string Size,
    string Source,
    string Status);
public sealed record InstallPreviewActionRow(string Summary, string Kind, string RequiredSize, bool IsBlocking);
public sealed record SettingOption<T>(T Value, string Label);

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private FlowPage _currentPage = FlowPage.Home;
    private PackageRow? _selectedPackage;
    private readonly ThemePreferenceStore _themeStore;
    private readonly LibraryBindingStore _libraryBindingStore;
    private readonly IInstanceInspector _instanceInspector;
    private ResourceLibraryDatabase? _libraryDatabase;
    private InstanceFingerprint? _candidateInstance;
    private readonly IComfyDesktopDetector? _desktopDetector;
    private ComfyDesktopLocation? _detectedDesktop;
    private InstallPlan? _installPlan;
    private ThemeDefinition _appliedTheme;
    private ThemeDefinition _draftTheme;
    private string _themeNotice = "主题设置尚未更改。";
    private string _statusNotice = "●  尚未检查环境";
    private string _resourceLibraryLocation = "尚未选择资源库";
    private string _comfyUiLocation = "尚未选择候选 ComfyUI 环境";
    private bool _isDownloadingPackage;
    private string _packageSearchText = string.Empty;
    private string _workflowSearchText = string.Empty;
    private string _modelSearchText = string.Empty;
    private string _nodeSearchText = string.Empty;
    private PackageDraft _packageDraft = CreateEmptyDraft();
    private string _wizardStatus = "请选择工作流开始创建草稿。";

    public ShellViewModel() : this(null, null, null, null)
    {
    }

    public ShellViewModel(
        ThemePreferenceStore? themeStore,
        LibraryBindingStore? libraryBindingStore = null,
        IInstanceInspector? instanceInspector = null,
        IComfyDesktopDetector? desktopDetector = null)
    {
        _themeStore = themeStore ?? new ThemePreferenceStore();
        _libraryBindingStore = libraryBindingStore ?? new LibraryBindingStore();
        _instanceInspector = instanceInspector ?? new ComfyUiInspector();
        _desktopDetector = desktopDetector;
        _appliedTheme = LoadInitialTheme();
        _draftTheme = _appliedTheme;
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (Enum.TryParse<FlowPage>(parameter?.ToString(), out var page))
            {
                CurrentPage = page;
            }
        });
        OpenPackageCommand = new RelayCommand(OpenPackage, parameter => parameter is PackageRow);
        ToggleThemeCommand = new RelayCommand(_ => ToggleQuickTheme());
        StartInstallCommand = new RelayCommand(_ => { }, _ => false);
        DetectDesktopCommand = new RelayCommand(_ => _ = DetectDesktopAsync());
        ImportPackageCommand = new RelayCommand(_ => _ = ImportPackageAsync());
        ImportPackageFolderCommand = new RelayCommand(_ => _ = ImportPackageFolderAsync());
        ImportWorkflowCommand = new RelayCommand(_ => _ = ImportWorkflowAsync());
        RefreshTasksCommand = new RelayCommand(_ => _ = RefreshTasksAsync());
        DownloadPackageCommand = new RelayCommand(_ => _ = DownloadPackageAsync(), _ => !_isDownloadingPackage);
        NextWizardStepCommand = new RelayCommand(_ => _ = AdvanceWizardAsync());
        PreviousWizardStepCommand = new RelayCommand(_ => MoveWizardBackward());
        SaveWizardDraftCommand = new RelayCommand(_ => _ = SaveWizardDraftAsync());
        ExportDraftCommand = new RelayCommand(_ => _ = ExportDraftAsync());
        ImportDraftCommand = new RelayCommand(_ => _ = ImportDraftAsync());
        ExportWorkflowPackageCommand = new RelayCommand(_ => _ = ExportWorkflowPackageAsync());
        PreviewThemeCommand = new RelayCommand(_ => PreviewTheme());
        ApplyThemeCommand = new RelayCommand(_ => _ = ApplyThemeAsync());
        CancelThemeCommand = new RelayCommand(_ => CancelTheme());
        ResetThemeCommand = new RelayCommand(_ => ResetTheme());
        ImportThemeCommand = new RelayCommand(_ => _ = ImportThemeAsync());
        ExportThemeCommand = new RelayCommand(_ => _ = ExportThemeAsync());
        ReloadThemeCommand = new RelayCommand(_ => _ = ReloadThemeAsync());
        OpenRepositoryCommand = new RelayCommand(_ => OpenRepository());
        CopyRepositoryUrlCommand = new RelayCommand(_ => CopyRepositoryUrl());
        ExportDiagnosticsCommand = new RelayCommand(_ => _ = ExportDiagnosticsAsync());
        SelectResourceLibraryCommand = new RelayCommand(_ => _ = SelectResourceLibraryAsync());
        SelectComfyUiCommand = new RelayCommand(_ => _ = SelectComfyUiAsync());

        PackageView = CollectionViewSource.GetDefaultView(Packages);
        WorkflowView = CollectionViewSource.GetDefaultView(Workflows);
        ModelView = CollectionViewSource.GetDefaultView(Models);
        NodeView = CollectionViewSource.GetDefaultView(Nodes);
        PackageView.Filter = item => item is PackageRow package && MatchesSearch(PackageSearchText, package.Name, package.Version, package.Summary, package.Status);
        WorkflowView.Filter = item => item is WorkflowRow workflow && MatchesSearch(WorkflowSearchText, workflow.Name, workflow.Format, workflow.Summary);
        ModelView.Filter = item => item is ResourceLibraryRow model && MatchesSearch(ModelSearchText, model.Name, model.PackageName, model.PackageVersion, model.Status);
        NodeView.Filter = item => item is ResourceLibraryRow node && MatchesSearch(NodeSearchText, node.Name, node.PackageName, node.PackageVersion, node.Status);

        Packages.CollectionChanged += (_, _) =>
        {
            RebuildResourceRows();
            OnPropertyChanged(nameof(HasPackages));
            PackageView.Refresh();
        };
        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTasks));
        Workflows.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasWorkflows)); WorkflowView.Refresh(); };
        Models.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasModels)); ModelView.Refresh(); };
        Nodes.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasNodes)); NodeView.Refresh(); };
        ApplyThemeToResources(_appliedTheme);
        RestoreLibraryBinding();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand NavigateCommand { get; }
    public ICommand OpenPackageCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand StartInstallCommand { get; }
    public ICommand ImportPackageCommand { get; }
    public ICommand ImportPackageFolderCommand { get; }
    public ICommand ImportWorkflowCommand { get; }
    public ICommand RefreshTasksCommand { get; }
    public ICommand DownloadPackageCommand { get; }
    public ICommand NextWizardStepCommand { get; }
    public ICommand PreviousWizardStepCommand { get; }
    public ICommand SaveWizardDraftCommand { get; }
    public ICommand ExportDraftCommand { get; }
    public ICommand ImportDraftCommand { get; }
    public ICommand ExportWorkflowPackageCommand { get; }
    public ICommand PreviewThemeCommand { get; }
    public ICommand ApplyThemeCommand { get; }
    public ICommand CancelThemeCommand { get; }
    public ICommand ResetThemeCommand { get; }
    public ICommand ImportThemeCommand { get; }
    public ICommand ExportThemeCommand { get; }
    public ICommand ReloadThemeCommand { get; }
    public ICommand OpenRepositoryCommand { get; }
    public ICommand CopyRepositoryUrlCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public ICommand SelectResourceLibraryCommand { get; }
    public ICommand SelectComfyUiCommand { get; }

    public ObservableCollection<PackageRow> Packages { get; } = [];
    public ObservableCollection<TaskRow> Tasks { get; } = [];
    public ObservableCollection<WorkflowRow> Workflows { get; } = [];
    public ObservableCollection<ResourceLibraryRow> Models { get; } = [];
    public ObservableCollection<ResourceLibraryRow> Nodes { get; } = [];
    public ObservableCollection<InstallPreviewActionRow> InstallPreviewActions { get; } = [];
    public ICollectionView PackageView { get; }
    public ICollectionView WorkflowView { get; }
    public ICollectionView ModelView { get; }
    public ICollectionView NodeView { get; }

    public string PackageSearchText
    {
        get => _packageSearchText;
        set => SetSearchText(ref _packageSearchText, value, PackageView, nameof(PackageSearchText));
    }

    public string WorkflowSearchText
    {
        get => _workflowSearchText;
        set => SetSearchText(ref _workflowSearchText, value, WorkflowView, nameof(WorkflowSearchText));
    }

    public string ModelSearchText
    {
        get => _modelSearchText;
        set => SetSearchText(ref _modelSearchText, value, ModelView, nameof(ModelSearchText));
    }

    public string NodeSearchText
    {
        get => _nodeSearchText;
        set => SetSearchText(ref _nodeSearchText, value, NodeView, nameof(NodeSearchText));
    }

    public FlowPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage == value) return;
            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
            OnPropertyChanged(nameof(IsHomeContext));
            OnPropertyChanged(nameof(IsPackagesContext));
            OnPropertyChanged(nameof(IsWorkflowsContext));
            OnPropertyChanged(nameof(IsTasksContext));
        }
    }

    public PackageRow? SelectedPackage
    {
        get => _selectedPackage;
        private set
        {
            if (Equals(_selectedPackage, value)) return;
            _selectedPackage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedPackage));
            OnPropertyChanged(nameof(SelectedPackageSubtitle));
            OnPropertyChanged(nameof(SelectedPackageResourceSummary));
            OnPropertyChanged(nameof(HasDownloadablePackage));
            BuildInstallPreview();
        }
    }

    public string StatusNotice
    {
        get => _statusNotice;
        private set
        {
            if (_statusNotice == value) return;
            _statusNotice = value;
            OnPropertyChanged();
        }
    }

    private async Task DetectDesktopAsync()
    {
        if (_desktopDetector is null) return;
        try
        {
            var location = await _desktopDetector.DetectAsync();
            _detectedDesktop = location;
            OnPropertyChanged(nameof(DetectedDesktop));
            OnPropertyChanged(nameof(DesktopSummary));
            StatusNotice = location is null
                ? "●  未检测到 ComfyUI Desktop"
                : $"●  已检测 ComfyUI Desktop（{location.DetectedVia}）：{location.BasePath}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ThemeNotice = $"检测 ComfyUI Desktop 失败：{exception.Message}";
        }
    }
    public bool HasPackages => Packages.Count > 0;
    public bool HasTasks => Tasks.Count > 0;
    public bool HasWorkflows => Workflows.Count > 0;
    public bool HasModels => Models.Count > 0;
    public bool HasNodes => Nodes.Count > 0;
    public bool HasSelectedPackage => SelectedPackage is not null;
    public bool HasInstallPreview => _installPlan is not null;
    public bool HasDownloadablePackage => _libraryDatabase is not null && SelectedPackage is { Resources.Count: > 0 } package &&
        package.Resources.All(resource => !string.IsNullOrWhiteSpace(resource.SourceUrl) && !string.IsNullOrWhiteSpace(resource.Sha256));
    public bool IsDownloadingPackage
    {
        get => _isDownloadingPackage;
        private set
        {
            if (_isDownloadingPackage == value) return;
            _isDownloadingPackage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DownloadPackageButtonText));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public string DownloadPackageButtonText => IsDownloadingPackage ? "下载中…" : "下载到 staging";

    public ICommand DetectDesktopCommand { get; }
    public ComfyDesktopLocation? DetectedDesktop => _detectedDesktop;
    public string DesktopSummary => _detectedDesktop is null
        ? "未检测到 ComfyUI Desktop；可在设置中选择安装目录。"
        : $"已检测 ComfyUI Desktop（来自 {_detectedDesktop.DetectedVia}）";
    public string InstallPreviewEnvironment => _candidateInstance is null
        ? "●  尚未检查"
        : "●  候选布局已检查，待 Desktop 适配验证";
    public string InstallPreviewStatus
    {
        get
        {
            if (SelectedPackage is null) return "请选择资源包。";
            if (_candidateInstance is null) return "需要先选择并通过只读布局检查的候选 ComfyUI 目录，才能生成声明级预览。";
            if (_installPlan is null) return "正在生成预览。";
            if (!_installPlan.IsExecutable) return $"清单存在 {(_installPlan.BlockingReasons?.Count ?? 0)} 项阻断，不能进入安装。";
            return "这是基于清单与候选目录的声明级预览；未完成 Desktop 适配、库存、空间和写前复检，安装保持禁用。";
        }
    }

    public bool IsHomeContext => CurrentPage == FlowPage.Home;
    public bool IsPackagesContext => CurrentPage is FlowPage.Packages or FlowPage.InstallPreview or FlowPage.Models or FlowPage.Nodes;
    public bool IsWorkflowsContext => CurrentPage is FlowPage.Workflows or FlowPage.PackageWizard;
    public bool IsTasksContext => CurrentPage == FlowPage.Tasks;

    public IReadOnlyList<SettingOption<ThemeBase>> ThemeBaseOptions { get; } =
    [
        new(ThemeBase.System, "跟随系统"),
        new(ThemeBase.Light, "浅色"),
        new(ThemeBase.Dark, "深色")
    ];

    public IReadOnlyList<SettingOption<ThemeDensity>> ThemeDensityOptions { get; } =
    [
        new(ThemeDensity.Comfortable, "舒适密度"),
        new(ThemeDensity.Compact, "紧凑密度")
    ];

    public IReadOnlyList<int> FontSizeOptions { get; } = Enumerable.Range(12, 9).ToArray();
    public IReadOnlyList<int> CornerRadiusOptions { get; } = Enumerable.Range(0, 17).ToArray();

    public string ThemeName
    {
        get => _draftTheme.Name;
        set => UpdateDraft(_draftTheme with { Name = value ?? string.Empty });
    }

    public ThemeBase DraftThemeBase
    {
        get => _draftTheme.Base;
        set
        {
            if (_draftTheme.Base == value) return;
            var defaults = ThemeDefaults.Create(value == ThemeBase.System ? GetSystemThemeBase() : value);
            UpdateDraft(_draftTheme with { Base = value, SurfaceColor = defaults.SurfaceColor, TextColor = defaults.TextColor });
        }
    }

    public string AccentColor
    {
        get => _draftTheme.AccentColor;
        set => UpdateDraft(_draftTheme with { AccentColor = value ?? string.Empty });
    }

    public string SurfaceColor
    {
        get => _draftTheme.SurfaceColor;
        set => UpdateDraft(_draftTheme with { SurfaceColor = value ?? string.Empty });
    }

    public string TextColor
    {
        get => _draftTheme.TextColor;
        set => UpdateDraft(_draftTheme with { TextColor = value ?? string.Empty });
    }

    public int BodyFontSize
    {
        get => _draftTheme.BodyFontSize;
        set => UpdateDraft(_draftTheme with { BodyFontSize = value });
    }

    public int CornerRadius
    {
        get => _draftTheme.CornerRadius;
        set => UpdateDraft(_draftTheme with { CornerRadius = value });
    }

    public ThemeDensity Density
    {
        get => _draftTheme.Density;
        set => UpdateDraft(_draftTheme with { Density = value });
    }

    public bool EnableAnimations
    {
        get => _draftTheme.EnableAnimations;
        set => UpdateDraft(_draftTheme with { EnableAnimations = value });
    }

    public string ThemeNotice
    {
        get => _themeNotice;
        private set
        {
            if (_themeNotice == value) return;
            _themeNotice = value;
            OnPropertyChanged();
        }
    }

    public string ThemeFileLocation => _themeStore.FilePath;
    public string ResourceLibraryLocation
    {
        get => _resourceLibraryLocation;
        private set
        {
            if (_resourceLibraryLocation == value) return;
            _resourceLibraryLocation = value;
            OnPropertyChanged();
        }
    }
    public string ComfyUiLocation
    {
        get => _comfyUiLocation;
        private set
        {
            if (_comfyUiLocation == value) return;
            _comfyUiLocation = value;
            OnPropertyChanged();
        }
    }
    public string AuthorName => "LightyearXizIl";
    public string RepositoryUrl => "https://github.com/LightyearXizIl/ComfyUI-FlowPack";
    public string ApplicationVersion => GetApplicationVersion();

    public IReadOnlyList<SettingOption<DistributionDeclaration>> DraftDistributionOptions { get; } =
    [
        new(DistributionDeclaration.OnlineOnly, "在线清单"),
        new(DistributionDeclaration.OfflinePartial, "离线草稿"),
        new(DistributionDeclaration.OfflineComplete, "完整离线包")
    ];

    public string? DraftWorkflowId
    {
        get => _packageDraft.WorkflowId;
        set => UpdateDraft(_packageDraft with { WorkflowId = value });
    }

    public string DraftName
    {
        get => _packageDraft.Name;
        set => UpdateDraft(_packageDraft with { Name = value ?? string.Empty });
    }

    public string DraftVersion
    {
        get => _packageDraft.Version;
        set => UpdateDraft(_packageDraft with { Version = value ?? string.Empty });
    }

    public string DraftDescription
    {
        get => _packageDraft.Description;
        set => UpdateDraft(_packageDraft with { Description = value ?? string.Empty });
    }

    public string DraftAuthorName
    {
        get => _packageDraft.AuthorName;
        set => UpdateDraft(_packageDraft with { AuthorName = value ?? string.Empty });
    }

    public string DraftAuthorUrl
    {
        get => _packageDraft.AuthorUrl ?? string.Empty;
        set => UpdateDraft(_packageDraft with { AuthorUrl = string.IsNullOrWhiteSpace(value) ? null : value });
    }

    public string DraftSource
    {
        get => _packageDraft.Source;
        set => UpdateDraft(_packageDraft with { Source = value ?? string.Empty });
    }

    public DistributionDeclaration DraftDistribution
    {
        get => _packageDraft.Distribution;
        set => UpdateDraft(_packageDraft with { Distribution = value });
    }

    public int WizardStep => _packageDraft.CurrentStep;
    public string WizardStepText => $"第 {WizardStep} 步，共 4 步：{WizardStep switch { 1 => "选择工作流", 2 => "检查依赖", 3 => "填写信息", _ => "保存草稿" }}。";
    public string WizardStatus
    {
        get => _wizardStatus;
        private set
        {
            if (_wizardStatus == value) return;
            _wizardStatus = value;
            OnPropertyChanged();
        }
    }

    public string PageTitle => CurrentPage switch
    {
        FlowPage.Home => "让 ComfyUI 资源井然有序",
        FlowPage.Packages => "资源包",
        FlowPage.InstallPreview => "安装预览",
        FlowPage.Workflows => "工作流",
        FlowPage.Models => "模型库",
        FlowPage.Nodes => "节点管理",
        FlowPage.Tasks => "任务中心",
        FlowPage.PackageWizard => "创建资源包",
        FlowPage.Appearance => "外观设置",
        _ => string.Empty
    };

    public string PageSubtitle => CurrentPage switch
    {
        FlowPage.Home => "导入、安装和分享工作流需要的全部资源。",
        FlowPage.Packages => "查看资源包的版本、状态和兼容性。",
        FlowPage.InstallPreview => "确认真实环境检查和变更计划后才能安装。",
        FlowPage.Workflows => "从资源库管理工作流与所需依赖。",
        FlowPage.Models => "模型集中保存，并按 ComfyUI 实例共享加载。",
        FlowPage.Nodes => "只维护 FlowPack 已识别的自定义节点。",
        FlowPage.Tasks => "这里显示真实下载和安装任务，不生成示例进度。",
        FlowPage.PackageWizard => "固定四步创建一个可分享的资源包。",
        FlowPage.Appearance => "调整界面外观，页面与任务状态保持不变。",
        _ => string.Empty
    };

    public string SelectedPackageSubtitle => SelectedPackage is null
        ? "尚未选择资源包"
        : $"{SelectedPackage.Name} · {SelectedPackage.Version}";

    public string SelectedPackageResourceSummary
    {
        get
        {
            if (SelectedPackage is null) return "没有可预览的资源。";
            var knownSize = SelectedPackage.Resources.Sum(resource => resource.SizeBytes);
            return $"清单包含 {SelectedPackage.Resources.Count} 项资源，声明大小 {FormatBytes(knownSize)}。";
        }
    }

    private void OpenPackage(object? parameter)
    {
        if (parameter is not PackageRow package) return;
        SelectedPackage = package;
        CurrentPage = FlowPage.InstallPreview;
    }

    private void RestoreLibraryBinding()
    {
        try
        {
            var binding = _libraryBindingStore.LoadAsync().GetAwaiter().GetResult();
            if (binding is null || !Directory.Exists(binding.LibraryPath)) return;
            _libraryDatabase = new ResourceLibraryDatabase(binding.LibraryPath);
            OnPropertyChanged(nameof(HasDownloadablePackage));
            ResourceLibraryLocation = _libraryDatabase.LibraryPath;
            StatusNotice = "●  已关联资源库，尚未检查环境";
            _ = RestorePackagesAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusNotice = "●  资源库关联需要重新选择";
            ThemeNotice = $"无法恢复资源库关联：{exception.Message}";
        }
    }

    private async Task SelectComfyUiAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择一个候选 ComfyUI 目录；FlowPack 只会进行只读检查。"
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;

        try
        {
            var candidate = await _instanceInspector.InspectAsync(dialog.FolderName);
            if (candidate is null)
            {
                ComfyUiLocation = "未确认：所选目录没有完整的受控布局证据";
                StatusNotice = "●  尚未检查环境";
                ThemeNotice = "未绑定所选目录。FlowPack 不会根据任意 python.exe 或猜测的 user 目录创建环境关联。";
                return;
            }

            ComfyUiLocation = candidate.InstancePath;
            _candidateInstance = candidate;
            BuildInstallPreview();
            StatusNotice = "●  已选择候选环境，待版本适配验证";
            if (_libraryDatabase is not null)
            {
                await _libraryDatabase.SaveCandidateInstanceAsync(candidate);
                ThemeNotice = "候选目录已保存到资源库，并通过本地只读布局检查。版本、配置来源、节点与模型库仍需在实例适配阶段验证；安装保持禁用。";
            }
            else
            {
                ThemeNotice = "候选目录通过本地只读布局检查，但尚未选择资源库，因此不会跨重启保存。版本、配置来源、节点与模型库仍需验证；安装保持禁用。";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ThemeNotice = $"无法检查候选环境：{exception.Message}";
        }
    }

    private async Task SelectResourceLibraryAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择或新建一个由 FlowPack 管理元数据的资源库文件夹。"
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;

        if (!ResourceLibraryPathValidator.IsSafeLibraryPath(dialog.FolderName, AppContext.BaseDirectory, null, out var reason))
        {
            ThemeNotice = reason;
            return;
        }

        try
        {
            var database = new ResourceLibraryDatabase(dialog.FolderName);
            await database.InitializeAsync();
            await _libraryBindingStore.SaveAsync(new LibraryBinding(database.LibraryPath, DateTimeOffset.UtcNow));
            if (_libraryDatabase is not null) await _libraryDatabase.DisposeAsync();
            _libraryDatabase = database;
            OnPropertyChanged(nameof(HasDownloadablePackage));
            ResourceLibraryLocation = database.LibraryPath;
            StatusNotice = "●  已关联资源库，尚未检查环境";
            await RestorePackagesAsync();
            ThemeNotice = "资源库已关联；导入的资源包会保存到该库的元数据数据库中，尚未对 ComfyUI 写入任何文件。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ThemeNotice = $"无法关联资源库：{exception.Message}";
        }
    }

    private async Task RestorePackagesAsync()
    {
        if (_libraryDatabase is null) return;
        try
        {
            var storedPackages = await _libraryDatabase.LoadImportedPackagesAsync();
            Packages.Clear();
            foreach (var stored in storedPackages)
            {
                var status = stored.Manifest.IsComplete
                    ? "已保存，等待环境检查"
                    : $"已保存但不完整：{stored.Manifest.CompletenessIssues.First()}";
                Packages.Add(ToPackageRow(stored.Manifest, status));
            }
            await RestoreCandidateInstanceAsync();
            await RestoreWorkflowsAsync();
            await RestoreTasksAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ThemeNotice = $"无法读取资源库记录：{exception.Message}";
        }
    }

    private async Task RestoreCandidateInstanceAsync()
    {
        if (_libraryDatabase is null) return;
        var candidate = (await _libraryDatabase.LoadCandidateInstancesAsync()).FirstOrDefault();
        if (candidate is null) return;
        ComfyUiLocation = candidate.Instance.InstancePath;
        _candidateInstance = candidate.Instance;
        BuildInstallPreview();
        StatusNotice = "●  已恢复候选环境，待版本适配验证";
    }

    private async Task RestoreWorkflowsAsync()
    {
        if (_libraryDatabase is null) return;
        var storedWorkflows = await _libraryDatabase.LoadWorkflowsAsync();
        Workflows.Clear();
        foreach (var stored in storedWorkflows)
        {
            Workflows.Add(ToWorkflowRow(stored.Workflow));
        }
        await RestoreLatestDraftAsync();
    }

    private async Task RestoreTasksAsync()
    {
        if (_libraryDatabase is null) return;
        var storedTasks = await _libraryDatabase.LoadTasksAsync();
        Tasks.Clear();
        foreach (var task in storedTasks)
        {
            Tasks.Add(ToTaskRow(task));
        }
    }

    private async Task RefreshTasksAsync()
    {
        if (_libraryDatabase is null)
        {
            MessageBox.Show("请先在设置页选择资源库。任务中心只读取资源库中由 Worker 持久化的真实任务。", "需要资源库", MessageBoxButton.OK, MessageBoxImage.Information);
            CurrentPage = FlowPage.Appearance;
            return;
        }
        try
        {
            await RestoreTasksAsync();
            ThemeNotice = Tasks.Count == 0 ? "资源库中没有 Worker 任务记录。" : $"已从资源库刷新 {Tasks.Count} 条任务记录。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ThemeNotice = $"无法刷新任务记录：{exception.Message}";
        }
    }

    private async Task DownloadPackageAsync()
    {
        if (_libraryDatabase is null || SelectedPackage is null)
        {
            MessageBox.Show("请先关联资源库并选择资源包。", "无法下载", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var resources = SelectedPackage.Resources.ToArray();
        if (resources.Length == 0 || resources.Any(resource => string.IsNullOrWhiteSpace(resource.SourceUrl) || string.IsNullOrWhiteSpace(resource.Sha256)))
        {
            MessageBox.Show("资源包中存在缺少 HTTPS 来源或 SHA-256 的资源，不能安全下载。", "无法下载", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var worker = StartWorker(_libraryDatabase.LibraryPath, out var pipeName, out var secret);
        if (worker is null)
        {
            MessageBox.Show("找不到 Worker 可执行文件。请使用完整安装包，或先生成 Release Worker 输出。", "无法下载", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        IsDownloadingPackage = true;
        try
        {
            var client = new NamedPipeWorkerClient();
            var ready = false;
            for (var attempt = 0; attempt < 20 && !ready; attempt++)
            {
                try
                {
                    var ping = await client.SendAsync(pipeName, new WorkerRequest(WorkerProtocol.Version, Guid.NewGuid().ToString("N"), secret, WorkerProtocol.PingCommand), TimeSpan.FromMilliseconds(500));
                    ready = ping.Succeeded;
                }
                catch (OperationCanceledException) { await Task.Delay(50); }
            }
            if (!ready) throw new IOException("Worker 未在限定时间内就绪。");
            foreach (var resource in resources)
            {
                var response = await client.SendAsync(pipeName, new WorkerRequest(
                    WorkerProtocol.Version, Guid.NewGuid().ToString("N"), secret, WorkerProtocol.DownloadCommand,
                    System.Text.Json.JsonSerializer.SerializeToElement(new DownloadTaskPayload(resource.SourceUrl!, resource.Sha256!, resource.Name))), TimeSpan.FromSeconds(10));
                if (!response.Succeeded) throw new IOException(response.Error?.Message ?? "Worker 拒绝下载请求。");
            }
            await RestoreTasksAsync();
            CurrentPage = FlowPage.Tasks;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or HttpRequestException or OperationCanceledException)
        {
            MessageBox.Show(exception.Message, "下载失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            await RestoreTasksAsync();
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
            IsDownloadingPackage = false;
        }
    }

    private static Process? StartWorker(string libraryPath, out string pipeName, out string secret)
    {
        pipeName = $"flowpack-{Guid.NewGuid():N}";
        secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        try
        {
            var installedWorker = Path.Combine(AppContext.BaseDirectory, "worker", "ComfyUI.FlowPack.Worker.exe");
            if (File.Exists(installedWorker))
            {
                return Process.Start(new ProcessStartInfo(installedWorker, $"--pipe {pipeName} --secret {secret} --library \"{libraryPath}\"") { UseShellExecute = false, CreateNoWindow = true });
            }
            var developmentWorker = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FlowPack.Worker", "bin", "Release", "net10.0-windows", "ComfyUI.FlowPack.Worker.dll"));
            return File.Exists(developmentWorker)
                ? Process.Start(new ProcessStartInfo("dotnet", $"\"{developmentWorker}\" --pipe {pipeName} --secret {secret} --library \"{libraryPath}\"") { UseShellExecute = false, CreateNoWindow = true })
                : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private async Task RestoreLatestDraftAsync()
    {
        if (_libraryDatabase is null) return;
        var draft = (await _libraryDatabase.LoadDraftsAsync()).FirstOrDefault();
        if (draft is null) return;
        _packageDraft = draft.Draft;
        RaiseDraftProperties();
        WizardStatus = "已恢复最近保存的资源包草稿。";
    }

    private static PackageRow ToPackageRow(PackageManifest manifest, string status) => new(
        manifest.Id,
        manifest.Name,
        manifest.Version,
        $"资源库已记录 {manifest.Resources.Count} 项资源；尚未部署到 ComfyUI。",
        status,
        manifest.Resources,
        manifest);

    public static TaskRow ToTaskRow(WorkerTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var progress = task.CompletedBytes is not null && task.TotalBytes is > 0
            ? (int?)Math.Clamp((int)Math.Round(task.CompletedBytes.Value * 100d / task.TotalBytes.Value), 0, 100)
            : null;
        var detail = task.ErrorMessage ?? FormatTaskBytes(task.CompletedBytes, task.TotalBytes);
        return new TaskRow(task.Summary, task.Stage, progress, detail, task.State is WorkerTaskState.Queued or WorkerTaskState.Running or WorkerTaskState.Paused);
    }

    private static string FormatTaskBytes(long? completedBytes, long? totalBytes)
    {
        if (completedBytes is null && totalBytes is null) return "进度总量未知";
        if (completedBytes is null) return $"总量 {FormatBytes(totalBytes!.Value)}";
        if (totalBytes is null) return $"已处理 {FormatBytes(completedBytes.Value)}";
        return $"{FormatBytes(completedBytes.Value)} / {FormatBytes(totalBytes.Value)}";
    }

    private void BuildInstallPreview()
    {
        _installPlan = null;
        InstallPreviewActions.Clear();
        if (SelectedPackage is null || _candidateInstance is null)
        {
            RaiseInstallPreviewProperties();
            return;
        }

        var manifest = SelectedPackage.Manifest ?? new PackageManifest(
            SelectedPackage.Id,
            SelectedPackage.Name,
            SelectedPackage.Version,
            SelectedPackage.Resources);
        _installPlan = new SafeInstallPlanner().PlanAsync(manifest, _candidateInstance).GetAwaiter().GetResult();
        foreach (var action in _installPlan.Actions)
        {
            InstallPreviewActions.Add(new InstallPreviewActionRow(
                action.Summary,
                action.Kind.ToString(),
                FormatBytes(action.RequiredBytes),
                action.NeedsConfirmation || action.Kind == InstallActionKind.Conflict));
        }
        RaiseInstallPreviewProperties();
    }

    private void RaiseInstallPreviewProperties()
    {
        OnPropertyChanged(nameof(HasInstallPreview));
        OnPropertyChanged(nameof(InstallPreviewEnvironment));
        OnPropertyChanged(nameof(InstallPreviewStatus));
    }

    public static IReadOnlyList<ResourceLibraryRow> CreateResourceRows(IEnumerable<PackageRow> packages, ResourceKind kind)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return packages
            .SelectMany(package => package.Resources
                .Where(resource => resource.Kind == kind)
                .Select(resource => new ResourceLibraryRow(
                    resource.Id,
                    resource.Name,
                    package.Name,
                    package.Version,
                    FormatBytes(resource.SizeBytes),
                    resource.SourceUrl ?? "未提供下载来源",
                    "已导入清单，尚未部署")))
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void RebuildResourceRows()
    {
        ReplaceRows(Models, CreateResourceRows(Packages, ResourceKind.Model));
        ReplaceRows(Nodes, CreateResourceRows(Packages, ResourceKind.CustomNode));
    }

    private void SetSearchText(ref string field, string? value, ICollectionView view, string propertyName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (field == normalized) return;
        field = normalized;
        OnPropertyChanged(propertyName);
        view.Refresh();
    }

    public static bool MatchesSearch(string? query, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return values.Any(value => value?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void ReplaceRows<T>(ObservableCollection<T> destination, IEnumerable<T> rows)
    {
        destination.Clear();
        foreach (var row in rows) destination.Add(row);
    }

    private static WorkflowRow ToWorkflowRow(WorkflowDocument workflow) => new(
        workflow.Id,
        workflow.DisplayName,
        workflow.Format switch
        {
            WorkflowFormat.UiV04 => "UI 工作流 v0.4",
            WorkflowFormat.UiV10 => "UI 工作流 v1.0",
            WorkflowFormat.Api => "API 工作流",
            _ => "未识别 JSON 格式"
        },
        workflow.Format == WorkflowFormat.Api
            ? "已保留原始 API JSON；不会当作 UI 工作流自动转换。"
            : "已保留原始 JSON 和未知字段；依赖分析尚未完成。");

    private async Task AdvanceWizardAsync()
    {
        if (!ValidateCurrentWizardStep()) return;
        if (WizardStep == 4)
        {
            await SaveWizardDraftAsync();
            return;
        }
        _packageDraft = _packageDraft with { CurrentStep = WizardStep + 1 };
        RaiseDraftProperties();
        await SaveWizardDraftAsync();
    }

    private void MoveWizardBackward()
    {
        if (WizardStep <= 1) return;
        _packageDraft = _packageDraft with { CurrentStep = WizardStep - 1 };
        RaiseDraftProperties();
        WizardStatus = "已返回上一步；草稿中的内容保持不变。";
    }

    private bool ValidateCurrentWizardStep()
    {
        if (_libraryDatabase is null)
        {
            WizardStatus = "请先在设置页选择资源库，草稿不会只保存在当前会话中。";
            return false;
        }
        if (WizardStep == 1 && (string.IsNullOrWhiteSpace(DraftWorkflowId) || !Workflows.Any(workflow => workflow.Id == DraftWorkflowId)))
        {
            WizardStatus = "请选择一个已导入的工作流。";
            return false;
        }
        if (WizardStep == 3)
        {
            if (string.IsNullOrWhiteSpace(DraftName) || string.IsNullOrWhiteSpace(DraftVersion) || string.IsNullOrWhiteSpace(DraftAuthorName))
            {
                WizardStatus = "名称、版本和作者为必填项。";
                return false;
            }
            if (!Uri.TryCreate(DraftSource, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(source.UserInfo))
            {
                WizardStatus = "来源必须是不含凭据的 HTTPS 地址。";
                return false;
            }
        }
        return true;
    }

    private async Task SaveWizardDraftAsync()
    {
        if (_libraryDatabase is null)
        {
            WizardStatus = "请先在设置页选择资源库。";
            return;
        }
        try
        {
            await _libraryDatabase.SaveDraftAsync(_packageDraft);
            WizardStatus = WizardStep == 2
                ? "草稿已保存。依赖分析尚未完成，不能将此草稿标记为完整。"
                : "草稿已保存；未完成的依赖与资源信息会继续保留。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            WizardStatus = $"无法保存草稿：{exception.Message}";
        }
    }

    private async Task ExportDraftAsync()
    {
        if (_libraryDatabase is null) { WizardStatus = "请先选择资源库。"; return; }
        var dialog = new SaveFileDialog { Filter = "FlowPack 草稿 (*.flowpack-draft.json)|*.flowpack-draft.json", FileName = $"{(string.IsNullOrWhiteSpace(DraftName) ? "flowpack" : DraftName)}.flowpack-draft.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var workflow = Workflows.FirstOrDefault(item => item.Id == DraftWorkflowId);
            var stored = workflow is null ? null : (await _libraryDatabase.LoadWorkflowsAsync()).FirstOrDefault(item => item.Workflow.Id == workflow.Id)?.Workflow;
            await new PackageDraftExchangeService().ExportAsync(dialog.FileName, _packageDraft, stored);
            WizardStatus = "草稿已导出；该文件不是可安装资源包，仍不包含已分析依赖或资源载荷。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            WizardStatus = $"无法导出草稿：{exception.Message}";
        }
    }

    private async Task ImportDraftAsync()
    {
        if (_libraryDatabase is null) { WizardStatus = "请先选择资源库。"; return; }
        var dialog = new OpenFileDialog
        {
            Filter = "FlowPack 草稿 (*.flowpack-draft.json)|*.flowpack-draft.json|JSON 文件 (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var exchange = await new PackageDraftExchangeService().ImportAsync(dialog.FileName);
            if (exchange.Workflow is not null)
            {
                await _libraryDatabase.SaveWorkflowAsync(exchange.Workflow, $"草稿交换文件：{Path.GetFileName(dialog.FileName)}");
            }
            await _libraryDatabase.SaveDraftAsync(exchange.Draft);
            _packageDraft = exchange.Draft;
            RaiseDraftProperties();
            await RestoreWorkflowsAsync();
            CurrentPage = FlowPage.PackageWizard;
            WizardStatus = "草稿已导入并保存到资源库；它不是可安装资源包，仍不包含已分析依赖或资源载荷。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            WizardStatus = $"无法导入草稿：{exception.Message}";
        }
    }

    private async Task ExportWorkflowPackageAsync()
    {
        if (_libraryDatabase is null) { WizardStatus = "请先选择资源库。"; return; }
        if (string.IsNullOrWhiteSpace(DraftWorkflowId))
        {
            WizardStatus = "请先选择要打包的工作流。";
            return;
        }
        try
        {
            var workflow = (await _libraryDatabase.LoadWorkflowsAsync())
                .FirstOrDefault(item => item.Workflow.Id == DraftWorkflowId)?.Workflow;
            if (workflow is null)
            {
                WizardStatus = "资源库中找不到所选工作流，无法导出。";
                return;
            }
            if (!ValidateCurrentWizardStep()) return;
            var dialog = new SaveFileDialog
            {
                Filter = "FlowPack 离线资源包 (*.cpack)|*.cpack",
                DefaultExt = ".cpack",
                FileName = $"{(string.IsNullOrWhiteSpace(DraftName) ? "flowpack" : DraftName)}.cpack"
            };
            if (dialog.ShowDialog() != true) return;
            var manifest = await new WorkflowPackageExportService().ExportAsync(dialog.FileName, _packageDraft, workflow);
            await _libraryDatabase.SaveImportedPackageAsync(manifest, dialog.FileName);
            await RestorePackagesAsync();
            WizardStatus = "工作流包已导出并登记到资源库；它明确为不完整离线包，不包含未分析的节点、模型或素材。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            WizardStatus = $"无法导出工作流包：{exception.Message}";
        }
    }

    private void UpdateDraft(PackageDraft draft)
    {
        if (Equals(_packageDraft, draft)) return;
        _packageDraft = draft;
        RaiseDraftProperties();
        WizardStatus = "草稿已修改，切换步骤前会保存到资源库。";
    }

    private void RaiseDraftProperties()
    {
        OnPropertyChanged(nameof(DraftWorkflowId));
        OnPropertyChanged(nameof(DraftName));
        OnPropertyChanged(nameof(DraftVersion));
        OnPropertyChanged(nameof(DraftDescription));
        OnPropertyChanged(nameof(DraftAuthorName));
        OnPropertyChanged(nameof(DraftAuthorUrl));
        OnPropertyChanged(nameof(DraftSource));
        OnPropertyChanged(nameof(DraftDistribution));
        OnPropertyChanged(nameof(WizardStep));
        OnPropertyChanged(nameof(WizardStepText));
    }

    private static PackageDraft CreateEmptyDraft() => new(
        Guid.NewGuid().ToString("N"),
        null,
        string.Empty,
        "0.1.0",
        string.Empty,
        "LightyearXizIl",
        "https://github.com/LightyearXizIl",
        string.Empty,
        DistributionDeclaration.OnlineOnly,
        1);

    private static string GetApplicationVersion()
    {
        var informational = typeof(ShellViewModel).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .SingleOrDefault()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3) ?? "未知版本" : informational.Split('+')[0];
    }

    private ThemeDefinition LoadInitialTheme()
    {
        try
        {
            var saved = _themeStore.LoadAsync().GetAwaiter().GetResult();
            return saved ?? ThemeDefaults.Create(ThemeBase.System);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _themeNotice = $"无法读取已保存主题，已使用默认主题：{exception.Message}";
            return ThemeDefaults.Create(ThemeBase.System);
        }
    }

    private void ToggleQuickTheme()
    {
        DraftThemeBase = _draftTheme.Base == ThemeBase.Dark ? ThemeBase.Light : ThemeBase.Dark;
        PreviewTheme();
    }

    private void PreviewTheme()
    {
        if (!TryValidateDraft()) return;
        ApplyThemeToResources(_draftTheme);
        ThemeNotice = "正在预览草稿主题；选择“应用并保存”后才会写入配置。";
    }

    private async Task ApplyThemeAsync()
    {
        if (!TryValidateDraft()) return;
        try
        {
            await _themeStore.SaveAsync(_draftTheme);
            _appliedTheme = _draftTheme;
            ApplyThemeToResources(_appliedTheme);
            ThemeNotice = "主题已应用并保存；不会重置当前页面、任务或筛选状态。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ThemeNotice = $"无法保存主题：{exception.Message}";
        }
    }

    private void CancelTheme()
    {
        _draftTheme = _appliedTheme;
        ApplyThemeToResources(_appliedTheme);
        RaiseThemePropertyChanges();
        ThemeNotice = "已取消未保存的修改，并恢复已应用主题。";
    }

    private void ResetTheme()
    {
        _draftTheme = ThemeDefaults.Create(ThemeBase.System);
        RaiseThemePropertyChanges();
        PreviewTheme();
    }

    private async Task ImportThemeAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "FlowPack 主题 (*.flowpack-theme.json)|*.flowpack-theme.json|JSON 文件 (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            _draftTheme = await _themeStore.ImportAsync(dialog.FileName);
            RaiseThemePropertyChanges();
            PreviewTheme();
            ThemeNotice = "主题文件已读取并正在预览；确认无误后请应用并保存。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ThemeNotice = $"无法导入主题：{exception.Message}";
        }
    }

    private async Task ExportThemeAsync()
    {
        if (!TryValidateDraft()) return;
        var dialog = new SaveFileDialog
        {
            Filter = "FlowPack 主题 (*.flowpack-theme.json)|*.flowpack-theme.json",
            FileName = "flowpack-theme.flowpack-theme.json",
            AddExtension = true,
            DefaultExt = ".flowpack-theme.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await _themeStore.ExportAsync(dialog.FileName, _draftTheme);
            ThemeNotice = "主题已导出；导出文件不包含资源库、任务或凭据。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ThemeNotice = $"无法导出主题：{exception.Message}";
        }
    }

    private async Task ReloadThemeAsync()
    {
        try
        {
            var theme = await _themeStore.LoadAsync();
            if (theme is null)
            {
                ThemeNotice = "尚未保存主题文件；当前预览保持不变。";
                return;
            }
            _appliedTheme = theme;
            _draftTheme = theme;
            ApplyThemeToResources(theme);
            RaiseThemePropertyChanges();
            ThemeNotice = "已从保存的主题文件重新加载。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ThemeNotice = $"无法重新加载主题；已保留当前有效主题：{exception.Message}";
        }
    }

    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
            ThemeNotice = "已请求在默认浏览器中打开 GitHub 仓库。";
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ThemeNotice = $"无法打开 GitHub 仓库：{exception.Message}";
        }
    }

    private void CopyRepositoryUrl()
    {
        try
        {
            Clipboard.SetText(RepositoryUrl);
            ThemeNotice = "GitHub 仓库链接已复制到剪贴板。";
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            ThemeNotice = $"无法访问剪贴板：{exception.Message}";
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "FlowPack 诊断摘要 (*.flowpack-diagnostics.json)|*.flowpack-diagnostics.json|JSON 文件 (*.json)|*.json",
            DefaultExt = ".flowpack-diagnostics.json",
            FileName = "flowpack-diagnostics.flowpack-diagnostics.json"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IReadOnlyList<StoredPackageManifest> packages = [];
            IReadOnlyList<StoredWorkflow> workflows = [];
            IReadOnlyList<WorkerTask> tasks = [];
            if (_libraryDatabase is not null)
            {
                packages = await _libraryDatabase.LoadImportedPackagesAsync();
                workflows = await _libraryDatabase.LoadWorkflowsAsync();
                tasks = await _libraryDatabase.LoadTasksAsync();
            }
            var snapshot = DiagnosticSnapshotExporter.Create(ApplicationVersion, _libraryDatabase is not null, packages.Count, workflows.Count, tasks);
            await DiagnosticSnapshotExporter.ExportAsync(dialog.FileName, snapshot);
            ThemeNotice = "诊断摘要已导出。内容不包含路径、资源 URL、凭据、工作流原文或错误详情。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ThemeNotice = $"无法导出诊断摘要：{exception.Message}";
        }
    }

    private bool TryValidateDraft()
    {
        var validation = ThemeValidator.Validate(_draftTheme);
        if (validation.IsValid) return true;
        ThemeNotice = string.Join(" ", validation.Errors);
        return false;
    }

    private void UpdateDraft(ThemeDefinition theme)
    {
        if (Equals(_draftTheme, theme)) return;
        _draftTheme = theme;
        RaiseThemePropertyChanges();
        ThemeNotice = "主题草稿已修改；可先预览，再应用并保存。";
    }

    private void RaiseThemePropertyChanges()
    {
        OnPropertyChanged(nameof(ThemeName));
        OnPropertyChanged(nameof(DraftThemeBase));
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(SurfaceColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(BodyFontSize));
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(Density));
        OnPropertyChanged(nameof(EnableAnimations));
    }

    private void ApplyThemeToResources(ThemeDefinition theme)
    {
        if (Application.Current is null) return;
        var resolvedTheme = theme.Base == ThemeBase.System
            ? theme with { Base = GetSystemThemeBase() }
            : theme;
        var resources = Application.Current.Resources;
        var dark = resolvedTheme.Base == ThemeBase.Dark;
        resources["Color.Window"] = ReadColor(resolvedTheme.SurfaceColor);
        resources["Color.Surface"] = ReadColor(dark ? "#202E46" : "#FFFFFF");
        resources["Color.Text"] = ReadColor(resolvedTheme.TextColor);
        resources["Color.Muted"] = ReadColor(dark ? "#B2C0D4" : "#65748A");
        resources["Color.Border"] = ReadColor(dark ? "#32435C" : "#DCE4ED");
        resources["Color.Accent"] = ReadColor(resolvedTheme.AccentColor);
        resources["Color.AccentSoft"] = ReadColor(dark ? "#213A62" : "#EAF1FF");
        resources["Color.SuccessSoft"] = ReadColor(dark ? "#173B34" : "#EAF8F1");
        resources["Color.WarningSoft"] = ReadColor(dark ? "#44351B" : "#FFF4DE");
        resources["BodyFontSize"] = (double)resolvedTheme.BodyFontSize;
        resources["SubtleTextFontSize"] = Math.Max(12d, resolvedTheme.BodyFontSize - 1);
        resources["SectionTitleFontSize"] = resolvedTheme.BodyFontSize + 3d;
        resources["PageTitleFontSize"] = resolvedTheme.BodyFontSize + 16d;
        resources["CardCornerRadius"] = new CornerRadius(resolvedTheme.CornerRadius);
        resources["ControlCornerRadius"] = new CornerRadius(Math.Min(8, resolvedTheme.CornerRadius));
    }

    private static ThemeBase GetSystemThemeBase()
    {
        try
        {
            var setting = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return setting is int value && value == 0 ? ThemeBase.Dark : ThemeBase.Light;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return ThemeBase.Light;
        }
    }

    private async Task ImportPackageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "FlowPack 资源包 (*.cpack;*.cpack.json)|*.cpack;*.cpack.json|JSON 清单 (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        await ImportPackageFromSourceAsync(dialog.FileName);
    }

    private async Task ImportPackageFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 manifest.json 的已展开 FlowPack 离线资源包目录。不会解压、复制或部署任何文件。"
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;

        await ImportPackageFromSourceAsync(dialog.FolderName);
    }

    private async Task ImportPackageFromSourceAsync(string source)
    {
        if (_libraryDatabase is null)
        {
            MessageBox.Show("请先在设置页选择资源库。资源包不会只保存在当前会话中。", "需要资源库", MessageBoxButton.OK, MessageBoxImage.Information);
            CurrentPage = FlowPage.Appearance;
            return;
        }

        try
        {
            var imported = await new PackageImportReader().ReadAsync(source);
            await _libraryDatabase.SaveImportedPackageAsync(imported.Manifest, imported.Source);
            foreach (var workflow in imported.Workflows)
            {
                await _libraryDatabase.SaveWorkflowAsync(workflow, $"资源包：{imported.Source}");
            }
            await RestorePackagesAsync();
            CurrentPage = FlowPage.Packages;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(exception.Message, "无法导入资源包", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ImportWorkflowAsync()
    {
        if (_libraryDatabase is null)
        {
            MessageBox.Show("请先在设置页选择资源库。工作流不会只保存在当前会话中。", "需要资源库", MessageBoxButton.OK, MessageBoxImage.Information);
            CurrentPage = FlowPage.Appearance;
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "ComfyUI 工作流 (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var workflow = await new WorkflowReader().ReadAsync(dialog.FileName);
            await _libraryDatabase.SaveWorkflowAsync(workflow, dialog.FileName);
            await RestoreWorkflowsAsync();
            CurrentPage = FlowPage.Workflows;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(exception.Message, "无法导入工作流", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static System.Windows.Media.Color ReadColor(string value) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value.Length == 7 ? $"#FF{value[1..]}" : value);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
