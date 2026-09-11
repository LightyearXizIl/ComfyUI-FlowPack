namespace FlowPack.Infrastructure;

public static class ResourceLibraryPathValidator
{
    public static bool IsSafeLibraryPath(string libraryPath, string appDirectory, string? desktopDirectory, out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        var fullLibrary = Normalize(libraryPath);
        var fullApp = Normalize(appDirectory);
        if (IsSameOrDescendant(fullLibrary, fullApp)) { reason = "资源库不能放在 FlowPack 程序目录中。"; return false; }
        if (!string.IsNullOrWhiteSpace(desktopDirectory) && IsSameOrDescendant(fullLibrary, Normalize(desktopDirectory))) { reason = "资源库不能放在 ComfyUI Desktop 程序目录中。"; return false; }
        reason = string.Empty; return true;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrDescendant(string candidate, string parent) =>
        candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
