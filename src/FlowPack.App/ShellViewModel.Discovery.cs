using System.Windows.Input;
using FlowPack.ComfyUI;
using FlowPack.Infrastructure;

namespace FlowPack.App;

public sealed partial class ShellViewModel
{
    private InstanceDiscoveryReport? _discoveryReport;
    private bool _refreshingInstanceList;
    public bool HasDiscoveryReport => _discoveryReport is not null;
    public string DiscoveryConfigurationRoot => _discoveryReport?.ConfigurationRoot ?? "尚未检测";
    public string DiscoverySummary => _discoveryReport is not { } report ? "" :
        report.Instances.Count == 0 ? "未发现实例，打开检测详情核对配置目录或关联资源目录。" :
        $"检测到 {report.Instances.Count} 个实例" + (report.Errors.Count > 0 ? $"，{report.Errors.Count} 项读取问题。" : "。");
    public string DiscoveryDetails => string.Join("\n\n", DiscoveryDetailGroups.Select(group =>
        group.Title + "：\n" + string.Join("\n", group.Lines)));
    public IReadOnlyList<StatusDetailGroup> DiscoveryDetailGroups
    {
        get
        {
            if (_discoveryReport is not { } report) return [];
            var groups = new List<StatusDetailGroup>
            {
                new("配置目录", [report.ConfigurationRoot]),
                new("检测结果", [DiscoverySummary])
            };
            if (report.CheckedSources.Count > 0) groups.Add(new("已检查来源", report.CheckedSources));
            if (report.SkippedSources.Count > 0) groups.Add(new("已跳过", report.SkippedSources));
            if (report.Errors.Count > 0) groups.Add(new("读取问题", report.Errors));
            foreach (var instance in report.Instances.Where(instance => instance.Issues.Count > 0))
                groups.Add(new(instance.Name, instance.Issues));
            groups.Add(new("接下来可以", [
                "确认 FlowPack 与 Comfy Desktop 使用同一 Windows 用户。",
                "普通 ComfyUI 目录可用“关联目录”读取和导出。",
                "自定义 Desktop 配置可用 --desktop-profile \"包含 installations.json 的绝对目录\" 启动 FlowPack。"
            ]));
            return groups;
        }
    }
    private ICommand? _openDiscoveryDetails;
    public ICommand OpenDiscoveryDetailsCommand => _openDiscoveryDetails ??= new RelayCommand(
        _ => ShowSecondaryPanel(SecondaryPanelKind.Discovery), _ => HasDiscoveryReport);

    private void SetDiscoveryReport(InstanceDiscoveryReport report)
    {
        _discoveryReport = report;
        foreach (var name in new[] { nameof(HasDiscoveryReport), nameof(DiscoveryConfigurationRoot), nameof(DiscoverySummary),
            nameof(DiscoveryDetails), nameof(DiscoveryDetailGroups) }) OnPropertyChanged(name);
        _applicationLog.Write(LogSeverity.Information, "instance.discovery.completed",
            $"instances={report.Instances.Count}; skipped={report.SkippedSources.Count}; errors={report.Errors.Count}");
        NotifySecondaryContent();
        CommandManager.InvalidateRequerySuggested();
    }
}
