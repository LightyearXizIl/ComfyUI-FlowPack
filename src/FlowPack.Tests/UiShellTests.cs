using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FlowPack.App;

namespace FlowPack.Tests;

public sealed class UiShellTests
{
    [Fact]
    public void Window_renders_every_route_and_keeps_navigation_centered()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new FlowPack.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.InitializeComponent();
                var window = new MainWindow { Width = 1280, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
                window.Show();
                window.UpdateLayout();

                var viewModel = Assert.IsType<ShellViewModel>(window.DataContext);
                var rootLayout = Assert.IsType<Grid>(LogicalTreeHelper.FindLogicalNode(window, "RootLayout"));
                var pageHost = Assert.IsType<ContentControl>(LogicalTreeHelper.FindLogicalNode(window, "PageHost"));
                var navigation = Assert.IsType<StackPanel>(LogicalTreeHelper.FindLogicalNode(window, "PrimaryNavigation"));
                var brandArea = Assert.IsType<StackPanel>(LogicalTreeHelper.FindLogicalNode(window, "BrandArea"));
                var statusArea = Assert.IsType<StackPanel>(LogicalTreeHelper.FindLogicalNode(window, "StatusArea"));

                foreach (var page in Enum.GetValues<FlowPage>())
                {
                    viewModel.CurrentPage = page;
                    pageHost.ApplyTemplate();
                    pageHost.UpdateLayout();
                    Assert.True(pageHost.ActualWidth > 0, $"{page} page width was zero.");
                    Assert.True(pageHost.ActualHeight > 0, $"{page} page height was zero.");
                    Assert.Contains(Descendants(pageHost), element =>
                        AutomationProperties.GetAutomationId(element) == $"Page.{page}");
                }

                foreach (var size in new[] { (Width: 960d, Height: 640d), (Width: 1280d, Height: 800d), (Width: 1600d, Height: 1000d) })
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    window.UpdateLayout();
                    var navigationTopLeft = navigation.TransformToAncestor(rootLayout).Transform(new Point(0, 0));
                    var brandTopLeft = brandArea.TransformToAncestor(rootLayout).Transform(new Point(0, 0));
                    var statusTopLeft = statusArea.TransformToAncestor(rootLayout).Transform(new Point(0, 0));
                    var navigationCenter = navigationTopLeft.X + navigation.ActualWidth / 2;
                    var contentCenter = rootLayout.ActualWidth / 2;
                    Assert.InRange(Math.Abs(navigationCenter - contentCenter), 0, 1);
                    Assert.True(brandTopLeft.X + brandArea.ActualWidth <= navigationTopLeft.X, $"Brand overlapped navigation at {size.Width} DIP.");
                    Assert.True(navigationTopLeft.X + navigation.ActualWidth <= statusTopLeft.X, $"Status overlapped navigation at {size.Width} DIP.");
                }

                viewModel.CurrentPage = FlowPage.Home;
                pageHost.UpdateLayout();
                var primaryButton = Descendants(pageHost).OfType<Button>().First(button => Equals(button.Content, "创建资源包"));
                Assert.True(primaryButton.Padding.Left >= 18);
                Assert.True(primaryButton.ActualHeight >= 38);

                window.Close();
                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF layout test timed out.");
        Assert.Null(failure);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
