namespace FlowPack.ComfyUI;

/// <summary>
/// Combines a phase-C <see cref="DependencyResolution"/> with the online source providers to produce the
/// download entries shown in the install-page dependency view (requirement 8). Only missing items
/// (PresentLocally == false) become entries. Each entry carries the size when resolvable plus source/
/// search links so the user decides whether to download; local-present items are intentionally omitted.
/// </summary>
public sealed class MissingDependencyResolver
{
    private readonly INodeSourceProvider _nodeSource;
    private readonly IModelSourceProvider _modelSource;

    public MissingDependencyResolver(INodeSourceProvider nodeSource, IModelSourceProvider modelSource)
    {
        _nodeSource = nodeSource ?? throw new ArgumentNullException(nameof(nodeSource));
        _modelSource = modelSource ?? throw new ArgumentNullException(nameof(modelSource));
    }

    public async Task<IReadOnlyList<DownloadEntry>> ResolveDownloadPlanAsync(
        DependencyResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var entries = new List<DownloadEntry>();

        foreach (var node in resolution.Nodes.Where(n => !n.PresentLocally))
        {
            var source = await _nodeSource.GetSourceAsync(node.NodeType, cancellationToken);
            var links = source?.SearchLinks ??
            [
                new SourceLink("GitHub 仓库搜索", "https://github.com/search?q=" + Uri.EscapeDataString(node.NodeType + " ComfyUI") + "&type=repositories")
            ];
            entries.Add(new DownloadEntry(node.NodeType, DependencyKind.Node, SizeBytes: null, source?.RepositoryUrl, links));
        }

        foreach (var model in resolution.Models.Where(m => !m.PresentLocally))
        {
            var source = await _modelSource.GetSourceAsync(model.FileName, model.CategoryHint, cancellationToken);
            entries.Add(new DownloadEntry(model.FileName, DependencyKind.Model, source?.SizeBytes, source?.DirectUrl, source?.SearchLinks ?? []));
        }

        return entries;
    }
}
