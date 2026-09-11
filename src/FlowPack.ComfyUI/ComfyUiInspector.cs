using FlowPack.Core;

namespace FlowPack.ComfyUI;

/// <summary>
/// Performs a deliberately conservative, read-only check for known local ComfyUI layouts.
/// A directory is not an instance merely because it contains a file named python.exe.
/// </summary>
public sealed class ComfyUiInspector : IInstanceInspector
{
    private static readonly string[] KnownInterpreterRelativePaths =
    [
        Path.Combine("python_embeded", "python.exe"),
        Path.Combine("python_embedded", "python.exe"),
        Path.Combine("venv", "Scripts", "python.exe"),
        "python.exe"
    ];

    public Task<InstanceFingerprint?> InspectAsync(string candidatePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(candidatePath) || !Directory.Exists(candidatePath))
        {
            return Task.FromResult<InstanceFingerprint?>(null);
        }

        var root = Path.GetFullPath(candidatePath);
        var mainScript = Path.Combine(root, "main.py");
        var userDirectory = Path.Combine(root, "user");
        if (!File.Exists(mainScript) || !Directory.Exists(userDirectory))
        {
            return Task.FromResult<InstanceFingerprint?>(null);
        }

        var interpreters = KnownInterpreterRelativePaths
            .Select(relativePath => Path.Combine(root, relativePath))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (interpreters.Length != 1)
        {
            return Task.FromResult<InstanceFingerprint?>(null);
        }

        return Task.FromResult<InstanceFingerprint?>(
            new InstanceFingerprint(root, interpreters[0], userDirectory, null, DateTimeOffset.UtcNow));
    }
}
