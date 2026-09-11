using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>Reads format-1 manifests without turning incomplete drafts into installable packages.</summary>
public sealed class PackageManifestReader
{
    public async Task<PackageManifest> ReadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        await using var stream = File.OpenRead(manifestPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return Read(document.RootElement);
    }

    public PackageManifest Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("资源包清单根节点必须是对象。");
        var formatVersion = GetRequiredString(root, "formatVersion");
        if (formatVersion != "1") throw new InvalidDataException("不支持的资源包格式版本。");

        var resources = ReadResources(root);
        var workflows = ReadEntryWorkflows(root);
        var author = ReadAuthor(root);
        var source = GetOptionalString(root, "source");
        if (source is not null) EnsureHttpsUrl(source, "source");
        var distribution = ReadEnum(root, "distribution", DistributionDeclaration.Unspecified);

        var resourceIds = new HashSet<string>(resources.Select(resource => resource.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources)
        {
            foreach (var dependencyId in resource.DependencyIds)
            {
                if (!resourceIds.Contains(dependencyId)) throw new InvalidDataException($"资源 {resource.Id} 引用了不存在的依赖：{dependencyId}");
            }
        }

        var issues = FindCompletenessIssues(resources, workflows, author, source, distribution);
        return new PackageManifest(
            GetRequiredString(root, "id"),
            GetRequiredString(root, "name"),
            GetRequiredString(root, "version"),
            resources,
            formatVersion)
        {
            Description = GetOptionalString(root, "description"),
            Author = author,
            Source = source,
            EntryWorkflows = workflows,
            Distribution = distribution,
            CompletenessIssues = issues
        };
    }

