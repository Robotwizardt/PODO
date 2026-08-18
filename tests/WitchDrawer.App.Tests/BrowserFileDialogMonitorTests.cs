using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

[Collection("WPF Window Tests")]
public sealed class BrowserFileDialogMonitorTests
{
    private const string EdgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
    private const uint GaRoot = 2;

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task Podo_NavigatesEdgeUploadDialogWhenUserClicksAnAvailableBox()
    {
        Assert.True(File.Exists(EdgePath), "Microsoft Edge is required for this Windows integration test.");

        var testId = Guid.NewGuid().ToString("N");
        var title = "PODO browser upload " + testId;
        var testRoot = Path.Combine(Path.GetTempPath(), "PODO-BrowserDialogTests", testId);
        var profilePath = Path.Combine(testRoot, "profile");
        var podoDataPath = Path.Combine(testRoot, "podo-data");
        var htmlPath = Path.Combine(testRoot, "upload.html");
        Directory.CreateDirectory(profilePath);
        var drawerService = new DrawerService(
            new AppPaths(podoDataPath),
            new DrawerRepository(Path.Combine(podoDataPath, AppPaths.DatabaseFileName)));
        await drawerService.InitializeAsync();
        var targetBox = await drawerService.CreateBoxAsync(
            "PODO navigation target",
            BoxType.Normal);
        var targetPath = Assert.IsType<string>(targetBox.StoragePath);
        const string markerName = "PODO-browser-navigation-marker.txt";
        File.WriteAllText(Path.Combine(targetPath, markerName), "browser navigation target");
        File.WriteAllText(
            htmlPath,
            $"<!doctype html><html><head><title>{title}</title></head>"
                + "<body><input type=\"file\" aria-label=\"PODO choose file\"></body></html>");

        Process? edge = null;
        Process? podo = null;
        nint browserWindow = nint.Zero;
        nint dialogWindow = nint.Zero;
        try
        {
            var podoStartInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "PODO.exe"))
            {
                UseShellExecute = false
            };
            podoStartInfo.Environment["PODO_DATA_DIR"] = podoDataPath;
            podo = Process.Start(podoStartInfo);
            Assert.NotNull(podo);
            Assert.True(
                SpinWait.SpinUntil(
                    () => FindTopLevelWindow(null, "PODO", (uint)podo.Id) != nint.Zero,
                    TimeSpan.FromSeconds(15)),
                "The isolated PODO process did not finish starting.");

            var startInfo = new ProcessStartInfo(EdgePath)
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--user-data-dir=" + profilePath);
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--disable-extensions");
            startInfo.ArgumentList.Add("--disable-background-mode");
            startInfo.ArgumentList.Add("--force-renderer-accessibility");
            startInfo.ArgumentList.Add("--app=" + new Uri(htmlPath).AbsoluteUri);
            edge = Process.Start(startInfo);
            Assert.NotNull(edge);

            Assert.True(
                SpinWait.SpinUntil(
                    () => (browserWindow = FindTopLevelWindow("Chrome_WidgetWin_1", title)) != nint.Zero,
                    TimeSpan.FromSeconds(15)),
                "The isolated Edge upload page did not appear.");

