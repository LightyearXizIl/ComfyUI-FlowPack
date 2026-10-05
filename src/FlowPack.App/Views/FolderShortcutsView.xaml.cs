using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FlowPack.Core;
using System.ComponentModel;
using System.Windows.Media;

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
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, VerticalOffset = 6 };
        if (button.TryFindResource(typeof(ContextMenu)) is Style menuStyle) menu.Style = menuStyle;
        if (locations.Count == 0) menu.Items.Add(new MenuItem { Header = "当前实例没有已确认的目录", IsEnabled = false });
        foreach (var location in locations)
            menu.Items.Add(new MenuItem
            {
                Header = FolderHeader(location),
                ToolTip = location.Path,
                Style = button.TryFindResource(typeof(MenuItem)) as Style,
                InputGestureText = "",
                Command = vm.OpenResourceFolderCommand,
                CommandParameter = location
            });
        if (locations.Count > 0) menu.Items.Add(new Separator { Style = button.TryFindResource(typeof(Separator)) as Style });
        menu.Items.Add(new MenuItem { Header = "查看全部资源目录", Command = vm.OpenInstanceDetailsCommand, CommandParameter = "Directories", Style = button.TryFindResource(typeof(MenuItem)) as Style });
        PropertyChangedEventHandler changed = (_, args) => { if (args.PropertyName == nameof(ShellViewModel.SelectedInstance)) menu.IsOpen = false; };
        vm.PropertyChanged += changed;
        menu.Closed += (_, _) => vm.PropertyChanged -= changed;
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }
    private static StackPanel FolderHeader(ResourceFolderLocation location)
    {
        var header = new StackPanel { MaxWidth = 380 };
        var label = new TextBlock { Text = location.Label, FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var path = new TextBlock { Text = location.Path, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        path.SetResourceReference(TextBlock.FontSizeProperty, "SubtleTextFontSize");
        header.Children.Add(label); header.Children.Add(path);
        return header;
    }
}
