using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PaperTodo;

namespace WitchDrawer.App.Tests;

[Collection(WpfWindowTestCollection.Name)]
public sealed class NoteFileExporterTests
{
    [Fact]
    public void Save_MarkdownFilePreservesCurrentNoteContent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PODO Note Export Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "项目笔记.md");

        try
        {
            NoteFileExporter.Save(path, "# 今日计划\n\n- 完成右键导出");

            Assert.Equal(
                "# 今日计划\n\n- 完成右键导出",
                File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void DesktopNoteContextMenu_OffersSaveFile()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "PODO Note Menu Tests",
                Guid.NewGuid().ToString("N"));

            try
            {
                using var controller = new AppController(
                    directory,
                    enableStandaloneTray: false,
                    ownsApplicationLifetime: false);
                var paper = new PaperData
                {
                    Type = PaperTypes.Note,
                    Title = "项目复盘",
                    Content = "本周完成内容"
                };
                var window = new PaperWindow(paper, controller);

                try
                {
                    var menu = FindContextMenu(
                        Assert.IsAssignableFrom<DependencyObject>(window.Content));
                    var labels = menu.Items
                        .OfType<MenuItem>()
                        .Select(item => item.Header?.ToString());

                    Assert.Contains("保存文件", labels);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void DesktopNoteContextMenu_SaveFileWritesTxtChosenByUser()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "PODO Note Menu Tests",
                Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "项目复盘.txt");

            try
            {
                var dialog = new StubNoteFileSaveDialog(path);
                using var controller = new AppController(
                    directory,
                    enableStandaloneTray: false,
                    ownsApplicationLifetime: false);
                var paper = new PaperData
                {
                    Type = PaperTypes.Note,
                    Title = "项目复盘",
                    Content = "第一行\n第二行"
                };
                var window = new PaperWindow(
                    paper,
                    controller,
                    noteFileSaveDialog: dialog);

                try
                {
                    var menu = FindContextMenu(
                        Assert.IsAssignableFrom<DependencyObject>(window.Content));
                    var saveItem = menu.Items
                        .OfType<MenuItem>()
                        .Single(item => string.Equals(
                            item.Header?.ToString(),
                            "保存文件",
                            StringComparison.Ordinal));

                    saveItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                    Assert.Equal("第一行\n第二行", File.ReadAllText(path));
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void DesktopNoteContextMenu_SuggestsSafeTitleAsFilename()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "PODO Note Menu Tests",
                Guid.NewGuid().ToString("N"));

            try
            {
                var dialog = new StubNoteFileSaveDialog(path: null);
                using var controller = new AppController(
                    directory,
                    enableStandaloneTray: false,
                    ownsApplicationLifetime: false);
                var paper = new PaperData
                {
                    Type = PaperTypes.Note,
                    Title = "项目:复盘/计划",
                    Content = "内容"
                };
                var window = new PaperWindow(
                    paper,
                    controller,
                    noteFileSaveDialog: dialog);

                try
                {
                    var menu = FindContextMenu(
                        Assert.IsAssignableFrom<DependencyObject>(window.Content));
                    var saveItem = menu.Items
                        .OfType<MenuItem>()
                        .Single(item => string.Equals(
                            item.Header?.ToString(),
                            "保存文件",
                            StringComparison.Ordinal));

                    saveItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                    Assert.Equal("项目_复盘_计划", dialog.SuggestedFileName);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static ContextMenu FindContextMenu(DependencyObject root)
    {
        if (root is FrameworkElement { ContextMenu: not null } element)
        {
            return element.ContextMenu;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            try
            {
                return FindContextMenu(child);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException("The paper window has no context menu.");
    }

    private sealed class StubNoteFileSaveDialog(string? path) : INoteFileSaveDialog
    {
        public string? SuggestedFileName { get; private set; }

        public string? Show(string suggestedFileName)
        {
            SuggestedFileName = suggestedFileName;
            return path;
        }
    }
}
