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
    [InlineData(FlowPage.Home, true, false, false, false, false)]
    [InlineData(FlowPage.Library, false, true, false, false, false)]
    [InlineData(FlowPage.Packaging, false, false, true, false, false)]
    [InlineData(FlowPage.Install, false, false, false, true, false)]
    [InlineData(FlowPage.Tasks, false, false, false, false, true)]
    [InlineData(FlowPage.Settings, false, false, false, false, false)]
    public void Navigation_reports_the_correct_top_level_context(
        FlowPage page,
        bool home,
        bool library,
        bool packaging,
        bool install,
        bool tasks)
    {
        var viewModel = new ShellViewModel { CurrentPage = page };

        Assert.Equal(home, viewModel.IsHomeContext);
        Assert.Equal(library, viewModel.IsLibraryContext);
        Assert.Equal(packaging, viewModel.IsPackagingContext);
        Assert.Equal(install, viewModel.IsInstallContext);
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
        Assert.False(viewModel.HasInstallPreview);
        Assert.Contains("候选 ComfyUI", viewModel.InstallPreviewStatus);

        viewModel.OpenPackageCommand.Execute(second);
        Assert.Equal("第二包 · 2.0", viewModel.SelectedPackageSubtitle);
        Assert.Contains("2 项资源", viewModel.SelectedPackageResourceSummary);
    }

    [Fact]
    public void Settings_exposes_the_author_and_official_repository()
    {
        var viewModel = new ShellViewModel();

        Assert.Equal("LightyearXizIl", viewModel.AuthorName);
        Assert.Equal("https://github.com/LightyearXizIl/ComfyUI-FlowPack", viewModel.RepositoryUrl);
        Assert.Equal("0.1.0", viewModel.ApplicationVersion);
        Assert.True(viewModel.OpenRepositoryCommand.CanExecute(null));
        Assert.True(viewModel.CopyRepositoryUrlCommand.CanExecute(null));
        Assert.True(viewModel.ExportDiagnosticsCommand.CanExecute(null));
        Assert.True(viewModel.SelectResourceLibraryCommand.CanExecute(null));
        Assert.True(viewModel.SelectComfyUiCommand.CanExecute(null));
        Assert.True(viewModel.ImportPackageFolderCommand.CanExecute(null));
        Assert.True(viewModel.RefreshTasksCommand.CanExecute(null));
        Assert.True(viewModel.DownloadPackageCommand.CanExecute(null));
        Assert.True(viewModel.ImportDraftCommand.CanExecute(null));
        Assert.True(viewModel.ExportWorkflowPackageCommand.CanExecute(null));
        Assert.Equal("下载到 staging", viewModel.DownloadPackageButtonText);
        Assert.False(viewModel.HasDownloadablePackage);
        Assert.Equal("尚未选择候选 ComfyUI 环境", viewModel.ComfyUiLocation);
    }

    [Fact]
    public void Package_wizard_starts_with_an_honest_incomplete_draft()
    {
        var viewModel = new ShellViewModel();

        Assert.Equal(1, viewModel.WizardStep);
        Assert.Equal("LightyearXizIl", viewModel.DraftAuthorName);
        Assert.Equal("0.1.0", viewModel.DraftVersion);
        Assert.Equal(DistributionDeclaration.OnlineOnly, viewModel.DraftDistribution);
        Assert.Contains("选择工作流", viewModel.WizardStepText);
    }

    [Fact]
    public void Imported_package_resource_rows_are_derived_without_claiming_deployment()
    {
        var package = new PackageRow("portrait", "肖像包", "1.0.0", "", "", [
            new ResourceEntry("model", "portrait.safetensors", ResourceKind.Model, 1024, null, "https://example.invalid/model"),
            new ResourceEntry("node", "portrait-node", ResourceKind.CustomNode, 2048, null, null),
            new ResourceEntry("asset", "readme.txt", ResourceKind.Asset, 10, null, null)]);

        var model = Assert.Single(ShellViewModel.CreateResourceRows([package], ResourceKind.Model));
        var node = Assert.Single(ShellViewModel.CreateResourceRows([package], ResourceKind.CustomNode));

        Assert.Equal("portrait.safetensors", model.Name);
        Assert.Equal("1 KB", model.Size);
        Assert.Equal("https://example.invalid/model", model.Source);
        Assert.Contains("尚未部署", model.Status);
        Assert.Equal("portrait-node", node.Name);
        Assert.Equal("未提供下载来源", node.Source);
    }

    [Fact]
    public void Unknown_task_totals_are_rendered_without_a_fake_percentage()
    {
        var task = new WorkerTask("task", WorkerTaskKind.Download, WorkerTaskState.Running, "下载", "传输中", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var row = ShellViewModel.ToTaskRow(task);

        Assert.Null(row.Progress);
        Assert.Equal("进度总量未知", row.Detail);
        Assert.True(row.IsCurrent);
    }

    [Fact]
    public void Search_matches_displayed_metadata_case_insensitively()
    {
        Assert.True(ShellViewModel.MatchesSearch("flow", "FlowPack 示例", "1.0.0"));
        Assert.True(ShellViewModel.MatchesSearch("V1.0", "示例", "UI 工作流 v1.0"));
        Assert.False(ShellViewModel.MatchesSearch("missing", "FlowPack 示例", "1.0.0"));
        Assert.True(ShellViewModel.MatchesSearch("", "任意内容"));
    }
}
