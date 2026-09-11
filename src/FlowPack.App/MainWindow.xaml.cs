using System.Windows;

namespace FlowPack.App;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is ShellViewModel vm)
            {
                vm.DetectDesktopCommand.Execute(null);
            }
        };
    }
}
