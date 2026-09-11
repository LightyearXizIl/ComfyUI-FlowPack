using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class WorkflowPackageExportServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"flowpack-workflow-{Guid.NewGuid():N}.cpack");

    [Fact]
    public async Task Workflow_export_is_importable_and_honestly_declared_partial()
    {
        var draft = new PackageDraft(
            "example.workflow",
            "workflow-id",
            "示例工作流包",
            "1.0.0",
            "只含原始工作流",
            "LightyearXizIl",
            "https://github.com/LightyearXizIl",
            "https://example.invalid/example.workflow",
            DistributionDeclaration.OfflinePartial,
            4);
        var workflow = new WorkflowDocument("workflow-id", "示例工作流", WorkflowFormat.UiV10, "{\"version\":\"1.0\",\"nodes\":[]}");

        var exported = await new WorkflowPackageExportService().ExportAsync(_path, draft, workflow);
        var imported = await new PackageImportReader().ReadAsync(_path);

        Assert.True(File.Exists(_path));
        Assert.Equal(PackageImportKind.OfflineArchive, imported.Kind);
        Assert.Equal(DistributionDeclaration.OfflinePartial, exported.Distribution);
        Assert.False(exported.IsComplete);
        Assert.Contains(exported.CompletenessIssues, issue => issue.Contains("不完整离线分发"));
        var resource = Assert.Single(imported.Manifest.Resources);
        Assert.Equal(ResourceKind.Workflow, resource.Kind);
        Assert.Equal("payload/workflows/entry.json", resource.PackagePath);
        Assert.NotNull(resource.Sha256);
    }

    [Fact]
    public async Task Workflow_export_rejects_an_invalid_package_source_before_creating_an_archive()
    {
        var draft = new PackageDraft("invalid", "workflow-id", "示例", "1.0.0", "", "LightyearXizIl", null, "http://not-secure.invalid/package", DistributionDeclaration.OfflinePartial, 4);
        var workflow = new WorkflowDocument("workflow-id", "示例", WorkflowFormat.UiV10, "{\"version\":\"1.0\",\"nodes\":[]}");

        await Assert.ThrowsAsync<InvalidDataException>(() => new WorkflowPackageExportService().ExportAsync(_path, draft, workflow));

        Assert.False(File.Exists(_path));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
