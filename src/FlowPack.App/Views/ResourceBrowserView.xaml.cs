using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace FlowPack.App.Views;

public partial class ResourceBrowserView : UserControl
{
    public ResourceBrowserView() => InitializeComponent();
    public static readonly DependencyProperty TreeItemsProperty = DependencyProperty.Register(nameof(TreeItems), typeof(IEnumerable), typeof(ResourceBrowserView), new PropertyMetadata(null, (d, _) => ((ResourceBrowserView)d).SetValue(HasTreeItemsPropertyKey, ((ResourceBrowserView)d).TreeItems?.Cast<object>().Any() == true)));
    public IEnumerable? TreeItems { get => (IEnumerable?)GetValue(TreeItemsProperty); set => SetValue(TreeItemsProperty, value); }
    private static readonly DependencyPropertyKey HasTreeItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasTreeItems), typeof(bool), typeof(ResourceBrowserView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasTreeItemsProperty = HasTreeItemsPropertyKey.DependencyProperty;
    public bool HasTreeItems => (bool)GetValue(HasTreeItemsProperty);
    public static readonly DependencyProperty ShowDiskSectionProperty = DependencyProperty.Register(nameof(ShowDiskSection), typeof(bool), typeof(ResourceBrowserView), new PropertyMetadata(false));
    public bool ShowDiskSection { get => (bool)GetValue(ShowDiskSectionProperty); set => SetValue(ShowDiskSectionProperty, value); }
    private async void OnResourceSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ResourceTreeNode { Selection: { } row } && DataContext is ShellViewModel vm) await vm.ShowLibraryResourceAsync(row);
    }
    private async void OnDiskWorkflowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.OfType<ResourceSelection>().FirstOrDefault() is { } row && DataContext is ShellViewModel vm) await vm.ShowLibraryResourceAsync(row);
    }
}
