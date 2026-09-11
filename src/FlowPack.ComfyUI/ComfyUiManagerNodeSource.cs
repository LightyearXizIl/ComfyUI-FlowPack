using System.Net.Http;
using System.Text.Json;

namespace FlowPack.ComfyUI;

/// <summary>
/// Online node-source provider backed by the ComfyUI-Manager <c>custom-node-list.json</c> dataset.
/// The dataset URL is the well-known public raw URL maintained by the ComfyUI-Manager project
/// (verified to exist). Mapping a workflow node *type* to a package is best-effort: the dataset keys
/// packages by title/repository, not by every exported node class name, so a confident match is
/// returned when the type equals (or normalizes to) the package title or repository name; otherwise
/// only a GitHub search link is provided. No repository URL is fabricated for an unmatched type.
///
/// The same instance also implements <see cref="ICustomNodeRegistry"/> so the phase-C resolver can
/// use it for local-presence hints (folder name from the repository URL), staying conservative (null)
/// when no confident match exists.
/// </summary>
public sealed class ComfyUiManagerNodeSource : INodeSourceProvider, ICustomNodeRegistry, IDisposable
{
    private const string ListUrl = "https://raw.githubusercontent.com/ltdrdata/ComfyUI-Manager/main/custom-node-list.json";

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private List<NodeListEntry>? _entries;

    public ComfyUiManagerNodeSource(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public void Dispose() => _loadLock.Dispose();

    private async Task<IReadOnlyList<NodeListEntry>> GetEntriesAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null) return _entries;
        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_entries is not null) return _entries;
            using var response = await _httpClient.GetAsync(ListUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var list = new List<NodeListEntry>();
            if (document.RootElement.TryGetProperty("custom_nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    var title = node.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    var reference = node.TryGetProperty("reference", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                    if (title is null && reference is null) continue;
                    list.Add(new NodeListEntry(title, reference));
                }
            }

            _entries = list;
            return _entries;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<NodeSource?> GetSourceAsync(string nodeType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeType)) return null;
        var entries = await GetEntriesAsync(cancellationToken);
        var normalized = Normalize(nodeType);
        var match = entries.FirstOrDefault(e =>
            (e.Title is not null && Normalize(e.Title) == normalized) ||
            (e.RepoName is not null && Normalize(e.RepoName) == normalized));

        var links = new List<SourceLink>
        {
            new("GitHub 仓库搜索", "https://github.com/search?q=" + Uri.EscapeDataString(nodeType + " ComfyUI") + "&type=repositories")
        };
        if (match is null) return new NodeSource(null, null, links);

        var folder = match.RepositoryUrl is not null ? FolderFromUrl(match.RepositoryUrl) : null;
        return new NodeSource(match.RepositoryUrl, folder, links);
    }

    public string? ResolveInstalledPackage(string nodeType)
    {
        if (string.IsNullOrWhiteSpace(nodeType)) return null;
        var entries = GetEntriesAsync(CancellationToken.None).GetAwaiter().GetResult();
        var normalized = Normalize(nodeType);
        var match = entries.FirstOrDefault(e =>
            (e.Title is not null && Normalize(e.Title) == normalized) ||
            (e.RepoName is not null && Normalize(e.RepoName) == normalized));
        return match?.RepositoryUrl is not null ? FolderFromUrl(match.RepositoryUrl) : null;
    }

    private static string Normalize(string value) =>
        value.Replace("ComfyUI-", "", StringComparison.OrdinalIgnoreCase)
             .Replace("ComfyUI_", "", StringComparison.OrdinalIgnoreCase)
             .ToLowerInvariant();

    private static string? FolderFromUrl(string url)
    {
        var trimmed = url.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index >= 0 ? trimmed[(index + 1)..] : null;
    }

    private sealed record NodeListEntry(string? Title, string? RepositoryUrl)
    {
        public string? RepoName => RepositoryUrl is null ? null : FolderFromUrl(RepositoryUrl);
    }
}
