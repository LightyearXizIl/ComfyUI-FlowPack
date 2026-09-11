using System.Text.Json;
using System.Text.Json.Serialization;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public sealed record PackageDraftExchange(string FormatVersion, PackageDraft Draft, WorkflowDocument? Workflow);

/// <summary>Portable draft exchange only. It deliberately is not an installable resource package.</summary>
public sealed class PackageDraftExchangeService
{
    private const string FormatVersion = "1";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task ExportAsync(string path, PackageDraft draft, WorkflowDocument? workflow, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(draft, workflow);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(new PackageDraftExchange(FormatVersion, draft, workflow), Options), cancellationToken);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    public async Task<PackageDraftExchange> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var exchange = await JsonSerializer.DeserializeAsync<PackageDraftExchange>(stream, Options, cancellationToken)
            ?? throw new InvalidDataException("草稿交换文件为空或格式无效。");
        if (exchange.FormatVersion != FormatVersion) throw new InvalidDataException("不支持该草稿交换文件版本。");
        Validate(exchange.Draft, exchange.Workflow);
        return exchange;
    }

    private static void Validate(PackageDraft draft, WorkflowDocument? workflow)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.CurrentStep is < 1 or > 4 || string.IsNullOrWhiteSpace(draft.Id)) throw new InvalidDataException("草稿步骤或 ID 无效。");
        if (workflow is not null && (string.IsNullOrWhiteSpace(workflow.Id) || string.IsNullOrWhiteSpace(workflow.RawJson))) throw new InvalidDataException("草稿中的工作流无效。");
        if (draft.WorkflowId is not null && workflow is not null && draft.WorkflowId != workflow.Id) throw new InvalidDataException("草稿工作流 ID 与原始工作流不一致。");
    }
}
