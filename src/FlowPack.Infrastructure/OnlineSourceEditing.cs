using FlowPack.Core;

namespace FlowPack.Infrastructure;

public sealed record OnlineSourceInput(string PlanId, string ResourceId, string SourceUrl, long Revision);

public static class OnlineSourceEditing
{
    public static ImportPlan Apply(ImportPlan plan, OnlineSourceInput input)
    {
        var original = plan.PendingDownloads.SingleOrDefault(x => x.Id == input.ResourceId)
            ?? throw new InvalidDataException("资源不属于当前待补全计划。");
        var url = input.SourceUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("请填写不含凭据或片段的 HTTPS 文件链接。");
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var name = Path.GetFileName(path);
        var pinned = uri.Host == "codeload.github.com" && path.Split('/') is ["", _, _, "zip", { Length: 40 } commit] && commit.All(Uri.IsHexDigit);
        var githubArchive = uri.Host == "github.com" && path.Contains("/archive/", StringComparison.Ordinal) &&
            Path.GetFileNameWithoutExtension(name) is { Length: 40 } sha && sha.All(Uri.IsHexDigit) && Path.GetExtension(name) == ".zip";
        if (path.Contains("/blob/", StringComparison.OrdinalIgnoreCase) || path.Contains("/tree/", StringComparison.OrdinalIgnoreCase) ||
            (uri.Host == "github.com" && !githubArchive))
            throw new InvalidDataException("网页链接不能下载。节点请填写固定提交的 ZIP 归档链接。");
        if (!pinned && Path.GetExtension(name).ToLowerInvariant() is not (".zip" or ".cpack" or ".json" or ".py" or ".safetensors" or ".ckpt" or ".pt" or ".pth" or ".bin" or ".gguf" or ".onnx" or ".sft" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".wav" or ".mp3"))
            throw new InvalidDataException("链接未指向支持的资源文件，请选择文件直链或本地文件。");
        if (original.Kind == ResourceKind.CustomNode && Path.GetExtension(name).Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            original.Sha256 is null && !pinned && !githubArchive)
            throw new InvalidDataException("未提供来源哈希的节点 ZIP 必须固定到提交版本。");
        // Preserve the declared hash, identity and deployment target; a URL edit grants no installation authority.
        var oldNotice = original.Name + (original.SourceUrl is null ? "：未提供下载来源，需要补全。" : "：尚未下载，不能作为本地安装载荷。");
        return plan with { Id = Guid.NewGuid().ToString("N"), PendingDownloads = plan.PendingDownloads.Select(x =>
            x.Id == original.Id ? x with { SourceUrl = url } : x).ToArray(),
            Issues = plan.Issues.Where(x => x != oldNotice).Append(original.Name + "：尚未下载，不能作为本地安装载荷。").Distinct().ToArray() };
    }
}
