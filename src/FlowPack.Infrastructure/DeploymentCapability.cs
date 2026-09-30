using System.Diagnostics;
using FlowPack.ComfyUI;

namespace FlowPack.Infrastructure;

public sealed record DeploymentCapability(string InstanceId, string ConfigurationFingerprint,
    string? DesktopVersion, string? Layout, bool CanInstallFiles, bool CanInstallPython,
    string? EvidenceId, IReadOnlyList<string> Reasons)
{
    public bool Allows(bool requiresPython) => CanInstallFiles && (!requiresPython || CanInstallPython) && Reasons.Count == 0;
}

public sealed record DeploymentQualification(string DesktopVersion, string Layout, bool PythonDependencies, string EvidenceId);

public interface IDeploymentCapabilityProvider
{
    DeploymentCapability Evaluate(InstanceDescriptor instance, bool requiresPython);
}

/// <summary>Qualifications are shipped with the application, never read from an imported plan or user archive.</summary>
public sealed class DeploymentCapabilityProvider(
    IReadOnlyList<DeploymentQualification>? qualifications = null,
    Func<string, string?>? readVersion = null) : IDeploymentCapabilityProvider
{
    // Add only version/layout combinations with recorded Desktop + App + Worker qualification.
    private readonly IReadOnlyList<DeploymentQualification> _qualifications = qualifications ?? [];

    public DeploymentCapability Evaluate(InstanceDescriptor instance, bool requiresPython)
    {
        var reasons = new List<string>(instance.Issues);
        string? version = null;
        if (!instance.IsModern) reasons.Add("此环境仅支持检测和导出，不支持自动安装。");
        if (instance.ConfigurationRoot is null || instance.DesktopLayout is null)
            reasons.Add("实例记录缺少配置来源或布局依据，请重新扫描。");
        if (instance.DesktopExecutable is null || !File.Exists(instance.DesktopExecutable))
            reasons.Add("找不到 Desktop 程序，无法核对适配版本。");
        else
        {
            try
            {
                if (readVersion is null)
                {
                    var info = FileVersionInfo.GetVersionInfo(instance.DesktopExecutable);
                    version = ResolveDesktopVersion(info.ProductVersion, info.FileVersion);
                }
                else version = ResolveDesktopVersion(readVersion(instance.DesktopExecutable), null);
                if (version is null) reasons.Add("无法读取 Desktop 的有效产品版本号。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { reasons.Add("无法核对 Desktop 版本：" + ex.Message); }
        }
        var qualification = _qualifications.SingleOrDefault(x => x.DesktopVersion == version && x.Layout == instance.DesktopLayout);
        if (qualification is null || string.IsNullOrWhiteSpace(qualification.EvidenceId))
            reasons.Add($"Desktop {version ?? "未知版本"} / {instance.DesktopLayout ?? "未知布局"} 尚未通过隔离安装验收；可继续检查和导出。");
        else if (requiresPython && !qualification.PythonDependencies)
            reasons.Add("此版本及布局的 Python 依赖安装尚未通过验收；本计划需要该能力。");
        var allowed = reasons.Count == 0;
        return new(instance.Id, instance.ConfigurationFingerprint, version, instance.DesktopLayout,
            allowed, allowed && qualification?.PythonDependencies == true, qualification?.EvidenceId, reasons.Distinct().ToArray());
    }

    public static string? ResolveDesktopVersion(string? productVersion, string? fileVersion)
    {
        // Electron builds may put a non-version build identifier in FileVersion. A nonempty
        // but unrecognized product version must not inherit qualification from a fallback.
        var raw = string.IsNullOrWhiteSpace(productVersion) ? fileVersion : productVersion;
        if (!Version.TryParse(raw, out var parsed) || parsed.Build < 0) return null;
        return parsed.Revision > 0 ? parsed.ToString(4) : parsed.ToString(3);
    }
}
