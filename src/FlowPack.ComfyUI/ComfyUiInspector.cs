using FlowPack.Core;

namespace FlowPack.ComfyUI;

public sealed class ComfyUiInspector : IInstanceInspector
{
    public Task<InstanceFingerprint?> InspectAsync(string candidatePath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(candidatePath)) return Task.FromResult<InstanceFingerprint?>(null);
        var python = Directory.EnumerateFiles(candidatePath, "python*.exe", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
        return Task.FromResult<InstanceFingerprint?>(new(candidatePath, python, Path.Combine(candidatePath, "user"), null, DateTimeOffset.UtcNow));
    }
}
