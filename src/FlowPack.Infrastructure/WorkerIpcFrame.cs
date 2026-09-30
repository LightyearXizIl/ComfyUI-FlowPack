using System.Text;

namespace FlowPack.Infrastructure;

internal static class WorkerIpcFrame
{
    // Resource inventories contain paths and hashes, never binary model contents.
    public const int MaximumCharacters = 64 * 1024 * 1024;
    public static async Task<string?> ReadAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[8192];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) return result.Length == 0 ? null : result.ToString();
            var newline = Array.IndexOf(buffer, '\n', 0, count);
            var append = newline >= 0 ? newline : count;
            if (result.Length + append > MaximumCharacters) throw new IOException("IPC 资源清单超过 64 MiB，请减少本次选择数量。");
            result.Append(buffer, 0, append);
            if (newline >= 0) return result.ToString().TrimEnd('\r');
        }
    }
}
