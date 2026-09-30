using System.IO.Compression;
using System.Text.Json;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public sealed record ExportFile(string SourcePath, string ArchivePath, long SizeBytes, string Sha256);
public sealed record ExportPlan(string Id, IReadOnlyList<ExportFile> Files, IReadOnlyList<string> Issues, long TotalBytes);

public sealed class PlannedZipExportService
{
    public async Task<ExportPlan> PlanAsync(IReadOnlyList<LocalResource> resources, IReadOnlyList<string>? dependencyIssues = null, CancellationToken token = default)
    {
        var entries = new Dictionary<string, ExportFile>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>(dependencyIssues ?? []);
        foreach (var resource in resources.DistinctBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase))
        {
            if (resource.Kind == ResourceKind.Model && resource.ModelDirectoryRoot is { } bundleRoot)
            {
                var bundle = ModelDirectory.Read(bundleRoot);
                issues.AddRange(bundle.Issues);
                var suffix = Directory.Exists(resource.SourcePath) ? "" : ResourceFiles.Relative(bundleRoot, resource.SourcePath);
                var prefix = suffix.Length == 0 ? resource.RelativePath : resource.RelativePath[..^suffix.Length].TrimEnd('/');
                foreach (var file in bundle.Files)
                {
                    var relative = "models/" + prefix + "/" + ResourceFiles.Relative(bundleRoot, file);
                    ValidateRelative(relative);
                    var entry = new ExportFile(file, relative, new FileInfo(file).Length, await ResourceImportService.HashAsync(file, token));
                    if (entries.TryGetValue(relative, out var previous) && previous.Sha256 != entry.Sha256) issues.Add("同名异内容资源不能改名打包：" + relative);
                    else entries.TryAdd(relative, entry);
                }
                continue;
            }
            var singleNode = resource.Kind == ResourceKind.CustomNode && File.Exists(resource.SourcePath);
            var files = resource.Kind == ResourceKind.CustomNode && !singleNode ? ResourceFiles.Enumerate(resource.SourcePath) :
                resource.Kind == ResourceKind.Model ? ModelFiles(resource.SourcePath) : [resource.SourcePath];
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var relative = resource.Kind == ResourceKind.CustomNode ? singleNode ? "custom_nodes/" + Path.GetFileName(resource.SourcePath) : "custom_nodes/" + resource.Name + "/" + ResourceFiles.Relative(resource.SourcePath, file) :
                    resource.Kind == ResourceKind.Model ? "models/" + (file == resource.SourcePath ? resource.RelativePath :
                        ((Path.GetDirectoryName(resource.RelativePath)?.Replace('\\', '/') is { Length: > 0 } folder ? folder + "/" : "") + ResourceFiles.Relative(Path.GetDirectoryName(resource.SourcePath)!, file))) : resource.RelativePath;
                ValidateRelative(relative);
                var entry = new ExportFile(file, relative, new FileInfo(file).Length, await ResourceImportService.HashAsync(file, token));
                if (entries.TryGetValue(relative, out var existing))
                {
                    if (existing.Sha256 != entry.Sha256) issues.Add("同名异内容资源不能改名打包：" + relative);
                }
                else entries.Add(relative, entry);
            }
        }
        return new(Guid.NewGuid().ToString("N"), entries.Values.ToArray(), issues.Distinct().ToArray(), entries.Values.Sum(x => x.SizeBytes));
    }

    public async Task ExportAsync(ExportPlan plan, string output, bool allowPartial = false, CancellationToken token = default)
    {
        if (plan.Files.Count == 0) throw new InvalidDataException("没有选择可以导出的文件。");
        if (plan.Issues.Any(x => x.StartsWith("同名异内容")) || (!allowPartial && plan.Issues.Count > 0))
            throw new InvalidDataException(string.Join("\n", plan.Issues));
        output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new IOException("导出文件已存在，请选择新文件名。");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + "." + plan.Id + ".tmp";
        var ownsTemporary = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                ownsTemporary = true;
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                foreach (var file in plan.Files)
                {
                    token.ThrowIfCancellationRequested();
                    ValidateRelative(file.ArchivePath);
                    await using var source = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                    if (source.Length != file.SizeBytes) throw new IOException("导出源文件已改变：" + file.ArchivePath);
                    var entry = zip.CreateEntry(file.ArchivePath, ResourceFiles.IsModel(file.SourcePath) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    await using var destination = entry.Open();
                    using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                    var buffer = new byte[131072]; int read;
                    while ((read = await source.ReadAsync(buffer, token)) > 0)
                    { hash.AppendData(buffer, 0, read); await destination.WriteAsync(buffer.AsMemory(0, read), token); }
                    if (Convert.ToHexString(hash.GetHashAndReset()) != file.Sha256) throw new IOException("导出过程中源文件已改变：" + file.ArchivePath);
                }
                var manifest = zip.CreateEntry("flowpack-manifest.json");
                await using var metadata = manifest.Open();
                await JsonSerializer.SerializeAsync(metadata, new { formatVersion = "1", complete = plan.Issues.Count == 0,
                    issues = plan.Issues, resources = plan.Files.Select(x => new { path = x.ArchivePath, size = x.SizeBytes, sha256 = x.Sha256 }) }, cancellationToken: token);
            }
            File.Move(temporary, output, overwrite: false);
        }
        finally { if (ownsTemporary && File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') ||
            path.Replace('\\', '/').Split('/').Any(x => x is "" or "." or ".." || x.EndsWith(' ') || x.EndsWith('.') ||
                x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(x, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("不安全的资源相对路径：" + path);
    }
    private static IEnumerable<string> ModelFiles(string source)
    {
        var directory = Path.GetDirectoryName(source)!;
        // A config filename alone does not grant ownership of neighboring files.
        return new[] { source, Path.ChangeExtension(source, ".json"), Path.ChangeExtension(source, ".yaml") }.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
