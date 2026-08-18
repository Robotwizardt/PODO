using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

public enum FileDialogWindowChangeKind
{
    Activated,
    Updated,
    ForegroundChanged,
    Closed
}

public sealed record FileDialogWindowChangedEventArgs(
    FileDialogWindowChangeKind Kind,
    nint WindowHandle,
    FileDialogWindowInfo? Dialog);

public sealed class FileDialogWindowMonitor : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0;
    private const uint GaRoot = 2;
    private const int ObjectIdWindow = 0;

    private readonly WinEventCallback _callback;
    private nint _foregroundHook;
    private nint _minimizeHook;
    private nint _objectHook;
    private nint _trackedDialog;
    private bool _disposed;

    public FileDialogWindowMonitor()
    {
        _callback = OnWinEvent;
        _foregroundHook = Hook(EventSystemForeground, EventSystemForeground);
        _minimizeHook = Hook(EventSystemMinimizeStart, EventSystemMinimizeEnd);
        _objectHook = Hook(EventObjectDestroy, EventObjectLocationChange);
        EvaluateForeground();
    }

    public event EventHandler<FileDialogWindowChangedEventArgs>? WindowChanged;

    public bool IsActive => _foregroundHook != nint.Zero;

    public void EvaluateForeground()
    {
        if (_disposed)
        {
            return;
        }

        var foreground = GetForegroundWindow();
        var root = GetAncestor(foreground, GaRoot);
        if (FileDialogWindowDetector.TryGetInfo(root, out var dialog))
        {
            var kind = root == _trackedDialog
                ? FileDialogWindowChangeKind.Updated
                : FileDialogWindowChangeKind.Activated;
            _trackedDialog = root;
            Raise(kind, root, dialog);
            return;
        }

        Raise(FileDialogWindowChangeKind.ForegroundChanged, foreground, null);
    }

    public void Dispose()
    {
        _disposed = true;
        Unhook(ref _foregroundHook);
        Unhook(ref _minimizeHook);
        Unhook(ref _objectHook);
        GC.SuppressFinalize(this);
    }

    private nint Hook(uint minimumEvent, uint maximumEvent)
    {
        return SetWinEventHook(
            minimumEvent,
            maximumEvent,
            nint.Zero,
            _callback,
            0,
            0,
            WinEventOutOfContext);
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            if (eventType == EventSystemForeground)
            {
                EvaluateForeground();
                return;
            }

            if (eventType is not EventSystemMinimizeStart
                and not EventSystemMinimizeEnd
                and not EventObjectDestroy
                and not EventObjectShow
                and not EventObjectLocationChange)
            {
                return;
            }

            var root = GetAncestor(windowHandle, GaRoot);
            if (root == nint.Zero)
            {
                root = windowHandle;
            }

            if (eventType == EventObjectShow
                && GetAncestor(GetForegroundWindow(), GaRoot) == root
                && FileDialogWindowDetector.TryGetInfo(root, out var shownDialog))
            {
                if (root != _trackedDialog)
                {
                    _trackedDialog = root;
                    Raise(FileDialogWindowChangeKind.Activated, root, shownDialog);
                }
                else if (windowHandle == root && objectId == ObjectIdWindow)
                {
                    Raise(FileDialogWindowChangeKind.Updated, root, shownDialog);
                }

                return;
            }

            if (eventType == EventObjectDestroy)
            {
                if (_trackedDialog != nint.Zero
                    && (windowHandle == _trackedDialog || !IsWindow(_trackedDialog)))
                {
                    var closedDialog = _trackedDialog;
                    _trackedDialog = nint.Zero;
                    Raise(FileDialogWindowChangeKind.Closed, closedDialog, null);
                }

                return;
            }

            if (root != _trackedDialog)
            {
                return;
            }

            if (eventType == EventObjectLocationChange
                && (windowHandle != root || objectId != ObjectIdWindow))
            {
                return;
            }

            if (FileDialogWindowDetector.TryGetInfo(root, out var dialog))
            {
                Raise(FileDialogWindowChangeKind.Updated, root, dialog);
            }
            else if (eventType == EventSystemMinimizeStart)
            {
                Raise(FileDialogWindowChangeKind.Updated, root, null);
            }
        }
        catch
        {
            // Native callbacks must never propagate managed exceptions.
        }
    }

    private void Raise(
        FileDialogWindowChangeKind kind,
        nint windowHandle,
        FileDialogWindowInfo? dialog)
    {
        WindowChanged?.Invoke(
            this,
            new FileDialogWindowChangedEventArgs(kind, windowHandle, dialog));
    }

    private static void Unhook(ref nint hook)
    {
        if (hook == nint.Zero)
        {
            return;
        }

        _ = UnhookWinEvent(hook);
        hook = nint.Zero;
    }

    private delegate void WinEventCallback(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventCallback callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
