using System.Reflection;
using FlowPack.App;
using FlowPack.ComfyUI;

namespace FlowPack.Tests;

public sealed class DiscoveryPresentationTests
{
    [Fact]
    public void Unscanned_discovery_does_not_offer_empty_diagnostics()
    {
        var vm = new ShellViewModel();
        Assert.False(vm.HasDiscoveryReport);
        Assert.False(vm.OpenDiscoveryDetailsCommand.CanExecute(null));
    }

    [Fact]
    public void Empty_discovery_keeps_configuration_root_and_every_skip_or_error_visible()
    {
        var vm = new ShellViewModel();
        var report = new InstanceDiscoveryReport([], @"C:\fixture\different-user\Comfy Desktop",
            [@"C:\fixture\different-user\Comfy Desktop\installations.json", @"D:\Comfy-Desktop\ComfyUI-Installs"],
            ["cloud-instance：云端实例，无法读取本机资源。", "relative-instance：安装路径不是绝对路径。"],
            ["settings.json：JSON 格式无效。", "实例目录：访问被拒绝。"]);
        SetReport(vm, report);

        Assert.True(vm.HasDiscoveryReport);
        Assert.Equal(report.ConfigurationRoot, vm.DiscoveryConfigurationRoot);
        Assert.Contains(report.ConfigurationRoot, vm.DiscoveryDetails);
        Assert.All(report.CheckedSources.Concat(report.SkippedSources).Concat(report.Errors), line => Assert.Contains(line, vm.DiscoveryDetails));
        Assert.True(vm.OpenDiscoveryDetailsCommand.CanExecute(null));
        vm.OpenDiscoveryDetailsCommand.Execute(null);
        Assert.True(vm.IsSecondaryPanelOpen);
        Assert.True(vm.IsStatusDetails);
        var detailLines = vm.HomeStatusDetailGroups.SelectMany(group => group.Lines).ToArray();
        Assert.All(report.CheckedSources.Concat(report.SkippedSources).Concat(report.Errors), line => Assert.Contains(line, detailLines));
    }

    [Fact]
    public void Discovery_details_include_instance_problems_even_when_an_instance_was_found()
    {
        var vm = new ShellViewModel();
        var instance = HomeStatusTests.Instance() with { Issues = ["实例标识文件不存在。", "Python 程序不存在。"] };
        SetReport(vm, new([instance], @"C:\fixture\profile", [@"C:\fixture\profile\installations.json"], [], []));

        Assert.True(vm.HasDiscoveryReport);
        Assert.All(instance.Issues, issue => Assert.Contains(issue, vm.DiscoveryDetails));
        Assert.True(vm.OpenDiscoveryDetailsCommand.CanExecute(null));
    }

    private static void SetReport(ShellViewModel vm, InstanceDiscoveryReport report) =>
        typeof(ShellViewModel).GetField("_discoveryReport", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, report);
}
