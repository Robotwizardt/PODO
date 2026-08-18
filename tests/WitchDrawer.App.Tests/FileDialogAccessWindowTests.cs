using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.App.FileDialogAccess;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

[Collection("WPF Window Tests")]
public sealed class FileDialogAccessWindowTests
{
    [Fact]
    public void Window_IsAnAccessibleResizableToolSurfaceWithAVirtualizedList()
    {
        RunOnSta(() =>
        {
            var viewModel = new FileDialogAccessViewModel(_ => Task.CompletedTask);
            var window = new FileDialogAccessWindow(viewModel);
            try
            {
                Assert.False(window.ShowInTaskbar);
                Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);

                var search = Assert.IsType<TextBox>(window.FindName("SearchBox"));
                Assert.Equal("搜索收纳盒", AutomationProperties.GetName(search));
                var list = Assert.IsType<ListBox>(window.FindName("AccessList"));
                Assert.Equal("可访问的文件收纳盒", AutomationProperties.GetName(list));
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MouseWheel_OverABoxScrollsTheAccessWindow()
    {
        RunOnSta(() =>
        {
            var entries = Enumerable.Range(1, 30)
                .Select(index => new FileDialogAccessEntry(
                    Guid.NewGuid(),
                    $"Box {index:00}",
                    BoxType.Normal,
                    $@"C:\Boxes\{index}",
                    true,
                    null))
                .ToArray();
            var viewModel = new FileDialogAccessViewModel(_ => Task.CompletedTask);
            viewModel.Load(entries, []);
            var window = new FileDialogAccessWindow(viewModel)
            {
                Width = 320,
                Height = 360
            };
            try
            {
                window.Show();
                window.UpdateLayout();

                var list = Assert.IsType<ListBox>(window.FindName("AccessList"));
                var outerScrollViewer = Assert.IsType<ScrollViewer>(FindVisualAncestor<ScrollViewer>(list));
                var firstItem = Assert.IsType<ListBoxItem>(
                    list.ItemContainerGenerator.ContainerFromIndex(0));
                var firstButton = Assert.IsType<Button>(FindVisualDescendant<Button>(firstItem));

                var preview = new MouseWheelEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    -120)
                {
                    RoutedEvent = Mouse.PreviewMouseWheelEvent,
                    Source = firstButton
                };
                firstButton.RaiseEvent(preview);
                if (!preview.Handled)
                {
                    firstButton.RaiseEvent(new MouseWheelEventArgs(
                        Mouse.PrimaryDevice,
                        Environment.TickCount,
                        -120)
                    {
                        RoutedEvent = Mouse.MouseWheelEvent,
                        Source = firstButton
                    });
                }
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

                Assert.True(
                    outerScrollViewer.VerticalOffset > 0,
                    "The file-dialog access window did not scroll when the pointer was over a box.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(child);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null)
        {
            throw failure;
        }
    }
}
