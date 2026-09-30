using System.IO;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.App;

public sealed record ResourceFolderLocation(ResourceKind Kind, string Label, string Path);

/// <summary>Uses only discovered instance paths; never creates or guesses a directory.</summary>
public static class ResourceFolderNavigation
{
    public static IReadOnlyList<ResourceFolderLocation> GetLocations(InstanceDescriptor? instance, ResourceKind kind)
    {
        if (instance is null) return [];
        var candidates = kind switch
        {
            ResourceKind.Workflow => new[] { ("工作流目录", instance.WorkflowsDirectory) },
            ResourceKind.CustomNode => new[] { ("节点目录", instance.CustomNodesDirectory) }
                .Concat(instance.ExtraPaths.Where(p => p.Category == "custom_nodes").Select(p => ("附加节点目录", p.Path))),
            ResourceKind.Model => new[] { ("模型默认目录", instance.ModelsWriteDirectory) }
                .Concat(instance.ModelRoots.Select(p => ("模型搜索目录", p)))
                .Concat(instance.ExtraPaths.Where(p => p.Category != "custom_nodes").Select(p => ($"模型 · {p.Category}", p.Path))),
            _ => Enumerable.Empty<(string, string)>()
        };
        var result = new List<ResourceFolderLocation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, path) in candidates)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path)) continue;
            try
            {
                var fullPath = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
                if (seen.Add(fullPath)) result.Add(new(kind, label, fullPath));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return result;
    }
}
