using System.Text.Json;

namespace FlowPack.ComfyUI;

public sealed record ModelDirectoryInfo(string Root, IReadOnlyList<string> Files, IReadOnlyList<string> Issues);

public static class ModelDirectory
{
    public static string? FindRoot(string file, string boundary)
    {
        string? result = null;
        var directory = Path.GetDirectoryName(file);
        var limit = Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundary));
        while (directory is not null && directory.StartsWith(limit + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            if (HasDescriptor(directory)) result = directory;
            directory = Path.GetDirectoryName(directory);
        }
        return result;
    }

    public static bool HasDescriptor(string directory)
    {
        foreach (var name in new[] { "model_index.json", "config.json" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) continue;
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    new[] { "_class_name", "model_type", "architectures" }.Any(key => json.RootElement.TryGetProperty(key, out var value) &&
                        (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ||
                        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)))) return true;
            }
            catch (JsonException) { }
        }
        return false;
    }

    public static bool IsMember(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return ResourceFiles.IsModel(path) || name.EndsWith(".index.json", StringComparison.Ordinal) ||
            name is "model_index.json" or "config.json" or "generation_config.json" or "scheduler_config.json" or
                "tokenizer.json" or "tokenizer_config.json" or "special_tokens_map.json" or "added_tokens.json" or
                "vocab.json" or "vocab.txt" or "merges.txt" or "tokenizer.model" or "spiece.model" or
                "preprocessor_config.json" or "processor_config.json" or "chat_template.jinja" or "chat_template.json" or
                "readme.md" or "license" or "license.txt" or "notice";
    }

    public static ModelDirectoryInfo Read(string root)
    {
        var files = ResourceFiles.Enumerate(root).Where(IsMember).ToArray();
        var issues = new List<string>();
        if (!HasDescriptor(root)) issues.Add("目录模型缺少可识别的模型描述文件：" + root);
        foreach (var config in files.Where(x => Path.GetFileName(x) == "config.json" && new FileInfo(x).Length <= 4 * 1024 * 1024))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(config));
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("auto_map", out var map) && map.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    issues.Add("模型声明了自定义代码，需要确认配套源码和执行要求：" + config);
            }
            catch (JsonException) { issues.Add("模型配置 JSON 无效：" + config); }
        }
        foreach (var index in files.Where(x => x.EndsWith(".index.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (new FileInfo(index).Length > 16 * 1024 * 1024) { issues.Add("模型分片索引过大：" + index); continue; }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(index));
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("weight_map", out var map) || map.ValueKind != JsonValueKind.Object)
                { issues.Add("模型分片索引缺少有效 weight_map：" + index); continue; }
                if (map.EnumerateObject().Any(x => x.Value.ValueKind != JsonValueKind.String)) issues.Add("模型分片索引包含非字符串文件名：" + index);
                foreach (var shard in map.EnumerateObject().Select(x => x.Value).Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Distinct())
                {
                    var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(index)!, shard));
                    if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        !files.Contains(path, StringComparer.OrdinalIgnoreCase)) issues.Add("模型分片缺失或越界：" + shard);
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException) { issues.Add("模型分片索引无效：" + index); }
        }
        var pipeline = Path.Combine(root, "model_index.json");
        if (files.Contains(pipeline, StringComparer.OrdinalIgnoreCase) && new FileInfo(pipeline).Length <= 4 * 1024 * 1024)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pipeline));
                foreach (var component in doc.RootElement.EnumerateObject().Where(x => !x.Name.StartsWith('_') && x.Value.ValueKind == JsonValueKind.Array && x.Value.GetArrayLength() == 2 && x.Value[0].ValueKind == JsonValueKind.String))
                {
                    if (component.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || component.Name is "." or "..")
                    { issues.Add("模型组件名称无效：" + component.Name); continue; }
                    var prefix = Path.Combine(root, component.Name) + Path.DirectorySeparatorChar;
                    if (!files.Any(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) issues.Add("模型组件文件缺失：" + component.Name);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { issues.Add("模型管线索引无效：" + pipeline); }
        }
        if (!files.Any(ResourceFiles.IsModel)) issues.Add("模型目录没有权重文件：" + root);
        return new(root, files, issues);
    }
}
