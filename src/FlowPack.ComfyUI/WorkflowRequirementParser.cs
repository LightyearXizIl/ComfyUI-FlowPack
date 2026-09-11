using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

/// <summary>
/// Shared, read-only extraction of a workflow's structural requirements: the node types it uses and
/// the model files it references. UI format (nodes[].type + nodes[].widgets_values[]) and API format
/// (id -> class_type + inputs) are both supported. Model references are extracted heuristically from
/// loader widget values / input strings by file extension; this is best-effort and is later confirmed
/// against actual assets during dependency resolution (phase C) and download (phase E).
/// </summary>
public sealed record ModelReference(string Reference, string? CategoryHint, string FileName)
{
    public ModelReference(string reference)
        : this(reference, CategoryFrom(reference), FileNameFrom(reference))
    {
    }

    private static string? CategoryFrom(string reference)
    {
        var slash = reference.LastIndexOf('/');
        return slash > 0 ? reference.Substring(0, slash) : null;
    }

    private static string FileNameFrom(string reference)
    {
        var slash = reference.LastIndexOf('/');
        return slash >= 0 ? reference.Substring(slash + 1) : reference;
    }
}

public sealed record WorkflowRequirementSet(IReadOnlyList<string> NodeTypes, IReadOnlyList<ModelReference> ModelReferences);

public static class WorkflowRequirementParser
{
    private static readonly string[] ModelExtensions =
    [
        ".safetensors", ".ckpt", ".pt", ".pth", ".bin", ".gguf", ".onnx", ".sft"
    ];

    public static WorkflowRequirementSet Analyze(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return Analyze(document.RootElement);
    }

    public static WorkflowRequirementSet Analyze(JsonElement root)
    {
        var nodeTypes = new List<string>();
        var modelRefs = new List<ModelReference>();
        var format = WorkflowDocumentFactory.DetectFormat(root);

        if (format is WorkflowFormat.UiV04 or WorkflowFormat.UiV10 or WorkflowFormat.Unknown)
        {
            if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                    {
                        nodeTypes.Add(type.GetString()!);
                    }

                    if (node.TryGetProperty("widgets_values", out var widgets) && widgets.ValueKind == JsonValueKind.Array)
                    {
                        CollectModelRefs(widgets, modelRefs);
                    }
                }
            }
        }

        if (format is WorkflowFormat.Api or WorkflowFormat.Unknown)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                if (property.Value.TryGetProperty("class_type", out var classType) && classType.ValueKind == JsonValueKind.String)
                {
                    nodeTypes.Add(classType.GetString()!);
                }

                if (property.Value.TryGetProperty("inputs", out var inputs) && inputs.ValueKind == JsonValueKind.Object)
                {
                    CollectModelRefs(inputs, modelRefs);
                }
            }
        }

        return new WorkflowRequirementSet(
            nodeTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            modelRefs.Distinct(ModelReferenceComparer.Instance).ToList());
    }

    private static void CollectModelRefs(JsonElement value, List<ModelReference> sink)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString();
                if (text is not null && IsLikelyModelFileName(text)) sink.Add(new ModelReference(text));
                break;
            case JsonValueKind.Array:
                foreach (var child in value.EnumerateArray()) CollectModelRefs(child, sink);
                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject()) CollectModelRefs(property.Value, sink);
                break;
        }
    }

    private static bool IsLikelyModelFileName(string value)
    {
        if (value.Length is < 3 or > 260) return false;
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains('\\') || value.Contains(':')) return false; // reject drive letters / back-slashes
        var lower = value.ToLowerInvariant();
        return ModelExtensions.Any(lower.EndsWith);
    }

    private sealed class ModelReferenceComparer : IEqualityComparer<ModelReference>
    {
        public static readonly ModelReferenceComparer Instance = new();
        public bool Equals(ModelReference? x, ModelReference? y) =>
            x is not null && y is not null && string.Equals(x.Reference, y.Reference, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(ModelReference obj) => obj.Reference.ToLowerInvariant().GetHashCode();
    }
}
