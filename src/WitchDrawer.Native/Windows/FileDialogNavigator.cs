using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace WitchDrawer.Native.Windows;

public sealed record FileDialogNavigationResult(bool Succeeded, string? ErrorMessage)
{
    public static FileDialogNavigationResult Success { get; } = new(true, null);

    public static FileDialogNavigationResult Failure(string message) => new(false, message);
}

public static class FileDialogNavigator
{
    private const uint WmUser = 0x0400;
    private const uint CdmGetFolderPath = WmUser + 102;
    private const uint WmSetText = 0x000C;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint BmClick = 0x00F5;
    private const string LegacyFileNameEditAutomationId = "1148";
    private const string AddressEditAutomationId = "41477";
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyA = 0x41;
    private const ushort VirtualKeyAlt = 0x12;
    private const ushort VirtualKeyL = 0x4C;
    private const ushort VirtualKeyD = 0x44;
    private const ushort VirtualKeyEnter = 0x0D;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly nint HwndTopmost = new(-1);

    public static Task<FileDialogNavigationResult> NavigateToDirectoryAsync(
        nint dialogHandle,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => NavigateToDirectory(dialogHandle, directoryPath, cancellationToken),
            cancellationToken);
    }

    private static FileDialogNavigationResult NavigateToDirectory(
        nint dialogHandle,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!FileDialogWindowDetector.TryGetInfo(dialogHandle, out _))
            {
                return FileDialogNavigationResult.Failure("文件对话框已关闭或不受支持");
            }

            var targetPath = Path.GetFullPath(directoryPath);
            if (!Directory.Exists(targetPath))
            {
                return FileDialogNavigationResult.Failure("目录不可用");
            }

            if (!TryGetFileNameWithAutomation(dialogHandle, out var originalFileName))
            {
                return FileDialogNavigationResult.Failure("无法保护当前文件名，已取消导航");
            }

            if (!TryNavigateWithAddressBar(
                    dialogHandle,
                    targetPath,
                    originalFileName,
                    cancellationToken,
                    out var errorMessage))
            {
                if (!RestoreFileNameWithAutomation(dialogHandle, originalFileName))
                {
                    errorMessage = "导航已停止，但无法恢复原文件名";
                }

                return FileDialogNavigationResult.Failure(errorMessage);
            }

            return FileDialogNavigationResult.Success;
        }
        catch (OperationCanceledException)
        {
            return FileDialogNavigationResult.Failure("导航已取消");
        }
        catch (UnauthorizedAccessException)
        {
            return FileDialogNavigationResult.Failure("文件对话框权限高于 PODO，已跳过");
        }
        catch (Exception)
        {
            return FileDialogNavigationResult.Failure("无法切换到收纳盒目录");
        }
    }

    private static bool TryNavigateWithAddressBar(
        nint dialogHandle,
        string targetPath,
        string originalFileName,
        CancellationToken cancellationToken,
        out string errorMessage)
    {
        errorMessage = "无法安全定位文件对话框地址栏";
        _ = TryActivateWindow(dialogHandle);

        nint addressEditHandle = nint.Zero;
        if (!TryActivateAddressEdit(dialogHandle, allowKeyboardFallback: true, out addressEditHandle))
        {
            errorMessage += ": " + DescribeFocusedElement(dialogHandle);
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryEnterAddressValue(dialogHandle, addressEditHandle, targetPath))
        {
            errorMessage = "无法输入文件对话框地址";
            return false;
        }

        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWindow(dialogHandle))
            {
                errorMessage = "文件对话框已关闭，导航未完成";
                return false;
            }

            if (IsDialogAtDirectory(dialogHandle, targetPath))
            {
                if (!RestoreFileNameWithAutomation(dialogHandle, originalFileName))
                {
                    errorMessage = "无法恢复原文件名，已停止后续操作";
                    return false;
                }

                _ = SetForegroundWindow(dialogHandle);
                return true;
            }

            Thread.Sleep(25);
        }

        errorMessage = "文件对话框没有切换到目标目录";
        return false;
    }

    private static bool TryEnterAddressValue(
        nint dialogHandle,
        nint addressEditHandle,
        string targetPath)
    {
        try
        {
            SendChord(VirtualKeyControl, VirtualKeyA);
            if (!SendText(targetPath))
            {
                var text = new StringBuilder(targetPath);
                _ = SendMessageW(addressEditHandle, WmSetText, nint.Zero, text);
            }

            if (TryClickAddressGoButton(dialogHandle))
            {
                return true;
            }

            _ = SendMessageW(addressEditHandle, WmKeyDown, (nint)VirtualKeyEnter, nint.Zero);
            _ = SendMessageW(addressEditHandle, WmKeyUp, (nint)VirtualKeyEnter, nint.Zero);
            SendKey(VirtualKeyEnter);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryClickAddressGoButton(nint dialogHandle)
    {
        var button = GetDlgItem(dialogHandle, 100);
        if (button == nint.Zero)
        {
            nint match = nint.Zero;
            _ = EnumChildWindows(
                dialogHandle,
                (child, _) =>
                {
                    if (GetDlgCtrlID(child) == 100
                        && string.Equals(GetClassName(child), "Button", StringComparison.Ordinal))
                    {
                        match = child;
                        return false;
                    }

                    return true;
                },
                nint.Zero);
            button = match;
        }

        if (button == nint.Zero)
        {
            return false;
        }

        _ = SendMessageW(button, BmClick, nint.Zero, nint.Zero);
        return true;
    }

    private static bool IsDialogAtDirectory(nint dialogHandle, string targetPath)
    {
        if (TryGetDialogText(dialogHandle, CdmGetFolderPath, out var currentPath)
            && !string.IsNullOrWhiteSpace(currentPath)
            && PathsEqual(currentPath, targetPath))
        {
            return true;
        }

        try
        {
            var root = AutomationElement.FromHandle(dialogHandle);
            var addressToolbar = root.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "1001"),
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ToolBar)));
            if (addressToolbar is null)
            {
                return false;
            }

            var normalizedTarget = Path.TrimEndingDirectorySeparator(targetPath);
            var targetLeaf = Path.GetFileName(normalizedTarget);
            if (addressToolbar.Current.Name.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(targetLeaf)
                    && addressToolbar.Current.Name.TrimEnd().EndsWith(
                        targetLeaf,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return addressToolbar.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Any(element => string.Equals(
                    element.Current.Name,
                    targetLeaf,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryActivateWindow(nint dialogHandle)
    {
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(dialogHandle, out _);
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var attachedTarget = targetThread != 0
            && targetThread != currentThread
            && AttachThreadInput(currentThread, targetThread, true);
        var attachedForeground = foregroundThread != 0
            && foregroundThread != currentThread
            && foregroundThread != targetThread
            && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = SetWindowPos(
                dialogHandle,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoActivate);
            _ = BringWindowToTop(dialogHandle);
            _ = SetForegroundWindow(dialogHandle);
            _ = SetActiveWindow(dialogHandle);
            _ = SetFocus(dialogHandle);
            return SpinWait.SpinUntil(
                () => GetForegroundWindow() == dialogHandle,
                TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            if (attachedForeground)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }

            if (attachedTarget)
            {
                _ = AttachThreadInput(currentThread, targetThread, false);
            }
        }
    }

    private static string DescribeFocusedElement(nint dialogHandle)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            return focused is null
                ? "无焦点元素"
                : $"{focused.Current.ControlType.ProgrammaticName}|{focused.Current.AutomationId}|"
                    + $"{focused.Current.Name}|pid={focused.Current.ProcessId}|"
                    + $"belongs={BelongsToWindow(focused, dialogHandle)}|"
                    + $"fileNameAncestor={HasAncestorAutomationId(focused, "FileNameControlHost")}";
        }
        catch (Exception exception)
        {
            return exception.GetType().Name;
        }
    }

    private static bool TryActivateAddressEdit(
        nint dialogHandle,
        bool allowKeyboardFallback,
        out nint addressEditHandle)
    {
        addressEditHandle = nint.Zero;
        nint candidate = nint.Zero;
        try
        {
            var root = AutomationElement.FromHandle(dialogHandle);
            var toolbar = root.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "1001"),
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ToolBar)));
            if (toolbar is not null && toolbar.Current.NativeWindowHandle != 0)
            {
                var toolbarHandle = (nint)toolbar.Current.NativeWindowHandle;
                if (!GetClientRect(toolbarHandle, out var clientBounds))
                {
                    clientBounds.Right = Math.Max(1, (int)toolbar.Current.BoundingRectangle.Width);
                    clientBounds.Bottom = Math.Max(1, (int)toolbar.Current.BoundingRectangle.Height);
                }

                var x = Math.Max(1, clientBounds.Right / 2);
                var y = Math.Max(1, clientBounds.Bottom / 2);
                var coordinates = (nint)((y << 16) | (x & 0xFFFF));
                _ = SendMessageW(
                    toolbarHandle,
                    0x0200,
                    nint.Zero,
                    coordinates);
                _ = SendMessageW(
                    toolbarHandle,
                    0x0201,
                    (nint)1,
                    coordinates);
                _ = SendMessageW(
                    toolbarHandle,
                    0x0203,
                    (nint)1,
                    coordinates);
                _ = SendMessageW(
                    toolbarHandle,
                    0x0202,
                    nint.Zero,
                    coordinates);
                if (SpinWait.SpinUntil(
                        () => TryGetAddressEditHandle(dialogHandle, out candidate),
                        TimeSpan.FromSeconds(1)))
                {
                    addressEditHandle = candidate;
                    return true;
                }

                if (TryActivateAddressEditWithPointer(toolbar)
                    && SpinWait.SpinUntil(
                        () => TryGetAddressEditHandle(dialogHandle, out candidate),
                        TimeSpan.FromSeconds(1)))
                {
                    addressEditHandle = candidate;
                    return true;
                }

            }

            if (!allowKeyboardFallback)
            {
                return false;
            }

            SendChord(VirtualKeyAlt, VirtualKeyD);
            if (SpinWait.SpinUntil(
                    () => TryGetAddressEditHandle(dialogHandle, out candidate),
                    TimeSpan.FromSeconds(1)))
            {
                addressEditHandle = candidate;
                return true;
            }

            SendChord(VirtualKeyControl, VirtualKeyL);
            var activated = SpinWait.SpinUntil(
                () => TryGetAddressEditHandle(dialogHandle, out candidate),
                TimeSpan.FromSeconds(1));
            addressEditHandle = candidate;
            return activated;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryActivateAddressEditWithPointer(AutomationElement toolbar)
    {
        var bounds = toolbar.Current.BoundingRectangle;
        if (bounds.Width < 4 || bounds.Height < 4)
        {
            return false;
        }

        var target = new NativePoint
        {
            X = (int)Math.Round(bounds.Left + Math.Max(2, bounds.Width - 48)),
            Y = (int)Math.Round(bounds.Top + (bounds.Height / 2))
        };
        if (!GetCursorPos(out var original))
        {
            return false;
        }

        try
        {
            if (!SetCursorPos(target.X, target.Y))
            {
                return false;
            }

            MouseEvent(MouseEventLeftDown, 0, 0, 0, nint.Zero);
            MouseEvent(MouseEventLeftUp, 0, 0, 0, nint.Zero);
            Thread.Sleep(50);
            MouseEvent(MouseEventLeftDown, 0, 0, 0, nint.Zero);
            MouseEvent(MouseEventLeftUp, 0, 0, 0, nint.Zero);
            return true;
        }
        finally
        {
            _ = SetCursorPos(original.X, original.Y);
        }
    }

    private static bool TryGetAddressEditHandle(
        nint dialogHandle,
        out nint addressEditHandle)
    {
        addressEditHandle = nint.Zero;
        try
        {
            nint nativeEdit = nint.Zero;
            try
            {
                nativeEdit = FindAddressEditHandle(dialogHandle);
            }
            catch
            {
                // Fall back to the UI Automation tree when a provider exposes no child HWND.
            }

            if (nativeEdit != nint.Zero)
            {
                addressEditHandle = nativeEdit;
                return true;
            }

            var root = AutomationElement.FromHandle(dialogHandle);
            var edit = root.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        AddressEditAutomationId),
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Edit)));
            if (edit is null || edit.Current.NativeWindowHandle == 0)
            {
                return false;
            }

            addressEditHandle = (nint)edit.Current.NativeWindowHandle;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static nint FindAddressEditHandle(nint dialogHandle)
    {
        var addressCombo = GetDlgItem(dialogHandle, 41477);
        if (addressCombo != nint.Zero)
        {
            var directEdit = FindWindowExW(addressCombo, nint.Zero, "Edit", null);
            if (directEdit != nint.Zero)
            {
                return directEdit;
            }
        }

        nint match = nint.Zero;
        _ = EnumChildWindows(
            dialogHandle,
            (child, _) =>
            {
                if (GetDlgCtrlID(child) == 41477)
                {
                    match = FindWindowExW(child, nint.Zero, "Edit", null);
                    if (match == nint.Zero
                        && string.Equals(GetClassName(child), "Edit", StringComparison.Ordinal))
                    {
                        match = child;
                    }

                    return match == nint.Zero;
                }

                return true;
            },
            nint.Zero);
        return match;
    }

    private static bool RestoreFileNameWithAutomation(nint dialogHandle, string fileName)
    {
        try
        {
            if (!TryGetFileNamePattern(dialogHandle, out var valuePattern))
            {
                return false;
            }

            if (!string.Equals(valuePattern.Current.Value, fileName, StringComparison.Ordinal))
            {
                valuePattern.SetValue(fileName);
            }

            return string.Equals(valuePattern.Current.Value, fileName, StringComparison.Ordinal);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryGetFileNameWithAutomation(nint dialogHandle, out string fileName)
    {
        fileName = string.Empty;
        try
        {
            if (!TryGetFileNamePattern(dialogHandle, out var valuePattern))
            {
                return false;
            }

            fileName = valuePattern.Current.Value;
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryGetFileNamePattern(
        nint dialogHandle,
        out ValuePattern valuePattern)
    {
        valuePattern = null!;
        var root = AutomationElement.FromHandle(dialogHandle);
        var fileNameHost = root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                "FileNameControlHost"));
        var edit = fileNameHost?.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit))
            ?? root.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        LegacyFileNameEditAutomationId),
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Edit)));
        if (edit is null
            || !edit.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject))
        {
            return false;
        }

        valuePattern = (ValuePattern)patternObject;
        return true;
    }

    private static bool BelongsToWindow(AutomationElement element, nint windowHandle)
    {
        var current = element;
        var walker = TreeWalker.RawViewWalker;
        while (current is not null)
        {
            if (current.Current.NativeWindowHandle == windowHandle)
            {
                return true;
            }

            current = walker.GetParent(current);
        }

        return false;
    }

    private static bool HasAncestorAutomationId(AutomationElement element, string automationId)
    {
        var current = element;
        var walker = TreeWalker.RawViewWalker;
        while ((current = walker.GetParent(current)) is not null)
        {
            if (string.Equals(
                    current.Current.AutomationId,
                    automationId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void SendChord(ushort modifier, ushort key)
    {
        var inputs = new[]
        {
            CreateKeyInput(modifier, keyUp: false),
            CreateKeyInput(key, keyUp: false),
            CreateKeyInput(key, keyUp: true),
            CreateKeyInput(modifier, keyUp: true)
        };
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static void SendKey(ushort key)
    {
        var inputs = new[]
        {
            CreateKeyInput(key, keyUp: false),
            CreateKeyInput(key, keyUp: true)
        };
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static bool SendText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var inputs = new Input[value.Length * 2];
        for (var index = 0; index < value.Length; index++)
        {
            inputs[index * 2] = CreateUnicodeInput(value[index], keyUp: false);
            inputs[(index * 2) + 1] = CreateUnicodeInput(value[index], keyUp: true);
        }

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static Input CreateKeyInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyEventKeyUp : 0
            }
        }
    };

    private static Input CreateUnicodeInput(char value, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                ScanCode = value,
                Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0)
            }
        }
    };

    private static bool TryGetDialogText(nint dialogHandle, uint message, out string value)
    {
        var buffer = new StringBuilder(32768);
        var result = SendMessageW(dialogHandle, message, (nint)buffer.Capacity, buffer);
        value = buffer.ToString();
        return result != nint.Zero;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetClassName(nint window)
    {
        var value = new StringBuilder(128);
        return GetClassNameW(window, value, value.Capacity) > 0 ? value.ToString() : string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(
        nint window,
        uint message,
        nint wParam,
        StringBuilder lParam);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetDlgItem(nint dialog, int controlId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(
        nint parent,
        nint after,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        nint parent,
        EnumChildProc callback,
        nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        nint window,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private delegate bool EnumChildProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        nint extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
