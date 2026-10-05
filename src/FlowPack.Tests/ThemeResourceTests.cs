using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using FlowPack.App;
using FlowPack.ComfyUI;
using FlowPack.Core;

namespace FlowPack.Tests;

public sealed class ThemeResourceTests
{
    [Fact]
    public void Populated_resource_tree_retains_readable_text_arrows_selection_and_focus_when_theme_changes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            HwndSource? source = null;
            try
            {
                // Load the actual shared styles and resource browser markup without creating a second
                // Application singleton. Event handlers are excluded because this fixture tests rendering.
                var repository = FindRepository();
                var resources = XDocument.Load(System.IO.Path.Combine(repository, "src", "FlowPack.App", "App.xaml"));
                var browserMarkup = XDocument.Load(System.IO.Path.Combine(repository, "src", "FlowPack.App", "Views", "ResourceBrowserView.xaml"));
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var root = browserMarkup.Root!;
                root.Attribute(x + "Class")!.Remove();
                root.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:FlowPack.App;assembly=ComfyUI.FlowPack");
                root.SetAttributeValue(XNamespace.Xmlns + "sys", "clr-namespace:System;assembly=mscorlib");
                foreach (var handler in root.Descendants().Attributes().Where(attribute => attribute.Name.LocalName is "SelectedItemChanged" or "SelectionChanged").ToArray())
                    handler.Remove();
                root.Element(presentation + "UserControl.Resources")!.AddFirst(
                    resources.Root!.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
                var browser = Assert.IsType<UserControl>(XamlReader.Parse(browserMarkup.ToString()));
                // Window.Show depends on process-wide Application shutdown state. A dedicated
                // presentation source remains valid whether UiShellTests ran before or after this test.
                source = new HwndSource(new HwndSourceParameters("FlowPack resource theme acceptance fixture")
                {
                    Width = 960, Height = 640, WindowStyle = unchecked((int)0x90000000), ExtendedWindowStyle = 0x80
                });
                var layout = new Grid { Resources = browser.Resources };
                layout.SetResourceReference(Panel.BackgroundProperty, "WindowBrush");
                browser.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                layout.Children.Add(browser);
                source.RootVisual = layout;
                SetForegroundWindow(source.Handle);
                SetFocus(source.Handle);
                Realize(layout);
                var tree = Descendants(browser).OfType<TreeView>().Single();
                var model = new ResourceSelection(new("theme-fixture", ResourceKind.Model,
                    "用于深浅色主题验收的长名称模型.safetensors", @"E:\隔离实例\models\checkpoints\主题验收模型.safetensors",
                    "checkpoints/主题验收模型.safetensors", "checkpoints")) { IsSelected = true };
                var directory = new ResourceTreeNode("checkpoints", @"E:\隔离实例\models\checkpoints");
                directory.Children.Add(new ResourceTreeNode(model.Name, model.Path, model));
                tree.ItemsSource = new[] { directory };
                browser.DataContext = new
                {
                    LibrarySelectionTitle = model.Name,
                    LibrarySelectionNotice = "已选择模型，可与工作流一起导出。",
                    LibrarySelectionPath = model.Path,
                    LibraryAnalysisBusy = false,
                    LibraryDependencyGroups = Array.Empty<ResourceDependencyGroup>(),
                    HasDiskOnlyWorkflows = false
                };
                Realize(layout);
                var folder = Assert.IsType<TreeViewItem>(tree.ItemContainerGenerator.ContainerFromIndex(0));
                folder.IsExpanded = true; Realize(layout);
                var resource = Assert.IsType<TreeViewItem>(folder.ItemContainerGenerator.ContainerFromIndex(0));
                var focusTarget = new Button { Content = "Focus away from tree" };
                focusTarget.VerticalAlignment = VerticalAlignment.Bottom;
                focusTarget.HorizontalAlignment = HorizontalAlignment.Right;
                focusTarget.Style = (Style)browser.Resources["SecondaryButton"];
                layout.Children.Add(focusTarget); Realize(layout);

                foreach (var dark in new[] { false, true, false })
                {
                    ApplyPalette(browser.Resources, dark);
                    Realize(layout);
                    var expectedText = ReadColor(dark ? "#F5F5F7" : "#1D1D1F");
                    var expectedMuted = ReadColor(dark ? "#B1B1B8" : "#646469");
                    var expectedSelection = ReadColor(dark ? "#38383C" : "#EBEBEF");
                    resource.IsSelected = false;
                    Realize(layout);
                    Assert.Equal(expectedText, BrushColor(folder.Foreground));
                    Assert.Equal(expectedText, BrushColor(resource.Foreground));
                    var folderLabel = Descendants(folder).OfType<TextBlock>().Single(block => block.Text == directory.Name);
                    var modelLabel = Descendants(resource).OfType<TextBlock>().Single(block => block.Text == model.Name);
                    Assert.Equal(expectedText, BrushColor(folderLabel.Foreground));
                    Assert.Equal(expectedText, BrushColor(modelLabel.Foreground));
                    var expander = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(folder.Template.FindName("Expander", folder));
                    var arrow = Assert.IsType<System.Windows.Shapes.Path>(expander.Template.FindName("TreeChevron", expander));
                    Assert.Equal(expectedText, BrushColor(arrow.Stroke));
                    Assert.Equal(90d, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
                    var checkbox = Descendants(resource).OfType<CheckBox>().Single();
                    Assert.True(checkbox.IsChecked);
                    Assert.True(checkbox.IsEnabled);

                    resource.IsSelected = true;
                    Assert.True(resource.Focus());
                    Realize(layout);
                    var header = Assert.IsType<Border>(resource.Template.FindName("TreeHeader", resource));
                    var focus = Assert.IsType<Border>(resource.Template.FindName("TreeFocus", resource));
                    Assert.Equal(expectedSelection, BrushColor(header.Background));
                    Assert.Equal(expectedText, BrushColor(modelLabel.Foreground));
                    Assert.Equal(Visibility.Visible, focus.Visibility);
                    Assert.True(focusTarget.Focus());
                    Realize(layout);
                    Assert.True(resource.IsSelected);
                    Assert.Equal(expectedSelection, BrushColor(header.Background));
                    Assert.Equal(expectedText, BrushColor(modelLabel.Foreground));
                    Assert.Equal(Visibility.Collapsed, focus.Visibility);

                    folder.IsEnabled = false;
                    Realize(layout);
                    Assert.Equal(expectedMuted, BrushColor(folderLabel.Foreground));
                    Assert.Equal(expectedMuted, BrushColor(modelLabel.Foreground));
                    Assert.Equal(expectedMuted, BrushColor(arrow.Stroke));
                    Assert.False(checkbox.IsEnabled);
                    folder.IsEnabled = true;
                    Realize(layout);
                    foreach (var dpi in new[] { 96d, 120d, 144d })
                    {
                        AssertVisibleWithin(modelLabel, tree);
                        AssertVisibleWithin(checkbox, tree);
                        Assert.Equal(browser.Resources["ControlCornerRadius"], header.CornerRadius);
                        Capture(layout, $"ResourceTree-{(dark ? "Dark" : "Light")}-Populated", dpi);
                    }
                }

                browser.Resources["ControlCornerRadius"] = new CornerRadius(11);
                Realize(layout);
                Assert.Equal(new CornerRadius(11), Assert.IsType<Border>(resource.Template.FindName("TreeHeader", resource)).CornerRadius);
                Assert.True(tree.ActualHeight >= 100);
                VerifyDisabledExportList(repository, resources, source, layout, model);
            }
            catch (Exception exception) { failure = exception; }
            finally { source?.Dispose(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Resource theme fixture timed out.");
        Assert.Null(failure);
    }

    private static void VerifyDisabledExportList(string repository, XDocument appResources, HwndSource source, Grid layout, ResourceSelection model)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var markup = XDocument.Load(System.IO.Path.Combine(repository, "src", "FlowPack.App", "Views", "PackageWizardView.xaml"));
        var root = markup.Root!;
        root.Attribute(x + "Class")!.Remove();
        root.SetAttributeValue(XNamespace.Xmlns + "sys", "clr-namespace:System;assembly=mscorlib");
        root.AddFirst(new XElement(presentation + "UserControl.Resources",
            appResources.Root!.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element))));
        var view = Assert.IsType<UserControl>(XamlReader.Parse(markup.ToString()));
        view.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        view.DataContext = new
        {
            ExportSelectionRows = new[] { model }, CoreReady = true,
            ExportContentsSummary = "工作流 0 · 模型 1 · 节点包 0",
            ExportStatus = "非空导出清单主题验收"
        };
        layout.Children.Clear(); layout.Resources = view.Resources; layout.Children.Add(view);
        Realize(layout);
        var list = Descendants(view).OfType<ListBox>().Single();
        Assert.Single(list.Items);

        void AssertListTheme(bool dark)
        {
            var surface = Assert.IsType<Border>(list.Template.FindName("ListSurface", list));
            Assert.Equal(0, BrushColor(surface.Background).A); // Export's explicit Transparent must survive disabling.
            var item = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(ReadColor(dark ? "#F5F5F7" : "#1D1D1F"), BrushColor(item.Foreground));
            var label = Descendants(item).OfType<TextBlock>().Single(block => block.Text == model.Name);
            Assert.Equal(ReadColor(dark ? "#F5F5F7" : "#1D1D1F"), BrushColor(label.Foreground));
            var check = Descendants(item).OfType<CheckBox>().Single();
            Assert.Equal(list.IsEnabled, check.IsEnabled);
            AssertVisibleWithin(label, list);
            AssertVisibleWithin(check, list);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.True(ScrollViewer.GetCanContentScroll(list));
        }

        foreach (var dark in new[] { false, true })
        {
            ApplyPalette(view.Resources, dark);
            foreach (var enabled in new[] { true, false })
            {
                list.IsEnabled = enabled; Realize(layout);
                AssertListTheme(dark);
                Capture(layout, $"ExportList-{(dark ? "Dark" : "Light")}-{(enabled ? "Enabled" : "Disabled")}", 96);
            }
        }

        var dialog = new SaveFileDialog { Title = "FlowPack disabled export theme fixture", Filter = "ZIP resource package|*.zip", FileName = "theme-fixture.zip" };
        Exception? modalFailure = null;
        var observed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) =>
        {
            // Enumerate only this STA thread, and act only on a native dialog owned by this fixture's HWND.
            var dialogHandle = IntPtr.Zero;
            EnumThreadWindows(GetCurrentThreadId(), (handle, _) =>
            {
                if (GetWindow(handle, 4) != source.Handle) return true;
                var className = new StringBuilder(64); GetClassName(handle, className, className.Capacity);
                if (className.ToString() != "#32770") return true;
                dialogHandle = handle; return false;
            }, IntPtr.Zero);
            if (dialogHandle == IntPtr.Zero) return;
            try
            {
                observed = true;
                Assert.False(IsWindowEnabled(source.Handle));
                Assert.False(list.IsEnabled);
                AssertListTheme(true);
                Capture(layout, "ExportList-Dark-NativeSaveDialog", 96);
            }
            catch (Exception exception) { modalFailure = exception; }
            finally { timer.Stop(); PostMessage(dialogHandle, 0x0111, new IntPtr(2), IntPtr.Zero); }
        };
        try
        {
            timer.Start();
            // The protected native entry point supplies the exact owner HWND. Using ShowDialog()
            // would resolve a process-global Application owner left by another UI test.
            var runDialog = typeof(SaveFileDialog).GetMethod("RunDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.False(Assert.IsType<bool>(runDialog.Invoke(dialog, new object[] { source.Handle })));
            Assert.True(observed, "The fixture's owned native save dialog must have been observed.");
            Assert.Null(modalFailure);
        }
        finally { timer.Stop(); }
    }

