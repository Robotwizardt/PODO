using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

[Collection(WpfWindowTestCollection.Name)]
public sealed class DesktopBoxWindowSizingTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace ShellNamespace =
        "clr-namespace:System.Windows.Shell;assembly=PresentationFramework";

    [Fact]
    public void DesktopBoxWindow_StartsContentSizedAndAllowsDirectResize()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var window = Assert.Single(document.Descendants(PresentationNamespace + "Window"));

        Assert.Equal("WidthAndHeight", (string?)window.Attribute("SizeToContent"));
        Assert.Equal("CanResize", (string?)window.Attribute("ResizeMode"));
    }

    [Fact]
    public void DesktopBoxWindow_DoesNotExposeSizeModeChoices()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var buttons = document.Descendants(PresentationNamespace + "Button").ToArray();

        Assert.DoesNotContain(
            buttons,
            button => ((string?)button.Attribute("Command"))?.Contains(
                "UseAdaptiveModeCommand",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            buttons,
            button => ((string?)button.Attribute("Command"))?.Contains(
                "UseFixedModeCommand",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            buttons,
            button => string.Equals(
                (string?)button.Attribute("Content"),
                "固定",
                StringComparison.Ordinal));
    }

    [Fact]
    public void DesktopBoxWindow_ReservesAnOuterNativeResizeBorder()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var chrome = Assert.Single(
            document.Descendants(ShellNamespace + "WindowChrome"));

        Assert.Equal("8", (string?)chrome.Attribute("ResizeBorderThickness"));
    }

    [Fact]
    public void MappingListView_FillsTheWindowWhenItIsResizedManually()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var list = Assert.Single(
            document.Descendants(PresentationNamespace + "ListBox"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "FileList");

        Assert.Null(list.Attribute("Width"));
        Assert.Null(list.Attribute("MaxHeight"));
        Assert.Equal("Stretch", (string?)list.Attribute("HorizontalAlignment"));
        Assert.Equal("Stretch", (string?)list.Attribute("VerticalAlignment"));
        Assert.Equal("Stretch", (string?)list.Attribute("HorizontalContentAlignment"));
    }

    [Fact]
    public void ResizeBorderPoint_IsNotRoutedAsWholeBoxDrag()
    {
        Assert.True(DesktopBoxWindow.IsResizeBorderPoint(new Point(2, 80), 400, 240));
        Assert.True(DesktopBoxWindow.IsResizeBorderPoint(new Point(200, 2), 400, 240));
        Assert.False(DesktopBoxWindow.IsResizeBorderPoint(new Point(200, 80), 400, 240));
    }

    [Fact]
    public void ApplySavedWindowSize_SelectsManualOrContentSizingBeforeShow()
    {
        RunOnSta(() =>
        {
            var root = CreateTempRoot();
            var application = new WitchDrawer.App.App();
            application.InitializeComponent();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                var paths = new AppPaths(root);
                var repository = new DrawerRepository(paths.DatabasePath);
                var drawerService = new DrawerService(paths, repository);
                drawerService.InitializeAsync().GetAwaiter().GetResult();
                var box = drawerService.CreateBoxAsync("尺寸切换测试", BoxType.Normal)
                    .GetAwaiter()
                    .GetResult();
                var viewModel = new DesktopBoxViewModel(
                    box,
                    drawerService,
                    new TodoService(repository),
                    new NoOpFileLauncher(),
                    new NoOpLogger(),
                    BoxVisualStyle.Modern);
                viewModel.LoadAsync().GetAwaiter().GetResult();

                var contentSizedWindow = new DesktopBoxWindow(viewModel)
                {
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false
                };
                try
                {
                    contentSizedWindow.ApplySavedWindowSize(new BoxWindowSizeState(640, 480));
                    Assert.Equal(SizeToContent.Manual, contentSizedWindow.SizeToContent);
                    Assert.Equal(640, contentSizedWindow.Width);
                    Assert.Equal(480, contentSizedWindow.Height);

                    contentSizedWindow.ApplySavedWindowSize(null);
                    Assert.Equal(SizeToContent.WidthAndHeight, contentSizedWindow.SizeToContent);
                    Assert.True(double.IsNaN(contentSizedWindow.Width));
                    Assert.True(double.IsNaN(contentSizedWindow.Height));
                    contentSizedWindow.Show();
                    contentSizedWindow.UpdateLayout();
                    PumpDispatcher();
                    Assert.True(
                        contentSizedWindow.ActualWidth < 640,
                        $"Expected content sizing to use the content width, got {contentSizedWindow.ActualWidth}.");
                    Assert.True(
                        contentSizedWindow.ActualHeight < 480,
                        $"Expected content sizing to use the content height, got {contentSizedWindow.ActualHeight}.");
                }
                finally
                {
                    contentSizedWindow.ForceClose();
                }
            }
            finally
            {
                application.Shutdown();
                CleanupTempRoot(root);
            }
        });
    }

    [Fact]
    public void ProjectManagementView_DoesNotExposeSizeModeChoices()
    {
        var document = XDocument.Load(GetProjectManagementViewXamlPath());
        var xaml = document.ToString(SaveOptions.DisableFormatting);

        Assert.DoesNotContain("UseAdaptiveModeCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UseFixedModeCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("固定格数", xaml, StringComparison.Ordinal);
        Assert.Contains("直接拖动边缘", xaml, StringComparison.Ordinal);
    }

    private static string GetDesktopBoxWindowXamlPath() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "WitchDrawer.App",
                "Views",
                "DesktopBoxWindow.xaml"));

    private static string GetProjectManagementViewXamlPath() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "WitchDrawer.App",
                "Views",
                "ProjectManagementView.xaml"));

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }

    private static void PumpDispatcher()
    {
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

    private static void CleanupTempRoot(string root)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Error(Exception exception, string message) { }
    }
}
