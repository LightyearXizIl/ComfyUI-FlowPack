using FlowPack.Core;

namespace FlowPack.Infrastructure;

internal static class ProgressIO
{
    public static async Task CopyAsync(Stream source, Stream destination, long total, string stage,
        IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var buffer = new byte[131072]; long completed = 0; int read;
        progress?.Report(new(stage, 0, total, "字节"));
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            completed += read; progress?.Report(new(stage, completed, total, "字节"));
        }
    }
}
