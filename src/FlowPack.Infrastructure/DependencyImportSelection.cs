using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public static class DependencyImportSelection
{
    public static IReadOnlyList<ImportResource> Model(ImportPlan import, string category, string reference, string? requiredSha256)
    {
        if (string.IsNullOrWhiteSpace(category) || category.Contains('/') || category.Contains('\\')) throw new InvalidDataException("请填写单一模型类别。");
        PlannedZipExportService.ValidateRelative(category);
        category = ResourceFiles.NormalizeCategory(category);
        var relative = reference.Replace('\\', '/');
        var parts = relative.Split('/');
        if (parts.Length > 1 && ResourceFiles.NormalizeCategory(parts[0]) == category) relative = string.Join('/', parts.Skip(1));
        var target = "models/" + category + "/" + relative;
        PlannedZipExportService.ValidateRelative(target);
        if (requiredSha256 is not null && (requiredSha256.Length != 64 || !requiredSha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("模型哈希要求无效。");
        string Canonical(string path)
        {
            var segments = path.Replace('\\', '/').Split('/');
            if (segments.Length > 2 && segments[0] == "models") segments[1] = ResourceFiles.NormalizeCategory(segments[1]);
            return string.Join('/', segments);
        }
        var matches = import.Resources.Where(x => x.Kind == ResourceKind.Model && ResourceFiles.IsModel(x.SourcePath) &&
            Canonical(x.TargetRelativePath).Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray();
        // An explicitly selected single model may have a different filename; never apply this to every ZIP member.
        if (matches.Length == 0 && import.Resources.Count == 1 && import.Resources[0] is { Kind: ResourceKind.Model } single &&
            ResourceFiles.IsModel(single.SourcePath) && Path.GetFullPath(import.Source).Equals(Path.GetFullPath(single.SourcePath), StringComparison.OrdinalIgnoreCase)) matches = [single];
        if (requiredSha256 is not null) matches = matches.Where(x => x.Sha256.Equals(requiredSha256, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1) return [matches[0] with { TargetRelativePath = target, State = RecognitionState.Confirmed,
            Evidence = requiredSha256 is null ? "所需模型的唯一文件匹配；未提供工作流哈希" : "所需模型路径及工作流 SHA-256 匹配" }];
        // A folder reference preserves every recognized member, instead of renaming all members onto one file.
        var members = import.Resources.Where(x => x.Kind == ResourceKind.Model && x.State == RecognitionState.Confirmed &&
            Canonical(x.TargetRelativePath).StartsWith(target + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0 && members.Length > 0 && requiredSha256 is null)
        {
            var descriptor = members.FirstOrDefault(x => Path.GetFileName(x.SourcePath) == "model_index.json" || Path.GetFileName(x.SourcePath) == "config.json");
            if (descriptor is not null)
            {
                var suffix = Canonical(descriptor.TargetRelativePath)[(target.Length + 1)..];
                var root = descriptor.SourcePath;
                foreach (var _ in suffix.Split('/')) root = Path.GetDirectoryName(root)!;
                var bundle = ModelDirectory.Read(root);
                var selected = members.Select(x => Path.GetFullPath(x.SourcePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (bundle.Issues.Count == 0 && bundle.Files.All(x => selected.Contains(Path.GetFullPath(x))))
                    return members.Select(x => x with { TargetRelativePath = Canonical(x.TargetRelativePath) }).ToArray();
            }
        }
        throw new InvalidDataException("无法唯一匹配所需模型，或工作流哈希/目录成员不满足；请检查包内容并选择正确文件：" + reference);
    }
}
