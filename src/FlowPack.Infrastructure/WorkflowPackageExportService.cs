using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>
/// Creates a portable archive containing exactly one raw workflow. Dependency analysis is
/// intentionally outside this service, so every output is declared OfflinePartial.
/// </summary>
public sealed class WorkflowPackageExportService
{
    private const string WorkflowPath = "payload/workflows/entry.json";
    private const string WorkflowResourceId = "workflow-entry";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<PackageManifest> ExportAsync(
        string path,
        PackageDraft draft,
        WorkflowDocument workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(workflow);
        if (string.IsNullOrWhiteSpace(draft.WorkflowId) || !string.Equals(draft.WorkflowId, workflow.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException("草稿未选择与其匹配的工作流。");
        }

        ValidateRawWorkflow(workflow.RawJson);
        var payload = Encoding.UTF8.GetBytes(workflow.RawJson);
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var manifestJson = CreateManifestJson(draft, workflow, payload.LongLength, hash);
        var manifest = new PackageManifestReader().Read(manifestJson.RootElement);

        var fullPath = Path.GetFullPath(path);
        if (!fullPath.EndsWith(".cpack", StringComparison.OrdinalIgnoreCase)) fullPath += ".cpack";
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteEntryAsync(archive, "manifest.json", Encoding.UTF8.GetBytes(manifestJson.RootElement.GetRawText()), CompressionLevel.Optimal, cancellationToken);
                await WriteEntryAsync(archive, WorkflowPath, payload, CompressionLevel.Optimal, cancellationToken);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
            return manifest;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static JsonDocument CreateManifestJson(PackageDraft draft, WorkflowDocument workflow, long sizeBytes, string hash) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            formatVersion = "1",
            id = draft.Id,
            name = draft.Name,
            version = draft.Version,
            description = draft.Description,
            author = new { name = draft.AuthorName, url = draft.AuthorUrl },
            source = draft.Source,
            distribution = DistributionDeclaration.OfflinePartial,
            entryWorkflows = new[] { new { id = workflow.Id, path = WorkflowPath, isEntryPoint = true } },
            resources = new[]
            {
                new
                {
                    id = WorkflowResourceId,
                    name = $"{workflow.DisplayName}.json",
                    kind = ResourceKind.Workflow,
                    sizeBytes,
                    sha256 = hash,
                    packagePath = WorkflowPath,
                    deploymentPurpose = "workflow",
                    distribution = DistributionDeclaration.OfflinePartial,
                    dependencyIds = Array.Empty<string>()
                }
            }
        }, JsonOptions));

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] payload, CompressionLevel compressionLevel, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, compressionLevel);
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(payload, cancellationToken);
    }

    private static void ValidateRawWorkflow(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) throw new InvalidDataException("工作流原始 JSON 不能为空。");
        using var document = JsonDocument.Parse(rawJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
        {
            throw new InvalidDataException("工作流原始 JSON 必须是对象或数组。");
        }
    }
}
