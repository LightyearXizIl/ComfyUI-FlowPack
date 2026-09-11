using FlowPack.App;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void Startup_uses_unchecked_empty_state()
    {
        var viewModel = new ShellViewModel();

        Assert.Equal(FlowPage.Home, viewModel.CurrentPage);
        Assert.Equal("●  尚未检查环境", viewModel.StatusNotice);
        Assert.Empty(viewModel.Packages);
        Assert.Empty(viewModel.Tasks);
        Assert.False(viewModel.StartInstallCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(FlowPage.Home, true, false, false, false)]
    [InlineData(FlowPage.Packages, false, true, false, false)]
    [InlineData(FlowPage.InstallPreview, false, true, false, false)]
    [InlineData(FlowPage.Models, false, true, false, false)]
    [InlineData(FlowPage.Nodes, false, true, false, false)]
    [InlineData(FlowPage.Workflows, false, false, true, false)]
    [InlineData(FlowPage.PackageWizard, false, false, true, false)]
    [InlineData(FlowPage.Tasks, false, false, false, true)]
    [InlineData(FlowPage.Appearance, false, false, false, false)]
    public void Navigation_reports_the_correct_top_level_context(
        FlowPage page,
        bool home,
        bool packages,
        bool workflows,
        bool tasks)
    {
        var viewModel = new ShellViewModel { CurrentPage = page };

        Assert.Equal(home, viewModel.IsHomeContext);
        Assert.Equal(packages, viewModel.IsPackagesContext);
        Assert.Equal(workflows, viewModel.IsWorkflowsContext);
        Assert.Equal(tasks, viewModel.IsTasksContext);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.PageTitle));
    }

    [Fact]
    public void Package_details_follow_the_selected_package()
    {
        var viewModel = new ShellViewModel();
        var first = new PackageRow("first", "第一包", "1.0", "一项资源", "等待环境检查", [
            new ResourceEntry("model", "first.safetensors", ResourceKind.Model, 1024, null, null)]);
        var second = new PackageRow("second", "第二包", "2.0", "两项资源", "等待环境检查", [
            new ResourceEntry("node", "node", ResourceKind.CustomNode, 2048, null, null),
            new ResourceEntry("workflow", "workflow.json", ResourceKind.Workflow, 512, null, null)]);

        viewModel.OpenPackageCommand.Execute(first);
        Assert.Equal("第一包 · 1.0", viewModel.SelectedPackageSubtitle);
        Assert.Contains("1 项资源", viewModel.SelectedPackageResourceSummary);

        viewModel.OpenPackageCommand.Execute(second);
        Assert.Equal("第二包 · 2.0", viewModel.SelectedPackageSubtitle);
        Assert.Contains("2 项资源", viewModel.SelectedPackageResourceSummary);
    }
}
