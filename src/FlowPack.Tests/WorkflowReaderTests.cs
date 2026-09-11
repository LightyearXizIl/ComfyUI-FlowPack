using System.IO;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class WorkflowReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"FlowPack.WorkflowTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Ui_v1_workflow_is_classified_without_rewriting_its_unknown_fields()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "ui-v1.json");
        const string rawJson = """{ "version": "1.0", "nodes": [], "unknown_extension_field": { "keep": true } }""";
        await File.WriteAllTextAsync(path, rawJson);

        var workflow = await new WorkflowReader().ReadAsync(path);

        Assert.Equal(WorkflowFormat.UiV10, workflow.Format);
        Assert.Equal(rawJson, workflow.RawJson);
    }

    [Fact]
    public async Task Api_workflow_is_not_mislabeled_as_ui_workflow()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "api.json");
        await File.WriteAllTextAsync(path, """{ "1": { "class_type": "KSampler", "inputs": {} } }""");

        var workflow = await new WorkflowReader().ReadAsync(path);

        Assert.Equal(WorkflowFormat.Api, workflow.Format);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
