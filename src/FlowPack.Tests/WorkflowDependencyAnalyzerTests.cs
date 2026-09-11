using System.IO;
using FlowPack.ComfyUI;
using Xunit;

namespace FlowPack.Tests;

public sealed class WorkflowDependencyAnalyzerTests
{
    private const string UiWorkflowJson = """
        {
          "nodes": [
            {"type": "CheckpointLoaderSimple", "widgets_values": ["sd_xl_base_1.0.safetensors"]},
            {"type": "LoraLoader", "widgets_values": ["loras/cat_lora.safetensors", 0.75]},
            {"type": "Note", "widgets_values": ["just a note, not a model"]}
          ],
          "links": [],
          "version": 1
        }
        """;

    private const string ApiWorkflowJson = """
        {
          "10": {"class_type": "UNETLoader", "inputs": {"unet_name": "unet/flux.safetensors"}},
          "11": {"class_type": "VAELoader", "inputs": {"vae_name": "vae/flux_vae.safetensors"}}
        }
        """;

    [Fact]
    public void AnalyzeRaw_extracts_ui_node_types_and_models()
    {
        var analyzer = new WorkflowDependencyAnalyzer();
        var dependency = analyzer.AnalyzeRaw(UiWorkflowJson, "ui");

        Assert.Equal(new[] { "CheckpointLoaderSimple", "LoraLoader", "Note" }, dependency.RequiredNodeTypes);
        Assert.Contains(dependency.RequiredModels, m =>
            m.Reference == "sd_xl_base_1.0.safetensors" && m.CategoryHint is null && m.FileName == "sd_xl_base_1.0.safetensors");
        Assert.Contains(dependency.RequiredModels, m =>
            m.Reference == "loras/cat_lora.safetensors" && m.CategoryHint == "loras" && m.FileName == "cat_lora.safetensors");
        Assert.DoesNotContain(dependency.RequiredModels, m => m.Reference.Contains("note"));
    }

    [Fact]
    public void AnalyzeRaw_extracts_api_node_types_and_models()
    {
        var analyzer = new WorkflowDependencyAnalyzer();
        var dependency = analyzer.AnalyzeRaw(ApiWorkflowJson, "api");

        Assert.Equal(new[] { "UNETLoader", "VAELoader" }, dependency.RequiredNodeTypes);
        Assert.Contains(dependency.RequiredModels, m => m.Reference == "unet/flux.safetensors" && m.CategoryHint == "unet");
        Assert.Contains(dependency.RequiredModels, m => m.Reference == "vae/flux_vae.safetensors" && m.CategoryHint == "vae");
    }

    [Fact]
    public async Task AnalyzeAsync_reads_from_file()
    {
        var dir = Temp();
        try
        {
            File.WriteAllText(Path.Combine(dir, "w.json"), UiWorkflowJson);
            var analyzer = new WorkflowDependencyAnalyzer();
            var dependency = await analyzer.AnalyzeAsync(Path.Combine(dir, "w.json"));

            Assert.Equal("w", dependency.DisplayName);
            Assert.Single(dependency.RequiredModels, m => m.FileName == "sd_xl_base_1.0.safetensors");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "flowpack-dep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
