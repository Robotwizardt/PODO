using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WitchDrawer.Native.Windows;

public readonly record struct NativeScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);
}

public sealed record FileDialogWindowInfo(
    nint Handle,
    uint ProcessId,
    string ProcessPath,
    string Title,
    NativeScreenRect Bounds,
    bool IsMinimized);

public static class FileDialogWindowDetector
{
    private const int FileNameEditId = 0x480;
    private const int FileNameComboId = 0x47C;

    public static bool TryGetInfo(nint windowHandle, out FileDialogWindowInfo info)
    {
        info = null!;
        if (windowHandle == nint.Zero
            || !IsWindow(windowHandle)
            || !IsWindowVisible(windowHandle)
            || !string.Equals(GetClassName(windowHandle), "#32770", StringComparison.Ordinal)
            || !HasFileDialogControls(windowHandle)
            || !GetWindowRect(windowHandle, out var bounds))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        info = new FileDialogWindowInfo(
            windowHandle,
            processId,
            TryGetProcessPath(processId),
            GetWindowText(windowHandle),
            new NativeScreenRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            IsIconic(windowHandle));
        return true;
    }

    private static bool HasFileDialogControls(nint root)
    {
        var hasKnownFileNameControl = false;
        var hasDirectUi = false;
        var hasShellView = false;
        var hasShellNavigationBand = false;
        var hasShellToolbar = false;
        _ = EnumChildWindows(
            root,
            (child, _) =>
            {
                var controlId = GetDlgCtrlID(child);
                hasKnownFileNameControl |= controlId is FileNameEditId or FileNameComboId;
                var className = GetClassName(child);
                hasDirectUi |= string.Equals(className, "DirectUIHWND", StringComparison.Ordinal);
                hasShellView |= string.Equals(className, "SHELLDLL_DefView", StringComparison.Ordinal);
                hasShellNavigationBand |= string.Equals(className, "ReBarWindow32", StringComparison.Ordinal)
                    && controlId == 40965;
                hasShellToolbar |= string.Equals(className, "ToolbarWindow32", StringComparison.Ordinal);
                return !(hasKnownFileNameControl
                    || (hasDirectUi && hasShellView)
                    || (hasShellNavigationBand && hasShellToolbar));
            },
            nint.Zero);
        return hasKnownFileNameControl
            || (hasDirectUi && hasShellView)
            || (hasShellNavigationBand && hasShellToolbar);
    }

    private static string TryGetProcessPath(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetClassName(nint window)
    {
        var value = new StringBuilder(256);
        return GetClassNameW(window, value, value.Capacity) > 0 ? value.ToString() : string.Empty;
    }

    private static string GetWindowText(nint window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var value = new StringBuilder(length + 1);
        return GetWindowTextW(window, value, value.Capacity) > 0 ? value.ToString() : string.Empty;
    }

    private delegate bool EnumChildProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect bounds);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int maximumCount);
}
