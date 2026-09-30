using System.Text.Json;

namespace FlowPack.Core;

/// <summary>Builds workflow documents without modifying their original JSON payload.</summary>
public static class WorkflowDocumentFactory
{
    public static WorkflowDocument Create(string id, string displayName, string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);
        using var document = JsonDocument.Parse(rawJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("工作流根节点必须是 JSON 对象。");
        }
        return new WorkflowDocument(id, displayName, DetectFormat(document.RootElement), rawJson);
    }

    public static WorkflowFormat DetectFormat(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return WorkflowFormat.Unknown;
        if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            if (nodes.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Object ||
                !x.TryGetProperty("id", out var id) || id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) ||
                !x.TryGetProperty("type", out var nodeType) || nodeType.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nodeType.GetString()))) return WorkflowFormat.Unknown;
            if (root.TryGetProperty("version", out var version))
            {
                var text = version.ValueKind == JsonValueKind.String ? version.GetString() : version.GetRawText();
                if (text is "0.4" or "0.4.0") return WorkflowFormat.UiV04;
                if (text is "1" or "1.0" or "1.0.0") return WorkflowFormat.UiV10;
            }
            else if (nodes.GetArrayLength() > 0 && root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
                nodes.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out _) &&
                    x.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String))
                return WorkflowFormat.UiUnversioned;
            return WorkflowFormat.Unknown;
        }

        var properties = root.EnumerateObject().ToArray();
        if (properties.Length > 0 && properties.All(property =>
                int.TryParse(property.Name, out _) &&
                property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("class_type", out var classType) &&
                classType.ValueKind == JsonValueKind.String))
        {
            return WorkflowFormat.Api;
        }
        return WorkflowFormat.Unknown;
    }
}
