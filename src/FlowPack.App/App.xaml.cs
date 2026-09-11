using System.Windows;
using FlowPack.App.Services;
using FlowPack.ComfyUI;

namespace FlowPack.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var localization = new LocalizationService();
        MainWindow = new MainWindow(new ShellViewModel(desktopDetector: new ComfyDesktopDetector(), localization: localization));
        MainWindow.Show();
    }
}
