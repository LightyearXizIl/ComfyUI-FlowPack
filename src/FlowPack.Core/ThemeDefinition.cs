using System.Globalization;

namespace FlowPack.Core;

public enum ThemeBase { System, Light, Dark }
public enum ThemeDensity { Comfortable, Compact }

public sealed record ThemeDefinition(
    string FormatVersion,
    string Name,
    ThemeBase Base,
    string AccentColor,
    string SurfaceColor,
    string TextColor,
    int BodyFontSize,
    int CornerRadius,
    ThemeDensity Density,
    bool EnableAnimations);

public sealed record ThemeValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ThemeValidationResult Success { get; } = new(true, []);
}

public static class ThemeValidator
{
    public static ThemeValidationResult Validate(ThemeDefinition theme)
    {
        var errors = new List<string>();
        if (theme.FormatVersion != "1") errors.Add("不支持的主题格式版本。");
        if (string.IsNullOrWhiteSpace(theme.Name)) errors.Add("主题名称不能为空。");
        if (!TryReadColor(theme.AccentColor, out _)) errors.Add("强调色必须是 #RRGGBB 格式。");
        if (!TryReadColor(theme.SurfaceColor, out var surface)) errors.Add("背景色必须是 #RRGGBB 格式。");
        if (!TryReadColor(theme.TextColor, out var text)) errors.Add("文字颜色必须是 #RRGGBB 格式。");
        if (theme.BodyFontSize is < 12 or > 20) errors.Add("正文字号必须在 12 到 20 DIP 之间。");
        if (theme.CornerRadius is < 0 or > 16) errors.Add("圆角必须在 0 到 16 DIP 之间。");
        if (errors.Count == 0 && ContrastRatio(surface, text) < 4.5) errors.Add("正文与背景的对比度必须至少为 4.5:1。");
        return errors.Count == 0 ? ThemeValidationResult.Success : new(false, errors);
    }

    private static bool TryReadColor(string value, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (value.Length != 7 || value[0] != '#') return false;
        if (!byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        color = (r, g, b);
        return true;
    }

    private static double ContrastRatio((byte R, byte G, byte B) first, (byte R, byte G, byte B) second)
    {
        static double Linear(byte component)
        {
            var value = component / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance((byte R, byte G, byte B) color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var light = Math.Max(Luminance(first), Luminance(second));
        var dark = Math.Min(Luminance(first), Luminance(second));
        return (light + 0.05) / (dark + 0.05);
    }
}
