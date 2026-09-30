using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Infrastructure;

/// <summary>Translates verified format-1 payloads into the same resource plan as third-party ZIPs.</summary>
public static class LegacyImportMapping
{
    public static List<ImportResource> Apply(PackageManifest manifest, IReadOnlyList<ImportResource> resources, List<string> issues)
    {
        var mapped = resources.ToList();
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Resources)
        {
            if (item.PackagePath is null)
            {
                issues.Add("旧清单资源尚未包含本地载荷，需要补全：" + item.Name);
                continue;
            }
            var packagePath = item.PackagePath.Replace('\\', '/');
            if (!declared.Add(packagePath)) throw new InvalidDataException("旧清单重复声明同一载荷：" + packagePath);
            var index = mapped.FindIndex(x => x.OriginalPath.Equals(packagePath, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidDataException("旧清单载荷未进入安全资源索引：" + packagePath);
            var file = mapped[index];
            ResourceInstallationService.EnsureNoLinks(file.SourcePath);
            if (file.SizeBytes != item.SizeBytes || item.Sha256 is not null && !file.Sha256.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("旧清单载荷在识别时与声明不一致：" + packagePath);
            // An explanatory purpose string is not necessarily a deployment path.
            var target = item.DeploymentPurpose?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(target)) continue;
            PlannedZipExportService.ValidateRelative(target);
            var parts = target.Split('/');
            var valid = item.Kind switch
            {
                ResourceKind.Model => parts.Length >= 3 && parts[0] == "models" && ResourceFiles.IsModel(file.SourcePath),
                ResourceKind.Workflow => parts.Length >= 2 && parts[0] == "workflows" && file.Kind == ResourceKind.Workflow && file.State == RecognitionState.Confirmed,
                ResourceKind.CustomNode => parts[0] == "custom_nodes" && (parts.Length >= 3 || parts.Length == 2 && target.EndsWith(".py", StringComparison.OrdinalIgnoreCase)),
                ResourceKind.Asset => parts.Length >= 2 && parts[0] == "input",
                _ => false
            };
            mapped[index] = valid ? file with { Kind = item.Kind, TargetRelativePath = target, State = RecognitionState.Confirmed,
                Evidence = "旧版清单部署路径与资源类型一致；大小已核对，" + (item.Sha256 is null ? "未提供来源哈希" : "来源哈希匹配") + "；运行可用性仍需检查" } :
                file with { State = RecognitionState.NeedsConfirmation, Evidence = "旧清单用途不是匹配资源类型的部署路径，请确认：" + target };
        }
        foreach (var entry in manifest.EntryWorkflows)
        {
            var index = mapped.FindIndex(x => x.OriginalPath.Equals(entry.RelativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (index < 0 || mapped[index].Kind != ResourceKind.Workflow)
                throw new InvalidDataException("旧包入口不是有效工作流：" + entry.RelativePath);
        }
        issues.AddRange(manifest.CompletenessIssues);
        return mapped;
    }
}
