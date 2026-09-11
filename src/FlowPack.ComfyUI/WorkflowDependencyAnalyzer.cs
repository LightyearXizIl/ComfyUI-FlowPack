using System.Security.Cryptography;
using System.Text;
using FlowPack.Core;

namespace FlowPack.ComfyUI;

/// <summary>
/// Extracts the dependency requirements of a single workflow file: the node types it invokes and the
/// model files it references. This is the read-only input to <see cref="DependencyResolver"/> (phase C)
/// and to the packaging wizard (phase D, requirement 6: "select workflows -> auto-detect required nodes
/// and models to bundle together").
/// </summary>
public sealed record WorkflowDependency(
    WorkflowDocument Document,
    IReadOnlyList<string> RequiredNodeTypes,
    IReadOnlyList<ModelReference> RequiredModels)
{
    public string Id => Document.Id;
    public string DisplayName => Document.DisplayName;
    public WorkflowFormat Format => Document.Format;
}

public sealed class WorkflowDependencyAnalyzer
{
    public async Task<WorkflowDependency> AnalyzeAsync(string workflowPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        var rawJson = await File.ReadAllTextAsync(workflowPath, cancellationToken);
        return AnalyzeRaw(rawJson, Path.GetFileNameWithoutExtension(workflowPath));
    }

    public WorkflowDependency AnalyzeRaw(string rawJson, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));
        var document = WorkflowDocumentFactory.Create(id, displayName, rawJson);
        var set = WorkflowRequirementParser.Analyze(rawJson);
        return new WorkflowDependency(document, set.NodeTypes, set.ModelReferences);
    }
}
