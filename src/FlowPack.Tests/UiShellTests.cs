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
using FlowPack.App.Services;

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
                var updateService = new UiUpdateService();
                var window = new MainWindow(new ShellViewModel(updateService: updateService)) { Width = 1280, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
                window.Show();
                window.UpdateLayout();

                var viewModel = Assert.IsType<ShellViewModel>(window.DataContext);
                Assert.Null(window.FindName("TasksButton"));
                Assert.Null(window.FindName("TaskPanel"));
                viewModel.LocalModels.Add(new(new("fixture", ResourceKind.Model, "测试模型.safetensors", "E:\\隔离实例\\models\\checkpoints\\测试模型.safetensors", "checkpoints/测试模型.safetensors", "checkpoints")) { IsSelected = true });
                Assert.False(viewModel.PreviewZipCommand.CanExecute("Workflow"));
                Assert.True(viewModel.PreviewZipCommand.CanExecute("Model"));
                Assert.False(viewModel.PreviewZipCommand.CanExecute("CustomNode"));
                Assert.False(viewModel.SaveZipCommand.CanExecute(null));
                viewModel.LocalModels[0].IsSelected = false;
                Assert.False(viewModel.PreviewZipCommand.CanExecute("Model"));
                viewModel.LocalModels[0].IsSelected = true;
                var homeExport = Descendants(window).OfType<Button>().Single(button => Equals(button.Content, "导出资源"));
                homeExport.Command.Execute(homeExport.CommandParameter);
                window.UpdateLayout();
                Assert.Equal(FlowPage.Packaging, viewModel.CurrentPage);
                Assert.Empty(Descendants(window).OfType<TabControl>());
                Assert.Contains(Descendants(window).OfType<ListBox>(), list => AutomationProperties.GetName(list) == "最终导出清单" && list.ActualHeight >= 100);
                var returnResources = Descendants(window).OfType<Button>().Single(button => Equals(button.Content, "返回资源库"));
                returnResources.Command.Execute(null);
                viewModel.NavigateCommand.Execute("Models"); window.UpdateLayout();
                var libraryTabs = Descendants(window).OfType<TabControl>().Single();
                Assert.Equal(new[] { "工作流", "模型", "节点" }, libraryTabs.Items.Cast<TabItem>().Select(tab => tab.Header));
                Assert.Equal(1, libraryTabs.SelectedIndex);
                Assert.True(viewModel.LocalModels[0].IsSelected);
                viewModel.BeginExportCommand.Execute(null); window.UpdateLayout();
                Assert.True(viewModel.IsPackagingContext);
                viewModel.NavigateCommand.Execute("Home");
                window.UpdateLayout();
                var homeImport = Descendants(window).OfType<Button>().Single(button => Equals(button.Content, "导入资源"));
                homeImport.Command.Execute(homeImport.CommandParameter);
                window.UpdateLayout();
                Assert.Empty(Descendants(window).OfType<TabControl>());
                Assert.Contains(Descendants(window).OfType<Border>(), b => AutomationProperties.GetAutomationId(b) == "Import.Source");
                Assert.Contains(Descendants(window).OfType<Button>(), button => button.Command == viewModel.ImportNativeFolderCommand);

                viewModel.NavigateCommand.Execute("Install");
                window.UpdateLayout();
                Assert.Equal(0, viewModel.InstallTabIndex);
                viewModel.NavigateCommand.Execute("Home");
                window.UpdateLayout();
                Assert.DoesNotContain(Descendants(window).OfType<Button>(), button => Equals(button.Content, "查看后台任务"));
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
                foreach (var theme in new[] { ThemeBase.Light, ThemeBase.Dark })
                {
                    viewModel.DraftThemeBase = theme; viewModel.PreviewThemeCommand.Execute(null);
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Assert.Equal((Color)ColorConverter.ConvertFromString(ThemeDefaults.Create(theme).SurfaceColor), application.Resources["Color.Window"]);
                    Assert.Equal((Color)application.Resources["Color.Window"], ((SolidColorBrush)window.Background).Color);
                    Assert.True(viewModel.LocalModels[0].IsSelected);
                    window.Width = 960; window.Height = 640;
                    window.Width = 1280; window.Height = 800; window.UpdateLayout();
                    var combo = new ComboBox { ItemsSource = new[] { new { DisplayName = "可读的实例名称" } }, DisplayMemberPath = "DisplayName", SelectedIndex = 0 };
                    var controlWindow = new Window { Content = combo, Width = 320, Height = 160, Owner = window, ShowInTaskbar = false };
                    controlWindow.Show(); controlWindow.UpdateLayout();
                    Assert.Contains(Descendants(combo).OfType<TextBlock>(), text => text.Text == "可读的实例名称");
                    Assert.DoesNotContain(Descendants(combo).OfType<TextBlock>(), text => text.Text.Contains("DisplayName ="));
                    var modelView = new FlowPack.App.Views.ModelsView { DataContext = viewModel };
                    controlWindow.Content = modelView; controlWindow.UpdateLayout();
                    typeof(ShellViewModel).GetMethod("RebuildResourceTrees", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(viewModel, null);
                    controlWindow.UpdateLayout();
                    var tree = Descendants(modelView).OfType<TreeView>().Single();
                    foreach (var item in Descendants(tree).OfType<TreeViewItem>().ToArray()) item.IsExpanded = true;
                    controlWindow.UpdateLayout();
                    var selectionCheck = Descendants(modelView).OfType<CheckBox>().Single(check => check.DataContext is ResourceTreeNode { Selection: not null });
                    Assert.True(selectionCheck.IsEnabled); Assert.True(selectionCheck.Focusable);
                    var toggle = (System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.CheckBoxAutomationPeer(selectionCheck)
                        .GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);
                    toggle.Toggle(); Assert.False(viewModel.LocalModels[0].IsSelected);
                    toggle.Toggle(); Assert.True(viewModel.LocalModels[0].IsSelected);
                    controlWindow.Close();
                    Assert.True(viewModel.LocalModels[0].IsSelected);
                    foreach (var page in Enum.GetValues<FlowPage>().Where(page => page != FlowPage.Tasks))
                    {
                        viewModel.CurrentPage = page;
                        if (page == FlowPage.Library) viewModel.LibraryTabIndex = 0;
                        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        window.UpdateLayout();
                        Assert.Equal((Color)ColorConverter.ConvertFromString(ThemeDefaults.Create(theme).SurfaceColor), application.Resources["Color.Window"]);
                        if (page is FlowPage.Home or FlowPage.Library or FlowPage.Install)
                        {
                            Assert.Equal(page == FlowPage.Home ? 1 : 0, Descendants(pageHost).OfType<FlowPack.App.Views.FolderShortcutsView>().Count());
                            if (page == FlowPage.Home)
                            {
                                var homeActions = Descendants(pageHost).OfType<Grid>().Single(element => AutomationProperties.GetAutomationId(element) == "Home.Actions");
                                var homeStatus = Descendants(pageHost).OfType<Border>().Single(element => AutomationProperties.GetAutomationId(element) == "Home.Status");
                                var actionsBounds = homeActions.TransformToAncestor(pageHost).TransformBounds(new Rect(homeActions.RenderSize));
                                var statusBounds = homeStatus.TransformToAncestor(pageHost).TransformBounds(new Rect(homeStatus.RenderSize));
                                Assert.True(statusBounds.Left >= actionsBounds.Right + 20, "首页状态模块应在操作区域右侧独立展示。");
                                Assert.InRange(statusBounds.Right, 1, pageHost.ActualWidth);
                                Assert.InRange(statusBounds.Bottom, 1, pageHost.ActualHeight);
                                Assert.InRange(Math.Abs(statusBounds.Top - actionsBounds.Top), 0, 1);
                                Assert.InRange(Math.Abs(statusBounds.Bottom - actionsBounds.Bottom), 0, 1);
                            }
                        }
                        if (page == FlowPage.Install)
                        {
                            Descendants(pageHost).OfType<Expander>().Single(x => Equals(x.Header, "查看并调整资源")).IsExpanded = true; window.UpdateLayout();
                            var actionStyle = Assert.IsType<Style>(application.Resources["StandardActionButton"]);
                            var expectedAction = new Button { Style = actionStyle };
                            foreach (var action in Descendants(pageHost).OfType<Button>().Where(button => button.Content is string))
                            {
                                Assert.Equal(((SolidColorBrush)expectedAction.Background).Color, ((SolidColorBrush)action.Background).Color);
                                Assert.Equal(((SolidColorBrush)expectedAction.Foreground).Color, ((SolidColorBrush)action.Foreground).Color);
                                Assert.Equal(expectedAction.Padding, action.Padding);
                                Assert.Same(expectedAction.Template, action.Template);
                                Assert.Same(expectedAction.FocusVisualStyle, action.FocusVisualStyle);
                            }
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
                            var settingsTabs = Descendants(pageHost).OfType<TabControl>().Single();
                            Assert.Equal(new[] { "界面偏好", "Desktop 实例", "存储位置", "日志", "关于作者" }, settingsTabs.Items.Cast<TabItem>().Select(tab => tab.Header));
                            Assert.Equal(Dock.Left, settingsTabs.TabStripPlacement);
                            foreach (var size in new[] { (Width: 1280d, Height: 800d), (Width: 960d, Height: 640d) })
                            {
                                window.Width = size.Width; window.Height = size.Height;
                                for (var section = 0; section < 5; section++)
                                {
                                    settingsTabs.SelectedIndex = section;
                                    window.UpdateLayout();
                                    Assert.Equal(section, viewModel.SettingsTabIndex);
                                    Assert.Equal(section == 4, Descendants(pageHost).OfType<Button>().Any(button => button.Command == viewModel.CheckForUpdatesCommand));
                                    Assert.Equal(section == 2, Descendants(pageHost).OfType<Button>().Any(button => button.Command == viewModel.SelectResourceLibraryCommand));
                                    Assert.Equal(section == 1, Descendants(pageHost).OfType<Button>().Any(button => button.Command == viewModel.AssociateInstanceCommand));
                                    Assert.Equal(section is 0 or 3 ? 2 : 0, Descendants(pageHost).OfType<ComboBox>().Count());
                                    if (section == 3)
                                    {
                                        var loggingInputs = Descendants(pageHost).OfType<ComboBox>().ToArray();
                                        Assert.Contains(loggingInputs, input => AutomationProperties.GetAutomationId(input) == "Logs.Intensity" && input.Items.Count == 4);
                                        Assert.Contains(loggingInputs, input => AutomationProperties.GetAutomationId(input) == "Logs.Retention" && input.Items.Cast<int>().SequenceEqual(new[] { 7, 10, 15, 30 }));
                                        var clearLogs = Descendants(pageHost).OfType<Button>().Single(button => button.Command == viewModel.ClearLogsCommand);
                                        Assert.InRange(clearLogs.TransformToAncestor(pageHost).TransformBounds(new Rect(clearLogs.RenderSize)).Bottom, 1, pageHost.ActualHeight);
                                        Assert.Contains(Descendants(pageHost).OfType<Button>(), button => button.Command == viewModel.OpenLogsFolderCommand);
                                    }
                                    Assert.DoesNotContain(Descendants(pageHost).OfType<Button>(), button =>
                                        button.Command == viewModel.ImportThemeCommand || button.Command == viewModel.ExportThemeCommand ||
                                        button.Command == viewModel.PreviewThemeCommand || button.Command == viewModel.ApplyThemeCommand);
                                    foreach (var button in Descendants(pageHost).OfType<Button>())
                                        Assert.InRange(button.TransformToAncestor(pageHost).TransformBounds(new Rect(button.RenderSize)).Right, 1, pageHost.ActualWidth);
                                    if (section == 4)
                                    {
                                        var updateAction = Descendants(pageHost).OfType<Button>().Single(button => button.Command == viewModel.CheckForUpdatesCommand);
                                        updateAction.BringIntoView(); window.UpdateLayout();
                                        Assert.InRange(updateAction.TransformToAncestor(pageHost).TransformBounds(new Rect(updateAction.RenderSize)).Bottom, 1, pageHost.ActualHeight);
                                    }
                                    if (!string.IsNullOrWhiteSpace(artifactDirectory))
                                    {
                                        Directory.CreateDirectory(artifactDirectory);
                                        var settingsBitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                                        settingsBitmap.Render(window);
                                        var settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
                                        using var settingsOutput = File.Create(Path.Combine(artifactDirectory, $"{theme}-Settings-{section}-{size.Width}.png")); settingsEncoder.Save(settingsOutput);
                                    }
                                }
                            }
                            window.Width = 1280; window.Height = 800;
                            viewModel.SettingsTabIndex = 4;
                            window.UpdateLayout();
                            var checkUpdate = Descendants(pageHost).OfType<Button>().Single(button => button.Command == viewModel.CheckForUpdatesCommand);
                            Assert.Same(application.Resources[typeof(Button)], checkUpdate.Style);
                            Assert.Equal(((SolidColorBrush)application.Resources["SurfaceBrush"]).Color, ((SolidColorBrush)checkUpdate.Background).Color);
                            Assert.Equal(((SolidColorBrush)application.Resources["AccentBrush"]).Color, ((SolidColorBrush)checkUpdate.Foreground).Color);
                            Assert.NotNull(checkUpdate.FocusVisualStyle);
                            Assert.True(checkUpdate.ActualWidth < pageHost.ActualWidth / 2);
                            var repositoryLink = Assert.Single(Descendants(pageHost).OfType<TextBlock>().SelectMany(text => text.Inlines.OfType<System.Windows.Documents.Hyperlink>()));
                            Assert.Same(viewModel.OpenRepositoryCommand, repositoryLink.Command);
                            Assert.Equal(viewModel.RepositoryUrl, Assert.Single(repositoryLink.Inlines.OfType<System.Windows.Documents.Run>()).Text);
                            viewModel.SettingsTabIndex = 1;
                            window.UpdateLayout();
                            Assert.Contains(Descendants(pageHost).OfType<Button>(), button => button.Command == viewModel.RefreshInstancesCommand);
                            Assert.Contains(Descendants(pageHost).OfType<Button>(), button => button.Command == viewModel.AssociateInstanceCommand);
                            Assert.Contains(Descendants(pageHost).OfType<TextBlock>(), text => text.Text == "尚未选择实例");
                            var instanceHint = Descendants(pageHost).OfType<TextBlock>().Single(text => text.Text.StartsWith("多个实例可在"));
                            Assert.Equal(((SolidColorBrush)application.Resources["MutedBrush"]).Color, ((SolidColorBrush)instanceHint.Foreground).Color);
                            viewModel.SettingsTabIndex = 0;
                            window.UpdateLayout();
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
                            if (compactPage == FlowPage.Library) viewModel.LibraryTabIndex = 0;
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

                VerifyBackgroundProgressAndNavigation(window, viewModel);

                // Long instance names and scan evidence must not move the main actions or stretch the status panel.
                var homeInstance = HomeStatusTests.Instance() with { ModelRoots = [@"E:\fixture\共享模型搜索目录\用于检查路径省略与提示的较长名称目录"], InputDirectory = @"E:\fixture\input" };
                viewModel.DesktopInstances.Add(homeInstance);
                var homeHints = Enumerable.Range(1, 20).Select(i => $"节点包需要运行实例信息补充类型：界面测试节点-{i}").ToArray();
                HomeStatusTests.Set(viewModel, "_selectedInstance", homeInstance);
                HomeStatusTests.Set(viewModel, "_inventory", new ResourceInventory(homeInstance, [], [], homeHints));
                HomeStatusTests.Set(viewModel, "_lastHomeScanNotice", "界面测试扫描结果");
                HomeStatusTests.Set(viewModel, "_coreNotice", "界面测试扫描结果");
                foreach (var theme in new[] { ThemeBase.Light, ThemeBase.Dark })
                {
                    viewModel.DraftThemeBase = theme; viewModel.PreviewThemeCommand.Execute(null);
                    foreach (var size in new[] { (Width: 960d, Height: 640d), (Width: 1280d, Height: 800d), (Width: 1600d, Height: 900d) })
                    {
                        viewModel.CurrentPage = FlowPage.Install;
                        viewModel.CurrentPage = FlowPage.Home;
                        window.Width = size.Width; window.Height = size.Height;
                        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        window.UpdateLayout();
                        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(pageHost).TransformBounds(new Rect(element.RenderSize));
                        var status = Descendants(pageHost).OfType<Border>().Single(e => AutomationProperties.GetAutomationId(e) == "Home.Status");
                        var actions = Descendants(pageHost).OfType<Grid>().Single(e => AutomationProperties.GetAutomationId(e) == "Home.Actions");
                        Assert.InRange(Math.Abs(Bounds(status).Bottom - Bounds(actions).Bottom), 0, 1);
                        var import = Descendants(pageHost).OfType<Button>().Single(b => Equals(b.Content, "导入资源"));
                        var export = Descendants(pageHost).OfType<Button>().Single(b => Equals(b.Content, "导出资源"));
                        Assert.InRange(Math.Abs(Bounds(import).Top - Bounds(export).Top), 0, 1);
                        Assert.InRange(Bounds(import).Bottom, 1, pageHost.ActualHeight);
                        var instanceCard = Descendants(pageHost).OfType<Border>().Single(e => AutomationProperties.GetAutomationId(e) == "Home.Instance");
                        Assert.Empty(Descendants(instanceCard).OfType<ScrollViewer>());
                        foreach (var action in Descendants(instanceCard).OfType<Button>())
                            Assert.InRange(action.TransformToAncestor(instanceCard).TransformBounds(new Rect(action.RenderSize)).Bottom, 1, instanceCard.ActualHeight);
                        var disclosure = Descendants(status).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Home.Details");
                        Assert.Empty(Descendants(status).OfType<Expander>());
                        Assert.InRange(Bounds(disclosure).Bottom, 1, pageHost.ActualHeight);
                        if (!string.IsNullOrWhiteSpace(artifactDirectory))
                        {
                            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var output = File.Create(Path.Combine(artifactDirectory, $"{theme}-Home-Hints-{size.Width}.png")); encoder.Save(output);
                        }
                        var importBeforeDetails = Bounds(import);
                        var statusBeforeDetails = Bounds(status);
                        disclosure.Command.Execute(null);
                        window.UpdateLayout();
                        Assert.InRange(Math.Abs(Bounds(status).Bottom - Bounds(actions).Bottom), 0, 1);
                        Assert.InRange(Math.Abs(Bounds(import).Top - importBeforeDetails.Top), 0, 1);
                        Assert.InRange(Math.Abs(Bounds(status).Height - statusBeforeDetails.Height), 0, 1);
                        var panel = Assert.IsType<ContentControl>(window.FindName("SecondaryPanelHost"));
                        Assert.True(panel.IsVisible);
                        Assert.InRange(panel.ActualHeight, 100, window.ActualHeight);
                        var overlay = Assert.IsType<Grid>(window.FindName("SecondaryOverlay"));
                        Assert.False(pageHost.IsEnabled); Assert.False(navigation.IsEnabled);
                        var dialogBounds = panel.TransformToAncestor(overlay).TransformBounds(new Rect(panel.RenderSize));
                        Assert.InRange(Math.Abs(dialogBounds.X + dialogBounds.Width / 2 - overlay.ActualWidth / 2), 0, 1);
                        Assert.InRange(Math.Abs(dialogBounds.Y + dialogBounds.Height / 2 - overlay.ActualHeight / 2), 0, 1);
                        Assert.InRange(dialogBounds.Top, 0, overlay.ActualHeight);
                        Assert.InRange(dialogBounds.Bottom, 0, overlay.ActualHeight);
                        var backdrop = Assert.IsType<Border>(window.FindName("SecondaryBackdrop"));
                        var dialogHit = overlay.InputHitTest(new Point(dialogBounds.X + 20, dialogBounds.Y + 20));
                        Assert.NotSame(backdrop, dialogHit);
                        Assert.Same(backdrop, overlay.InputHitTest(new Point(4, 4)));
                        var detailScroll = Descendants(panel).OfType<ScrollViewer>().Single(scroll => AutomationProperties.GetAutomationId(scroll) == "Secondary.StatusScroll");
                        detailScroll.ScrollToBottom(); window.UpdateLayout();
                        Assert.Contains(Descendants(panel).OfType<TextBlock>(), block => block.Text == "界面测试节点-20");
                        Assert.True(detailScroll.ExtentHeight > detailScroll.ViewportHeight);
                        SaveSecondaryScreenshot(window, artifactDirectory, $"{theme}-Secondary-Status-{size.Width}.png");
                        viewModel.CloseSecondaryPanelCommand.Execute(null); window.UpdateLayout();
                        viewModel.OpenInstanceDetailsCommand.Execute("Versions"); window.UpdateLayout();
                        Assert.Contains(Descendants(panel).OfType<TextBlock>(), block => block.Text == "Desktop 版本");
                        var dialogTabs = Descendants(panel).OfType<TabControl>().Single();
                        var topNav = navigation.Children.OfType<Button>().First();
                        foreach (var tab in dialogTabs.Items.Cast<TabItem>())
                        {
                            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(tab.Background).Color);
                            Assert.Equal(topNav.Padding, tab.Padding);
                            Assert.Equal(topNav.MinHeight, tab.MinHeight);
                            Assert.Equal(topNav.FontSize, tab.FontSize);
                            Assert.Equal(topNav.FontWeight, Descendants(tab).OfType<TextBlock>().First(text => text.Text == (string)tab.Header).FontWeight);
                            Assert.Equal(new Thickness(0, 0, 0, 3), tab.BorderThickness);
                            Assert.Equal(((SolidColorBrush)application.Resources[tab.IsSelected ? "AccentBrush" : "MutedBrush"]).Color,
                                Assert.IsType<SolidColorBrush>(tab.Foreground).Color);
                        }
                        VerifyDialogActions(panel, import);
                        var bodyLabel = Descendants(panel).OfType<TextBlock>().Single(block => block.Text == "Desktop 版本");
                        Assert.Equal(FontWeights.Normal, bodyLabel.FontWeight);
                        Assert.InRange(bodyLabel.TransformToAncestor(panel).Transform(new Point()).X, 24, 32);
                        SaveSecondaryScreenshot(window, artifactDirectory, $"{theme}-Secondary-Versions-{size.Width}.png");
                        viewModel.InstanceDetailTabIndex = 1; window.UpdateLayout();
                        Assert.Contains(Descendants(panel).OfType<TextBlock>(), block => block.Text == homeInstance.ModelsWriteDirectory);
                        VerifyDialogActions(panel, import);
                        Assert.Equal(importBeforeDetails, Bounds(import));
                        SaveSecondaryScreenshot(window, artifactDirectory, $"{theme}-Secondary-Directories-{size.Width}.png");
                        backdrop.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
                            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                        window.UpdateLayout(); Assert.False(viewModel.IsSecondaryPanelOpen);
                        Assert.True(pageHost.IsEnabled); Assert.True(navigation.IsEnabled);
                        VerifySecondaryMenus(window, viewModel, pageHost, artifactDirectory, theme, size.Width);
                        if (size.Width == 1280)
                            VerifyOtherDialogs(window, viewModel, import, artifactDirectory, theme);
                    }
                }
                HomeStatusTests.Set(viewModel, "_selectedInstance", null);
                HomeStatusTests.Set(viewModel, "_inventory", null);
                HomeStatusTests.Set(viewModel, "_lastHomeScanNotice", null);
                HomeStatusTests.Set(viewModel, "_coreNotice", "等待扫描 Desktop 实例。");
                viewModel.DesktopInstances.Clear();
                window.Width = 1280; window.Height = 800;

                foreach (var page in Enum.GetValues<FlowPage>().Where(page => page != FlowPage.Tasks))
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
                viewModel.SettingsTabIndex = 0;
                pageHost.UpdateLayout();
                Assert.Empty(Descendants(pageHost).OfType<Expander>());
                Assert.DoesNotContain(Descendants(pageHost).OfType<TextBox>(), input =>
                    new[] { "强调色", "页面背景色", "正文颜色", "主题名称" }.Contains(AutomationProperties.GetName(input)));
                Assert.DoesNotContain(Descendants(pageHost).OfType<Button>(), button =>
                    button.Command == viewModel.ImportThemeCommand || button.Command == viewModel.ExportThemeCommand ||
                    button.Command == viewModel.PreviewThemeCommand || button.Command == viewModel.ApplyThemeCommand);
                Assert.Equal(2, Descendants(pageHost).OfType<ComboBox>().Count());
                viewModel.SettingsTabIndex = 4;
                pageHost.UpdateLayout();
                Assert.Contains(Descendants(pageHost).OfType<TextBlock>().SelectMany(text => text.Inlines.OfType<System.Windows.Documents.Hyperlink>()), link =>
                    AutomationProperties.GetName(link) == "打开 LightyearXizIl 的 GitHub 仓库");
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "复制 LightyearXizIl 的 GitHub 仓库链接");
                viewModel.NavigateCommand.Execute("Home");
                viewModel.NavigateCommand.Execute("Settings");
                pageHost.UpdateLayout();
                Assert.Equal(4, Descendants(pageHost).OfType<TabControl>().Single().SelectedIndex);
                viewModel.SettingsTabIndex = 2;
                pageHost.UpdateLayout();
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "关联 FlowPack 数据目录");
                viewModel.SettingsTabIndex = 3;
                pageHost.UpdateLayout();
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "导出脱敏诊断摘要");

                viewModel.CurrentPage = FlowPage.Packaging;
                pageHost.UpdateLayout();
                Descendants(pageHost).OfType<Expander>().Single(x => Equals(x.Header, "导出详情与高级选项")).IsExpanded = true;
                pageHost.UpdateLayout();
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    button.Command == viewModel.ImportDraftCommand);
                Assert.Contains(Descendants(pageHost).OfType<Button>(), button =>
                    Equals(button.Content, "导出 ZIP…") && button.Command == viewModel.SaveZipCommand);

                foreach (var size in new[] { (Width: 960d, Height: 640d), (Width: 1280d, Height: 800d), (Width: 1600d, Height: 900d) })
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
                viewModel.NavigateCommand.Execute("Models");
                window.UpdateLayout();
                var resourceGrid = Descendants(pageHost).OfType<TreeView>().Single();
                Assert.True(resourceGrid.ActualHeight >= 100);
                Assert.Equal(0, Descendants(pageHost).OfType<Button>().Count(b => AutomationProperties.GetName(b).StartsWith("打开") && AutomationProperties.GetName(b).EndsWith("文件夹")));
                viewModel.NavigateCommand.Execute("Packaging");
                window.UpdateLayout();
                var packageGrid = Descendants(pageHost).OfType<ListBox>().Single(x => x.ItemsSource == viewModel.ExportSelectionRows);
                Assert.True(packageGrid.ActualHeight >= 100, $"最小窗口打包预览高度不足：{packageGrid.ActualHeight}");
                var saveZip = Descendants(pageHost).OfType<Button>().Single(button => button.Command == viewModel.SaveZipCommand);
                Assert.InRange(saveZip.TransformToAncestor(pageHost).TransformBounds(new Rect(saveZip.RenderSize)).Bottom, 1, pageHost.ActualHeight);
                window.Width = 1280; window.Height = 800;

                viewModel.CurrentPage = FlowPage.Home;
                pageHost.UpdateLayout();
                var primaryButton = Descendants(pageHost).OfType<Button>().First(button => button.Command == viewModel.NavigateCommand && Equals(button.CommandParameter, "Install"));
                Assert.True(primaryButton.Padding.Left >= 14);
                foreach (var button in Descendants(pageHost).OfType<Button>()) { Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(button.Background).Color); Assert.Equal((Color)ColorConverter.ConvertFromString("#1D1D1F"), Assert.IsType<SolidColorBrush>(button.Foreground).Color); }
                Assert.True(primaryButton.ActualHeight >= 38);

                window.Width = 960; window.Height = 640;
                viewModel.CurrentPage = FlowPage.Settings; viewModel.SettingsTabIndex = 4;
                window.UpdateLayout();
                var updateButton = Descendants(pageHost).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Update.Action");
                Assert.Equal(viewModel.Text["Update.Check"], updateButton.Content);
                Assert.DoesNotContain(Descendants(pageHost).OfType<Button>(), button => button.Command == viewModel.InstallUpdateCommand);
                updateService.Result = new(new Version(9, 0, 0), new Uri("https://example.invalid/setup.exe"), "setup.exe", new string('A', 64), new Uri("https://example.invalid/SHA256SUMS.txt"));
                updateButton.Command.Execute(null);
                window.UpdateLayout();
                Assert.Same(updateButton, Descendants(pageHost).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Update.Action"));
                Assert.Equal(viewModel.Text["Update.Download"], updateButton.Content);
                Assert.Same(viewModel.InstallUpdateCommand, updateButton.Command);
                Assert.True(updateButton.IsEnabled);
                Assert.InRange(updateButton.TransformToAncestor(pageHost).TransformBounds(new Rect(updateButton.RenderSize)).Bottom, 1, pageHost.ActualHeight);
                if (!string.IsNullOrWhiteSpace(artifactDirectory))
                {
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(artifactDirectory, "Dark-UpdateAvailable-960.png")); encoder.Save(output);
                }

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

                // Exercise WPF's actual keyboard focus traversal, including the shared navigation.
                viewModel.NavigateCommand.Execute("Home"); window.UpdateLayout(); window.Activate();
                var firstNavigation = navigation.Children.OfType<Button>().First();
                Assert.True(firstNavigation.Focus());
                for (var step = 0; step < 5; step++)
                {
                    var focused = Assert.IsAssignableFrom<UIElement>(System.Windows.Input.Keyboard.FocusedElement);
                    Assert.True(focused.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
                    var next = Assert.IsAssignableFrom<UIElement>(System.Windows.Input.Keyboard.FocusedElement);
                    Assert.NotSame(focused, next); Assert.True(next.IsVisible); Assert.True(next.IsEnabled);
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

    private static void VerifyDialogActions(DependencyObject dialog, Button homeAction)
    {
        foreach (var button in Descendants(dialog).OfType<Button>().Where(button => button.Content is string))
        {
            Assert.Equal(Assert.IsType<SolidColorBrush>(homeAction.Background).Color, Assert.IsType<SolidColorBrush>(button.Background).Color);
            Assert.Equal(Assert.IsType<SolidColorBrush>(homeAction.Foreground).Color, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            Assert.Equal(homeAction.MinHeight, button.MinHeight);
            Assert.Equal(homeAction.Padding, button.Padding);
            Assert.Same(homeAction.Template, button.Template);
            Assert.Same(homeAction.FocusVisualStyle, button.FocusVisualStyle);
        }
    }

    private static void VerifyOtherDialogs(Window owner, ShellViewModel viewModel, Button homeAction, string? artifacts, ThemeBase theme)
    {
        var notice = new NoticeDialog(string.Join("\n", Enumerable.Range(1, 20).Select(i => $"提示测试第 {i} 行：完整错误原因与较长目录仍可阅读。")), "需要处理的提示")
            { Owner = owner };
        Exception? failure = null;
        notice.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                notice.UpdateLayout();
                VerifyDialogActions(notice, homeAction);
                Assert.InRange(Math.Abs(notice.Left + notice.ActualWidth / 2 - owner.Left - owner.ActualWidth / 2), 0, 2);
                Assert.InRange(Math.Abs(notice.Top + notice.ActualHeight / 2 - owner.Top - owner.ActualHeight / 2), 0, 2);
                var scroll = Descendants(notice).OfType<ScrollViewer>().Single();
                Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
                var close = Descendants(notice).OfType<Button>().Single();
                Assert.True(close.IsDefault); Assert.True(close.IsCancel); Assert.True(close.IsKeyboardFocused);
                SaveSecondaryScreenshot(notice, artifacts, $"{theme}-Notice.png");
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception) { failure = exception; notice.Close(); }
        }));
        notice.ShowDialog();
        Assert.False(notice.IsVisible);
        if (failure is not null) throw failure;
        var legacy = new ExportPreviewWindow(viewModel) { Owner = owner };
        legacy.Show(); legacy.UpdateLayout();
        try
        {
            VerifyDialogActions(legacy, homeAction);
            SaveSecondaryScreenshot(legacy, artifacts, $"{theme}-Legacy-Export.png");
        }
        finally { legacy.Close(); }
    }

    private static void SaveSecondaryScreenshot(Window window, string? directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }

    private static void VerifySecondaryMenus(MainWindow window, ShellViewModel vm, ContentControl pageHost, string? directory, ThemeBase theme, double width)
    {
        // Complete the modal's queued focus restoration before simulating the next click.
        // Otherwise a CI dispatcher can restore background focus after the menu opens,
        // causing WPF to dismiss it as it would when another control receives focus.
        window.Activate();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var modelButton = Descendants(pageHost).OfType<Button>().Single(button => Equals(button.Content, "模型文件夹"));
        modelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu = Assert.IsType<ContextMenu>(modelButton.ContextMenu);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        menu.UpdateLayout();
        Assert.True(menu.IsOpen);
        var surface = Assert.IsType<Border>(menu.Template.FindName("MenuSurface", menu));
        Assert.Equal(window.FindResource("CardCornerRadius"), surface.CornerRadius);
        Assert.Equal(((SolidColorBrush)window.FindResource("SurfaceBrush")).Color, Assert.IsType<SolidColorBrush>(surface.Background).Color);
        var first = Assert.IsType<MenuItem>(menu.Items[0]);
        first.ApplyTemplate();
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Grid>(first.Template.FindName("IconSlot", first)).Visibility);
        var header = Assert.IsType<StackPanel>(first.Header);
        Assert.Equal(2, header.Children.Count);
        Assert.Equal(vm.GetResourceFolders(ResourceKind.Model)[0].Path, Assert.IsType<TextBlock>(header.Children[1]).Text);
        var parent = new MenuItem { Header = "嵌套菜单验收", Style = (Style)window.FindResource(typeof(MenuItem)) };
        var child = new MenuItem { Header = "三级菜单条目", Style = (Style)window.FindResource(typeof(MenuItem)) };
        parent.Items.Add(child); menu.Items.Add(parent); menu.UpdateLayout(); parent.IsSubmenuOpen = true;
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(parent.Template.FindName("PART_Popup", parent));
        Assert.True(popup.IsOpen);
        var subSurface = Assert.IsType<Border>(popup.Child);
        Assert.Equal(surface.CornerRadius, subSurface.CornerRadius);
        Assert.Equal(Assert.IsType<SolidColorBrush>(surface.Background).Color, Assert.IsType<SolidColorBrush>(subSurface.Background).Color);
        parent.IsSubmenuOpen = false;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, $"{theme}-Folder-Menu-{width}.png")); encoder.Save(output);
        }
        menu.IsOpen = false;
        var combo = Descendants(window).OfType<ComboBox>().First();
        combo.IsDropDownOpen = true;
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var dropdown = Assert.IsType<Border>(combo.Template.FindName("DropdownSurface", combo));
        Assert.Equal(surface.CornerRadius, dropdown.CornerRadius);
        Assert.Equal(Assert.IsType<SolidColorBrush>(surface.Background).Color, Assert.IsType<SolidColorBrush>(dropdown.Background).Color);
        combo.IsDropDownOpen = false;
    }

    private static void VerifyBackgroundProgressAndNavigation(MainWindow window, ShellViewModel vm)
    {
        var file = Path.Combine(Path.GetTempPath(), "flowpack-ui-slow-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(file, new byte[2 * 1024 * 1024]);
        var frame = new System.Windows.Threading.DispatcherFrame();
        var heartbeat = 0; var sawIntermediate = false;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(ShellViewModel).GetMethod("BeginOperationProgress", flags)!.Invoke(vm, null);
        Assert.True(vm.OperationIndeterminate); Assert.Equal(0, vm.OperationPercent);
        var progress = new SlowUiProgress(window.Dispatcher, value =>
        {
            var job = new WorkerJob("slow", "export.plan", System.Text.Json.JsonSerializer.SerializeToElement(new { }), "Running", value.Stage, null, null, 1, DateTimeOffset.UtcNow)
                { CompletedUnits = value.Completed, TotalUnits = value.Total, ProgressUnit = value.Unit };
            typeof(ShellViewModel).GetMethod("SetOperationProgress", flags)!.Invoke(vm, [job]);
            if (vm.OperationPercent > 0 && vm.OperationPercent < 100) sawIntermediate = true;
        });
        var hash = Task.Run(() => ResourceImportService.HashAsync(file, progress: progress));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            heartbeat++;
            vm.NavigateCommand.Execute(heartbeat % 2 == 0 ? "Home" : "Install"); window.UpdateLayout();
            if (hash.IsCompleted) { timer.Stop(); frame.Continue = false; }
        };
        try
        {
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); hash.GetAwaiter().GetResult();
            Assert.True(heartbeat >= 3, "后台读取期间 UI 心跳必须持续执行。"); Assert.True(sawIntermediate);
            Assert.True(vm.OperationVisible);
            typeof(ShellViewModel).GetMethod("FailOperationProgress", flags)!.Invoke(vm, ["测试失败"]);
            typeof(ShellViewModel).GetMethod("EndOperationProgress", flags)!.Invoke(vm, null);
            Assert.Contains("测试失败", vm.OperationText); Assert.False(vm.OperationIndeterminate);
            vm.DismissOperationCommand.Execute(null); Assert.False(vm.OperationVisible);
        }
        finally { timer.Stop(); File.Delete(file); }
    }
    private sealed class SlowUiProgress(System.Windows.Threading.Dispatcher dispatcher, Action<OperationProgress> action) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) { dispatcher.BeginInvoke(() => action(value)); Thread.Sleep(30); }
    }

    private sealed class UiUpdateService : IUpdateService
    {
        public UpdateInfo? Result { get; set; }
        public Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default) => Task.FromResult(Result);
    }
}
