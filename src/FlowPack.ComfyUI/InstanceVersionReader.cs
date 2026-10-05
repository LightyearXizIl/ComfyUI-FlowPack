using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FlowPack.ComfyUI;

public static class InstanceVersionReader
{
    public static string? Core(string root)
    {
        try
        {
            var file = Path.Combine(root, "comfyui_version.py");
            if (!File.Exists(file) || new FileInfo(file).Length > 65536) return null;
            var match = Regex.Match(File.ReadAllText(file), "(?m)^__version__\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.None, TimeSpan.FromSeconds(1));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    public static string? Desktop(string? executable)
    {
        try { return executable is not null && File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).ProductVersion : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
