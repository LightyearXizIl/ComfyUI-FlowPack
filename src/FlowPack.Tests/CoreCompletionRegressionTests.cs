using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class CoreCompletionRegressionTests
{
    [Theory]
    [InlineData("LoadImage")]
    [InlineData("LoadAudio")]
    [InlineData("SaveImage")]
    [InlineData("CLIPTextEncode")]
    [InlineData("KSampler")]
    public void Known_non_model_widgets_do_not_become_dynamic_model_dependencies(string type)
    {
        var json = JsonSerializer.Serialize(new { version = 1, nodes = new[] { new { id = 1, type, widgets_values = new[] { "this-is-not-a-model.pth" } } } });
        var requirements = WorkflowRequirementParser.Analyze(json);
        Assert.Empty(requirements.ModelReferences);
        Assert.Empty(requirements.Issues);
        if (type.StartsWith("Load", StringComparison.Ordinal)) Assert.Single(requirements.InputReferences);
    }

    [Theory]
    [InlineData("{\"version\":1,\"nodes\":[{}]}")]
    [InlineData("{\"version\":0.4,\"nodes\":[42]}")]
    [InlineData("{\"version\":1,\"nodes\":[{\"id\":1,\"type\":\"\"}]}")]
    public void Version_does_not_make_arbitrary_json_a_workflow(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(WorkflowFormat.Unknown, WorkflowDocumentFactory.DetectFormat(document.RootElement));
        WorkflowRequirementParser.Analyze(json);
    }

    [Fact]
    public void Unknown_custom_widgets_still_require_inspection()
    {
        var result = WorkflowRequirementParser.Analyze("""{"nodes":[{"id":1,"type":"UnknownLoader","widgets_values":["folder"]}],"links":[]}""");
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Version_conflict_survives_a_third_workflow()
    {
        WorkflowDocument Flow(string id, string version) => WorkflowDocumentFactory.Create(id, id,
            JsonSerializer.Serialize(new { version = 1, nodes = new[] { new { id = 1, type = "Custom", properties = new { cnr_id = "package", ver = version } } } }));
        var workflows = new[] { Flow("one", "1"), Flow("two", "2"), Flow("three", "2") };
        var instance = new InstanceDescriptor("fixture", "fixture", "desktop-2", null, "", "", "", "", "", "", null, [], "", [], "fixture", []);
        var inventory = new ResourceInventory(instance, [], [], []);
        var dependency = Assert.Single(new InventoryDependencyAnalyzer().Analyze(workflows, inventory).Dependencies);
        Assert.Equal(DependencyState.Ambiguous, dependency.State);
        Assert.Equal(3, dependency.RequiredBy.Count);
    }

    [Fact]
    public async Task Same_name_model_hashes_remain_scoped_to_the_referencing_category()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-hashes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var resources = new List<LocalResource>();
            var workflows = new List<WorkflowDocument>();
            foreach (var pair in new[] { ("CheckpointLoaderSimple", "checkpoints"), ("VAELoader", "vae") })
            {
                var path = Path.Combine(root, pair.Item2);
                await File.WriteAllTextAsync(path, pair.Item2);
                var hash = await ResourceImportService.HashAsync(path);
                resources.Add(new(path, ResourceKind.Model, "same.safetensors", path, pair.Item2 + "/same.safetensors", pair.Item2));
                workflows.Add(WorkflowDocumentFactory.Create(pair.Item2, pair.Item2, JsonSerializer.Serialize(new { version = 1,
                    nodes = new[] { new { id = 1, type = pair.Item1, widgets_values = new[] { "same.safetensors" } } },
                    models = new[] { new { name = "same.safetensors", sha256 = hash } } })));
            }
            var instance = new InstanceDescriptor("fixture", "fixture", "desktop-2", null, "", "", "", "", "", "", null, [], "", [], "fixture", []);
            var result = await new InventoryDependencyAnalyzer().AnalyzeAsync(workflows, new(instance, resources, ["CheckpointLoaderSimple", "VAELoader"], []));
            Assert.Equal(2, result.Dependencies.Count);
            Assert.All(result.Dependencies, x => Assert.Equal(DependencyState.Present, x.State));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Recovery_preserves_recorded_partial_copy_and_does_not_sweep_unrelated_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var target = Path.Combine(root, "model.pth");
            var temporary = target + ".flowpack-" + id + ".tmp";
            var unrelated = Path.Combine(root, "unrelated.tmp");
            await File.WriteAllTextAsync(temporary, "partial-model");
            await File.WriteAllTextAsync(unrelated, "user-data");
            var journal = new DeploymentJournal(id, "Running", [new(target, "expected", "Intent") { TemporaryPath = temporary }]);
            var journalRoot = Path.Combine(root, "state", "journal");
            Directory.CreateDirectory(journalRoot);
            await File.WriteAllTextAsync(Path.Combine(journalRoot, id + ".json"), JsonSerializer.Serialize(journal));
            var service = new ResourceInstallationService(root);
            var recovered = Assert.Single(await service.RecoverAsync());
            var file = Assert.Single(recovered.Files);
            Assert.Equal("NeedsReview", recovered.State);
            Assert.Equal("NotPresent", file.State);
            Assert.Equal("partial-model", await File.ReadAllTextAsync(file.RecoveryPath!));
            Assert.False(File.Exists(temporary));
            Assert.Equal("user-data", await File.ReadAllTextAsync(unrelated));
            Assert.Equal(file.RecoveryPath, Assert.Single(Assert.Single(await service.RecoverAsync()).Files).RecoveryPath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Interrupted_python_attempt_preserves_baseline_and_blocks_further_node_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-python-recovery-" + Guid.NewGuid().ToString("N"));
        var attempts = Path.Combine(root, "state", "python-attempts");
        Directory.CreateDirectory(attempts);
        try
        {
            var python = Path.Combine(root, "python.exe");
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(attempts, id + ".json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { state = "Installing", PythonPath = python, before = new { torch = "fixture-version" }, planned = Array.Empty<object>() }));
            var service = new PythonDependencyService(root);
            var issue = Assert.Single(await service.RecoverAsync());
            Assert.Equal(python, issue.PythonPath);
            using var journal = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal("NeedsRepair", journal.RootElement.GetProperty("state").GetString());
            Assert.Equal("fixture-version", journal.RootElement.GetProperty("before").GetProperty("torch").GetString());
            var instance = new InstanceDescriptor("fixture", "fixture", "desktop-2", null, root, root, root, root, root, root, python, [], root, [], "fixture", []);
            var plan = new ResourceInstallPlan(Guid.NewGuid().ToString("N"), instance,
                [new("unread-source", Path.Combine(root, "custom_nodes", "node", "__init__.py"), 0, "", false)], [], 0, DateTimeOffset.UtcNow);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(plan));
            Assert.Contains("需要修复", error.Message);
            Assert.False(File.Exists(python)); // No interpreter was launched or changed during recovery.
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Connected_but_silent_worker_respects_the_whole_request_deadline()
    {
        var name = "flowpack-timeout-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = server.WaitForConnectionAsync();
        var request = new WorkerRequest(WorkerProtocol.Version, "timeout", "test", WorkerProtocol.PingCommand);
        var response = new NamedPipeWorkerClient().SendAsync(name, request, TimeSpan.FromMilliseconds(250));
        await connected;
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        Assert.NotNull(await reader.ReadLineAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
    }
}
