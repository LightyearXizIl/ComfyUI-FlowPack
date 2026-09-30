using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class LegacyImportMappingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-legacy-mapping-" + Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archive_and_expanded_legacy_payloads_share_native_import_targets(bool archive)
    {
        var source = await MakePackageAsync("models/checkpoints/model.bin");
        if (archive)
        {
            var zip = Path.Combine(_root, "bundle.cpack"); ZipFile.CreateFromDirectory(source, zip); source = zip;
        }
        var imported = await new ResourceImportService().ImportAsync(source, Path.Combine(_root, "staging"));
        Assert.Equal(2, imported.Resources.Count);
        var model = Assert.Single(imported.Resources, x => x.Kind == ResourceKind.Model);
        Assert.Equal("models/checkpoints/model.bin", model.TargetRelativePath);
        Assert.Equal(RecognitionState.Confirmed, model.State);
        Assert.Single(imported.Workflows);
        Assert.DoesNotContain(imported.Resources, x => x.OriginalPath == "manifest.json");
    }
    [Fact]
    public async Task Explanatory_purpose_does_not_become_a_deployment_path()
    {
        var source = await MakePackageAsync("image-generation");
        var imported = await new ResourceImportService().ImportAsync(source, Path.Combine(_root, "staging"));
        Assert.Equal(RecognitionState.NeedsConfirmation, Assert.Single(imported.Resources, x => x.Kind == ResourceKind.Model).State);
    }
    [Fact]
    public async Task Invalid_target_and_changed_payload_are_rejected()
    {
        var source = await MakePackageAsync("models/checkpoints/../../escape.bin");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ResourceImportService().ImportAsync(source, Path.Combine(_root, "stage-a")));
        await File.WriteAllTextAsync(Path.Combine(source, "payload", "model.bin"), "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ResourceImportService().ImportAsync(source, Path.Combine(_root, "stage-b")));
    }
    private async Task<string> MakePackageAsync(string purpose)
    {
        var directory = Path.Combine(_root, "expanded"); Directory.CreateDirectory(Path.Combine(directory, "payload"));
        var model = Path.Combine(directory, "payload", "model.bin"); await File.WriteAllTextAsync(model, "model");
        await File.WriteAllTextAsync(Path.Combine(directory, "payload", "workflow.json"), "{\"version\":0.4,\"nodes\":[{\"id\":1,\"type\":\"SaveImage\"}]}");
        var manifest = new { formatVersion = "1", id = "legacy", name = "legacy", version = "1.0.0",
            entryWorkflows = new[] { new { id = "w", path = "payload/workflow.json" } },
            resources = new[] { new { id = "m", name = "model", kind = "Model", sizeBytes = 5,
                sha256 = await ResourceImportService.HashAsync(model), packagePath = "payload/model.bin", deploymentPurpose = purpose } } };
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
        return directory;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
