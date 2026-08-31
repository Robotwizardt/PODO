using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

[Collection("WPF Window Tests")]
public sealed class FileDialogWindowDetectorTests
{
    [Fact]
    public void TryGetInfo_RecognizesARealWindowsSaveDialog()
    {
        var title = "PODO file dialog detector " + Guid.NewGuid().ToString("N");
        var dialogThread = new Thread(() =>
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                FileName = "keep-me.txt"
            };
            _ = dialog.ShowDialog();
        });
        dialogThread.SetApartmentState(ApartmentState.STA);
        dialogThread.Start();

        nint handle = nint.Zero;
        try
        {
            Assert.True(
                SpinWait.SpinUntil(
                    () => (handle = FindWindowW("#32770", title)) != nint.Zero,
                    TimeSpan.FromSeconds(5)),
                "The controlled Windows save dialog did not appear.");

            FileDialogWindowInfo? info = null;
            Assert.True(
                SpinWait.SpinUntil(
                    () => FileDialogWindowDetector.TryGetInfo(handle, out info),
                    TimeSpan.FromSeconds(5)),
                "Child controls: " + DescribeChildren(handle));
            Assert.Equal(handle, info!.Handle);
            Assert.Equal((uint)Environment.ProcessId, info.ProcessId);
            Assert.True(info.Bounds.Width > 0);
            Assert.True(info.Bounds.Height > 0);
        }
        finally
        {
            if (handle != nint.Zero)
            {
                _ = PostMessageW(handle, 0x0010, nint.Zero, nint.Zero);
            }

            Assert.True(dialogThread.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task NavigateToDirectoryAsync_NavigatesAndPreservesFileNameWithoutConfirming()
    {
        var root = Path.Combine(Path.GetTempPath(), "PODO-FileDialogTests", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        const string markerName = "PODO-navigation-marker.txt";
        File.WriteAllText(Path.Combine(target, markerName), "navigation target");
        var title = "PODO file dialog navigation " + Guid.NewGuid().ToString("N");
        var dialogThread = new Thread(() =>
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                FileName = "keep-me.txt",
                InitialDirectory = root
            };
            _ = dialog.ShowDialog();
        });
        dialogThread.SetApartmentState(ApartmentState.STA);
        dialogThread.Start();

        nint handle = nint.Zero;
        try
        {
            Assert.True(SpinWait.SpinUntil(
                () => (handle = FindWindowW("#32770", title)) != nint.Zero
                    && FileDialogWindowDetector.TryGetInfo(handle, out _),
                TimeSpan.FromSeconds(5)));

            var result = await FileDialogNavigator.NavigateToDirectoryAsync(handle, target);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.True(
                SpinWait.SpinUntil(
                    () => HasVisibleFileItem(handle, markerName),
                    TimeSpan.FromSeconds(5)),
                "The target directory marker was not shown in the file list.");

            Assert.Equal("keep-me.txt", GetVisibleFileName(handle));
            Assert.True(IsWindow(handle), "Navigation must not confirm or close the save dialog.");
        }
        finally
        {
            if (handle != nint.Zero)
            {
                _ = PostMessageW(handle, 0x0010, nint.Zero, nint.Zero);
            }

            Assert.True(dialogThread.Join(TimeSpan.FromSeconds(5)));
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NavigateToDirectoryAsync_NavigatesAnOpenFolderDialog()
    {
        var root = Path.Combine(Path.GetTempPath(), "PODO-FolderDialogTests", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        const string markerName = "PODO-folder-navigation-marker";
        Directory.CreateDirectory(Path.Combine(target, markerName));
        var title = "PODO folder dialog navigation " + Guid.NewGuid().ToString("N");
        var dialogThread = new Thread(() =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                InitialDirectory = root
            };
            _ = dialog.ShowDialog();
        });
        dialogThread.SetApartmentState(ApartmentState.STA);
        dialogThread.Start();

        nint handle = nint.Zero;
        try
        {
            Assert.True(SpinWait.SpinUntil(
                () => (handle = FindWindowW("#32770", title)) != nint.Zero
                    && FileDialogWindowDetector.TryGetInfo(handle, out _),
                TimeSpan.FromSeconds(5)));

            var result = await FileDialogNavigator.NavigateToDirectoryAsync(handle, target);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.True(
                SpinWait.SpinUntil(
                    () => HasVisibleFileItem(handle, markerName),
                    TimeSpan.FromSeconds(5)),
                "The target directory marker was not shown in the folder list.");
            Assert.True(IsWindow(handle), "Navigation must not confirm or close the folder dialog.");
        }
        finally
        {
            if (handle != nint.Zero)
            {
                _ = PostMessageW(handle, 0x0010, nint.Zero, nint.Zero);
            }

            Assert.True(dialogThread.Join(TimeSpan.FromSeconds(5)));
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool HasVisibleFileItem(nint dialog, string name)
    {
        try
        {
            var root = AutomationElement.FromHandle(dialog);
            return root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ListItem))
                .Cast<AutomationElement>()
                .Any(item => string.Equals(item.Current.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static string GetVisibleFileName(nint dialog)
    {
        var root = AutomationElement.FromHandle(dialog);
        var host = root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                "FileNameControlHost"));
        var edit = host?.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit));
        Assert.NotNull(edit);
        Assert.True(edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern));
        return ((ValuePattern)pattern).Current.Value;
    }

    private static string DescribeChildren(nint parent)
    {
        var descriptions = new List<string>();
        _ = EnumChildWindows(
            parent,
            (child, _) =>
            {
                var className = new System.Text.StringBuilder(128);
                _ = GetClassNameW(child, className, className.Capacity);
                descriptions.Add($"{className}:{GetDlgCtrlID(child)}");
                return true;
            },
            nint.Zero);
        return string.Join(", ", descriptions);
    }

    private delegate bool EnumChildProc(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        nint window,
        System.Text.StringBuilder className,
        int maximumCount);
}
