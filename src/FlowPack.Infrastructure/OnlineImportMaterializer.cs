using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>Converts a downloaded legacy payload to native resources; never downloads or deploys by itself.</summary>
public sealed class OnlineImportMaterializer
{
    public async Task<ImportPlan> MaterializeAsync(ImportPlan plan, string resourceId, string downloadedPath, CancellationToken token = default)
    {
        if (plan.OnlineManifest is not { } manifest) throw new InvalidDataException("此导入计划不是在线清单。");
        var declaration = plan.PendingDownloads.SingleOrDefault(x => x.Id == resourceId)
            ?? throw new InvalidDataException("此资源不在原计划的待下载列表中，请刷新计划。");
        ResourceInstallationService.EnsureNoLinks(downloadedPath);
        var length = new FileInfo(downloadedPath).Length;
        var hash = await ResourceImportService.HashAsync(downloadedPath, token);
        if (length != declaration.SizeBytes || declaration.Sha256 is not null && !hash.Equals(declaration.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("下载载荷与在线清单声明的大小或 SHA-256 不一致。");
        var native = await new ResourceImportService().ImportAsync(downloadedPath, plan.StagingRoot, token);
        if (native.OnlineManifest is not null) throw new InvalidDataException("在线清单载荷不能递归引用另一在线清单。");
        var issues = new List<string>(native.Issues);
        List<ImportResource> resources;
        if (native.Resources.Count == 1 && string.Equals(Path.GetFullPath(native.Resources[0].SourcePath), Path.GetFullPath(downloadedPath), StringComparison.OrdinalIgnoreCase))
        {
            var item = native.Resources[0];
            var localDeclaration = declaration with { PackagePath = item.OriginalPath };
            resources = LegacyImportMapping.Apply(manifest with { Resources = [localDeclaration], EntryWorkflows = [], CompletenessIssues = [] }, native.Resources, issues);
        }
        else
        {
            // An archive hash describes the archive, not its members. Keep recognition evidence
            // and relative paths; a single file destination must never be copied onto every member.
            resources = native.Resources.Select(item => item with
            {
                State = item.Kind == declaration.Kind ? item.State : RecognitionState.NeedsConfirmation,
                Evidence = item.Evidence + "; 在线载荷大小已核对，" + (declaration.Sha256 is null ? "清单未提供来源哈希" : "归档来源哈希匹配")
            }).ToList();
            if (resources.Count == 0) throw new InvalidDataException("下载载荷没有可识别的资源文件。");
            if (declaration.DeploymentPurpose is { Length: > 0 })
                issues.Add("归档载荷保留内部资源路径；请核对清单用途与各项目标：" + declaration.DeploymentPurpose);
        }
        var combined = plan.Resources.Concat(resources).ToArray();
        foreach (var group in combined.GroupBy(x => x.TargetRelativePath, StringComparer.OrdinalIgnoreCase))
            if (group.Select(x => x.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                issues.Add("同名异内容资源需要处理，不能自动改名：" + group.Key);
        var remaining = plan.PendingDownloads.Where(x => x.Id != resourceId).ToArray();
        // Keep warnings from previously imported payloads; recompute pending notices separately.
        var pendingNotices = plan.PendingDownloads.Select(x => x.Name + (x.SourceUrl is null ? "：未提供下载来源，需要补全。" : "：尚未下载，不能作为本地安装载荷。"));
        issues.AddRange(plan.Issues.Except(pendingNotices));
        issues.AddRange(remaining.Select(x => x.Name + (x.SourceUrl is null ? "：未提供下载来源，需要补全。" : "：尚未下载，不能作为本地安装载荷。")));
        return plan with { Id = Guid.NewGuid().ToString("N"), Resources = combined,
            Workflows = plan.Workflows.Concat(native.Workflows).DistinctBy(x => x.Id).ToArray(),
            PendingDownloads = remaining, Issues = issues.Distinct().ToArray() };
    }
}
