using System.IO;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

/// <summary>
/// Maps a required node type to the custom node package that provides it, and reports whether that
/// package is currently installed under a ComfyUI Desktop instance.
///
/// The authoritative node-type -> repository mapping is the ComfyUI-Manager
/// <c>custom-node-list.json</c> dataset (phase C3, online). Until that lookup is wired in, callers
/// pass a registry that may return null for unknown types; the resolver then reports the node as
/// <see cref="NodePresenceSource.CustomUnknown"/> (treated as missing / needs download) rather than
/// falsely claiming it is already present.
/// </summary>
public interface ICustomNodeRegistry
{
    string? ResolveInstalledPackage(string nodeType);
}

public enum NodePresenceSource { Core, CustomInstalled, CustomUnknown }

public sealed record NodeDependency(string NodeType, bool PresentLocally, NodePresenceSource Source, string? LocalPackage);

public sealed record ModelDependency(string Reference, string FileName, string? CategoryHint, bool PresentLocally, string? LocalPath);

public sealed record DependencyResolution(
    IReadOnlyList<NodeDependency> Nodes,
    IReadOnlyList<ModelDependency> Models,
    IReadOnlyList<string> WorkflowDisplayNames);

/// <summary>
/// Compares a set of analyzed workflows against a detected ComfyUI Desktop instance (phase A) and
/// reports which node types and model files are already present locally versus missing. Model presence
/// is checked directly against the on-disk models directory (reliable). Node presence is confirmed only
/// via <see cref="ICustomNodeRegistry"/> + an installed package directory; without that data a node is
/// conservatively reported as missing so the UI never hides a download the user may actually need
/// (requirement 8: "if the node/model is already in the local Comfy Desktop, ignore it").
/// </summary>
public sealed class DependencyResolver
{
    private readonly ICustomNodeRegistry? _registry;

    public DependencyResolver(ICustomNodeRegistry? registry = null)
    {
        _registry = registry;
    }

    public Task<DependencyResolution> ResolveAsync(
        ComfyDesktopLocation? local,
        IEnumerable<WorkflowDependency> workflows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = workflows.ToList();
        var models = ResolveModels(local, list);
        var nodes = ResolveNodes(local, list);
        var names = list.Select(w => w.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult(new DependencyResolution(nodes, models, names));
    }

    private static IReadOnlyList<ModelDependency> ResolveModels(ComfyDesktopLocation? local, IReadOnlyList<WorkflowDependency> workflows)
    {
        var byCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (local?.ModelsDirectory is { } modelsDir && Directory.Exists(modelsDir))
        {
            foreach (var file in Directory.EnumerateFiles(modelsDir, "*", SearchOption.AllDirectories))
            {
                var relative = NormalizeSlashes(Path.GetRelativePath(modelsDir, file));
                var segments = relative.Split('/');
                var name = segments[^1];
                byName[name] = relative;
                if (segments.Length >= 3) byCategory[segments[1] + "/" + name] = relative;
            }
        }

        var result = new List<ModelDependency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workflow in workflows)
        {
            foreach (var requirement in workflow.RequiredModels)
            {
                if (!seen.Add(requirement.Reference)) continue;
                string? localPath = null;
                if (requirement.CategoryHint is not null &&
                    byCategory.TryGetValue(requirement.CategoryHint + "/" + requirement.FileName, out var byCat))
                {
                    localPath = byCat;
                }
                else if (byName.TryGetValue(requirement.FileName, out var byNameOnly))
                {
                    localPath = byNameOnly;
                }

                result.Add(new ModelDependency(requirement.Reference, requirement.FileName, requirement.CategoryHint, localPath is not null, localPath));
            }
        }

        return result;
    }

    private IReadOnlyList<NodeDependency> ResolveNodes(ComfyDesktopLocation? local, IReadOnlyList<WorkflowDependency> workflows)
    {
        var customNodesDir = local?.CustomNodesDirectory;
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workflow in workflows)
        {
            foreach (var nodeType in workflow.RequiredNodeTypes) required.Add(nodeType);
        }

        var result = new List<NodeDependency>();
        foreach (var nodeType in required)
        {
            var package = _registry?.ResolveInstalledPackage(nodeType);
            if (package is not null && customNodesDir is not null &&
                Directory.Exists(Path.Combine(customNodesDir, package)))
            {
                result.Add(new NodeDependency(nodeType, PresentLocally: true, NodePresenceSource.CustomInstalled, package));
            }
            else
            {
                result.Add(new NodeDependency(nodeType, PresentLocally: false, NodePresenceSource.CustomUnknown, null));
            }
        }

        return result;
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/');
}
