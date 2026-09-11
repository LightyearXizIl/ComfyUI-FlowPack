using System.Windows;
using System.IO;
using FlowPack.App;
using FlowPack.Core;
using FlowPack.Infrastructure;

namespace FlowPack.Smoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new FlowPack.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow();
        if (window.DataContext is not ShellViewModel || window.Title != "ComfyUI FlowPack")
        {
            Console.Error.WriteLine("FlowPack WPF shell did not initialize as expected.");
            return 1;
        }
        var theme = new ThemeDefinition("1", "默认", ThemeBase.Light, "#1E63D6", "#FFFFFF", "#12233D", 14, 12, ThemeDensity.Comfortable, false);
        if (!ThemeValidator.Validate(theme).IsValid || ThemeValidator.Validate(theme with { BodyFontSize = 22 }).IsValid)
        {
            Console.Error.WriteLine("Theme validation did not enforce the documented bounds.");
            return 1;
        }
        var planner = new SafeInstallPlanner();
        var plan = planner.PlanAsync(new PackageManifest("portrait", "Portrait", "1.0", [new ResourceEntry("model", "model.safetensors", ResourceKind.Model, 1024, null, "https://example.invalid/model")]), new InstanceFingerprint("C:\\ComfyUI", "python.exe", "C:\\ComfyUI\\user", null, DateTimeOffset.UtcNow)).GetAwaiter().GetResult();
        if (plan.PeakRequiredBytes != 1024 || plan.Actions.Count != 1)
        {
            Console.Error.WriteLine("Install planning did not preserve resource requirements.");
            return 1;
        }
        if (ResourceLibraryPathValidator.IsSafeLibraryPath("C:\\FlowPack\\resources", "C:\\FlowPack", null, out _))
        {
            Console.Error.WriteLine("Resource library safety check accepted the app directory.");
            return 1;
        }
        var manifestPath = Path.Combine(Path.GetTempPath(), $"flowpack-smoke-{Guid.NewGuid():N}.cpack.json");
        try
        {
            File.WriteAllText(manifestPath, """{"formatVersion":"1","id":"demo","name":"Demo","version":"1.0","resources":[{"id":"node","name":"Example node","kind":"CustomNode","sizeBytes":42,"sourceUrl":"https://example.invalid/node"}]}""");
            var manifest = new PackageManifestReader().ReadAsync(manifestPath).GetAwaiter().GetResult();
            if (manifest.Resources.Single().Kind != ResourceKind.CustomNode)
            {
                Console.Error.WriteLine("Package manifest reader did not parse resource metadata.");
                return 1;
            }
        }
        finally { File.Delete(manifestPath); }
        Console.WriteLine("FlowPack WPF shell initialized successfully.");
        app.Shutdown();
        return 0;
    }
}
