using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace FlowPack.App;
public partial class MainWindow : Window
{
    private IInputElement? _detailReturnFocus;
    private bool _detailsWereOpen;
    public MainWindow(ShellViewModel? viewModel = null, bool initializeWorkspace = false)
    {
        InitializeComponent();
        DataContext = viewModel ?? new ShellViewModel(desktopDetector: new FlowPack.ComfyUI.ComfyDesktopDetector());
        if (DataContext is ShellViewModel detailsModel) detailsModel.PropertyChanged += OnSecondaryPanelChanged;
        Loaded += async (_, _) =>
        {
            if ((viewModel is null || initializeWorkspace) && DataContext is ShellViewModel vm)
            {
                await vm.InitializeWorkspaceAsync();
            }
        };
        Closed += (_, _) => { if (DataContext is ShellViewModel vm) { vm.PropertyChanged -= OnSecondaryPanelChanged; vm.DetachWindow(); } };
    }
    private void OnSecondaryPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ShellViewModel.SecondaryPanel) or nameof(ShellViewModel.IsSecondaryPanelOpen)) || sender is not ShellViewModel vm || _detailsWereOpen == vm.IsSecondaryPanelOpen) return;
        _detailsWereOpen = vm.IsSecondaryPanelOpen;
        if (vm.IsSecondaryPanelOpen) _detailReturnFocus = Keyboard.FocusedElement;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded || !IsVisible) return;
            if (vm.IsSecondaryPanelOpen) SecondaryPanelHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            else if (_detailReturnFocus is UIElement { IsVisible: true, IsEnabled: true } target) target.Focus();
        }, DispatcherPriority.Input);
    }
    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        e.Effects = files is { Length: 1 } && DataContext is ShellViewModel { CoreReady: true }
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnSecondaryBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ShellViewModel vm) vm.CloseSecondaryPanelCommand.Execute(null);
        e.Handled = true;
    }
    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files || DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        await vm.ImportSourceAsync(files[0]);
    }
}
