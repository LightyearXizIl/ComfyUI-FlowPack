namespace FlowPack.ComfyUI;

/// <summary>
/// Kind of a missing dependency that the user may choose to download.
/// </summary>
public enum DependencyKind { Node, Model }

/// <summary>
/// A clickable source page for a dependency (search page, project page, etc.).
/// </summary>
public sealed record SourceLink(string Label, string Url);

/// <summary>
/// One downloadable entry surfaced in the install-page dependency view (requirement 8):
/// a missing node or model, its size when known, an optional direct download URL,
/// and the source/search links the user can open to decide whether to download.
/// </summary>
public sealed record DownloadEntry(
    string Name,
    DependencyKind Kind,
    long? SizeBytes,
    string? DirectUrl,
    IReadOnlyList<SourceLink> SourceLinks);
