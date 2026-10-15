using FlowPack.ComfyUI;

namespace FlowPack.Infrastructure;

/// <summary>An explicit Desktop profile limits discovery; it does not grant deployment capabilities.</summary>
public static class DesktopProfileOptions
{
    public static string? FromArguments(IReadOnlyList<string> arguments)
    {
        var indexes = arguments.Select((value, index) => (value, index))
            .Where(item => item.value.Equals("--desktop-profile", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index).ToArray();
        if (indexes.Length == 0) return null;
        if (indexes.Length != 1 || indexes[0] + 1 >= arguments.Count || arguments[indexes[0] + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("--desktop-profile 需要一个绝对配置目录，且只能指定一次。");
        return NormalizeAndValidate(arguments[indexes[0] + 1]);
    }

    public static string? NormalizeAndValidate(string? profile)
    {
        if (profile is null) return null;
        if (!Path.IsPathFullyQualified(profile))
            throw new ArgumentException("--desktop-profile 需要绝对配置目录。");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("指定的 Desktop 配置目录不存在：" + root);
        var registry = Path.Combine(root, "installations.json");
        if (!File.Exists(registry)) throw new FileNotFoundException("指定的 Desktop 配置目录缺少 installations.json；不会使用默认配置。", registry);
        // Content errors belong in InstanceDiscoveryReport. Requiring every record
        // and settings file to parse here hid otherwise valid instances before the
        // UI could display diagnostics. The explicit profile remains an isolation
        // boundary even when its registration is malformed.
        return root;
    }

    public static bool SessionMatches(string? expectedProfile, string? sessionProfile)
    {
        // Sessions written before this option have no field and belong only to default discovery.
        if (expectedProfile is null || sessionProfile is null) return expectedProfile is null && sessionProfile is null;
        return Path.IsPathFullyQualified(sessionProfile) &&
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionProfile)).Equals(expectedProfile, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The older settings detector must respect the same explicitly selected profile as Worker discovery.</summary>
public sealed class DesktopProfileDetector(string profile) : IComfyDesktopDetector
{
    private readonly string _profile = DesktopProfileOptions.NormalizeAndValidate(profile)!;
    public async Task<ComfyDesktopLocation?> DetectAsync(CancellationToken cancellationToken = default)
    {
        var instances = await new DesktopInstanceDiscovery(configurationRoot: _profile).DiscoverAsync(cancellationToken).ConfigureAwait(false);
        return instances.Count == 1 ? instances[0].ToLegacyLocation() : null;
    }
}
