using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Sockets;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class RuntimeNodeInspectorTests
{
    [Fact]
    public void Venv_base_requires_one_absolute_home_and_exact_python_executable()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-venv-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Scripts"));
        try
        {
            var launcher = Path.Combine(root, "Scripts", "python.exe");
            var basis = Path.Combine(root, "base"); var executable = Path.Combine(basis, "python.exe");
            var config = Path.Combine(root, "pyvenv.cfg");
            File.WriteAllText(config, "home = " + basis);
            Assert.True(DesktopRuntimeEndpointResolver.MatchesVenvBase(launcher, executable));
            Assert.False(DesktopRuntimeEndpointResolver.MatchesVenvBase(launcher, Path.Combine(root, "other", "python.exe")));
            Assert.False(DesktopRuntimeEndpointResolver.MatchesVenvBase(launcher, Path.Combine(basis, "unrelated.exe")));
            File.WriteAllText(config, "home = relative");
            Assert.False(DesktopRuntimeEndpointResolver.MatchesVenvBase(launcher, executable));
            File.WriteAllText(config, "home = " + basis + "\nhome = " + basis);
            Assert.False(DesktopRuntimeEndpointResolver.MatchesVenvBase(launcher, executable));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Official_lock_is_cross_checked_against_actual_windows_listener_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-runtime-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "port-locks"));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var process = Process.GetCurrentProcess();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var path = Path.Combine(root, "port-locks", $"port-{port}.json");
            var instance = Instance with { PythonPath = process.MainModule!.FileName };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { pid = process.Id, installationName = instance.Name,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));
            var resolver = new DesktopRuntimeEndpointResolver(root);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var bound = await resolver.ResolveAsync(instance, [instance], timeout.Token);
            Assert.NotNull(bound); Assert.Equal(port, bound.Port); Assert.Equal(process.Id, bound.ProcessId);
            Assert.Null(await resolver.ResolveAsync(instance, [instance, instance with { Id = "duplicate-name-shared-python" }], timeout.Token));
            listener.Stop();
            Assert.Null(await resolver.ResolveAsync(instance, [instance], timeout.Token));
            Assert.True(File.Exists(path)); // Read-only: no stale Desktop lock is removed.
        }
        finally { listener.Stop(); Directory.Delete(root, true); }
    }

    private static InstanceDescriptor Instance => new("a", "A", "desktop-2", null, @"C:\instances\A", @"C:\instances\A\ComfyUI",
        @"C:\data\A", @"C:\data\A\user", @"C:\data\A\user\default\workflows", @"C:\data\A\custom_nodes",
        @"C:\data\A\.venv\Scripts\python.exe", [], @"C:\models", [], "fingerprint-A", []);
    private static string[] Arguments => ["ComfyUI/main.py", "--base-directory", Instance.DataDirectory, "--user-directory", Instance.UserDirectory];

    [Fact]
    public void Runtime_arguments_must_match_data_user_and_core_not_just_a_port()
    {
        Assert.True(RuntimeNodeInspector.MatchesPaths(Instance, Arguments));
        Assert.False(RuntimeNodeInspector.MatchesPaths(Instance, ["ComfyUI/main.py"]));
        Assert.False(RuntimeNodeInspector.MatchesPaths(Instance, ["ComfyUI/main.py", "--base-directory", @"C:\data\B", "--user-directory", Instance.UserDirectory]));
        Assert.False(RuntimeNodeInspector.MatchesPaths(Instance, [@"C:\other\main.py", "--base-directory", Instance.DataDirectory, "--user-directory", Instance.UserDirectory]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_bound_runtime_node_types_satisfy_local_dependencies(bool loaded)
    {
        var resource = new LocalResource("p", ResourceKind.CustomNode, "package", Path.Combine(Instance.CustomNodesDirectory, "package"),
            "custom_nodes/package", NodeTypes: ["Custom"]);
        var workflow = WorkflowDocumentFactory.Create("w", "w", """{"version":1,"nodes":[{"id":1,"type":"Custom"}]}""");
        using var http = new HttpClient(new Responses(loaded, Arguments));
        var inventory = await new RuntimeNodeInspector(http, new Endpoint()).InspectAsync(new(Instance, [resource], [], []), [Instance]);
        Assert.Equal(Instance.ConfigurationFingerprint, inventory.RuntimeFingerprint);
        Assert.True(Assert.Single(inventory.Resources).RuntimeChecked);
        var dependency = Assert.Single(new InventoryDependencyAnalyzer().Analyze([workflow], inventory).Dependencies);
        Assert.Equal(loaded ? DependencyState.Present : DependencyState.Unresolved, dependency.State);
        Assert.Contains(loaded ? "已加载" : "没有加载", dependency.Evidence);
    }

    [Fact]
    public async Task Wrong_instance_and_restarted_process_do_not_reuse_loaded_flags()
    {
        var resource = new LocalResource("p", ResourceKind.CustomNode, "package", Path.Combine(Instance.CustomNodesDirectory, "package"),
            "custom_nodes/package", NodeTypes: ["Custom"]) { RuntimeChecked = true, LoadedNodeTypes = ["Custom"] };
        var prior = new ResourceInventory(Instance, [resource], [], []) { RuntimeFingerprint = "stale" };
        using var wrong = new HttpClient(new Responses(true, ["ComfyUI/main.py", "--base-directory", @"C:\wrong"]));
        var result = await new RuntimeNodeInspector(wrong, new Endpoint()).InspectAsync(prior, [Instance]);
        Assert.Null(result.RuntimeFingerprint); Assert.False(result.Resources[0].RuntimeChecked);
        using var correct = new HttpClient(new Responses(true, Arguments));
        result = await new RuntimeNodeInspector(correct, new Endpoint(restart: true)).InspectAsync(prior, [Instance]);
        Assert.Null(result.RuntimeFingerprint); Assert.Empty(result.Resources[0].LoadedNodeTypes);
    }

    private sealed class Endpoint(bool restart = false) : IRuntimeEndpointResolver
    {
        private int _calls;
        public Task<RuntimeEndpoint?> ResolveAsync(InstanceDescriptor instance, IReadOnlyList<InstanceDescriptor> peers, CancellationToken token) =>
            Task.FromResult<RuntimeEndpoint?>(new(restart ? ++_calls : 1, 8000, DateTimeOffset.UnixEpoch));
    }
    private sealed class Responses(bool loaded, string[] args) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            var json = request.RequestUri.AbsolutePath == "/system_stats" ? JsonSerializer.Serialize(new { system = new { argv = args } }) :
                loaded ? """{"Custom":{"python_module":"custom_nodes.package"}}""" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
