using System.Windows;
using System.Windows.Media;

namespace FlowPack.App;

public static class HighContrastPalette
{
    public static void Apply(ResourceDictionary resources, Color background, Color foreground)
    {
        foreach (var role in new[] { "Window", "Surface", "AccentSoft", "SuccessSoft", "WarningSoft", "SelectionText" }) Set(role, background);
        foreach (var role in new[] { "Text", "Muted", "Border", "Accent", "Success", "Warning", "Error", "Selection" }) Set(role, foreground);
        resources["PrimaryTextBrush"] = new SolidColorBrush(background);
        void Set(string role, Color color)
        {
            resources["Color." + role] = color;
            resources[role + "Brush"] = new SolidColorBrush(color);
        }
    }
}
