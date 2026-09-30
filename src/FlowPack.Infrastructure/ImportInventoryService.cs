using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public static class ImportInventoryService
{
    public static async Task<ResourceInventory> MergeVerifiedAsync(ResourceInventory local,
        IReadOnlyList<ImportResource> selected, CancellationToken token = default)
    {
        var merged = Merge(local, selected);
        var resources = merged.Resources.ToList();
        foreach (var staged in merged.Resources.Where(x => x.IsStaged))
        {
            token.ThrowIfCancellationRequested();
            var matches = local.Resources.Where(x => !x.IsStaged && x.Kind == staged.Kind &&
                string.Equals(x.Category, staged.Category, StringComparison.OrdinalIgnoreCase) &&
                x.RelativePath.Replace('\\', '/').Equals(staged.RelativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).ToArray();
            // Do not silently choose among shared directories or replace runtime state with staged metadata.
            if (matches.Length == 1 && await SameContentsAsync(staged.SourcePath, matches[0].SourcePath, token))
                resources.Remove(staged);
        }
        return merged with { Resources = resources };
    }

    private static async Task<bool> SameContentsAsync(string staged, string local, CancellationToken token)
    {
        ResourceInstallationService.EnsureNoLinks(staged);
        ResourceInstallationService.EnsureNoLinks(local);
        if (File.Exists(staged) && File.Exists(local))
            return new FileInfo(staged).Length == new FileInfo(local).Length &&
                await ResourceImportService.HashAsync(staged, token) == await ResourceImportService.HashAsync(local, token);
        if (!Directory.Exists(staged) || !Directory.Exists(local)) return false;
        var stagedFiles = ResourceFiles.Enumerate(staged).ToDictionary(x => ResourceFiles.Relative(staged, x), StringComparer.OrdinalIgnoreCase);
        var localFiles = ResourceFiles.Enumerate(local).ToDictionary(x => ResourceFiles.Relative(local, x), StringComparer.OrdinalIgnoreCase);
        if (stagedFiles.Count == 0 || stagedFiles.Count != localFiles.Count) return false;
        foreach (var file in stagedFiles)
        {
            token.ThrowIfCancellationRequested();
            if (!localFiles.TryGetValue(file.Key, out var counterpart) || !await SameContentsAsync(file.Value, counterpart, token)) return false;
        }
        return true;
    }

    public static ResourceInventory Merge(ResourceInventory local, IReadOnlyList<ImportResource> selected)
    {
        var resources = new List<LocalResource>(local.Resources);
        var nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in selected.Where(x => x.State == RecognitionState.Confirmed))
        {
            var parts = resource.TargetRelativePath.Replace('\\', '/').Split('/');
            PlannedZipExportService.ValidateRelative(resource.TargetRelativePath);
            if (parts[0] == "models" && parts.Length >= 3)
                resources.Add(new(resource.Id, ResourceKind.Model, Path.GetFileName(resource.SourcePath), resource.SourcePath, string.Join('/', parts.Skip(1)), parts[1]) { IsStaged = true });
            else if (parts[0] == "input" && parts.Length >= 2)
                resources.Add(new(resource.Id, ResourceKind.Asset, Path.GetFileName(resource.SourcePath), resource.SourcePath, resource.TargetRelativePath) { IsStaged = true });
            else if (parts[0] == "custom_nodes" && parts.Length == 2 && parts[1].EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                resources.Add(new(resource.Id, ResourceKind.CustomNode, parts[1], resource.SourcePath, resource.TargetRelativePath,
                    NodeTypes: ResourceInventoryService.ReadDeclaredNodeTypesFile(resource.SourcePath)) { IsStaged = true });
            }
            else if (parts[0] == "custom_nodes" && parts.Length >= 3)
            {
                var root = resource.SourcePath;
                foreach (var unused in parts.Skip(2)) root = Path.GetDirectoryName(root)!;
                if (!nodes.Add(root)) continue;
                var selectedSources = selected.Where(x => x.State == RecognitionState.Confirmed &&
                    x.TargetRelativePath.Replace('\\', '/').StartsWith("custom_nodes/" + parts[1] + "/", StringComparison.OrdinalIgnoreCase))
                    .Select(x => Path.GetFullPath(x.SourcePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                // A checked __init__.py must not make unchecked runtime files appear installed.
                if (ResourceFiles.Enumerate(root).Any(x => !selectedSources.Contains(x))) continue;
                var identity = ResourceInventoryService.ReadPackageIdentity(root);
                resources.Add(new(root, ResourceKind.CustomNode, parts[1], root, "custom_nodes/" + parts[1], NodeTypes: ResourceInventoryService.ReadDeclaredNodeTypes(root), Version: identity.Version)
                    { PackageIdentity = identity.Name, IsStaged = true });
            }
        }
        var selectedSourcesForModels = selected.Where(x => x.State == RecognitionState.Confirmed)
            .Select(x => Path.GetFullPath(x.SourcePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modelRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in resources.Where(x => x.IsStaged && x.Kind == ResourceKind.Model).ToArray())
        {
            var boundary = model.SourcePath;
            foreach (var unused in model.RelativePath.Split('/').Skip(1)) boundary = Path.GetDirectoryName(boundary)!;
            var root = ModelDirectory.FindRoot(model.SourcePath, boundary);
            if (root is null || !modelRoots.Add(root)) continue;
            var bundle = ModelDirectory.Read(root);
            var relativeFile = ResourceFiles.Relative(root, model.SourcePath);
            var relativeRoot = model.RelativePath[..^relativeFile.Length].TrimEnd('/');
            var issues = bundle.Issues.ToList();
            if (bundle.Files.Any(x => !selectedSourcesForModels.Contains(x))) issues.Add("目录模型的配套文件未全部选中");
            resources.Add(new(root, ResourceKind.Model, Path.GetFileName(root), root, relativeRoot, model.Category)
                { IsStaged = true, ModelDirectoryRoot = root, IntegrityIssues = issues });
        }
        return local with { Resources = resources };
    }
}
