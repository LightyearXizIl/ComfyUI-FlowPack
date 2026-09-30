using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FlowPack.Core;

namespace FlowPack.App.Views;

public partial class FolderShortcutsView : UserControl
{
    public static readonly DependencyProperty ShowLabelProperty = DependencyProperty.Register(
        nameof(ShowLabel), typeof(bool), typeof(FolderShortcutsView), new PropertyMetadata(true));
    public bool ShowLabel { get => (bool)GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }
    public FolderShortcutsView() => InitializeComponent();

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not ShellViewModel vm ||
            !Enum.TryParse<ResourceKind>(button.Tag as string, out var kind)) return;
        var locations = vm.GetResourceFolders(kind);
        if (locations.Count == 1) { vm.OpenResourceFolderCommand.Execute(locations[0]); return; }
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        if (locations.Count == 0) menu.Items.Add(new MenuItem { Header = "当前实例没有已确认的目录", IsEnabled = false });
        foreach (var location in locations)
            menu.Items.Add(new MenuItem
            {
                Header = location.Label + "\n" + location.Path,
                ToolTip = location.Path,
                Command = vm.OpenResourceFolderCommand,
                CommandParameter = location
            });
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
