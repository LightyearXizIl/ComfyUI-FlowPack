using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class SecondaryDetailsTests
{
    [Fact]
    public void Details_are_a_separate_layer_and_return_keeps_resource_selection()
    {
        var vm = new ShellViewModel();
        HomeStatusTests.Set(vm, "_selectedInstance", HomeStatusTests.Instance());
        vm.LocalModels.Add(new(new("chosen", ResourceKind.Model, "模型", @"E:\fixture\model.pth", "model.pth")) { IsSelected = true });
        vm.OpenInstanceDetailsCommand.Execute("Directories");
        Assert.True(vm.IsInstanceDetails); Assert.Equal(1, vm.InstanceDetailTabIndex);
        Assert.Equal(FlowPage.Home, vm.CurrentPage);
        vm.CloseSecondaryPanelCommand.Execute(null);
        Assert.False(vm.IsSecondaryPanelOpen); Assert.True(vm.LocalModels[0].IsSelected);
        vm.OpenInstanceDetailsCommand.Execute("Versions");
        Assert.Equal(0, vm.InstanceDetailTabIndex);
        vm.NavigateCommand.Execute("Library");
        Assert.False(vm.IsSecondaryPanelOpen); Assert.True(vm.LocalModels[0].IsSelected);
    }

    [Fact]
    public void Directory_action_rejects_a_previous_instance_before_opening_anything()
    {
        var vm = new ShellViewModel();
        HomeStatusTests.Set(vm, "_selectedInstance", HomeStatusTests.Instance());
        var row = vm.InstanceDirectoryRows.First();
        Assert.True(vm.OpenInstanceDirectoryCommand.CanExecute(row));
        HomeStatusTests.Set(vm, "_selectedInstance", HomeStatusTests.Instance("other"));
        Assert.False(vm.OpenInstanceDirectoryCommand.CanExecute(row));
        vm.OpenInstanceDirectoryCommand.Execute(row);
        Assert.Contains("实例已变化", vm.SecondaryPanelNotice);
    }

    [Fact]
    public void Status_groups_preserve_every_hint_without_repeating_the_same_heading()
    {
        var vm = new ShellViewModel();
        var instance = HomeStatusTests.Instance();
        var hints = Enumerable.Range(1, 20).Select(i => $"节点包需要运行实例信息补充类型：节点-{i}").ToArray();
        HomeStatusTests.Set(vm, "_selectedInstance", instance);
        HomeStatusTests.Set(vm, "_inventory", new ResourceInventory(instance, [], [], hints));
        HomeStatusTests.Set(vm, "_coreNotice", "扫描完成");
        HomeStatusTests.Set(vm, "_lastHomeScanNotice", "扫描完成");
        var group = Assert.Single(vm.HomeStatusDetailGroups);
        Assert.Equal("节点类型待核验", group.Title);
        Assert.Equal(20, group.Lines.Count); Assert.Contains("节点-20", group.Lines);
        Assert.All(hints, hint => Assert.Contains(hint, vm.HomeStatusDetails));
        vm.OpenStatusDetailsCommand.Execute(null);
        Assert.True(vm.IsStatusDetails);
    }

    [Fact]
    public void Version_fields_never_reuse_another_instances_inventory()
    {
        var vm = new ShellViewModel();
        var previous = HomeStatusTests.Instance();
        HomeStatusTests.Set(vm, "_inventory", new ResourceInventory(previous, [], [], []) { CoreVersion = "0.38.0", DesktopVersion = "1.1.4.0" });
        HomeStatusTests.Set(vm, "_selectedInstance", HomeStatusTests.Instance("current"));
        Assert.Equal("未确认", vm.InstanceVersionFields[0].Value);
        Assert.Equal("未确认", vm.InstanceVersionFields[1].Value);
        Assert.All(vm.InstanceDirectoryRows, row => Assert.Equal("current", row.InstanceId));
    }
}
