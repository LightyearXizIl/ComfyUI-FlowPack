using System.IO;
using System.Text.Json;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PackageManifestReaderTests
{
    private readonly PackageManifestReader _reader = new();

    [Fact]
    public void Complete_format_1_manifest_preserves_author_entry_and_distribution()
    {
        using var document = JsonDocument.Parse("""
            {
              "formatVersion": "1",
              "id": "lightyear.example",
              "name": "示例工作流包",
              "version": "1.0.0",
              "description": "可安全导入的示例。",
              "author": { "name": "LightyearXizIl", "url": "https://github.com/LightyearXizIl" },
              "source": "https://example.invalid/packages/lightyear.example.cpack.json",
              "distribution": "OnlineOnly",
              "entryWorkflows": [{ "id": "main", "path": "payload/workflows/main.json", "isEntryPoint": true }],
              "resources": [{
                "id": "model",
                "name": "model.safetensors",
                "kind": "Model",
                "sizeBytes": 1024,
                "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "sourceUrl": "https://example.invalid/models/model.safetensors",
                "distribution": "OnlineOnly"
              }]
            }
            """);

        var manifest = _reader.Read(document.RootElement);

        Assert.True(manifest.IsComplete);
        Assert.Equal("LightyearXizIl", manifest.Author!.Name);
        Assert.Equal("payload/workflows/main.json", Assert.Single(manifest.EntryWorkflows).RelativePath);
        Assert.Equal(DistributionDeclaration.OnlineOnly, manifest.Distribution);
    }

    [Fact]
    public async Task Legacy_minimal_manifest_is_saved_as_incomplete_and_cannot_be_planned()
    {
        using var document = JsonDocument.Parse("""
            {
              "formatVersion": "1",
              "id": "draft",
              "name": "草稿",
              "version": "0.1.0",
              "resources": []
            }
            """);
        var manifest = _reader.Read(document.RootElement);

        var plan = await new SafeInstallPlanner().PlanAsync(
            manifest,
            new InstanceFingerprint("C:\\isolated\\desktop", "C:\\isolated\\python.exe", "C:\\isolated\\user", null, DateTimeOffset.UtcNow));

        Assert.False(manifest.IsComplete);
        Assert.False(plan.IsExecutable);
        Assert.NotEmpty(plan.BlockingReasons!);
    }

    [Theory]
    [InlineData("C:/outside.json")]
    [InlineData("../outside.json")]
    [InlineData("payload/../../outside.json")]
    public void Entry_workflow_outside_the_package_is_rejected(string path)
    {
        using var document = JsonDocument.Parse($$"""
            {
              "formatVersion": "1",
              "id": "unsafe",
              "name": "不安全",
              "version": "1.0.0",
              "entryWorkflows": [{ "id": "main", "path": "{{path}}" }],
              "resources": []
            }
            """);

        Assert.Throws<InvalidDataException>(() => _reader.Read(document.RootElement));
    }

    [Fact]
    public void Token_bearing_resource_url_is_rejected_before_persistence()
    {
        using var document = JsonDocument.Parse("""
            {
              "formatVersion": "1",
              "id": "unsafe-url",
              "name": "不安全地址",
              "version": "1.0.0",
              "resources": [{
                "id": "model",
                "name": "model.safetensors",
                "kind": "Model",
                "sizeBytes": 1,
                "sourceUrl": "https://example.invalid/model?token=not-allowed"
              }]
            }
            """);

        Assert.Throws<InvalidDataException>(() => _reader.Read(document.RootElement));
    }

    [Fact]
    public async Task Resource_without_a_hash_is_blocked_before_the_worker_download_command()
    {
        var manifest = new PackageManifest("no-hash", "未校验资源", "1.0.0", [
            new ResourceEntry("model", "model.safetensors", ResourceKind.Model, 1024, null, "https://example.invalid/model")]);

        var plan = await new SafeInstallPlanner().PlanAsync(
            manifest,
            new InstanceFingerprint("C:\\isolated\\desktop", "C:\\isolated\\python.exe", "C:\\isolated\\user", null, DateTimeOffset.UtcNow));

        Assert.False(plan.IsExecutable);
        Assert.Contains(plan.Actions, action => action.Kind == InstallActionKind.Conflict && action.Summary.Contains("SHA-256"));
    }
}
