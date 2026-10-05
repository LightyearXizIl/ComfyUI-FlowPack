using System.Reflection;
using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class HomeStatusTests
{
    internal static InstanceDescriptor Instance(string id = "home-fixture") => new(id,
        "界面测试实例 · 很长的名称用于检查首页布局，不是实际连接结果", "desktop-2", null,
        "E:\\fixture", "E:\\fixture\\core", "E:\\fixture\\data", "E:\\fixture\\user",
        "E:\\fixture\\workflows", "E:\\fixture\\nodes", null, [], "E:\\fixture\\models", [], "fixture", []);

    internal static void Set(ShellViewModel vm, string name, object? value) =>
        typeof(ShellViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    [Fact]
    public void Empty_state_does_not_present_unscanned_resources_as_zero()
    {
        var vm = new ShellViewModel();
        vm.LocalModels.Add(new(new("old", ResourceKind.Model, "旧实例模型", "old", "old")));
        Assert.Equal("尚未选择实例", vm.HomeStatusTitle);
        Assert.Equal("—", vm.HomeModelCount);
        Assert.False(vm.HomeHasStatusDetails);
    }

    [Fact]
    public void Scan_shows_counts_and_preserves_every_hint_in_disclosure()
    {
        var vm = new ShellViewModel();
        var instance = Instance();
        var hints = Enumerable.Range(1, 20).Select(i => $"节点包需要运行实例信息补充类型：测试节点-{i}").ToArray();
        Set(vm, "_selectedInstance", instance);
        Set(vm, "_inventory", new ResourceInventory(instance, [], [], hints));
        Set(vm, "_lastHomeScanNotice", "扫描原始内容");
        Set(vm, "_coreNotice", "扫描原始内容");
        vm.LocalModels.Add(new(new("model", ResourceKind.Model, "测试模型", "model", "model")));
        Assert.Equal("资源扫描完成", vm.HomeStatusTitle);
        Assert.Equal("1", vm.HomeModelCount);
        Assert.Equal("扫描提示 · 20 项", vm.HomeScanHint);
        Assert.DoesNotContain("测试节点", vm.HomeStatusSummary);
        Assert.All(hints, hint => Assert.Contains(hint, vm.HomeStatusDetails));
        Assert.DoesNotContain("未加载", vm.HomeStatusSummary);
        Set(vm, "_selectedInstance", Instance("another-instance"));
        Assert.Equal("—", vm.HomeModelCount);
        Assert.DoesNotContain("测试节点", vm.HomeStatusDetails);
    }

    [Fact]
    public void Progress_uses_a_readable_operation_and_error_keeps_full_evidence()
    {
        var vm = new ShellViewModel();
        Set(vm, "_coreBusy", true);
        Set(vm, "_homeOperation", "inventory.scan");
        Set(vm, "_coreNotice", "inventory.scan：running");
        Assert.Equal("正在扫描资源", vm.HomeStatusTitle);
        Assert.DoesNotContain("inventory.scan", vm.HomeStatusSummary);
        Set(vm, "_coreBusy", false);
        Set(vm, "_homeOperationFailed", true);
        Set(vm, "_coreNotice", "无法读取资源目录。\n详细错误及完整路径。\n请检查访问权限。");
        Assert.Equal("操作未完成", vm.HomeStatusTitle);
        Assert.Equal("无法读取资源目录。", vm.HomeStatusSummary);
        Assert.Contains("请检查访问权限。", vm.HomeStatusDetails);
    }

    [Fact]
    public void Export_keeps_the_destination_in_details_without_stretching_summary()
    {
        var vm = new ShellViewModel();
        Set(vm, "_selectedInstance", Instance());
        var notice = "ZIP 已导出：E:\\" + new string('a', 300) + "\\资源包.zip";
        Set(vm, "_coreNotice", notice);
        Assert.Equal("资源包已导出，保存位置见详情。", vm.HomeStatusSummary);
        Assert.Equal(notice, vm.HomeStatusDetails);
    }
}
