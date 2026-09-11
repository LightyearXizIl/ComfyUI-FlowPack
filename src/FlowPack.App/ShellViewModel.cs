using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
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
    IReadOnlyList<ResourceEntry> Resources);

public sealed record TaskRow(string Name, string Stage, int? Progress, string Detail, bool IsCurrent);

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private FlowPage _currentPage = FlowPage.Home;
    private PackageRow? _selectedPackage;
    private bool _isDark;

    public ShellViewModel()
    {
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (Enum.TryParse<FlowPage>(parameter?.ToString(), out var page))
            {
                CurrentPage = page;
            }
        });
        OpenPackageCommand = new RelayCommand(OpenPackage, parameter => parameter is PackageRow);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        StartInstallCommand = new RelayCommand(_ => { }, _ => false);
        ImportPackageCommand = new RelayCommand(_ => _ = ImportPackageAsync());

        Packages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPackages));
        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTasks));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand NavigateCommand { get; }
    public ICommand OpenPackageCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand StartInstallCommand { get; }
    public ICommand ImportPackageCommand { get; }

    public ObservableCollection<PackageRow> Packages { get; } = [];
    public ObservableCollection<TaskRow> Tasks { get; } = [];

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
        }
    }

    public string StatusNotice => "●  尚未检查环境";
    public bool HasPackages => Packages.Count > 0;
    public bool HasTasks => Tasks.Count > 0;
    public bool HasSelectedPackage => SelectedPackage is not null;

    public bool IsHomeContext => CurrentPage == FlowPage.Home;
    public bool IsPackagesContext => CurrentPage is FlowPage.Packages or FlowPage.InstallPreview or FlowPage.Models or FlowPage.Nodes;
    public bool IsWorkflowsContext => CurrentPage is FlowPage.Workflows or FlowPage.PackageWizard;
    public bool IsTasksContext => CurrentPage == FlowPage.Tasks;

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

    private void ToggleTheme()
    {
        _isDark = !_isDark;
        var resources = Application.Current.Resources;
        resources["Color.Window"] = ReadColor(_isDark ? "#FF111A2A" : "#FFF9F7F2");
        resources["Color.Surface"] = ReadColor(_isDark ? "#FF19253A" : "#FFFFFFFF");
        resources["Color.Text"] = ReadColor(_isDark ? "#FFF1F6FF" : "#FF12233D");
        resources["Color.Muted"] = ReadColor(_isDark ? "#FFB2C0D4" : "#FF65748A");
        resources["Color.Border"] = ReadColor(_isDark ? "#FF32435C" : "#FFDCE4ED");
        resources["Color.AccentSoft"] = ReadColor(_isDark ? "#FF213A62" : "#FFEAF1FF");
        resources["Color.SuccessSoft"] = ReadColor(_isDark ? "#FF173B34" : "#FFEAF8F1");
        resources["Color.WarningSoft"] = ReadColor(_isDark ? "#FF44351B" : "#FFFFF4DE");
    }

    private async Task ImportPackageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "FlowPack 在线清单 (*.cpack.json)|*.cpack.json|JSON 文件 (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var manifest = await new PackageManifestReader().ReadAsync(dialog.FileName);
            var existing = Packages.FirstOrDefault(package => package.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) Packages.Remove(existing);
            Packages.Insert(0, new PackageRow(
                manifest.Id,
                manifest.Name,
                manifest.Version,
                $"当前会话已读取 {manifest.Resources.Count} 项资源",
                "等待环境检查",
                manifest.Resources));
            CurrentPage = FlowPage.Packages;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(exception.Message, "无法导入资源包", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static System.Windows.Media.Color ReadColor(string value) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);

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
