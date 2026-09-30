using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class StartupPreferenceTests
{
    [Fact]
    public async Task Startup_reads_saved_preferences_without_dispatcher_continuations()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bindingStore = new LibraryBindingStore(Path.Combine(root, "binding.json"));
            var themeStore = new ThemePreferenceStore(Path.Combine(root, "theme.json"));
            var binding = new LibraryBinding(root, DateTimeOffset.UtcNow);
            var theme = ThemeDefaults.Create(ThemeBase.Dark);
            await bindingStore.SaveAsync(binding);
            await themeStore.SaveAsync(theme);
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new NoDispatchContext());
                Assert.Equal(binding, bindingStore.Load());
                Assert.Equal(theme, themeStore.Load());
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Invalid_saved_preferences_produce_recoverable_errors()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowPack-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "invalid.json");
            File.WriteAllText(file, "{");
            Assert.Throws<InvalidDataException>(() => new LibraryBindingStore(file).Load());
            Assert.Throws<InvalidDataException>(() => new ThemePreferenceStore(file).Load());
            Assert.Null(new LibraryBindingStore(Path.Combine(root, "absent.json")).Load());
            Assert.Null(new ThemePreferenceStore(Path.Combine(root, "absent.json")).Load());
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class NoDispatchContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            throw new InvalidOperationException("Startup must not wait for a dispatcher continuation.");
    }
}
