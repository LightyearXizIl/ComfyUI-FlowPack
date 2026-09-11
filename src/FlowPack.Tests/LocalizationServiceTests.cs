using System;
using System.IO;
using FlowPack.App.Services;

namespace FlowPack.Tests;

public sealed class LocalizationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flowpack-language-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Language_switch_raises_indexed_binding_notification_and_persists_preference()
    {
        var preference = Path.Combine(_directory, "language.json");
        var service = new LocalizationService(preference);
        var itemChanged = false;
        service.PropertyChanged += (_, eventArgs) => itemChanged |= eventArgs.PropertyName == "Item[]";

        service.SetLanguage(AppLanguage.EnUs);

        Assert.True(itemChanged);
        Assert.Equal("Home", service["Nav.Home"]);
        Assert.Equal(AppLanguage.EnUs, new LocalizationService(preference).Language);
    }

    [Fact]
    public void Missing_resource_key_is_visible_instead_of_being_silently_blank()
    {
        var service = new LocalizationService(Path.Combine(_directory, "language.json"));

        Assert.Equal("Missing.Key", service["Missing.Key"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
