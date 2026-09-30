using System.Windows;
using FlowPack.App.Services;
using FlowPack.ComfyUI;

namespace FlowPack.App;
public partial class App : Application
{
    public bool SuppressAutomaticWindow { get; set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (SuppressAutomaticWindow) return;
        var localization = new LocalizationService();
        MainWindow = new MainWindow(new ShellViewModel(desktopDetector: new ComfyDesktopDetector(), localization: localization), initializeWorkspace: true);
        MainWindow.Show();
    }
}
