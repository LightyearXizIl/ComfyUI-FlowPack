using System.Windows;

namespace FlowPack.App;
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel? viewModel = null)
    {
        InitializeComponent();
        DataContext = viewModel ?? new ShellViewModel(desktopDetector: new FlowPack.ComfyUI.ComfyDesktopDetector());
        Loaded += (_, _) =>
        {
            if (DataContext is ShellViewModel vm)
            {
                vm.DetectDesktopCommand.Execute(null);
            }
        };
    }
}
