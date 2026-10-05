using System.IO;
using FlowPack.App.Services;
using FlowPack.ComfyUI;
using FlowPack.Infrastructure;

namespace FlowPack.App;

/// <summary>Portable builds and source previews keep their state beside, never inside, the application files.</summary>
public sealed class PortableWorkspace(string root, string? desktopProfile = null)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string? DesktopProfile { get; } = DesktopProfileOptions.NormalizeAndValidate(desktopProfile);
    public string Preferences => Path.Combine(Root, "Data", "Preferences");
    public string Library => Path.Combine(Root, "Data", "Library");

    public static PortableWorkspace? FromStartup(string executableDirectory, IReadOnlyList<string> arguments)
    {
        var desktopProfile = DesktopProfileOptions.FromArguments(arguments);
        var option = arguments.ToList().IndexOf("--portable-root");
        if (option >= 0)
        {
            if (option + 1 >= arguments.Count || !Path.IsPathFullyQualified(arguments[option + 1]))
                throw new ArgumentException("--portable-root requires an absolute directory path.");
            return new(arguments[option + 1], desktopProfile);
        }
        return File.Exists(Path.Combine(executableDirectory, "portable.mode"))
            ? new(Path.GetFullPath(Path.Combine(executableDirectory, "..")), desktopProfile) : null;
    }

    public ShellViewModel CreateViewModel() => new(
        themeStore: new ThemePreferenceStore(Path.Combine(Preferences, "theme.flowpack-theme.json")),
        libraryBindingStore: new LibraryBindingStore(Path.Combine(Preferences, "library-binding.json"), Root),
        desktopDetector: new ComfyDesktopDetector(),
        localization: new LocalizationService(Path.Combine(Preferences, "language.json")),
        defaultLibraryPath: Library,
        applicationLog: new ApplicationLog(Path.Combine(Preferences, "logging.json"), Path.Combine(Root, "Data", "Logs")),
        desktopProfile: DesktopProfile);
}
