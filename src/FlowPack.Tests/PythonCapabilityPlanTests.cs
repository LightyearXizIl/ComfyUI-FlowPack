using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PythonCapabilityPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-python-capability-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("declared")]
    [InlineData("requirements.txt")]
    [InlineData("pyproject.toml")]
    public async Task File_only_qualification_cannot_install_python_dependency_sources(string sourceKind)
    {
        var (instance, resource) = await FixtureAsync(sourceKind);
        await using var worker = await WorkerAsync(instance, pythonQualified: false);
        var plan = await PlanAsync(worker, instance, resource);
        Assert.True(plan.RequiresPythonDependencies);
        Assert.NotEmpty(plan.Capability!.Reasons);
        Assert.False(plan.Capability.Allows(true));
        if (sourceKind != "declared") Assert.Empty(plan.PythonRequirements);
        var execute = Request("install.execute", plan);
        await worker.HandleAsync(execute, default);
        var job = await WaitAsync(worker, execute.RequestId);
        Assert.Equal("Failed", job.State);
        Assert.Contains("Python 依赖安装尚未通过验收", job.Error);
        Assert.False(Directory.Exists(instance.CustomNodesDirectory));
        Assert.False(Directory.Exists(Path.Combine(_root, "library", "staging", "python")));
    }

    [Theory]
    [InlineData("requirements.txt")]
    [InlineData("pyproject.toml")]
    public async Task Changed_metadata_is_rejected_before_python_preparation_or_target_writes(string metadata)
    {
        var (instance, resource) = await FixtureAsync(metadata);
        await using var worker = await WorkerAsync(instance, pythonQualified: true);
        var plan = await PlanAsync(worker, instance, resource);
        await File.AppendAllTextAsync(resource.SourcePath, "\n# changed after plan\n");
        var execute = Request("install.execute", plan);
        await worker.HandleAsync(execute, default);
        var job = await WaitAsync(worker, execute.RequestId);
        Assert.Equal("Failed", job.State);
        Assert.Contains("安装源文件已改变", job.Error);
        Assert.False(Directory.Exists(instance.CustomNodesDirectory));
        Assert.False(Directory.Exists(Path.Combine(_root, "library", "staging", "python")));
        Assert.False(Directory.Exists(Path.Combine(_root, "library", "state", "journal")));
    }

    [Fact]
    public async Task Legacy_plan_without_dependency_evidence_can_be_read_but_cannot_execute()
    {
        var (instance, resource) = await FixtureAsync("plain");
        await using var worker = await WorkerAsync(instance, pythonQualified: false);
        var plan = await PlanAsync(worker, instance, resource);
        var legacy = JsonNode.Parse(JsonSerializer.Serialize(plan))!.AsObject();
        legacy.Remove(nameof(ResourceInstallPlan.RequiresPythonDependencies));
        var legacyElement = JsonSerializer.SerializeToElement(legacy);
        Assert.Null(legacyElement.Deserialize<ResourceInstallPlan>()!.RequiresPythonDependencies);
        var execute = Request("install.execute", new { }) with { Payload = legacyElement };
        await worker.HandleAsync(execute, default);
        var job = await WaitAsync(worker, execute.RequestId);
        Assert.Equal("Failed", job.State);
        Assert.Contains("旧计划缺少 Python", job.Error);
        Assert.False(Directory.Exists(instance.CustomNodesDirectory));
    }

    [Fact]
    public async Task Dependency_flag_cannot_be_downgraded_in_a_retained_plan()
    {
        var (instance, resource) = await FixtureAsync("requirements.txt");
        await using var worker = await WorkerAsync(instance, pythonQualified: false);
        var plan = await PlanAsync(worker, instance, resource);
        var execute = Request("install.execute", plan with { RequiresPythonDependencies = false });
        await worker.HandleAsync(execute, default);
        var job = await WaitAsync(worker, execute.RequestId);
        Assert.Equal("Failed", job.State);
        Assert.Contains("原始计划", job.Error);
        Assert.False(Directory.Exists(instance.CustomNodesDirectory));
    }

    [Fact]
    public async Task Node_without_dependency_metadata_keeps_file_only_installation()
    {
        var (instance, resource) = await FixtureAsync("plain");
        await using var worker = await WorkerAsync(instance, pythonQualified: false);
        var plan = await PlanAsync(worker, instance, resource);
        Assert.False(plan.RequiresPythonDependencies);
        Assert.True(plan.Capability!.Allows(false));
        var execute = Request("install.execute", plan);
        await worker.HandleAsync(execute, default);
        Assert.Equal("Completed", (await WaitAsync(worker, execute.RequestId)).State);
        Assert.Equal(resource.Sha256, await ResourceImportService.HashAsync(Path.Combine(instance.CustomNodesDirectory, "fixture", "__init__.py")));
    }

    [Theory]
    [InlineData("requirements.txt")]
    [InlineData("PYPROJECT.TOML")]
    public async Task Dependency_detection_uses_target_name_even_when_staging_renames_source(string metadata)
    {
        var (instance, resource) = await FixtureAsync("plain");
        resource = resource with { TargetRelativePath = "custom_nodes/fixture/" + metadata };
        var plan = await new ResourceInstallationService(Path.Combine(_root, "library")).PlanAsync(instance, [resource]);
        Assert.True(plan.RequiresPythonDependencies);
        var sources = PythonDependencyService.Inspect(plan);
        Assert.Equal(1, sources.RequirementFiles.Count + sources.ProjectFiles.Count);
    }

    private async Task<(InstanceDescriptor, ImportResource)> FixtureAsync(string sourceKind)
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "Desktop.exe"); await File.WriteAllTextAsync(executable, "fixture executable");
        var target = Path.Combine(_root, "target");
        var instance = new InstanceDescriptor("fixture", "Fixture", "desktop-2", executable, target, target, target,
            Path.Combine(target, "user"), Path.Combine(target, "user", "default", "workflows"), Path.Combine(target, "custom_nodes"),
            null, [], Path.Combine(target, "models"), [], "fixture-fingerprint", [])
            { ConfigurationRoot = Path.Combine(_root, "profile"), DesktopLayout = "standalone-native" };
        var name = sourceKind is "plain" or "declared" ? "__init__.py" : sourceKind;
        var content = sourceKind switch
        {
            "requirements.txt" => "six==1.17.0\n",
            "pyproject.toml" => "[project]\nname = 'fixture'\nversion = '1.0'\ndependencies = ['six==1.17.0']\n",
            _ => "NODE_CLASS_MAPPINGS = {}\n"
        };
        var source = Path.Combine(_root, name); await File.WriteAllTextAsync(source, content);
        var resource = new ImportResource("fixture", source, name, ResourceKind.CustomNode, "custom_nodes/fixture/" + name,
            new FileInfo(source).Length, await ResourceImportService.HashAsync(source), RecognitionState.Confirmed, "test fixture")
            { DeclaredPythonDependencies = sourceKind == "declared" ? ["six==1.17.0"] : [] };
        return (instance, resource);
    }

    private async Task<PersistentWorkerService> WorkerAsync(InstanceDescriptor instance, bool pythonQualified)
    {
        var capabilities = new DeploymentCapabilityProvider([new("1.1.4", "standalone-native", pythonQualified, "unit-test-fixture")], _ => "1.1.4.0");
        var worker = new PersistentWorkerService(new(Path.Combine(_root, "library")),
            _ => Task.FromResult<IReadOnlyList<InstanceDescriptor>>([instance]), capabilities);
        await worker.InitializeAsync(); return worker;
    }

    private static async Task<ResourceInstallPlan> PlanAsync(PersistentWorkerService worker, InstanceDescriptor instance, ImportResource resource)
    {
        var request = Request("install.plan", new InstallPlanningInput(instance, [resource]));
        await worker.HandleAsync(request, default);
        var job = await WaitAsync(worker, request.RequestId);
        Assert.Equal("Completed", job.State);
        var plan = job.Result!.Value.Deserialize<ResourceInstallPlan>()!;
        Assert.Empty(plan.BlockingReasons); return plan;
    }

    private static WorkerRequest Request(string command, object payload) =>
        new(WorkerProtocol.Version, Guid.NewGuid().ToString("N"), "fixture", command, JsonSerializer.SerializeToElement(payload));

    private static async Task<WorkerJob> WaitAsync(PersistentWorkerService worker, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var response = await worker.HandleAsync(Request("job.get", id), timeout.Token);
            var job = response.Payload!.Value.Deserialize<WorkerJob>()!;
            if (job.State is not ("Queued" or "Running" or "PauseRequested" or "CancelRequested")) return job;
            await Task.Delay(10, timeout.Token);
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
