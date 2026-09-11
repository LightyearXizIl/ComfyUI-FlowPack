using System.Text.Json;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

public sealed class PackageManifestReader
{
    public async Task<PackageManifest> ReadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(manifestPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var formatVersion = GetRequiredString(root, "formatVersion");
        if (formatVersion != "1") throw new InvalidDataException("不支持的资源包格式版本。");
        var resources = new List<ResourceEntry>();
        if (root.TryGetProperty("resources", out var resourcesJson))
        {
            if (resourcesJson.ValueKind != JsonValueKind.Array) throw new InvalidDataException("resources 必须是数组。");
            foreach (var item in resourcesJson.EnumerateArray())
            {
                var kindText = GetRequiredString(item, "kind");
                if (!Enum.TryParse<ResourceKind>(kindText, ignoreCase: true, out var kind)) throw new InvalidDataException($"不支持的资源类型：{kindText}");
                resources.Add(new ResourceEntry(
                    GetRequiredString(item, "id"),
                    GetRequiredString(item, "name"),
                    kind,
                    item.TryGetProperty("sizeBytes", out var size) && size.TryGetInt64(out var bytes) && bytes >= 0 ? bytes : throw new InvalidDataException("资源大小必须是非负整数。"),
                    item.TryGetProperty("sha256", out var hash) ? hash.GetString() : null,
                    item.TryGetProperty("sourceUrl", out var source) ? source.GetString() : null));
            }
        }
        return new PackageManifest(GetRequiredString(root, "id"), GetRequiredString(root, "name"), GetRequiredString(root, "version"), resources, formatVersion);
    }

    private static string GetRequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new InvalidDataException($"缺少必填字段：{property}");
}
