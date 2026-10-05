using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

public static class FrontendWorkflowList
{
    public static IReadOnlyList<string> Parse(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array) throw new InvalidDataException("前端工作流列表结构无效。");
        return list.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() :
            x.ValueKind == JsonValueKind.Object && x.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() :
            throw new InvalidDataException("前端工作流条目缺少路径。"))
            .Where(x => x is not null).Select(x => x!.Replace('\\', '/').TrimStart('/'))
            .Select(x => x.StartsWith("workflows/", StringComparison.Ordinal) ? x[10..] : x)
            .Where(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !x.Split('/').Any(p => p is ".." or ".") && !x.StartsWith("subgraphs/", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
