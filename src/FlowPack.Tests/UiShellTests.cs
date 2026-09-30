using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using FlowPack.Core;
using FlowPack.ComfyUI;
using FlowPack.App;
using FlowPack.Infrastructure;

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
                var application = new FlowPack.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown, SuppressAutomaticWindow = true };
                application.InitializeComponent();
                var window = new MainWindow(new ShellViewModel()) { Width = 1280, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
                window.Show();
                window.UpdateLayout();

                var viewModel = Assert.IsType<ShellViewModel>(window.DataContext);
                var taskPanel = Assert.IsType<Border>(window.FindName("TaskPanel"));
                var tasksButton = Assert.IsType<Button>(window.FindName("TasksButton"));
                var closeTasks = Assert.IsType<Button>(window.FindName("CloseTasksButton"));
                var originalPage = viewModel.CurrentPage;
                tasksButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, taskPanel.Visibility);
                Assert.Equal(originalPage, viewModel.CurrentPage);
                closeTasks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, taskPanel.Visibility);
                Assert.Equal(originalPage, viewModel.CurrentPage);
                viewModel.LocalModels.Add(new(new("fixture", ResourceKind.Model, "测试模型.safetensors", "E:\\隔离实例\\models\\checkpoints\\测试模型.safetensors", "checkpoints/测试模型.safetensors", "checkpoints")) { IsSelected = true });
                Assert.False(viewModel.PreviewZipCommand.CanExecute("Workflow"));
                Assert.True(viewModel.PreviewZipCommand.CanExecute("Model"));
                Assert.False(viewModel.PreviewZipCommand.CanExecute("CustomNode"));
                Assert.False(viewModel.SaveZipCommand.CanExecute(null));
                viewModel.LocalModels[0].IsSelected = false;
                Assert.False(viewModel.PreviewZipCommand.CanExecute("Model"));
                viewModel.LocalModels[0].IsSelected = true;
                viewModel.ImportResources.Add(new(new("fixture", "fixture", "workflows/图像放大.json", ResourceKind.Workflow,
                    "workflows/图像放大.json", 1024, "fixture", RecognitionState.Confirmed, "界面测试示例，不是实际安装结果")));
                foreach (var fileName in new[] { "RealESRGAN_x2plus.pth", "__init__.py" })
                    viewModel.ImportResources.Add(new(new(fileName, fileName, fileName, ResourceKind.Asset,
                        "input/" + fileName, 1, "fixture", RecognitionState.Confirmed, "仅文件名显示测试")));
                viewModel.DependencyRows.Add(new(new("fixture-model", ResourceKind.Model, "示例放大模型.pth", "upscale_models",
                    DependencyState.Missing, [], ["图像放大"], "来源待确认，可填写文件链接或选择本地文件")));
                var onlineRow = new OnlineResourceRow(new("online-fixture", "在线测试工作流.json", ResourceKind.Workflow, 32, null, "https://example.invalid/workflow.json"))
                    { Status = "测试错误说明，保持可见" };
                viewModel.OnlineResources.Add(onlineRow);
                var rootLayout = Assert.IsType<Grid>(LogicalTreeHelper.FindLogicalNode(window, "RootLayout"));
                var pageHost = Assert.IsType<ContentControl>(LogicalTreeHelper.FindLogicalNode(window, "PageHost"));
                var navigation = Assert.IsType<StackPanel>(LogicalTreeHelper.FindLogicalNode(window, "PrimaryNavigation"));
                var brandArea = Assert.IsType<StackPanel>(LogicalTreeHelper.FindLogicalNode(window, "BrandArea"));
                var statusArea = Assert.IsType<StackPanel>(LogicalTreeHelper.FindLogicalNode(window, "StatusArea"));
                Assert.Equal(3, navigation.Children.OfType<Button>().Count());

                var artifactDirectory = Environment.GetEnvironmentVariable("FLOWPACK_UI_ARTIFACTS");
                viewModel.CoreTasks.Add(new("ui-task-fixture", "task.download", System.Text.Json.JsonSerializer.SerializeToElement(new { }),
                    "Failed", "模型下载 · 界面测试示例", null, "示例：连接中断，已保留暂存文件，可重试。不是实际下载结果。", 1, DateTimeOffset.UtcNow)
                    { CompletedBytes = 512, TotalBytes = 1024 });
                foreach (var theme in new[] { ThemeBase.Light, ThemeBase.Dark })
                {
                    viewModel.DraftThemeBase = theme; viewModel.PreviewThemeCommand.Execute(null);
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Assert.Equal((Color)ColorConverter.ConvertFromString(ThemeDefaults.Create(theme).SurfaceColor), application.Resources["Color.Window"]);
                    Assert.Equal((Color)application.Resources["Color.Window"], ((SolidColorBrush)window.Background).Color);
                    Assert.True(viewModel.LocalModels[0].IsSelected);
                    window.Width = 960; window.Height = 640;
                    tasksButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                    Assert.Equal(440, taskPanel.ActualWidth);
                    Assert.Empty(Descendants(taskPanel).OfType<DataGrid>());
                    var taskProgress = Assert.Single(Descendants(taskPanel).OfType<ProgressBar>());
                    Assert.Equal(512, taskProgress.Value); Assert.Equal(1024, taskProgress.Maximum);
                    Assert.Contains(Descendants(taskPanel).OfType<TextBlock>(), t => t.Text.Contains("示例：连接中断"));
                    Assert.Contains(Descendants(taskPanel).OfType<Button>(), b => Equals(b.Content, "重试") && b.IsVisible);
                    Assert.DoesNotContain(Descendants(taskPanel).OfType<Button>(), b => Equals(b.Content, "暂停") && b.IsVisible);
                    if (!string.IsNullOrWhiteSpace(artifactDirectory))
                    {
                        Directory.CreateDirectory(artifactDirectory);
                        var panelBitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        panelBitmap.Render(window);
                        var panelEncoder = new PngBitmapEncoder(); panelEncoder.Frames.Add(BitmapFrame.Create(panelBitmap));
                        using var panelOutput = File.Create(Path.Combine(artifactDirectory, $"{theme}-TaskPanel.png")); panelEncoder.Save(panelOutput);
                    }
                    closeTasks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.Width = 1280; window.Height = 800; window.UpdateLayout();
                    var combo = new ComboBox { ItemsSource = new[] { new { DisplayName = "可读的实例名称" } }, DisplayMemberPath = "DisplayName", SelectedIndex = 0 };
                    var controlWindow = new Window { Content = combo, Width = 320, Height = 160, Owner = window, ShowInTaskbar = false };
                    controlWindow.Show(); controlWindow.UpdateLayout();
                    Assert.Contains(Descendants(combo).OfType<TextBlock>(), text => text.Text == "可读的实例名称");
                    Assert.DoesNotContain(Descendants(combo).OfType<TextBlock>(), text => text.Text.Contains("DisplayName ="));
                    var modelView = new FlowPack.App.Views.ModelsView { DataContext = viewModel };
                    controlWindow.Content = modelView; controlWindow.UpdateLayout();
                    var grid = Descendants(modelView).OfType<DataGrid>().Single();
                    Assert.IsType<DataGridTemplateColumn>(grid.Columns[0]);
                    var selectionCheck = Descendants(modelView).OfType<CheckBox>().Single(check => check.DataContext is ResourceSelection);
                    Assert.True(selectionCheck.IsEnabled); Assert.True(selectionCheck.Focusable);
                    var toggle = (System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.CheckBoxAutomationPeer(selectionCheck)
                        .GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);
                    toggle.Toggle(); Assert.False(viewModel.LocalModels[0].IsSelected);
                    toggle.Toggle(); Assert.True(viewModel.LocalModels[0].IsSelected);
                    controlWindow.Close();
                    var pageBeforePreview = viewModel.CurrentPage;
                    var preview = new ExportPreviewWindow(viewModel) { Owner = window };
                    preview.Show(); preview.UpdateLayout();
                    Assert.Same(viewModel, preview.DataContext);
                    Assert.Equal((Color)application.Resources["Color.Window"], ((SolidColorBrush)preview.Background).Color);
                    Assert.Equal(2, Descendants(preview).OfType<DataGrid>().Single().Columns.Count);
                    Assert.True(Descendants(preview).OfType<DataGrid>().Single().Columns[0].ActualWidth >= 340);
                    Assert.False(Descendants(preview).OfType<Expander>().Single().IsExpanded);
                    Assert.Contains(Descendants(preview).OfType<Button>(), button => button.IsCancel);
                    if (!string.IsNullOrWhiteSpace(artifactDirectory))
                    {
                        Directory.CreateDirectory(artifactDirectory);
                        var previewBitmap = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        previewBitmap.Render(preview);
                        var previewEncoder = new PngBitmapEncoder(); previewEncoder.Frames.Add(BitmapFrame.Create(previewBitmap));
                        using var previewOutput = File.Create(Path.Combine(artifactDirectory, $"{theme}-ExportPreview.png")); previewEncoder.Save(previewOutput);
                    }
                    preview.Close();
                    Assert.Equal(pageBeforePreview, viewModel.CurrentPage);
                    Assert.True(viewModel.LocalModels[0].IsSelected);
                    foreach (var page in Enum.GetValues<FlowPage>())
                    {
                        viewModel.CurrentPage = page;
                        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        window.UpdateLayout();
                        Assert.Equal((Color)ColorConverter.ConvertFromString(ThemeDefaults.Create(theme).SurfaceColor), application.Resources["Color.Window"]);
                        if (page == FlowPage.Install)
                        {
                            foreach (var fileName in new[] { "RealESRGAN_x2plus.pth", "__init__.py" })
                            {
                                var resourceCheck = Descendants(pageHost).OfType<CheckBox>().Single(check => AutomationProperties.GetName(check) == fileName);
                                Assert.Equal(fileName, Assert.IsType<TextBlock>(resourceCheck.Content).Text);
                                Assert.DoesNotContain(Descendants(resourceCheck), child => child is AccessText);
                                Assert.Contains(Descendants(resourceCheck).OfType<TextBlock>(), text => text.Text == fileName);
                                resourceCheck.BringIntoView();
                            }
                            window.UpdateLayout();
                            var sourceDetails = Descendants(pageHost).OfType<Expander>().Single(x => Equals(x.Header, "来源与校验"));
                            sourceDetails.IsExpanded = true;
                            window.UpdateLayout();
                            sourceDetails.BringIntoView();
                            window.UpdateLayout();
                        }
                        if (page == FlowPage.Settings)
                        {
                            var checkUpdate = Descendants(pageHost).OfType<Button>().Single(button => button.Command == viewModel.CheckForUpdatesCommand);
                            Assert.Same(application.Resources[typeof(Button)], checkUpdate.Style);
                            Assert.Equal(((SolidColorBrush)application.Resources["SurfaceBrush"]).Color, ((SolidColorBrush)checkUpdate.Background).Color);
                            Assert.Equal(((SolidColorBrush)application.Resources["AccentBrush"]).Color, ((SolidColorBrush)checkUpdate.Foreground).Color);
                            Assert.NotNull(checkUpdate.FocusVisualStyle);
                            Assert.True(checkUpdate.ActualWidth < pageHost.ActualWidth / 2);
                            Assert.Contains(Descendants(pageHost).OfType<Button>(), button => button.Command == viewModel.RefreshInstancesCommand);
                            Assert.Contains(Descendants(pageHost).OfType<Button>(), button => button.Command == viewModel.AssociateInstanceCommand);
                            Assert.Contains(Descendants(pageHost).OfType<TextBlock>(), text => text.Text == "尚未选择实例");
                            var instanceHint = Descendants(pageHost).OfType<TextBlock>().Single(text => text.Text.StartsWith("多个实例可在"));
                            Assert.Equal(((SolidColorBrush)application.Resources["MutedBrush"]).Color, ((SolidColorBrush)instanceHint.Foreground).Color);
                        }
                        if (string.IsNullOrWhiteSpace(artifactDirectory)) continue;
                        Directory.CreateDirectory(artifactDirectory);
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(artifactDirectory, $"{theme}-{page}.png")); encoder.Save(output);
                    }
                    if (!string.IsNullOrWhiteSpace(artifactDirectory))
                    {
                        window.Width = 960; window.Height = 640;
                        foreach (var compactPage in new[] { FlowPage.Home, FlowPage.Library, FlowPage.Install, FlowPage.Settings, FlowPage.Packaging })
                        {
                            viewModel.CurrentPage = compactPage;
                            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                            window.UpdateLayout();
                            var compactBitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            compactBitmap.Render(window);
                            var compactEncoder = new PngBitmapEncoder(); compactEncoder.Frames.Add(BitmapFrame.Create(compactBitmap));
                            using var compactOutput = File.Create(Path.Combine(artifactDirectory, $"{theme}-{compactPage}-960.png")); compactEncoder.Save(compactOutput);
                        }
                        window.Width = 1280; window.Height = 800;
                    }
                }

                foreach (var page in Enum.GetValues<FlowPage>())
                {
                    viewModel.CurrentPage = page;
                    pageHost.ApplyTemplate();
                    pageHost.UpdateLayout();
                    Assert.True(pageHost.ActualWidth > 0, $"{page} page width was zero.");
                    Assert.True(pageHost.ActualHeight > 0, $"{page} page height was zero.");
                    Assert.Contains(Descendants(pageHost), element =>
                        AutomationProperties.GetAutomationId(element) == $"Page.{page}");
                    if (page == FlowPage.Install)
                    {
                        Descendants(pageHost).OfType<Expander>().Single(x => Equals(x.Header, "来源与校验")).IsExpanded = true;
                        pageHost.UpdateLayout();
                        Assert.Contains(Descendants(pageHost).OfType<Button>(), button => Equals(button.Content, "下载并检查") &&
                            button.Command == viewModel.DownloadOnlineCommand && ReferenceEquals(button.CommandParameter, onlineRow));
                        Assert.Contains(Descendants(pageHost).OfType<Button>(), button => button.Command == viewModel.ChooseOnlineLocalCommand);
                        Assert.Contains(Descendants(pageHost).OfType<Button>(), button => button.Command == viewModel.SaveOnlineSourceCommand && ReferenceEquals(button.CommandParameter, onlineRow));
                        Assert.Contains(Descendants(pageHost).OfType<TextBlock>(), text => text.Text == onlineRow.Status);
                    }
                }

                viewModel.CurrentPage = FlowPage.Settings;
                pageHost.UpdateLayout();
                var advancedSettings = Descendants(pageHost).OfType<Expander>().Single();
                Assert.False(advancedSettings.IsExpanded);
                advancedSettings.IsExpanded = true;
                pageHost.UpdateLayout();
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "打开 LightyearXizIl 的 GitHub 仓库");
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "复制 LightyearXizIl 的 GitHub 仓库链接");
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "选择资源库");
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "导出脱敏诊断摘要");

                viewModel.CurrentPage = FlowPage.Packaging;
                pageHost.UpdateLayout();
                Descendants(pageHost).OfType<Expander>().Single(x => Equals(x.Header, "旧草稿兼容工具")).IsExpanded = true;
                pageHost.UpdateLayout();
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "导入资源包草稿");
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    Equals(button.Content, "保存 ZIP") && button.Command == viewModel.SaveZipCommand);

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

                // Long import content scrolls independently while the install action stays in the viewport.
                window.Width = 960; window.Height = 640;
                viewModel.CurrentPage = FlowPage.Install;
                window.UpdateLayout();
                var installAction = Descendants(pageHost).OfType<Button>().Single(b => b.Command == viewModel.ExecuteDeploymentCommand);
                var installBounds = installAction.TransformToAncestor(pageHost).TransformBounds(new Rect(installAction.RenderSize));
                Assert.InRange(installBounds.Bottom, 1, pageHost.ActualHeight);
                Assert.False(viewModel.ExecuteDeploymentCommand.CanExecute(null));
                viewModel.CurrentPage = FlowPage.Library;
                window.UpdateLayout();
                var resourceGrid = Descendants(pageHost).OfType<DataGrid>().Single();
                Assert.True(resourceGrid.ActualHeight >= 100);
                Assert.Equal(3, Descendants(pageHost).OfType<Button>().Count(b => AutomationProperties.GetName(b).StartsWith("打开") && AutomationProperties.GetName(b).EndsWith("文件夹")));
                window.Width = 1280; window.Height = 800;

                viewModel.CurrentPage = FlowPage.Home;
                pageHost.UpdateLayout();
                var primaryButton = Descendants(pageHost).OfType<Button>().First(button => button.Command == viewModel.ImportNativeCommand);
                Assert.True(primaryButton.Padding.Left >= 18);
                Assert.True(primaryButton.ActualHeight >= 38);

                foreach (var darkContrast in new[] { true, false })
                {
                    var background = darkContrast ? Colors.Black : Colors.White;
                    var foreground = darkContrast ? Colors.White : Colors.Black;
                    HighContrastPalette.Apply(application.Resources, background, foreground);
                    var selectedItem = new ComboBoxItem { Content = "已选择的实例 · 高对比度夹具", IsSelected = true };
                    var unselectedItem = new ComboBoxItem { Content = "未选择的实例" };
                    var cell = new DataGridCell { Content = new TextBlock { Text = "已选择的资源" }, IsSelected = true };
                    var sample = new StackPanel { Margin = new Thickness(24) };
                    sample.Children.Add(selectedItem); sample.Children.Add(unselectedItem); sample.Children.Add(cell);
                    var contrastWindow = new Window { Content = sample, Width = 640, Height = 230, Background = new SolidColorBrush(background),
                        ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow, Title = "High contrast palette fixture" };
                    contrastWindow.Show(); contrastWindow.UpdateLayout();
                    var selectedBorder = Assert.IsType<Border>(selectedItem.Template.FindName("ItemBorder", selectedItem));
                    Assert.Equal(foreground, Assert.IsType<SolidColorBrush>(selectedBorder.Background).Color);
                    Assert.Equal(background, Assert.IsType<SolidColorBrush>(selectedItem.Foreground).Color);
                    Assert.Equal(foreground, Assert.IsType<SolidColorBrush>(cell.Background).Color);
                    Assert.Equal(background, Assert.IsType<SolidColorBrush>(cell.Foreground).Color);
                    Assert.Equal(background, Assert.IsType<SolidColorBrush>(unselectedItem.Background).Color);
                    if (!string.IsNullOrWhiteSpace(artifactDirectory))
                    {
                        var bitmap = new RenderTargetBitmap(640, 230, 96, 96, PixelFormats.Pbgra32); bitmap.Render(contrastWindow);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(artifactDirectory, $"HighContrast-{(darkContrast ? "Dark" : "Light")}.png")); encoder.Save(output);
                    }
                    contrastWindow.Close();
                }

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
