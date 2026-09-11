using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PackageDraftExchangeServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"flowpack-draft-{Guid.NewGuid():N}.flowpack-draft.json");

    [Fact]
    public async Task Draft_exchange_round_trips_a_raw_workflow_without_claiming_a_package_export()
    {
        var draft = new PackageDraft("draft", "workflow", "草稿", "0.1.0", "", "LightyearXizIl", null, "https://example.invalid/source", DistributionDeclaration.OfflinePartial, 4);
        var workflow = new WorkflowDocument("workflow", "示例", WorkflowFormat.UiV10, "{\"version\":\"1.0\",\"unknown\":true}");
        var service = new PackageDraftExchangeService();

        await service.ExportAsync(_path, draft, workflow);
        var imported = await service.ImportAsync(_path);

        Assert.Equal("1", imported.FormatVersion);
        Assert.Equal(draft, imported.Draft);
        Assert.Equal(workflow, imported.Workflow);
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
