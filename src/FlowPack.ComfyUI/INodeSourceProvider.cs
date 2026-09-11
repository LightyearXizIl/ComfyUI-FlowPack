namespace FlowPack.ComfyUI;

/// <summary>
/// Resolves where a required custom-node type can be obtained (project repository and/or search pages).
/// The authoritative mapping is the ComfyUI-Manager dataset (online). Implementations must never fabricate
/// a repository URL; when no confident mapping exists they return search links only.
/// </summary>
public interface INodeSourceProvider
{
    Task<NodeSource?> GetSourceAsync(string nodeType, CancellationToken cancellationToken = default);
}

public sealed record NodeSource(
    string? RepositoryUrl,
    string? PackageFolderHint,
    IReadOnlyList<SourceLink> SearchLinks);
