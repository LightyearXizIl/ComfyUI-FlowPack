using System.Text.Json;

namespace FlowPack.ComfyUI;

public sealed record ResolvedDependencySource(string? DownloadUrl, string? ExpectedSha256, string? Revision, string Description)
{
    public string? PackageIdentity { get; init; }
    public IReadOnlyList<string> PythonDependencies { get; init; } = [];
}

public sealed class DependencySourceResolver(HttpClient http)
{
    private JsonDocument? _nodeMap;
    public async Task<ResolvedDependencySource> ResolveRegistryAsync(string identity, string? version, CancellationToken token = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(identity, @"^[a-zA-Z0-9][a-zA-Z0-9._-]*$"))
            throw new InvalidDataException("Registry 包标识无效。");
        if (version is not null && !System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
            return new(null, null, version, "工作流要求提交或非 Registry 版本，请提供对应固定提交的节点归档。");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.comfy.org/nodes/" + Uri.EscapeDataString(identity) + "/install" +
            (version is null ? "" : "?version=" + Uri.EscapeDataString(version)));
        request.Headers.UserAgent.ParseAdd("ComfyUI-FlowPack");
        using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); var root = doc.RootElement;
        if (root.GetProperty("node_id").GetString() != identity || root.GetProperty("status").GetString() != "NodeVersionStatusActive" ||
            (version is not null && root.GetProperty("version").GetString() != version) ||
            (root.TryGetProperty("deprecated", out var deprecated) && deprecated.ValueKind == JsonValueKind.True))
            throw new InvalidDataException("Registry 节点身份、版本或可用状态未通过核对。");
        if (!Uri.TryCreate(root.GetProperty("downloadUrl").GetString(), UriKind.Absolute, out var download) || download.Scheme != "https")
            throw new InvalidDataException("Registry 没有提供有效的 HTTPS 节点归档。");
        var dependencies = root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array
            ? deps.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
        var pinnedVersion = root.GetProperty("version").GetString();
        return new(download.ToString(), null, pinnedVersion, "Comfy Registry 固定版本 " + pinnedVersion + "；来源未提供归档 SHA-256")
            { PackageIdentity = identity, PythonDependencies = dependencies };
    }

    public async Task<ResolvedDependencySource> ResolveNodeAsync(string nodeType, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://raw.githubusercontent.com/Comfy-Org/ComfyUI-Manager/main/extension-node-map.json");
        request.Headers.UserAgent.ParseAdd("ComfyUI-FlowPack");
        if (_nodeMap is null)
        {
            using var response = await http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            _nodeMap = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        }
        var matches = _nodeMap.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array &&
            p.Value.GetArrayLength() > 0 && p.Value[0].ValueKind == JsonValueKind.Array &&
            p.Value[0].EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == nodeType)).Select(p => p.Name).Distinct().ToArray();
        if (matches.Length != 1) return new(null, null, null, matches.Length > 1 ? "多个仓库提供该类型，请确认来源" : "未找到唯一来源，可填写仓库或选择本地文件");
        return await PinGitHubAsync(matches[0], token);
    }

    public async Task<ResolvedDependencySource> PinGitHubAsync(string repository, CancellationToken token = default)
    {
        if (!Uri.TryCreate(repository, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com")
            return new(null, null, null, "节点来源不是可解析的 GitHub 仓库");
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2) return new(null, null, null, "请提供明确的节点仓库地址");
        var repositoryName = segments[1].EndsWith(".git", StringComparison.Ordinal) ? segments[1][..^4] : segments[1];
        var repo = segments[0] + "/" + repositoryName;
        var reference = "HEAD";
        if (segments.Length > 2)
        {
            if (segments[2] == "tree" && segments.Length >= 4) reference = string.Join('/', segments.Skip(3));
            else if (segments[2] == "archive" && segments.Length >= 4 && segments[^1].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var skip = segments.Length > 5 && segments[3] == "refs" && segments[4] is "heads" or "tags" ? 5 : 3;
                reference = string.Join('/', segments.Skip(skip))[..^4];
            }
            else return new(null, null, null, "请提供节点仓库、固定提交或标签 ZIP 地址");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + repo + "/commits/" + Uri.EscapeDataString(reference));
        request.Headers.UserAgent.ParseAdd("ComfyUI-FlowPack");
        using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var revision = doc.RootElement.GetProperty("sha").GetString();
        if (revision is null || revision.Length != 40 || !revision.All(Uri.IsHexDigit)) throw new InvalidDataException("仓库没有有效提交标识。");
        return new("https://codeload.github.com/" + repo + "/zip/" + revision, null, revision, "节点类型映射到仓库；固定提交 " + revision[..12] + "，来源未提供归档 SHA-256");
    }

    public static ResolvedDependencySource ResolveModel(string reference, IEnumerable<string> workflowJson)
    {
        var matches = new List<(string Url, string? Hash)>();
        foreach (var raw in workflowJson)
        {
            using var doc = JsonDocument.Parse(raw);
            Visit(doc.RootElement);
        }
        var unique = matches.Distinct().ToArray();
        return unique.Length == 1 ? new(unique[0].Url, unique[0].Hash, null, "工作流模型元数据中的明确下载来源") :
            new(null, null, null, unique.Length > 1 ? "工作流包含多个同名来源，请确认" : "工作流未给出来源，可填写直链或选择本地模型");
        void Visit(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Visit(item);
            if (value.ValueKind != JsonValueKind.Object) return;
            if (value.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                string.Equals(Path.GetFileName(name.GetString()), Path.GetFileName(reference.Replace('\\', '/')), StringComparison.OrdinalIgnoreCase) &&
                value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == "https")
            {
                string? hash = value.TryGetProperty("sha256", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
                if (hash is not null && (hash.Length != 64 || !hash.All(Uri.IsHexDigit))) hash = null;
                matches.Add((uri.ToString(), hash));
            }
            foreach (var property in value.EnumerateObject()) Visit(property.Value);
        }
    }
}
