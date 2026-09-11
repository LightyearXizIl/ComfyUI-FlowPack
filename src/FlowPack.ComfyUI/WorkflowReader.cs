using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

/// <summary>Preserves the original JSON and only classifies a workflow; it never converts UI JSON into API JSON.</summary>
public sealed class WorkflowReader
{
    public async Task<WorkflowDocument> ReadAsync(string workflowPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        var rawJson = await File.ReadAllTextAsync(workflowPath, cancellationToken);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));
        return WorkflowDocumentFactory.Create(id, Path.GetFileNameWithoutExtension(workflowPath), rawJson);
    }

    public static WorkflowFormat DetectFormat(JsonElement root) => WorkflowDocumentFactory.DetectFormat(root);
}
