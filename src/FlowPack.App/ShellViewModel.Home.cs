using System.Globalization;

namespace FlowPack.App;

public sealed partial class ShellViewModel
{
    private string? _lastHomeScanNotice;
    private string? _homeOperation;
    private bool _homeOperationFailed;

    private bool HomeHasInventory => _inventory is not null && _inventory.Instance.Id == SelectedInstance?.Id;
    private bool HomeShowsScan => HomeHasInventory && CoreNotice == _lastHomeScanNotice;
    public string HomeStatusTitle => _updating ? "正在更新软件" : _homeOperationFailed ? "操作未完成" : !CoreReady ? HomeOperationLabel(_homeOperation) :
        SelectedInstance is null ? "尚未选择实例" : HomeShowsScan ? "资源扫描完成" : "最近操作";
    public string HomeStatusSummary => _homeOperationFailed ? HomeNoticeSummary(CoreNotice) : !CoreReady ? "正在处理，请稍候。" :
        SelectedInstance is null ? "在顶部选择实例，或点击“关联目录”。" : HomeShowsScan ?
        (_inventory!.Issues.Count > 0 ? "扫描发现需要核对的信息，原因见详情。" : "可以管理或导出所选实例的资源。") : HomeNoticeSummary(CoreNotice);
    public string HomeWorkflowCount => HomeResourceCount(LocalWorkflows.Count);
    public string HomeModelCount => HomeResourceCount(LocalModels.Count);
    public string HomeNodeCount => HomeResourceCount(LocalNodes.Count);
    public string HomeScanHint => HomeHasInventory ? (_inventory!.Issues.Count > 0 ? $"扫描提示 · {_inventory.Issues.Count} 项" : "本次扫描没有提示。") : "扫描后显示资源数量。";
    public string HomeStatusDetails
    {
        get
        {
            var lines = new List<string>();
            if (!HomeShowsScan && CoreNotice != "等待扫描 Desktop 实例。") lines.Add(CoreNotice);
            if (HomeHasInventory && _inventory!.Issues.Count > 0)
            {
                lines.Add("扫描提示：");
                lines.AddRange(_inventory.Issues);
            }
            return string.Join("\n", lines);
        }
    }
    public bool HomeHasStatusDetails => !string.IsNullOrWhiteSpace(HomeStatusDetails);

    private string HomeResourceCount(int count) => HomeHasInventory ? count.ToString("N0", CultureInfo.CurrentCulture) : "—";
    private static string HomeOperationLabel(string? operation) => operation switch
    {
        "instance.discover" => "正在检测实例",
        "instance.associate" => "正在关联目录",
        "inventory.scan" => "正在扫描资源",
        "resource.import" => "正在检查导入内容",
        "export.plan" => "正在准备导出",
        "export.execute" => "正在导出资源包",
        "install.plan" => "正在检查安装计划",
        "install.execute" => "正在安装资源",
        "task.download" => "正在下载资源",
        _ => "正在处理"
    };
    private static string HomeNoticeSummary(string notice)
    {
        if (notice.StartsWith("ZIP 已导出：", StringComparison.Ordinal)) return "资源包已导出，保存位置见详情。";
        if (notice.StartsWith("后台任务状态暂时无法刷新", StringComparison.Ordinal)) return "资源服务暂时无法连接，请稍后重新扫描。";
        if (notice == "后台任务连接已恢复。") return "资源服务连接已恢复。";
        var firstLine = notice.Split('\n', 2)[0].Trim();
        var separator = firstLine.IndexOf('：');
        if (separator > 0 && firstLine[..separator] is "instance.discover" or "instance.associate" or "inventory.scan" or "resource.import" or "export.plan" or "export.execute" or "install.plan" or "install.execute" or "task.download")
            return "处理信息已更新，内容见详情。";
        return firstLine;
    }
    private void NotifyHomeStatusChanged()
    {
        foreach (var name in new[] { nameof(HomeStatusTitle), nameof(HomeStatusSummary), nameof(HomeWorkflowCount), nameof(HomeModelCount),
            nameof(HomeNodeCount), nameof(HomeScanHint), nameof(HomeStatusDetails), nameof(HomeHasStatusDetails) })
            OnPropertyChanged(name);
        NotifySecondaryContent();
    }
}
