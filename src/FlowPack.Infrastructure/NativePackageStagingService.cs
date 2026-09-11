using System.IO.Compression;

namespace FlowPack.Infrastructure;

public sealed record StagedNativePackage(string RootPath, int FileCount, long UncompressedBytes);

/// <summary>
/// Extracts an arbitrary native ComfyUI zip into a private staging directory. It deliberately
/// has no knowledge of Desktop target paths: successful extraction is still only an input to a
/// later, reviewed installation plan.
/// </summary>
public sealed class NativePackageStagingService
{
    public const int DefaultMaximumEntries = 10_000;
    public const long DefaultMaximumUncompressedBytes = 20L * 1024 * 1024 * 1024;

    public async Task<StagedNativePackage> StageAsync(
        string zipPath,
        string stagingRoot,
        int maximumEntries = DefaultMaximumEntries,
        long maximumUncompressedBytes = DefaultMaximumUncompressedBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        if (maximumEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumUncompressedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumUncompressedBytes));
        if (!File.Exists(zipPath)) throw new FileNotFoundException("找不到要暂存的 ZIP 资源包。", zipPath);

        var root = Path.GetFullPath(stagingRoot);
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, ".incoming-" + Guid.NewGuid().ToString("N"));
        var final = Path.Combine(root, "package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);

        try
        {
            await using var stream = File.OpenRead(zipPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count > maximumEntries)
            {
                throw new InvalidDataException($"资源包条目数超过安全上限（{maximumEntries}）。");
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            var files = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSafeEntry(entry, paths);
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                totalBytes = checked(totalBytes + entry.Length);
                if (totalBytes > maximumUncompressedBytes)
                {
                    throw new InvalidDataException($"资源包解压后的总大小超过安全上限（{maximumUncompressedBytes} 字节）。");
                }

                var target = ResolveInside(temporary, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var source = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await source.CopyToAsync(output, cancellationToken);
                files++;
            }

            Directory.Move(temporary, final);
            return new StagedNativePackage(final, files, totalBytes);
        }
        catch
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            throw;
        }
    }

    private static void EnsureSafeEntry(ZipArchiveEntry entry, ISet<string> paths)
    {
        var path = entry.FullName.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("\\", StringComparison.Ordinal) || path.Contains(":", StringComparison.Ordinal) ||
            path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".." || IsReservedWindowsName(segment)))
        {
            throw new InvalidDataException($"资源包包含不安全路径：{entry.FullName}");
        }
        if (IsSymbolicLink(entry)) throw new InvalidDataException($"资源包不允许符号链接：{entry.FullName}");
        if (!paths.Add(path)) throw new InvalidDataException($"资源包包含重复路径：{entry.FullName}");
    }

    private static string ResolveInside(string root, string entryPath)
    {
        var target = Path.GetFullPath(Path.Combine(root, entryPath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"资源包路径越出暂存目录：{entryPath}");
        }
        return target;
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixType == 0xA000;
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var name = Path.GetFileNameWithoutExtension(segment).TrimEnd(' ', '.');
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
        return name.Length == 4 &&
               ((name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                name[3] is >= '1' and <= '9');
    }
}
