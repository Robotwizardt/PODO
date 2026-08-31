using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Performance.Tests;

public sealed class DesktopBoxDragPerformanceTests
{
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const double LinkedFollowBudgetMilliseconds = 1000d / 60d;
    private const double FrameBudgetMilliseconds = 1000d / 120d;

    [Fact]
    public void MovingProjectBox_KeepsLinkedBoxFollowingWithinThe120HzFrameBudget()
    {
        RunOnSta(() =>
        {
            var root = CreateTempRoot();
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            InitializeWindowTestResources(application);
            var previousSynchronizationContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            DesktopBoxManager? manager = null;
            Window? mouseInputWindow = null;
            GetCursorPos(out var originalCursor);

            try
            {
                var paths = new AppPaths(root);
                var repository = new DrawerRepository(paths.DatabasePath);
                var drawerService = new DrawerService(paths, repository);
                AwaitWithDispatcher(drawerService.InitializeAsync());
                var projectTask = drawerService.CreateBoxAsync("拖动项目", BoxType.Project);
                AwaitWithDispatcher(projectTask);
                var projectBox = projectTask.GetAwaiter().GetResult();
                var linkedBoxTask = drawerService.CreateBoxAsync("跟随资料", BoxType.Normal);
                AwaitWithDispatcher(linkedBoxTask);
                var linkedBox = linkedBoxTask.GetAwaiter().GetResult();
                AwaitWithDispatcher(
                    new ProjectService(repository).LinkBoxAsync(projectBox.Id, linkedBox.Id));
                for (var index = 0; index < 22; index++)
                {
                    AwaitWithDispatcher(
                        drawerService.CreateBoxAsync($"性能盒 {index + 1}", BoxType.Normal));
                }

                var logger = new NoOpLogger();
                manager = new DesktopBoxManager(
                    drawerService,
                    new TodoService(repository),
                    new NoOpFileLauncher(),
                    logger,
                    new BoxVisualStyleStore(drawerService, logger),
                    new BoxPositionLockStateStore(drawerService, logger));
                AwaitWithDispatcher(manager.RefreshAsync(), TimeSpan.FromSeconds(30));

                var draggedWindow = Application.Current.Windows
                    .OfType<DesktopBoxWindow>()
                    .Single(window => window.ViewModel.Name == "拖动项目");
                var linkedWindow = Application.Current.Windows
                    .OfType<DesktopBoxWindow>()
                    .Single(window => window.ViewModel.Name == "跟随资料");
                var originalLeft = draggedWindow.Left;

                mouseInputWindow = new Window
                {
                    Width = 16,
                    Height = 16,
                    Left = 0,
                    Top = 0,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true
                };
                mouseInputWindow.Show();
                mouseInputWindow.Activate();
                PumpRenderFrame();
                var inputHandle = new WindowInteropHelper(mouseInputWindow).Handle;
                for (var attempt = 0;
                     attempt < 5 && Mouse.LeftButton != MouseButtonState.Pressed;
                     attempt++)
                {
                    SetForegroundWindow(inputHandle);
                    SetCursorPos(8, 8);
                    mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    PumpInput();
                    Mouse.PrimaryDevice.Synchronize();
                    if (Mouse.LeftButton != MouseButtonState.Pressed)
                    {
                        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                        PumpInput();
                    }
                }

                Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);

                var linkedLeftBeforeDrag = linkedWindow.Left;
                MoveProjectAndAssertLinkedBoxFollows(
                    draggedWindow,
                    linkedWindow,
                    originalLeft,
                    linkedLeftBeforeDrag,
                    originalLeft + 12);

                var frameTimes = new double[24];
                for (var index = 0; index < frameTimes.Length; index++)
                {
                    frameTimes[index] = MoveProjectAndAssertLinkedBoxFollows(
                        draggedWindow,
                        linkedWindow,
                        originalLeft,
                        linkedLeftBeforeDrag,
                        originalLeft + 2 + (index % 2));
                }

                draggedWindow.Left = originalLeft;
                PumpRenderFrame();
                var percentile95 = frameTimes
                    .Order()
                    .ElementAt((int)Math.Ceiling(frameTimes.Length * 0.95) - 1);

                Assert.True(
                    percentile95 <= FrameBudgetMilliseconds,
                    $"Project-box drag p95 was {percentile95:0.00} ms; " +
                    $"the 120 Hz frame budget is {FrameBudgetMilliseconds:0.00} ms. " +
                    $"Samples: {string.Join(", ", frameTimes.Select(value => value.ToString("0.00")))}");
            }
            finally
            {
                mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                SetCursorPos(originalCursor.X, originalCursor.Y);
                mouseInputWindow?.Close();
                if (manager is not null)
                {
                    AwaitWithDispatcher(manager.CloseAllAsync(), TimeSpan.FromSeconds(30));
                }

                SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
                application.Shutdown();
                CleanupTempRoot(root);
            }
        });
    }

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The drag performance test timed out.");
        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }

    private static void AwaitWithDispatcher(Task task, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        while (!task.IsCompleted && stopwatch.Elapsed < limit)
        {
            Dispatcher.CurrentDispatcher.Invoke(
                () => { },
                DispatcherPriority.Background);
            Thread.Sleep(2);
        }

        Assert.True(task.IsCompleted, "The desktop manager operation timed out.");
        task.GetAwaiter().GetResult();
    }

    private static void PumpRenderFrame()
    {
        Dispatcher.CurrentDispatcher.Invoke(
            () => { },
            DispatcherPriority.Render);
    }

    private static void PumpInput()
    {
        Dispatcher.CurrentDispatcher.Invoke(
            () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private static double MoveProjectAndAssertLinkedBoxFollows(
        DesktopBoxWindow projectWindow,
        DesktopBoxWindow linkedWindow,
        double projectLeftBeforeDrag,
        double linkedLeftBeforeDrag,
        double projectLeftAfterMove)
    {
        var stopwatch = Stopwatch.StartNew();
        projectWindow.Left = projectLeftAfterMove;
        PumpRenderFrame();
        stopwatch.Stop();

        var expectedLinkedLeft = linkedLeftBeforeDrag
            + (projectWindow.Left - projectLeftBeforeDrag);
        var linkedError = Math.Abs(linkedWindow.Left - expectedLinkedLeft);
        Assert.True(
            linkedError <= 0.1,
            $"The linked box was {linkedError:0.00} DIP behind after the next render tick. "
            + $"Expected Left {expectedLinkedLeft:0.00}, actual {linkedWindow.Left:0.00}.");
        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds <= LinkedFollowBudgetMilliseconds,
            $"The linked box followed after {stopwatch.Elapsed.TotalMilliseconds:0.00} ms; "
            + $"the one-render-tick budget is {LinkedFollowBudgetMilliseconds:0.00} ms.");
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static void InitializeWindowTestResources(Application application)
    {
        application.Resources["BooleanToVisibilityConverter"] =
            new BooleanToVisibilityConverter();
        application.Resources["InverseBooleanToVisibilityConverter"] =
            new InverseBooleanToVisibilityConverter();
        application.Resources["ZeroToVisibilityConverter"] =
            new ZeroToVisibilityConverter();
        application.Resources["NonEmptyToVisibilityConverter"] =
            new NonEmptyToVisibilityConverter();
        application.Resources["BoxVisualStyleEqualityConverter"] =
            new BoxVisualStyleEqualityConverter();
        application.Resources["NoteHeadingFontSizeConverter"] =
            new NoteHeadingFontSizeConverter();
        application.Resources["NoteHeadingFontWeightConverter"] =
            new NoteHeadingFontWeightConverter();
        application.Resources[typeof(ListBox)] = new Style(typeof(ListBox));
        application.Resources["ProjectCompactComboBoxStyle"] = new Style(typeof(ComboBox));
        application.Resources["MappingModeButtonStyle"] = new Style(typeof(Button));
        application.Resources["TodoArchiveButtonStyle"] = new Style(typeof(Button));
        application.Resources["TodoCompletionButtonStyle"] = new Style(typeof(Button));
        application.Resources["DrawerTileButtonStyle"] = new Style(typeof(Button));
        application.Resources["DrawerScrollBarStyle"] =
            new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
        application.Resources["GhostButtonStyle"] = new Style(typeof(Button));
        application.Resources["DangerButtonStyle"] = new Style(typeof(Button));
        application.Resources["PrimaryButtonStyle"] = new Style(typeof(Button));
        application.Resources["AppBackgroundBrush"] = Brushes.White;
        application.Resources["PanelBrush"] = Brushes.White;
        application.Resources["PanelAltBrush"] = Brushes.WhiteSmoke;
        application.Resources["BorderBrushSoft"] = Brushes.LightGray;
        application.Resources["TextPrimaryBrush"] = Brushes.Black;
        application.Resources["TextMutedBrush"] = Brushes.DimGray;
        application.Resources["AccentBrush"] = Brushes.DodgerBlue;
        application.Resources["AccentSoftBrush"] = Brushes.LightBlue;
        application.Resources["HoverBrush"] = Brushes.Gainsboro;
        application.Resources["DangerBrush"] = Brushes.IndianRed;
        application.Resources["DangerSoftBrush"] = Brushes.MistyRose;
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

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        UIntPtr extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

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
