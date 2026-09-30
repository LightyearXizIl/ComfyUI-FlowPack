using System.Windows;

namespace FlowPack.App;
public partial class MainWindow : Window
{
    private ExportPreviewWindow? _exportPreview;
    private IInputElement? _taskReturnFocus;
    private void OnTasksToggle(object sender, RoutedEventArgs e)
    {
        if (TaskPanel.Visibility == Visibility.Visible) { CloseTaskPanel(); return; }
        _taskReturnFocus = System.Windows.Input.Keyboard.FocusedElement;
        TaskPanel.Visibility = Visibility.Visible;
        CloseTasksButton.Focus();
    }
    private void OnTasksClose(object sender, RoutedEventArgs e) => CloseTaskPanel();
    private void OnTaskPanelKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        CloseTaskPanel(); e.Handled = true;
    }
    private void CloseTaskPanel()
    {
        TaskPanel.Visibility = Visibility.Collapsed;
        if (_taskReturnFocus is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus();
        else TasksButton.Focus();
        _taskReturnFocus = null;
    }
    public MainWindow(ShellViewModel? viewModel = null, bool initializeWorkspace = false)
    {
        InitializeComponent();
        DataContext = viewModel ?? new ShellViewModel(desktopDetector: new FlowPack.ComfyUI.ComfyDesktopDetector());
        if (DataContext is ShellViewModel initial) initial.ExportPreviewRequested += OnExportPreviewRequested;
        Loaded += async (_, _) =>
        {
            if ((viewModel is null || initializeWorkspace) && DataContext is ShellViewModel vm)
            {
                await vm.InitializeWorkspaceAsync();
            }
        };
        Closed += (_, _) => { if (DataContext is ShellViewModel vm) { vm.ExportPreviewRequested -= OnExportPreviewRequested; vm.DetachWindow(); } };
    }
    private void OnExportPreviewRequested(object? sender, EventArgs e)
    {
        // Return to the command first so its busy state is cleared before entering the modal message loop.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded || sender is not ShellViewModel vm) return;
            if (_exportPreview is not null) { _exportPreview.Activate(); return; }
            _exportPreview = new ExportPreviewWindow(vm) { Owner = this };
            try { _exportPreview.ShowDialog(); }
            finally { _exportPreview = null; }
        }));
    }
    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        e.Effects = files is { Length: 1 } && DataContext is ShellViewModel { CoreReady: true }
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files || DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        await vm.ImportSourceAsync(files[0]);
    }
}
