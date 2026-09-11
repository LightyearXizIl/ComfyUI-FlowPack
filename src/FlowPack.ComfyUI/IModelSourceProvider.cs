namespace FlowPack.ComfyUI;

/// <summary>
/// Resolves where a required model file can be obtained, and its size when metadata is available.
/// Per requirement 8 the canonical sources are huggingface.co and modelscope.cn. Implementations must
/// not fabricate a direct download URL for an unknown repository; search links are always provided.
/// </summary>
public interface IModelSourceProvider
{
    Task<ModelSource?> GetSourceAsync(string fileName, string? categoryHint = null, CancellationToken cancellationToken = default);
}

public sealed record ModelSource(
    string? DirectUrl,
    long? SizeBytes,
    IReadOnlyList<SourceLink> SearchLinks);