    private static IReadOnlyList<ResourceEntry> ReadResources(JsonElement root)
    {
        if (!root.TryGetProperty("resources", out var resourcesJson)) return [];
        if (resourcesJson.ValueKind != JsonValueKind.Array) throw new InvalidDataException("resources 必须是数组。");

        var resources = new List<ResourceEntry>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in resourcesJson.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("resources 中的每一项必须是对象。");
            var id = GetRequiredString(item, "id");
            if (!ids.Add(id)) throw new InvalidDataException($"资源 ID 重复：{id}");
            var kindText = GetRequiredString(item, "kind");
            if (!Enum.TryParse<ResourceKind>(kindText, true, out var kind)) throw new InvalidDataException($"不支持的资源类型：{kindText}");

            var hash = GetOptionalString(item, "sha256");
            if (hash is not null && !IsSha256(hash)) throw new InvalidDataException($"资源 {id} 的 SHA-256 必须是 64 位十六进制值。");
            var sourceUrl = GetOptionalString(item, "sourceUrl");
            if (sourceUrl is not null) EnsureHttpsUrl(sourceUrl, $"资源 {id} 的 sourceUrl");
            var packagePath = GetOptionalString(item, "packagePath");
            if (packagePath is not null) EnsureSafeRelativePath(packagePath, $"资源 {id} 的 packagePath");

            var dependencies = ReadStringArray(item, "dependencyIds");
            resources.Add(new ResourceEntry(
                id,
                GetRequiredString(item, "name"),
                kind,
                GetRequiredNonNegativeInt64(item, "sizeBytes"),
                hash,
                sourceUrl)
            {
                PackagePath = packagePath,
                DeploymentPurpose = GetOptionalString(item, "deploymentPurpose"),
                Distribution = ReadEnum(item, "distribution", DistributionDeclaration.Unspecified),
                DependencyIds = dependencies
            });
        }
        return resources;
    }

    private static IReadOnlyList<WorkflowEntry> ReadEntryWorkflows(JsonElement root)
    {
        if (!root.TryGetProperty("entryWorkflows", out var workflowsJson)) return [];
        if (workflowsJson.ValueKind != JsonValueKind.Array) throw new InvalidDataException("entryWorkflows 必须是数组。");
        var workflows = new List<WorkflowEntry>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in workflowsJson.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("entryWorkflows 中的每一项必须是对象。");
            var id = GetRequiredString(item, "id");
            if (!ids.Add(id)) throw new InvalidDataException($"工作流 ID 重复：{id}");
            var relativePath = GetRequiredString(item, "path");
            EnsureSafeRelativePath(relativePath, $"工作流 {id} 的 path");
            var isEntryPoint = !item.TryGetProperty("isEntryPoint", out var entryPoint) || entryPoint.ValueKind == JsonValueKind.True;
            if (item.TryGetProperty("isEntryPoint", out entryPoint) && entryPoint.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new InvalidDataException($"工作流 {id} 的 isEntryPoint 必须是布尔值。");
            }
            workflows.Add(new WorkflowEntry(id, relativePath, isEntryPoint));
        }
        return workflows;
    }

    private static PackageAuthor? ReadAuthor(JsonElement root)
    {
        if (!root.TryGetProperty("author", out var authorJson)) return null;
        if (authorJson.ValueKind != JsonValueKind.Object) throw new InvalidDataException("author 必须是对象。");
        var url = GetOptionalString(authorJson, "url");
        if (url is not null) EnsureHttpsUrl(url, "author.url");
        return new PackageAuthor(GetRequiredString(authorJson, "name"), url);
    }

    private static IReadOnlyList<string> FindCompletenessIssues(
        IReadOnlyList<ResourceEntry> resources,
        IReadOnlyList<WorkflowEntry> workflows,
        PackageAuthor? author,
        string? source,
        DistributionDeclaration distribution)
    {
        var issues = new List<string>();
        if (author is null) issues.Add("清单缺少作者信息。");
        if (string.IsNullOrWhiteSpace(source)) issues.Add("清单缺少来源地址。");
        if (resources.Count == 0) issues.Add("清单未声明任何资源。");
        if (workflows.Count == 0 || !workflows.Any(workflow => workflow.IsEntryPoint)) issues.Add("清单缺少可用的入口工作流。");
        if (distribution == DistributionDeclaration.Unspecified) issues.Add("清单未声明分发方式。");
        if (distribution == DistributionDeclaration.OfflinePartial) issues.Add("清单声明为不完整离线分发，仍缺少部分依赖或资源。");
        foreach (var resource in resources.Where(resource => resource.SourceUrl is null && resource.PackagePath is null))
        {
            issues.Add($"资源 {resource.Id} 缺少下载来源或离线包内路径。");
        }
        return issues;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement element, string property, TEnum defaultValue) where TEnum : struct, Enum
    {
        var text = GetOptionalString(element, property);
        if (text is null) return defaultValue;
        return Enum.TryParse<TEnum>(text, true, out var value)
            ? value
            : throw new InvalidDataException($"{property} 包含不支持的值：{text}");
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var values)) return [];
        if (values.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"{property} 必须是数组。");
        var result = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException($"{property} 只能包含非空字符串。");
            result.Add(value.GetString()!);
        }
        return result;
    }

    private static string GetRequiredString(JsonElement element, string property) =>
        GetOptionalString(element, property) ?? throw new InvalidDataException($"缺少必填字段：{property}");

    private static string? GetOptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : null;

    private static long GetRequiredNonNegativeInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) && number >= 0
            ? number
            : throw new InvalidDataException($"{property} 必须是非负整数。");

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void EnsureHttpsUrl(string value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException($"{field} 必须是不含凭据或片段的 HTTPS 地址。");
        }
        var sensitiveQueryNames = new[] { "token", "key", "secret", "signature", "authorization" };
        if (uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Any(part => sensitiveQueryNames.Any(name => part.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException($"{field} 不得包含令牌或密钥查询参数。");
        }
    }

    private static void EnsureSafeRelativePath(string value, string field)
    {
        if (Path.IsPathRooted(value) || value.Contains(':') || value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"{field} 必须是受控目录内的相对路径。");
        }
    }
}