    private static void Realize(FrameworkElement visual)
    {
        visual.Measure(new Size(960, 640));
        visual.Arrange(new Rect(0, 0, 960, 640));
        visual.UpdateLayout();
        visual.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        visual.UpdateLayout();
    }

    private delegate bool EnumThreadWindowCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder className, int capacity);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr handle);

    private static void ApplyPalette(ResourceDictionary resources, bool dark)
    {
        foreach (var (name, color) in new[]
        {
            ("Window", dark ? "#161618" : "#F5F5F7"), ("Surface", dark ? "#222225" : "#FFFFFF"),
            ("Text", dark ? "#F5F5F7" : "#1D1D1F"), ("Muted", dark ? "#B1B1B8" : "#646469"),
            ("Border", dark ? "#3A3A3F" : "#DFDFE3"), ("Accent", dark ? "#F5F5F7" : "#1D1D1F"),
            ("AccentSoft", dark ? "#38383C" : "#EBEBEF"), ("Selection", dark ? "#38383C" : "#EBEBEF"),
            ("SelectionText", dark ? "#F5F5F7" : "#1D1D1F")
        }) resources[name + "Brush"] = new SolidColorBrush(ReadColor(color));
        resources["PrimaryTextBrush"] = new SolidColorBrush(dark ? Colors.Black : Colors.White);
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "src", "FlowPack.App", "App.xaml"))) return directory.FullName;
        throw new DirectoryNotFoundException("FlowPack source markup could not be found for the rendering fixture.");
    }

    private static Color ReadColor(string value) => (Color)ColorConverter.ConvertFromString(value)!;
    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void AssertVisibleWithin(FrameworkElement element, FrameworkElement ancestor)
    {
        Assert.True(element.IsVisible);
        Assert.True(element.ActualWidth > 0);
        Assert.True(element.ActualHeight > 0);
        var bounds = element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
        Assert.InRange(bounds.Left, -0.5, ancestor.ActualWidth);
        Assert.InRange(bounds.Top, -0.5, ancestor.ActualHeight);
        Assert.InRange(bounds.Right, 0, ancestor.ActualWidth + 0.5);
        Assert.InRange(bounds.Bottom, 0, ancestor.ActualHeight + 0.5);
    }

    private static void Capture(FrameworkElement visual, string name, double dpi)
    {
        // This verifies WPF bitmap output at the requested DPI; it does not change the HWND's
        // monitor DPI or the user's Windows scaling preference.
        var pixelWidth = (int)Math.Ceiling(visual.ActualWidth * dpi / 96d);
        var pixelHeight = (int)Math.Ceiling(visual.ActualHeight * dpi / 96d);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Assert.Equal(pixelWidth, bitmap.PixelWidth);
        Assert.Equal(pixelHeight, bitmap.PixelHeight);
        Assert.Equal(dpi, bitmap.DpiX);
        Assert.Equal(dpi, bitmap.DpiY);
        var directory = Environment.GetEnvironmentVariable("FLOWPACK_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(System.IO.Path.Combine(directory, $"{name}-{dpi:N0}Dpi.png"));
        encoder.Save(output);
    }
}
