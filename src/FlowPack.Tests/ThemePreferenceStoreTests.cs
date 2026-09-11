using System.IO;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Tests;

public sealed class ThemePreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"FlowPack.ThemeTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Save_then_load_preserves_every_supported_theme_parameter()
    {
        var store = new ThemePreferenceStore(Path.Combine(_directory, "theme.flowpack-theme.json"));
        var expected = new ThemeDefinition("1", "我的深色主题", ThemeBase.Dark, "#5A9BFF", "#19253A", "#F1F6FF", 20, 16, ThemeDensity.Compact, false);

        await store.SaveAsync(expected);

        Assert.Equal(expected, await store.LoadAsync());
    }

    [Fact]
    public async Task Invalid_import_does_not_replace_the_saved_theme()
    {
        var savedPath = Path.Combine(_directory, "theme.flowpack-theme.json");
        var importPath = Path.Combine(_directory, "invalid.flowpack-theme.json");
        var store = new ThemePreferenceStore(savedPath);
        var expected = ThemeDefaults.Create(ThemeBase.Light);
        await store.SaveAsync(expected);
        await File.WriteAllTextAsync(importPath, "{ \"formatVersion\": \"1\", \"name\": \"坏主题\" }");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(importPath));

        Assert.Equal(expected, await store.LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
