using System.Windows;

namespace FlowPack.App;

public partial class ExportPreviewWindow : Window
{
    public ExportPreviewWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    private void ClosePreview(object sender, RoutedEventArgs e) => Close();
}
