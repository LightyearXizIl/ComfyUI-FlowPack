using System.IO;
using System.Text.Json;
using FlowPack.App;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class PortableWorkspaceTests
{
    [Fact]
    public async Task Portable_library_binding_moves_with_package_and_external_library_stays_absolute()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "flowpack-portable-" + Guid.NewGuid().ToString("N"));
        try
        {
            var original = Path.Combine(sandbox, "original");
            var moved = Path.Combine(sandbox, "moved");
            var file = Path.Combine(original, "Data", "Preferences", "library-binding.json");
            var library = Path.Combine(original, "Data", "Library");
            var store = new LibraryBindingStore(file, original);
            await store.SaveAsync(new(library, DateTimeOffset.UtcNow));
            var serialized = JsonSerializer.Deserialize<LibraryBinding>(await File.ReadAllTextAsync(file))!;
            Assert.False(Path.IsPathRooted(serialized.LibraryPath));
            Directory.Move(original, moved);
            var relocated = new LibraryBindingStore(Path.Combine(moved, "Data", "Preferences", "library-binding.json"), moved);
            Assert.Equal(Path.Combine(moved, "Data", "Library"), relocated.Load()!.LibraryPath);
            Assert.Equal(relocated.Load(), await relocated.LoadAsync());
            var external = Path.Combine(sandbox, "external");
            await relocated.SaveAsync(new(external, DateTimeOffset.UtcNow));
            Assert.Equal(external, JsonSerializer.Deserialize<LibraryBinding>(await File.ReadAllTextAsync(relocated.FilePath))!.LibraryPath);
            Assert.Equal(external, relocated.Load()!.LibraryPath);
        }
        finally { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); }
    }

    [Fact]
    public void Ordinary_startup_stays_ordinary_and_portable_marker_or_argument_selects_local_state()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "flowpack-startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = Path.Combine(sandbox, "App");
            Directory.CreateDirectory(app);
            Assert.Null(PortableWorkspace.FromStartup(app, []));
            File.WriteAllText(Path.Combine(app, "portable.mode"), "");
            var workspace = PortableWorkspace.FromStartup(app, [])!;
            Assert.Equal(sandbox, workspace.Root);
            Assert.Equal(Path.Combine(sandbox, "Data", "Library"), workspace.Library);
            var explicitRoot = Path.Combine(sandbox, "live");
            Assert.Equal(explicitRoot, PortableWorkspace.FromStartup(app, ["--portable-root", explicitRoot])!.Root);
            Assert.Throws<ArgumentException>(() => { PortableWorkspace.FromStartup(app, ["--portable-root"]); });
            Assert.Throws<ArgumentException>(() => { PortableWorkspace.FromStartup(app, ["--portable-root", "relative"]); });
        }
        finally { Directory.Delete(sandbox, true); }
    }
}
