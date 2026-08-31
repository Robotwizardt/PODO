using PaperTodo;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WitchDrawer.App.Tests;

[Collection(WpfWindowTestCollection.Name)]
public sealed class TodoItemPresentationTests
{
    [Fact]
    public void TodoTextColorMenu_AppliesSelectedColorToRenderedEditor()
    {
        RunOnSta(() =>
        {
            using var workspace = new TempDirectory();
            using var controller = new AppController(
                workspace.Path,
                enableStandaloneTray: false,
                ownsApplicationLifetime: false);
            controller.State.Theme = "light";
            var item = new PaperItem { Text = "应当变蓝" };
            var paper = new PaperData
            {
                Type = PaperTypes.Todo,
                Items = [item]
            };
            var window = new PaperWindow(paper, controller);

            try
            {
                window.Show();
                window.UpdateLayout();
                var editor = FindVisualChild<TodoTextBox>(window)
                    ?? throw new Xunit.Sdk.XunitException("Todo editor was not created.");
                var itemMenu = editor.ContextMenu
                    ?? throw new Xunit.Sdk.XunitException("Todo context menu was not attached.");
                var colorMenu = itemMenu.Items
                    .OfType<MenuItem>()
                    .Single(candidate => Equals(candidate.Header, Strings.Get("MenuTodoTextColor")));
                var blueChoice = colorMenu.Items.OfType<MenuItem>().ElementAt(4);

                blueChoice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                var rebuiltEditor = FindVisualChild<TodoTextBox>(window)
                    ?? throw new Xunit.Sdk.XunitException("Todo editor was not rebuilt.");
                Assert.Equal(TodoTextColors.Blue, item.TextColor);
                Assert.NotSame(editor, rebuiltEditor);
                Assert.Equal(
                    Assert.IsType<SolidColorBrush>(
                        TodoTextColors.BrushFor(TodoTextColors.Blue)).Color,
                    Assert.IsType<SolidColorBrush>(rebuiltEditor.Foreground).Color);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ClonePreservesPinnedAndTextColorPresentation()
    {
        var original = new PaperItem
        {
            Text = "重要事项",
            Order = 3,
            IsPinned = true,
            TextColor = TodoTextColors.Red
        };

        var clone = TodoRules.Clone(original);

        Assert.NotSame(original, clone);
        Assert.True(clone.IsPinned);
        Assert.Equal(TodoTextColors.Red, clone.TextColor);
    }

    [Fact]
    public void OrderForDisplayKeepsPinnedItemsFirstAndStable()
    {
        var first = new PaperItem { Text = "普通一", Order = 0 };
        var pinnedFirst = new PaperItem { Text = "置顶一", Order = 1, IsPinned = true };
        var second = new PaperItem { Text = "普通二", Order = 2 };
        var pinnedSecond = new PaperItem { Text = "置顶二", Order = 3, IsPinned = true };

        var ordered = TodoRules.OrderForDisplay(
            [first, pinnedFirst, second, pinnedSecond]).ToList();

        Assert.Equal(
            [pinnedFirst, pinnedSecond, first, second],
            ordered);
    }

    [Fact]
    public void EdgeCapsulePreviewKeepsPinnedItemsAheadOfEarlierNormalItems()
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items =
            [
                new PaperItem { Text = "普通", Order = 0 },
                new PaperItem { Text = "置顶", Order = 1, IsPinned = true }
            ]
        };

        var snapshot = TodoEdgeCapsulePreviewProvider.CaptureSnapshot(paper);

        Assert.Equal(["置顶", "普通"], snapshot.Items.Select(item => item.Text));
    }

    [Fact]
    public void StateStoreRoundTripPreservesPinnedAndTextColorPresentation()
    {
        using var workspace = new TempDirectory();
        var store = new StateStore(workspace.Path);
        var state = new AppState
        {
            Papers =
            [
                new PaperData
                {
                    Type = PaperTypes.Todo,
                    Items =
                    [
                        new PaperItem { Text = "普通事项", Order = 0 },
                        new PaperItem
                        {
                            Text = "持续显示",
                            Order = 1,
                            IsPinned = true,
                            TextColor = TodoTextColors.Blue
                        }
                    ]
                }
            ]
        };

        store.SaveJsonSync(store.SerializeState(state), version: 1);

        var loaded = new StateStore(workspace.Path).Load();
        var items = Assert.Single(loaded.Papers).Items;
        Assert.Equal(["持续显示", "普通事项"], items.Select(item => item.Text));
        var item = items[0];
        Assert.True(item.IsPinned);
        Assert.Equal(TodoTextColors.Blue, item.TextColor);
    }

    [Fact]
    public void StateStoreNormalizesUnknownTextColorToDefault()
    {
        using var workspace = new TempDirectory();
        var store = new StateStore(workspace.Path);
        var state = new AppState
        {
            Papers =
            [
                new PaperData
                {
                    Type = PaperTypes.Todo,
                    Items = [new PaperItem { Text = "兼容旧数据", TextColor = "unknown" }]
                }
            ]
        };

        store.SaveJsonSync(store.SerializeState(state), version: 1);

        var loaded = new StateStore(workspace.Path).Load();
        Assert.Null(Assert.Single(Assert.Single(loaded.Papers).Items).TextColor);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "WitchDrawer.Tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Path))
            {
                System.IO.Directory.Delete(Path, recursive: true);
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(root, index));
            if (found != null)
            {
                return found;
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
        thread.Join();

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
