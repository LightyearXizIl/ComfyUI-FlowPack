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
    public string? SemanticCategory { get; init; }
    public string? Evidence { get; init; }
    public string? NodeType { get; init; }
    public ModelReference(string reference)
        : this(reference, CategoryFrom(reference), FileNameFrom(reference))
    {
    }

    private static string? CategoryFrom(string reference)
    {
        var normalized = reference.Replace('\\', '/');
        var slash = normalized.IndexOf('/');
        var category = slash > 0 ? normalized[..slash] : null;
        return category is "checkpoints" or "loras" or "vae" or "clip" or "unet" or "text_encoders" or "diffusion_models" or "controlnet" or "clip_vision" or "upscale_models" ? category : null;
    }

    private static string FileNameFrom(string reference)
    {
        var normalized = reference.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }
}

public sealed record WorkflowRequirementSet(IReadOnlyList<string> NodeTypes, IReadOnlyList<ModelReference> ModelReferences)
{
    public IReadOnlyList<string> Issues { get; init; } = [];
    public IReadOnlyList<string> InputReferences { get; init; } = [];
}

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
        if (root.ValueKind != JsonValueKind.Object)
            return new([], []) { Issues = ["工作流根节点必须是对象。"] };
        var nodeTypes = new List<string>();
        var modelRefs = new List<ModelReference>();
        var issues = new List<string>();
        var inputRefs = new List<string>();
        var format = WorkflowDocumentFactory.DetectFormat(root);

        if (format is WorkflowFormat.UiV04 or WorkflowFormat.UiV10 or WorkflowFormat.UiUnversioned or WorkflowFormat.Unknown)
        {
            if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    if (node.ValueKind != JsonValueKind.Object) continue;
                    if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                    {
                        nodeTypes.Add(type.GetString()!);
                    }

                    if (node.TryGetProperty("widgets_values", out var widgets) && widgets.ValueKind == JsonValueKind.Array)
                    {
                        var nodeType = type.ValueKind == JsonValueKind.String ? type.GetString() ?? "" : "";
                        if (nodeType is "LoadImage" or "LoadAudio" or "LoadVideo" && widgets.GetArrayLength() > 0 && widgets[0].ValueKind == JsonValueKind.String)
                            inputRefs.Add(widgets[0].GetString()!);
                        if (!HasKnownNonModelWidgets(nodeType))
                        {
                            var start = modelRefs.Count;
                            CollectModelRefs(widgets, modelRefs);
                            if (nodeType == "DiffusersLoader" && widgets.GetArrayLength() > 0 && widgets[0].ValueKind == JsonValueKind.String && IsDirectoryReference(widgets[0].GetString()!))
                                modelRefs.Add(new(widgets[0].GetString()!));
                            for (var m = start; m < modelRefs.Count; m++) modelRefs[m] = modelRefs[m] with { NodeType = nodeType, SemanticCategory = CategoryFor(nodeType), Evidence = nodeType + " widgets_values" };
                            if (CategoryFor(nodeType) is null && widgets.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String))
                                issues.Add(nodeType + " 的动态输入需要检查，扩展名提示不代表依赖完整。");
                        }
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
                    foreach (var input in inputs.EnumerateObject())
                    {
                        if (input.Name is "text" or "prompt" or "negative" or "filename_prefix") continue;
                        if (classType.ValueKind == JsonValueKind.String && classType.GetString() is "LoadImage" or "LoadAudio" or "LoadVideo" &&
                            input.Name is "image" or "audio" or "file" or "video" && input.Value.ValueKind == JsonValueKind.String)
                            inputRefs.Add(input.Value.GetString()!);
                        if (classType.ValueKind == JsonValueKind.String && HasKnownNonModelWidgets(classType.GetString()!)) continue;
                        var start = modelRefs.Count;
                        CollectModelRefs(input.Value, modelRefs);
                        if (classType.ValueKind == JsonValueKind.String && classType.GetString() == "DiffusersLoader" && input.Name == "model_path" &&
                            input.Value.ValueKind == JsonValueKind.String && IsDirectoryReference(input.Value.GetString()!)) modelRefs.Add(new(input.Value.GetString()!));
                        for (var m = start; m < modelRefs.Count; m++) modelRefs[m] = modelRefs[m] with { NodeType = classType.ValueKind == JsonValueKind.String ? classType.GetString() : null, SemanticCategory = CategoryFor(classType.ValueKind == JsonValueKind.String ? classType.GetString()! : ""), Evidence = "API input " + input.Name };
                    }
                }
            }
        }

        if (root.TryGetProperty("definitions", out var definitions) && definitions.ValueKind == JsonValueKind.Object &&
            definitions.TryGetProperty("subgraphs", out var subgraphs) && subgraphs.ValueKind == JsonValueKind.Array)
        {
            foreach (var graph in subgraphs.EnumerateArray())
            {
                if (graph.ValueKind != JsonValueKind.Object) continue;
                var nested = Analyze(graph);
                nodeTypes.AddRange(nested.NodeTypes); modelRefs.AddRange(nested.ModelReferences); issues.AddRange(nested.Issues);
                inputRefs.AddRange(nested.InputReferences);
                if (graph.TryGetProperty("id", out var graphId) && graphId.ValueKind == JsonValueKind.String) nodeTypes.RemoveAll(x => x == graphId.GetString());
            }
        }
        return new WorkflowRequirementSet(
            nodeTypes.Distinct(StringComparer.Ordinal).ToList(),
            modelRefs.Distinct(ModelReferenceComparer.Instance).ToList()) { Issues = issues, InputReferences = inputRefs.Distinct().ToArray() };
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
        if (value.Contains(':') || value.Replace('\\', '/').Split('/').Any(x => x is "." or "..")) return false;
        var lower = value.ToLowerInvariant();
        return ModelExtensions.Any(lower.EndsWith);
    }

    private static string? CategoryFor(string type) => type switch
    {
        "DiffusersLoader" => "diffusers",
        "CheckpointLoaderSimple" or "CheckpointLoader" => "checkpoints",
        "LoraLoader" or "LoraLoaderModelOnly" => "loras",
        "VAELoader" => "vae", "UNETLoader" => "diffusion_models",
        "CLIPLoader" or "DualCLIPLoader" or "TripleCLIPLoader" => "text_encoders",
        "CLIPVisionLoader" => "clip_vision", "ControlNetLoader" => "controlnet",
        "UpscaleModelLoader" => "upscale_models", _ => null
    };

    private static bool IsDirectoryReference(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 1024 &&
        !Path.IsPathRooted(value) && !value.Contains(':') && !value.Replace('\\', '/').Split('/').Any(x => x is "." or "..");

    // These widgets represent text, input assets, output names or sampler options, not model files.
    // Unknown custom nodes still require explicit inspection; this is not a blanket suppression.
    private static bool HasKnownNonModelWidgets(string type) => type is
        "Note" or "CLIPTextEncode" or "PrimitiveNode" or "LoadImage" or "LoadImageMask" or
        "LoadAudio" or "LoadVideo" or "SaveImage" or "PreviewImage" or "SaveAudio" or
        "SaveAnimatedWEBP" or "SaveAnimatedPNG" or "KSampler" or "KSamplerAdvanced" or
        "EmptyLatentImage" or "ImageScale" or "ImageScaleBy";

    private sealed class ModelReferenceComparer : IEqualityComparer<ModelReference>
    {
        public static readonly ModelReferenceComparer Instance = new();
        public bool Equals(ModelReference? x, ModelReference? y) =>
            x is not null && y is not null && string.Equals(x.Reference, y.Reference, StringComparison.OrdinalIgnoreCase) &&
            x.NodeType == y.NodeType && string.Equals(x.CategoryHint ?? x.SemanticCategory, y.CategoryHint ?? y.SemanticCategory, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(ModelReference obj) => HashCode.Combine(obj.Reference.ToLowerInvariant(), (obj.CategoryHint ?? obj.SemanticCategory)?.ToLowerInvariant(), obj.NodeType);
    }
}
