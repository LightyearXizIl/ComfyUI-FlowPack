using System.IO;
using System.Text.Json;
using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class DesktopProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FlowPack-profile-" + Guid.NewGuid().ToString("N"));

    private string MakeProfile(string name)
    {
        var profile = Path.Combine(_root, name);
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "installations.json"), "[]");
        File.WriteAllText(Path.Combine(profile, "settings.json"), "{}");
        return profile;
    }

    [Fact]
    public void Explicit_profile_is_absolute_validated_and_passed_through_portable_and_shell_configuration()
    {
        var profile = MakeProfile("profile");
        var portable = PortableWorkspace.FromStartup(_root, ["--portable-root", Path.Combine(_root, "portable"), "--desktop-profile", profile + Path.DirectorySeparatorChar])!;
        Assert.Equal(profile, portable.DesktopProfile);
        Assert.Equal(profile, portable.CreateViewModel().DesktopProfile);
        Assert.Null(DesktopProfileOptions.FromArguments([]));
        Assert.Null(new WorkerLibraryClient(Path.Combine(_root, "library")).DesktopProfile);
        Assert.Equal(profile, new WorkerLibraryClient(Path.Combine(_root, "library"), desktopProfile: profile + Path.DirectorySeparatorChar).DesktopProfile);
        Assert.Throws<ArgumentException>(() => DesktopProfileOptions.FromArguments(["--desktop-profile"]));
        Assert.Throws<ArgumentException>(() => DesktopProfileOptions.FromArguments(["--desktop-profile", "relative"]));
        Assert.Throws<ArgumentException>(() => DesktopProfileOptions.FromArguments(["--desktop-profile", profile, "--desktop-profile", profile]));
        Assert.Throws<DirectoryNotFoundException>(() => DesktopProfileOptions.FromArguments(["--desktop-profile", Path.Combine(_root, "missing")]));
    }

    [Fact]
    public void Missing_or_invalid_explicit_registration_fails_without_falling_back()
    {
        var profile = MakeProfile("invalid");
        var registry = Path.Combine(profile, "installations.json");
        File.Delete(registry);
        Assert.Throws<FileNotFoundException>(() => new WorkerLibraryClient(Path.Combine(_root, "library"), desktopProfile: profile));
        File.WriteAllText(registry, "{}");
        Assert.Throws<InvalidDataException>(() => DesktopProfileOptions.NormalizeAndValidate(profile));
        File.WriteAllText(registry, "[1]");
        Assert.Throws<InvalidDataException>(() => DesktopProfileOptions.NormalizeAndValidate(profile));
        File.WriteAllText(registry, "[]");
        File.WriteAllText(Path.Combine(profile, "settings.json"), "[]");
        Assert.Throws<InvalidDataException>(() => DesktopProfileOptions.NormalizeAndValidate(profile));
        Assert.False(Directory.Exists(Path.Combine(_root, "library")));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task Live_session_requires_same_profile_and_legacy_session_only_allows_default(bool explicitProfile, bool matchingProfile, bool differentProfile)
    {
        var profile = MakeProfile("profile");
        var library = Path.Combine(_root, "library");
        Directory.CreateDirectory(Path.Combine(library, "state"));
        var pipe = "flowpack-profile-test-" + Guid.NewGuid().ToString("N");
        var storedProfile = matchingProfile ? (differentProfile ? MakeProfile("other") : profile.ToUpperInvariant() + Path.DirectorySeparatorChar) : null;
        var session = storedProfile is null ? JsonSerializer.Serialize(new { Pipe = pipe, Secret = "test" })
            : JsonSerializer.Serialize(new { Pipe = pipe, Secret = "test", DesktopProfile = storedProfile });
        await File.WriteAllTextAsync(Path.Combine(library, "state", "worker-session.json"), session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accepts = explicitProfile == matchingProfile && !differentProfile;
        var server = new NamedPipeWorkerServer(pipe, "test");
        var serving = Task.Run(async () =>
        {
            for (var count = 0; count < (accepts ? 2 : 1); count++)
                await server.ServeOnceAsync((request, _) => Task.FromResult(new WorkerResponse(request.RequestId, true, JsonSerializer.SerializeToElement(true))), timeout.Token);
        });
        try
        {
            await using var client = new WorkerLibraryClient(library, allowWorkerLaunch: false, desktopProfile: explicitProfile ? profile : null);
            if (accepts) await client.InitializeAsync(timeout.Token);
            else
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitializeAsync(timeout.Token));
                Assert.Contains("另一份 Desktop 配置", exception.Message);
            }
            await serving;
            Assert.False(File.Exists(Path.Combine(library, "state", "worker.lock")));
        }
        finally
        {
            timeout.Cancel();
            try { await serving; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Explicit_default_named_profile_still_excludes_legacy_registration()
    {
        var paths = new ProfilePaths(_root);
        var profile = MakeProfile(Path.Combine("roaming", "Comfy Desktop"));
        var legacy = Path.Combine(paths.RoamingApplicationData, "ComfyUI");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "config.json"), JsonSerializer.Serialize(new { basePath = _root }));
        Assert.Empty(await new DesktopInstanceDiscovery(paths, profile).DiscoverAsync());
        Assert.Single(await new DesktopInstanceDiscovery(paths).DiscoverAsync());
    }

    [Fact]
    public void Profile_selection_does_not_grant_installation_capabilities()
    {
        var profile = MakeProfile("profile");
        var instance = new InstanceDescriptor("a", "a", "desktop-2", null, _root, _root, _root,
            Path.Combine(_root, "user"), Path.Combine(_root, "workflows"), Path.Combine(_root, "nodes"), null,
            [], Path.Combine(_root, "models"), [], "fingerprint", []) { ConfigurationRoot = profile, DesktopLayout = "standalone-adopted" };
        Assert.False(new DeploymentCapabilityProvider().Evaluate(instance, false).Allows(false));
    }

    [Fact]
    public async Task Explicit_profile_association_rejects_unregistered_core_while_default_keeps_readonly_association()
    {
        var profile = MakeProfile("profile");
        var outside = Path.Combine(_root, "outside-core");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "main.py"), "# readonly fixture");
        var paths = new ProfilePaths(_root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DesktopInstanceDiscovery(paths, profile).AssociateAsync(outside));
        Assert.Equal("manual-readonly", (await new DesktopInstanceDiscovery(paths).AssociateAsync(outside)).Generation);
        var discovery = new DesktopInstanceDiscovery(paths, profile);
        await using var worker = new PersistentWorkerService(new ResourceLibraryDatabase(Path.Combine(_root, "library")), discovery.DiscoverAsync,
            associate: discovery.AssociateAsync);
        await worker.InitializeAsync();
        var request = new WorkerRequest(WorkerProtocol.Version, Guid.NewGuid().ToString("N"), "test", "instance.associate", JsonSerializer.SerializeToElement(outside));
        Assert.True((await worker.HandleAsync(request, default)).Succeeded);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        WorkerJob job;
        do
        {
            var response = await worker.HandleAsync(new(WorkerProtocol.Version, Guid.NewGuid().ToString("N"), "test", "job.get", JsonSerializer.SerializeToElement(request.RequestId)), timeout.Token);
            job = response.Payload!.Value.Deserialize<WorkerJob>()!;
            if (job.State is "Queued" or "Running") await Task.Delay(10, timeout.Token);
        } while (job.State is "Queued" or "Running");
        Assert.Equal("Failed", job.State);
        Assert.Contains("指定的 Desktop 配置", job.Error);
        Assert.Null(job.Result);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class ProfilePaths(string root) : IDesktopPathProvider
    {
        public string LocalApplicationData => Path.Combine(root, "local");
        public string RoamingApplicationData => Path.Combine(root, "roaming");
        public string MyDocuments => Path.Combine(root, "documents");
        public string UserProfile => Path.Combine(root, "user");
        public string SystemDrive => Path.GetPathRoot(root)!;
        public IReadOnlyList<string> GetProcessModulePaths(string processName) => [];
        public IReadOnlyList<string> GetRegisteredDesktopExecutables() => [];
    }
}
