using System.Windows;
using FlowPack.App.Services;
using FlowPack.ComfyUI;
using FlowPack.Infrastructure;

namespace FlowPack.App;
public partial class App : Application
{
    private UnhandledExceptionEventHandler? _unhandledHandler;
    private EventHandler<System.Threading.Tasks.UnobservedTaskExceptionEventArgs>? _taskExceptionHandler;
    public bool SuppressAutomaticWindow { get; set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (SuppressAutomaticWindow) return;
        PortableWorkspace? portable;
        string? desktopProfile;
        try
        {
            desktopProfile = DesktopProfileOptions.FromArguments(e.Args);
            portable = PortableWorkspace.FromStartup(AppContext.BaseDirectory, e.Args);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            NoticeDialog.ShowNotice(ex.Message, "无法启动 FlowPack");
            Shutdown(2); return;
        }
        var viewModel = portable?.CreateViewModel()
            ?? new ShellViewModel(desktopDetector: new ComfyDesktopDetector(), localization: new LocalizationService(), desktopProfile: desktopProfile);
        viewModel.StartLogging();
        DispatcherUnhandledException += (_, args) => viewModel.LogUnhandledException(args.Exception);
        _unhandledHandler = (_, args) => { if (args.ExceptionObject is Exception exception) viewModel.LogUnhandledException(exception); };
        _taskExceptionHandler = (_, args) => viewModel.LogUnhandledException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += _unhandledHandler;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += _taskExceptionHandler;
        if (portable is not null || e.Args.Contains("--settings")) viewModel.CurrentPage = FlowPage.Settings;
        if (e.Args.Contains("--about")) { viewModel.CurrentPage = FlowPage.Settings; viewModel.SettingsTabIndex = 4; }
        if (e.Args.Contains("--home")) viewModel.CurrentPage = FlowPage.Home;
        MainWindow = new MainWindow(viewModel, initializeWorkspace: true);
        if (portable is not null) MainWindow.Title += " · 便携测试";
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_unhandledHandler is not null) AppDomain.CurrentDomain.UnhandledException -= _unhandledHandler;
        if (_taskExceptionHandler is not null) System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= _taskExceptionHandler;
        if (MainWindow?.DataContext is ShellViewModel viewModel) viewModel.StopLogging();
        base.OnExit(e);
    }
}