            DismissEdgeIntro(browserWindow);
            var uploadButton = FindUploadButton(browserWindow);
            Assert.True(
                uploadButton is not null,
                "Edge upload control was not exposed as expected. Buttons: "
                    + DescribeButtons(browserWindow));
            uploadButton.SetFocus();
            _ = SetForegroundWindow(browserWindow);
            Assert.True(uploadButton.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern));
            ((InvokePattern)pattern).Invoke();

            Assert.True(
                SpinWait.SpinUntil(
                    () => (dialogWindow = FindEdgeFileDialog()) != nint.Zero,
                    TimeSpan.FromSeconds(10)),
                "Edge did not open a native file upload dialog.");
            nint accessWindow = nint.Zero;
            Assert.True(
                SpinWait.SpinUntil(
                    () => (accessWindow = FindTopLevelWindow(
                            null,
                            "文件对话框访问窗",
                            (uint)podo.Id)) != nint.Zero
                        && IsWindowVisible(accessWindow),
                    TimeSpan.FromSeconds(5)),
                "PODO did not show its file-dialog access window for the active Edge upload dialog.");
            Assert.True(GetWindowRect(accessWindow, out var accessBounds));
            Assert.True(accessBounds.Right > accessBounds.Left);
            Assert.True(accessBounds.Bottom > accessBounds.Top);

            var targetButton = FindAccessEntryButton(accessWindow, targetBox.Name);
            Assert.NotNull(targetButton);
            Assert.True(
                targetButton.Current.IsEnabled,
                "The available box is rendered as a disabled access-window button.");
            var clickedWindow = GetElementHitWindow(targetButton);
            Assert.Equal(accessWindow, clickedWindow);
            Assert.True(targetButton.TryGetCurrentPattern(InvokePattern.Pattern, out var buttonPattern));
            ((InvokePattern)buttonPattern).Invoke();
            var commandBegan = SpinWait.SpinUntil(
                () => !targetButton.Current.IsEnabled,
                TimeSpan.FromSeconds(1));

            var navigated = SpinWait.SpinUntil(
                () => HasVisibleFileItem(dialogWindow, markerName),
                TimeSpan.FromSeconds(5));
            Assert.True(
                navigated,
                $"Clicking the available box did not show the target directory marker '{markerName}'. "
                    + $"; Clicked window: {clickedWindow}; Access window: {accessWindow}"
                    + $"; Command entered execution: {commandBegan}"
                    + $"; dialog alive: {IsWindow(dialogWindow)}"
                    + $"; address: {DescribeAddress(dialogWindow)}"
                    + $"; access text: {DescribeText(accessWindow)}");
            Assert.True(IsWindow(dialogWindow), "Navigation must not close the upload dialog.");
            Thread.Sleep(TimeSpan.FromSeconds(2));
            Assert.True(
                IsWindowVisible(accessWindow),
                "The file-dialog access window must remain visible after navigation completes.");
        }
        finally
        {
            if (dialogWindow != nint.Zero)
            {
                _ = PostMessageW(dialogWindow, 0x0010, nint.Zero, nint.Zero);
            }

            if (browserWindow != nint.Zero)
            {
                _ = PostMessageW(browserWindow, 0x0010, nint.Zero, nint.Zero);
            }

            if (edge is { HasExited: false })
            {
                edge.Kill(entireProcessTree: true);
                edge.WaitForExit(TimeSpan.FromSeconds(5));
            }

            if (podo is { HasExited: false })
            {
                podo.Kill(entireProcessTree: true);
                podo.WaitForExit(TimeSpan.FromSeconds(5));
            }

            TryDeleteDirectory(testRoot);
        }
    }

    private static AutomationElement? FindUploadButton(nint browserWindow)
    {
        var root = AutomationElement.FromHandle(browserWindow);
        AutomationElement? match = null;
        return SpinWait.SpinUntil(
            () =>
            {
                match = root.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.Button))
                    .Cast<AutomationElement>()
                    .FirstOrDefault(element => element.Current.Name.StartsWith(
                        "PODO choose file",
                        StringComparison.Ordinal));
                return match is not null;
            },
            TimeSpan.FromSeconds(10))
            ? match
            : null;
    }

    private static void DismissEdgeIntro(nint browserWindow)
    {
        var root = AutomationElement.FromHandle(browserWindow);
        AutomationElement? introButton = null;
        _ = SpinWait.SpinUntil(
            () =>
            {
                introButton = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "got-it-button"));
                return introButton is not null;
            },
            TimeSpan.FromSeconds(3));
        if (introButton?.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) == true)
        {
            introButton.SetFocus();
            _ = SetForegroundWindow(browserWindow);
            ((InvokePattern)pattern).Invoke();
            _ = SpinWait.SpinUntil(
                () => root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "got-it-button")) is null,
                TimeSpan.FromSeconds(3));
        }
    }

    private static string DescribeButtons(nint browserWindow)
    {
        var root = AutomationElement.FromHandle(browserWindow);
        return string.Join(
            ", ",
            root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Button))
                .Cast<AutomationElement>()
                .Select(element => $"{element.Current.Name}|{element.Current.AutomationId}"));
    }

    private static AutomationElement? FindAccessEntryButton(nint accessWindow, string name)
    {
        var root = AutomationElement.FromHandle(accessWindow);
        AutomationElement? match = null;
        return SpinWait.SpinUntil(
            () =>
            {
                match = root.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.Button))
                    .Cast<AutomationElement>()
                    .FirstOrDefault(element => string.Equals(
                        element.Current.Name,
                        name,
                        StringComparison.Ordinal));
                return match is not null
                    && !match.Current.IsOffscreen
                    && match.Current.BoundingRectangle.Width > 1
                    && match.Current.BoundingRectangle.Height > 1;
            },
            TimeSpan.FromSeconds(5))
            ? match
            : null;
    }

    private static bool HasVisibleFileItem(nint dialogWindow, string name)
    {
        try
        {
            var root = AutomationElement.FromHandle(dialogWindow);
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

    private static string DescribeAddress(nint dialogWindow)
    {
        try
        {
            var root = AutomationElement.FromHandle(dialogWindow);
            return root.FindFirst(
                    TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(
                            AutomationElement.AutomationIdProperty,
                            "1001"),
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.ToolBar)))
                ?.Current.Name ?? "missing";
        }
        catch (ElementNotAvailableException)
        {
            return "unavailable";
        }
    }

    private static string DescribeText(nint window)
    {
        try
        {
            var root = AutomationElement.FromHandle(window);
            return string.Join(
                " | ",
                root.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.Text))
                    .Cast<AutomationElement>()
                    .Select(element => element.Current.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
        }
        catch (ElementNotAvailableException)
        {
            return "unavailable";
        }
    }

    private static nint GetElementHitWindow(AutomationElement element)
    {
        var bounds = element.Current.BoundingRectangle;
        Assert.True(bounds.Width > 1 && bounds.Height > 1, "The access entry button has no clickable bounds.");
        var x = (int)Math.Round(bounds.Left + (bounds.Width / 2));
        var y = (int)Math.Round(bounds.Top + (bounds.Height / 2));
        Assert.True(SetCursorPos(x, y));
        return GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), GaRoot);
    }

    private static nint FindEdgeFileDialog()
    {
        nint match = nint.Zero;
        _ = EnumWindows(
            (window, parameter) =>
            {
                if (!string.Equals(GetClassName(window), "#32770", StringComparison.Ordinal))
                {
                    return true;
                }

                _ = GetWindowThreadProcessId(window, out var processId);
                try
                {
                    using var process = Process.GetProcessById(checked((int)processId));
                    if (string.Equals(process.ProcessName, "msedge", StringComparison.OrdinalIgnoreCase))
                    {
                        match = window;
                        return false;
                    }
                }
                catch
                {
                    return true;
                }

                return true;
            },
            nint.Zero);
        return match;
    }

    private static nint FindTopLevelWindow(string? className, string title, uint? processId = null)
    {
        nint match = nint.Zero;
        _ = EnumWindows(
            (window, parameter) =>
            {
                _ = GetWindowThreadProcessId(window, out var windowProcessId);
                if ((className is null
                        || string.Equals(GetClassName(window), className, StringComparison.Ordinal))
                    && string.Equals(GetWindowText(window), title, StringComparison.Ordinal)
                    && (processId is null || processId == windowProcessId))
                {
                    match = window;
                    return false;
                }

                return true;
            },
            nint.Zero);
        return match;
    }

    private static string GetClassName(nint window)
    {
        var value = new StringBuilder(256);
        return GetClassNameW(window, value, value.Capacity) > 0 ? value.ToString() : string.Empty;
    }

    private static string GetWindowText(nint window)
    {
        var value = new StringBuilder(512);
        return GetWindowTextW(window, value, value.Capacity) > 0 ? value.ToString() : string.Empty;
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

}
