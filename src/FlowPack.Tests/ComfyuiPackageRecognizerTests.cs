using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FlowPack.ComfyUI;
using Xunit;

namespace FlowPack.Tests;

public sealed class ComfyuiPackageRecognizerTests
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
    public async Task Recognize_directory_with_native_structure()
    {
        var root = Temp();
        try
        {
            WriteFile(Path.Combine(root, "workflows", "w1.json"), UiWorkflowJson);
            WriteFile(Path.Combine(root, "models", "checkpoints", "x.safetensors"), "MODEL");
            Directory.CreateDirectory(Path.Combine(root, "custom_nodes", "MyNode"));

            var recognizer = new ComfyuiPackageRecognizer();
            var package = await recognizer.RecognizeAsync(root);

            Assert.NotNull(package);
            Assert.Equal(PackageRecognitionKind.ExpandedDirectory, package!.Kind);
            Assert.Single(package.Workflows);
            Assert.Equal("w1", package.Workflows[0].DisplayName);
            Assert.Contains("MyNode", package.CustomNodes.Select(n => n.DirectoryName));
            Assert.Single(package.ModelFiles);
            Assert.Equal("checkpoints", package.ModelFiles[0].Category);
            Assert.Equal("models/checkpoints/x.safetensors", package.ModelFiles[0].RelativePath);
            Assert.False(package.HasManifest);
            Assert.False(package.IsEmpty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Recognize_zip_archive_with_native_structure()
    {
        var zipPath = Path.Combine(Temp(), "pack.zip");
        try
        {
            using (var fileStream = File.Create(zipPath))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                WriteZipEntry(archive, "workflows/w1.json", UiWorkflowJson);
                WriteZipEntry(archive, "models/checkpoints/x.safetensors", "MODEL");
                WriteZipEntry(archive, "custom_nodes/MyNode/__init__.py", "pass");
            }

            var recognizer = new ComfyuiPackageRecognizer();
            var package = await recognizer.RecognizeAsync(zipPath);

            Assert.NotNull(package);
            Assert.Equal(PackageRecognitionKind.ZipArchive, package!.Kind);
            Assert.Single(package.Workflows);
            Assert.Contains("MyNode", package.CustomNodes.Select(n => n.DirectoryName));
            Assert.Single(package.ModelFiles);
            Assert.Equal("checkpoints", package.ModelFiles[0].Category);
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var dir = Path.GetDirectoryName(zipPath)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Returns_null_when_no_native_structure()
    {
        var empty = Temp();
        var nonNative = Temp();
        try
        {
            File.WriteAllText(Path.Combine(nonNative, "readme.txt"), "not a comfyui package");

            var recognizer = new ComfyuiPackageRecognizer();

            Assert.Null(await recognizer.RecognizeAsync(empty));
            Assert.Null(await recognizer.RecognizeAsync(nonNative));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
            Directory.Delete(nonNative, recursive: true);
        }
    }

    [Fact]
    public async Task Extracts_node_types_and_model_refs_from_ui_workflow()
    {
        var root = Temp();
        try
        {
            WriteFile(Path.Combine(root, "workflows", "ui.json"), UiWorkflowJson);

            var recognizer = new ComfyuiPackageRecognizer();
            var package = await recognizer.RecognizeAsync(root);

            Assert.NotNull(package);
            var workflow = package!.Workflows.Single();
            Assert.Equal(new[] { "CheckpointLoaderSimple", "LoraLoader", "Note" }, workflow.NodeTypes);
            Assert.Contains("sd_xl_base_1.0.safetensors", workflow.ReferencedModelFileNames);
            Assert.Contains("loras/cat_lora.safetensors", workflow.ReferencedModelFileNames);
            Assert.DoesNotContain(workflow.ReferencedModelFileNames, name => name.Contains("note"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Extracts_node_types_and_model_refs_from_api_workflow()
    {
        var root = Temp();
        try
        {
            WriteFile(Path.Combine(root, "workflows", "api.json"), ApiWorkflowJson);

            var recognizer = new ComfyuiPackageRecognizer();
            var package = await recognizer.RecognizeAsync(root);

            Assert.NotNull(package);
            var workflow = package!.Workflows.Single();
            Assert.Equal(new[] { "UNETLoader", "VAELoader" }, workflow.NodeTypes);
            Assert.Contains("unet/flux.safetensors", workflow.ReferencedModelFileNames);
            Assert.Contains("vae/flux_vae.safetensors", workflow.ReferencedModelFileNames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Returns_null_for_cpack_source()
    {
        var cpack = Path.Combine(Temp(), "self.cpack");
        try
        {
            File.WriteAllText(cpack, "self packaged");

            var recognizer = new ComfyuiPackageRecognizer();

            Assert.Null(await recognizer.RecognizeAsync(cpack));
        }
        finally
        {
            if (File.Exists(cpack)) File.Delete(cpack);
            var dir = Path.GetDirectoryName(cpack)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task HasManifest_true_when_manifest_present()
    {
        var zipPath = Path.Combine(Temp(), "pack.zip");
        try
        {
            using (var fileStream = File.Create(zipPath))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                WriteZipEntry(archive, "manifest.json", "{\"id\":\"x\"}");
                WriteZipEntry(archive, "workflows/w1.json", UiWorkflowJson);
            }

            var recognizer = new ComfyuiPackageRecognizer();
            var package = await recognizer.RecognizeAsync(zipPath);

            Assert.NotNull(package);
            Assert.True(package!.HasManifest);
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var dir = Path.GetDirectoryName(zipPath)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteZipEntry(ZipArchive archive, string entryPath, string content)
    {
        var entry = archive.CreateEntry(entryPath);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "flowpack-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
