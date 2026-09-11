using System.IO;
using System.IO.Compression;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class ResourcePackageExportServiceTests : IDisposable
{
    private readonly List<string> _cleanup = [];

    // ---- Requirement 7: workflows only ----------------------------------------------------

    [Fact]
    public async Task ExportWorkflows_writes_each_workflow_under_workflows_without_models()
    {
        var outPath = Temp(".zip");
        var wfA = new WorkflowDocument("a", "WF-A", WorkflowFormat.UiV10, "{\"version\":1.0,\"nodes\":[]}");
        var wfB = new WorkflowDocument("b", "WF-B", WorkflowFormat.UiV10, "{\"version\":1.0,\"nodes\":[]}");

        var result = await new ResourcePackageExportService().ExportWorkflowsAsync(outPath, [wfA, wfB]);

        Assert.Equal(2, result.WorkflowCount);
        var entries = ListEntries(outPath);
        Assert.Contains(entries, e => e == "workflows/WF-A.json");
        Assert.Contains(entries, e => e == "workflows/WF-B.json");
        Assert.DoesNotContain(entries, e => e.StartsWith("models/", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Requirement 7: model only -------------------------------------------------------

    [Fact]
    public async Task ExportModels_copies_file_into_models_category()
    {
        var outPath = Temp(".zip");
        var modelFile = Path.Combine(TempDir(), "sd_xl.safetensors");
        WriteFile(modelFile, "M");

        var result = await new ResourcePackageExportService().ExportModelsAsync(outPath, [new ModelFileRef(modelFile, "checkpoints")]);

        Assert.Single(result.BundledModels);
        Assert.Equal("sd_xl.safetensors", result.BundledModels[0].FileName);
        Assert.Contains(ListEntries(outPath), e => e == "models/checkpoints/sd_xl.safetensors");
    }

    // ---- Requirement 7: node only --------------------------------------------------------

    [Fact]
    public async Task ExportNodes_copies_directory_into_custom_nodes()
    {
        var outPath = Temp(".zip");
        var nodeDir = TempDir();
        WriteFile(Path.Combine(nodeDir, "node.py"), "P");

        var result = await new ResourcePackageExportService().ExportNodesAsync(outPath, [new NodeDirRef(nodeDir)]);

        Assert.Single(result.BundledNodes);
        Assert.Contains(ListEntries(outPath), e => e.StartsWith("custom_nodes/" + new DirectoryInfo(nodeDir).Name + "/", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Requirements 2 + 6: bundle with resolved dependencies + round-trip ----------------

    private const string BundleWorkflow =
        "{\"version\":1.0,\"nodes\":[{\"type\":\"SomeNode\",\"widgets_values\":[\"sd_xl_base_1.0.safetensors\"]}]}";

    [Fact]
    public async Task ExportBundle_includes_present_models_and_nodes_and_round_trips_through_recognizer()
    {
        var local = MakeLocal(out var _);
        var outPath = Temp(".zip");
        var workflow = new WorkflowDocument("w1", "My Workflow", WorkflowFormat.UiV10, BundleWorkflow);

        var result = await new ResourcePackageExportService().ExportBundleAsync(outPath,
            new ExportBundleRequest([workflow], local, IncludeModels: true, IncludeNodes: true, Registry: new StubRegistry(new() { ["SomeNode"] = "some-node-pkg" })));

        Assert.Equal(1, result.WorkflowCount);
        Assert.Single(result.BundledModels);
        Assert.Single(result.BundledNodes);
        Assert.DoesNotContain(result.MissingModels, m => m.Contains("sd_xl"));
        Assert.DoesNotContain(result.MissingNodes, n => n == "SomeNode");

        var entries = ListEntries(outPath);
        Assert.Contains(entries, e => e == "workflows/My_Workflow.json" || e == "workflows/My Workflow.json");
        Assert.Contains(entries, e => e == "models/checkpoints/sd_xl_base_1.0.safetensors");
        Assert.Contains(entries, e => e.StartsWith("custom_nodes/some-node-pkg/", StringComparison.OrdinalIgnoreCase));

        // Round-trip: the produced zip must be recognizable as a native ComfyUI package (requirement 3 continuity).
        var recognized = await new ComfyuiPackageRecognizer().RecognizeAsync(outPath);
        Assert.NotNull(recognized);
        Assert.Equal(PackageRecognitionKind.ZipArchive, recognized!.Kind);
        Assert.Single(recognized.Workflows);
        Assert.Contains(recognized.ModelFiles, m => m.Category == "checkpoints");
        Assert.Contains(recognized.CustomNodes, n => n.DirectoryName == "some-node-pkg");
    }

    [Fact]
    public async Task ExportBundle_reports_missing_models_instead_of_fabricating_them()
    {
        var local = MakeLocal(out var _);
        var outPath = Temp(".zip");
        var workflow = new WorkflowDocument("w2", "Missing Model WF", WorkflowFormat.UiV10,
            "{\"version\":1.0,\"nodes\":[{\"type\":\"SomeNode\",\"widgets_values\":[\"missing_model.safetensors\"]}]}");

        var result = await new ResourcePackageExportService().ExportBundleAsync(outPath,
            new ExportBundleRequest([workflow], local, IncludeModels: true, IncludeNodes: true, Registry: new StubRegistry(new() { ["SomeNode"] = "some-node-pkg" })));

        Assert.Contains("missing_model.safetensors", result.MissingModels);
        Assert.Empty(result.BundledModels);
        Assert.DoesNotContain(ListEntries(outPath), e => e.StartsWith("models/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportBundle_skips_models_when_IncludeModels_is_false()
    {
        var local = MakeLocal(out var _);
        var outPath = Temp(".zip");
        var workflow = new WorkflowDocument("w3", "No Deps WF", WorkflowFormat.UiV10, BundleWorkflow);

        var result = await new ResourcePackageExportService().ExportBundleAsync(outPath,
            new ExportBundleRequest([workflow], local, IncludeModels: false, IncludeNodes: false));

        Assert.Empty(result.BundledModels);
        Assert.Empty(result.BundledNodes);
        Assert.DoesNotContain(ListEntries(outPath), e => e.StartsWith("models/", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Test scaffolding ---------------------------------------------------------------

    private sealed class StubRegistry : ICustomNodeRegistry
    {
        private readonly Dictionary<string, string> _map;
        public StubRegistry(Dictionary<string, string> map) => _map = map;
        public string? ResolveInstalledPackage(string nodeType) => _map.TryGetValue(nodeType, out var pkg) ? pkg : null;
    }

    private static ComfyDesktopLocation MakeLocal(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "flowpack-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "models", "checkpoints"));
        Directory.CreateDirectory(Path.Combine(root, "custom_nodes", "some-node-pkg"));
        WriteFile(Path.Combine(root, "models", "checkpoints", "sd_xl_base_1.0.safetensors"), "M");
        WriteFile(Path.Combine(root, "custom_nodes", "some-node-pkg", "node.py"), "P");
        return new ComfyDesktopLocation("test", root, null,
            Path.Combine(root, "models"),
            Path.Combine(root, "custom_nodes"),
            null, null, default);
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Temp(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"flowpack-export-{Guid.NewGuid():N}{extension}");
        _cleanup.Add(path);
        if (File.Exists(path)) File.Delete(path);
        return path;
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flowpack-node-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _cleanup.Add(path);
        return path;
    }

    private static IReadOnlyList<string> ListEntries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    public void Dispose()
    {
        foreach (var path in _cleanup)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
