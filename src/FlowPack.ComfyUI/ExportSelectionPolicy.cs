using FlowPack.Core;

namespace FlowPack.ComfyUI;

public sealed record ExportResourceSelection(IReadOnlyList<LocalResource> Resources, IReadOnlyList<string> Issues);
public static class ExportSelectionPolicy
{
    public static ExportResourceSelection Build(IEnumerable<LocalResource> checkedResources, DependencyAnalysis analysis,
        IReadOnlySet<ResourceKind> includedKinds, bool autoInclude = false)
    {
        var resources = checkedResources.Where(x => includedKinds.Contains(x.Kind)).ToList();
        if (autoInclude)
            resources.AddRange(analysis.Dependencies.Where(x => includedKinds.Contains(x.Kind) && x.Candidates.Count == 1 &&
                (x.State == DependencyState.Present || x.Kind == ResourceKind.CustomNode && x.State == DependencyState.Unresolved)).SelectMany(x => x.Candidates));
        // Reference dependency warnings remain visible in the dependency panel. Only the selected
        // payload is validated by the export planner; a checked kind is not a selection of every dependency.
        return new(resources.DistinctBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray(), []);
    }
}
