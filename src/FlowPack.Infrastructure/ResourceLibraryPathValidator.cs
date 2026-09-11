namespace FlowPack.Infrastructure;

public static class ResourceLibraryPathValidator
{
    public static bool IsSafeLibraryPath(string libraryPath, string appDirectory, string? desktopDirectory, out string reason)
    {
        var fullLibrary = Path.GetFullPath(libraryPath).TrimEnd(Path.DirectorySeparatorChar);
        var fullApp = Path.GetFullPath(appDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (fullLibrary.StartsWith(fullApp, StringComparison.OrdinalIgnoreCase)) { reason = "资源库不能放在 FlowPack 程序目录中。"; return false; }
        if (!string.IsNullOrWhiteSpace(desktopDirectory) && fullLibrary.StartsWith(Path.GetFullPath(desktopDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) { reason = "资源库不能放在 ComfyUI Desktop 程序目录中。"; return false; }
        reason = string.Empty; return true;
    }
}
