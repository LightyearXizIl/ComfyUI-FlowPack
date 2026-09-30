using System.IO;
using System.Text.Json;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class OnlineImportPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-online-plan-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Online_declarations_survive_plan_serialization_without_becoming_installable_files()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "bundle.cpack.json");
        await File.WriteAllTextAsync(path, """
            {"formatVersion":"1","id":"bundle","name":"Test","version":"1","resources":[
              {"id":"model","name":"model.safetensors","kind":"Model","sizeBytes":42,
               "sourceUrl":"https://example.invalid/model.safetensors","sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
               "deploymentPurpose":"models/checkpoints/model.safetensors","packagePath":"payload/model.safetensors"},
              {"id":"workflow","name":"workflow.json","kind":"Workflow","sizeBytes":20}
            ]}
            """);
        var staging = Path.Combine(_root, "staging");
        var plan = await new ResourceImportService().ImportAsync(path, staging);
        Assert.Empty(plan.Resources); Assert.Empty(plan.Workflows);
        Assert.False(Directory.Exists(staging));
        Assert.Equal(2, plan.PendingDownloads.Count);
        Assert.Contains(plan.Issues, issue => issue.Contains("尚未下载"));
        Assert.Contains(plan.Issues, issue => issue.Contains("未提供下载来源"));
        var restored = JsonSerializer.Deserialize<ImportPlan>(JsonSerializer.Serialize(plan))!;
        Assert.Equal("bundle", restored.OnlineManifest!.Id);
        Assert.Equal(new string('A', 64), restored.PendingDownloads[0].Sha256);
        Assert.Equal("models/checkpoints/model.safetensors", restored.PendingDownloads[0].DeploymentPurpose);
        Assert.Empty(restored.Resources);
    }

    [Fact]
    public async Task Unsupported_online_format_is_rejected_not_imported_as_an_unknown_local_file()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "future.cpack.json");
        await File.WriteAllTextAsync(path, """{"formatVersion":"999","id":"bundle","name":"Test","version":"1","resources":[]}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ResourceImportService().ImportAsync(path, Path.Combine(_root, "staging")));
    }

    [Fact]
    public void Legacy_serialized_import_plans_default_to_no_remote_resources()
    {
        var legacy = """{"Id":"a","Source":"source.zip","StagingRoot":"staging","Resources":[],"Workflows":[],"Issues":[]}""";
        var plan = JsonSerializer.Deserialize<ImportPlan>(legacy)!;
        Assert.Null(plan.OnlineManifest); Assert.Empty(plan.PendingDownloads);
    }

    [Fact]
    public async Task Materialized_workflow_preserves_json_and_checks_source_size_and_hash_before_mapping()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "workflow.json");
        const string raw = """{"1":{"class_type":"SaveImage","inputs":{"filename_prefix":"test"}}}""";
        await File.WriteAllTextAsync(file, raw);
        var hash = await ResourceImportService.HashAsync(file);
        var declaration = new FlowPack.Core.ResourceEntry("workflow", "workflow.json", FlowPack.Core.ResourceKind.Workflow,
            new FileInfo(file).Length, hash, "https://example.invalid/workflow.json") { DeploymentPurpose = "workflows/nested/original.json" };
        var manifest = new FlowPack.Core.PackageManifest("bundle", "Test", "1", [declaration]);
        var plan = new ImportPlan("a", "bundle.cpack.json", Path.Combine(_root, "staging"), [], [], [])
            { OnlineManifest = manifest, PendingDownloads = [declaration] };
        var result = await new OnlineImportMaterializer().MaterializeAsync(plan, "workflow", file);
        Assert.Empty(result.PendingDownloads); Assert.Single(plan.PendingDownloads);
        Assert.Equal(raw, Assert.Single(result.Workflows).RawJson);
        Assert.Equal("workflows/nested/original.json", Assert.Single(result.Resources).TargetRelativePath);
        Assert.Equal(RecognitionState.Confirmed, result.Resources[0].State);
        await File.WriteAllTextAsync(file, raw.Replace("test", "xxxx"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new OnlineImportMaterializer().MaterializeAsync(plan, "workflow", file));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
