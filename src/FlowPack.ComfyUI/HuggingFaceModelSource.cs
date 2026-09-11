using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowPack.ComfyUI;

/// <summary>
/// Model-source provider for requirement 8. Always returns Hugging Face and ModelScope search links
/// (deterministic URLs). When a repository id is known for a file name (via an optional curated map),
/// it also returns the direct resolve URL using the factual Hugging Face pattern and queries the
/// factual HF tree API for the file size. Unknown repositories get no fabricated direct URL.
/// </summary>
public sealed class HuggingFaceModelSource : IModelSourceProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyDictionary<string, string>? _knownRepos;

    public HuggingFaceModelSource(HttpClient httpClient, IReadOnlyDictionary<string, string>? knownRepos = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _knownRepos = knownRepos;
    }

    public void Dispose() { }

    public async Task<ModelSource?> GetSourceAsync(string fileName, string? categoryHint = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var links = new List<SourceLink>
        {
            new("Hugging Face 搜索", "https://huggingface.co/models?search=" + Uri.EscapeDataString(fileName)),
            new("ModelScope 搜索", "https://modelscope.cn/models?name=" + Uri.EscapeDataString(fileName))
        };

        if (_knownRepos is not null && _knownRepos.TryGetValue(fileName.ToLowerInvariant(), out var repo))
        {
            var direct = $"https://huggingface.co/{repo}/resolve/main/{fileName}";
            var size = await TryGetSizeAsync(repo, fileName, cancellationToken);
            return new ModelSource(direct, size, links);
        }

        return new ModelSource(null, null, links);
    }

    private async Task<long?> TryGetSizeAsync(string repo, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://huggingface.co/api/models/{repo}/tree/main";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String &&
                    string.Equals(path.GetString(), fileName, StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number)
                {
                    return size.GetInt64();
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }
}
