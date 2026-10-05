using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using FlowPack.Core;

namespace FlowPack.App;

public enum SecondaryPanelKind { None, Instance, Status }
public sealed record InstanceDetailField(string Label, string Value);
public sealed record InstanceDirectoryRow(string InstanceId, string Label, string Path);
public sealed record StatusDetailGroup(string Title, IReadOnlyList<string> Lines);

public sealed partial class ShellViewModel
{
    private SecondaryPanelKind _secondaryPanel;
    private int _instanceDetailTab;
    private string _secondaryPanelNotice = "";
    public SecondaryPanelKind SecondaryPanel => _secondaryPanel;
    public bool IsSecondaryPanelOpen => _secondaryPanel != SecondaryPanelKind.None;
    public bool IsInstanceDetails => _secondaryPanel == SecondaryPanelKind.Instance;
    public bool IsStatusDetails => _secondaryPanel == SecondaryPanelKind.Status;
    public string SecondaryPanelTitle => IsInstanceDetails ? "实例详情" : "状态详情";
    public string SecondaryPanelSubtitle => SelectedInstance?.DisplayName ?? "尚未选择实例";
    public string SecondaryPanelNotice => _secondaryPanelNotice;
    public int InstanceDetailTabIndex
    {
        get => _instanceDetailTab;
        set { if (value is < 0 or > 1 || value == _instanceDetailTab) return; _instanceDetailTab = value; OnPropertyChanged(); }
    }
    public IReadOnlyList<InstanceDetailField> InstanceVersionFields =>
    [
        new("Desktop 版本", HomeHasInventory ? _inventory!.DesktopVersion ?? "未确认" : "未确认"),
        new("磁盘 Core 版本", HomeHasInventory ? _inventory!.CoreVersion ?? "未确认" : "未确认"),
        new("运行 Core 版本", HomeHasInventory ? _inventory!.RunningCoreVersion ?? "尚未核对" : "尚未核对"),
        new("目录布局", SelectedInstance?.DesktopLayout switch
        { "standalone-native" => "原生目录", "standalone-adopted" => "接管已有目录", _ => "未确认" })
    ];
    public IReadOnlyList<InstanceDirectoryRow> InstanceDirectoryRows
    {
        get
        {
            if (SelectedInstance is not { } instance) return [];
            var rows = new List<InstanceDirectoryRow>();
            foreach (var kind in new[] { ResourceKind.Workflow, ResourceKind.Model, ResourceKind.CustomNode })
                rows.AddRange(GetResourceFolders(kind).Select(location => new InstanceDirectoryRow(instance.Id, location.Label, location.Path)));
            Add("核心目录", instance.CoreDirectory);
            Add("实例数据目录", instance.DataDirectory);
            Add("输入目录", instance.InputDirectory);
            Add("Python 程序目录", FileDirectory(instance.PythonPath));
            Add("Desktop 程序目录", FileDirectory(instance.DesktopExecutable));
            return rows;
            void Add(string label, string? path)
            { if (!string.IsNullOrWhiteSpace(path)) rows.Add(new(instance.Id, label, path)); }
        }
    }
    public IReadOnlyList<StatusDetailGroup> HomeStatusDetailGroups
    {
        get
        {
            var groups = new List<StatusDetailGroup>();
            if (!HomeShowsScan && CoreNotice != "等待扫描 Desktop 实例。" && !string.IsNullOrWhiteSpace(CoreNotice))
                groups.Add(new("最近操作", CoreNotice.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            if (HomeHasInventory)
                groups.AddRange(_inventory!.Issues.Select(SplitIssue).GroupBy(x => x.Title)
                    .Select(group => new StatusDetailGroup(group.Key, group.Select(x => x.Line).ToArray())));
            return groups;
        }
    }
    private ICommand? _openInstanceDetails, _openStatusDetails, _closeSecondaryPanel, _copySecondaryText, _openInstanceDirectory;
    public ICommand OpenInstanceDetailsCommand => _openInstanceDetails ??= new RelayCommand(value =>
    {
        InstanceDetailTabIndex = Equals(value, "Directories") ? 1 : 0;
        ShowSecondaryPanel(SecondaryPanelKind.Instance);
    }, _ => SelectedInstance is not null);
    public ICommand OpenStatusDetailsCommand => _openStatusDetails ??= new RelayCommand(_ => ShowSecondaryPanel(SecondaryPanelKind.Status), _ => HomeHasStatusDetails);
    public ICommand CloseSecondaryPanelCommand => _closeSecondaryPanel ??= new RelayCommand(_ => CloseSecondaryPanel(), _ => IsSecondaryPanelOpen);
    public ICommand CopySecondaryTextCommand => _copySecondaryText ??= new RelayCommand(value =>
    {
        try { Clipboard.SetText(value is string text ? text : HomeStatusDetails); SetSecondaryNotice("已复制。"); }
        catch (COMException) { SetSecondaryNotice("剪贴板暂时不可用，请稍后重试。"); }
    });
    public ICommand OpenInstanceDirectoryCommand => _openInstanceDirectory ??= new RelayCommand(value =>
    {
        if (value is not InstanceDirectoryRow row || !IsCurrentDirectory(row))
        { SetSecondaryNotice("实例已变化，请重新选择目录。"); return; }
        if (!Directory.Exists(row.Path)) { SetSecondaryNotice("目录不存在或无法访问，请核对路径。"); return; }
        try { Process.Start(new ProcessStartInfo(row.Path) { UseShellExecute = true }); SetSecondaryNotice(""); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { SetSecondaryNotice("无法打开目录：" + ex.Message); }
    }, value => value is InstanceDirectoryRow row && IsCurrentDirectory(row));
    private bool IsCurrentDirectory(InstanceDirectoryRow row) => row.InstanceId == SelectedInstance?.Id &&
        InstanceDirectoryRows.Any(current => string.Equals(current.Path, row.Path, StringComparison.OrdinalIgnoreCase));
    private static string? FileDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        return Path.GetDirectoryName(path);
    }
    private static (string Title, string Line) SplitIssue(string issue)
    {
        var separator = issue.IndexOf('：');
        if (separator is < 2 or > 40) return ("其他扫描提示", issue);
        var title = issue[..separator];
        if (title == "节点包需要运行实例信息补充类型") title = "节点类型待核验";
        return (title, issue[(separator + 1)..].Trim());
    }
    private void ShowSecondaryPanel(SecondaryPanelKind panel)
    {
        _secondaryPanel = panel; _secondaryPanelNotice = "";
        NotifySecondaryContent();
        foreach (var name in new[] { nameof(SecondaryPanel), nameof(IsSecondaryPanelOpen), nameof(IsInstanceDetails), nameof(IsStatusDetails) }) OnPropertyChanged(name);
    }
    private void CloseSecondaryPanel() { if (IsSecondaryPanelOpen) ShowSecondaryPanel(SecondaryPanelKind.None); }
    private void SetSecondaryNotice(string notice) { _secondaryPanelNotice = notice; OnPropertyChanged(nameof(SecondaryPanelNotice)); }
    private void NotifySecondaryContent()
    {
        foreach (var name in new[] { nameof(SecondaryPanelTitle), nameof(SecondaryPanelSubtitle), nameof(SecondaryPanelNotice), nameof(InstanceVersionFields), nameof(InstanceDirectoryRows), nameof(HomeStatusDetailGroups) }) OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}
