using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// Describes the native backdrop that should be requested for a WPF window.
/// <see cref="Automatic"/> keeps the existing <see cref="AppThemeManager.ApplyToWindow(Window)"/>
/// API source compatible while still allowing the main shell to opt into Mica explicitly.
/// </summary>
public enum WindowBackdropKind
{
    Automatic,
    MainWindow,
    Transient,
    None
}

/// <summary>
/// Applies optional Windows 11 DWM backdrops and deliberately treats them as an enhancement.
/// WPF layered windows (<see cref="Window.AllowsTransparency"/>) cannot reliably host a DWM
/// backdrop, so their Acrylic-looking resource surface remains the stable fallback.
/// </summary>
public static class WindowBackdropManager
{
    // DWMWINDOWATTRIBUTE values are available in the Windows SDK and do not require a newer
    // target framework. Keep them here so the policy is easy to audit and test.
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwaSystemBackdropType = 38;
    public const int DwmWindowCornerPreferenceRound = 2;
    public const int DwmSystemBackdropNone = 1;
    public const int DwmSystemBackdropMainWindow = 2; // Mica
    public const int DwmSystemBackdropTransientWindow = 3; // Acrylic-like transient surface

    /// <summary>
    /// Applies the policy inferred from the window type. Existing callers can continue using
    /// this overload; the shell can use the explicit overload when its intent is clearer.
    /// </summary>
    public static void Apply(Window window, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(window);
        Apply(window, theme, ResolveKind(window));
    }

    /// <summary>
    /// Requests a native backdrop when the platform and window style support it. The method is
    /// intentionally total: missing DWM, an old Windows build, a not-yet-created HWND, and a
    /// rejected attribute all leave the caller on its resource-backed surface without throwing.
    /// </summary>
    public static void Apply(Window window, AppTheme theme, WindowBackdropKind kind)
    {
        ArgumentNullException.ThrowIfNull(window);

        _ = TryApply(window, theme, kind);
    }

    /// <summary>
    /// Applies a native backdrop and reports whether DWM accepted the requested type. The
    /// result lets the resource-backed caller choose an opaque fallback when DWM is absent,
    /// disabled, or rejects the attribute.
    /// </summary>
    public static bool TryApply(Window window, AppTheme theme, WindowBackdropKind kind)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.AllowsTransparency)
        {
            // Layered WPF windows are used by desktop boxes and their transparent chrome. DWM
            // backdrops are not composited consistently beneath a layered HWND.
            return false;
        }

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            _ = TrySetIntAttribute(
                handle,
                DwmwaWindowCornerPreference,
                DwmWindowCornerPreferenceRound);

            var backdropType = GetPreferredBackdropType(window, theme, kind);
            if (!IsWindows11OrGreater())
            {
                return false;
            }

            if (backdropType == DwmSystemBackdropNone)
            {
                _ = TrySetIntAttribute(handle, DwmwaSystemBackdropType, DwmSystemBackdropNone);
                return false;
            }

            // A failed backdrop request is expected on older DWM revisions and on remote
            // sessions. Explicitly reset to None so a stale value cannot survive a theme change.
            if (TrySetIntAttribute(handle, DwmwaSystemBackdropType, backdropType))
            {
                return true;
            }

            _ = TrySetIntAttribute(handle, DwmwaSystemBackdropType, DwmSystemBackdropNone);
            return false;
        }
        catch (DllNotFoundException)
        {
            // Non-Windows test hosts and stripped-down environments have no dwmapi.dll.
        }
        catch (EntryPointNotFoundException)
        {
            // Keep the resource fallback when a legacy DWM does not expose the entry point.
        }
        catch (InvalidOperationException)
        {
            // The HWND can disappear during close/theme changes. This is visual-only work.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // DWM can reject attributes (for example in a remote session); do not fail the UI.
        }

        return false;
    }

    /// <summary>
    /// Resolves the default kind without referencing a window class from the infrastructure
    /// layer. Explicit callers should prefer the overload above; the type-name check keeps the
    /// long-standing ApplyToWindow API useful for the main shell.
    /// </summary>
    public static WindowBackdropKind ResolveKind(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return string.Equals(window.GetType().Name, "MainWindow", StringComparison.Ordinal)
            ? WindowBackdropKind.MainWindow
            : WindowBackdropKind.Transient;
    }

    public static int GetPreferredBackdropType(
        Window window,
        AppTheme theme,
        WindowBackdropKind kind = WindowBackdropKind.Automatic)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (SystemParameters.HighContrast || window.AllowsTransparency || theme == AppTheme.Moe)
        {
            return DwmSystemBackdropNone;
        }

        var resolvedKind = kind == WindowBackdropKind.Automatic ? ResolveKind(window) : kind;
        return resolvedKind switch
        {
            WindowBackdropKind.MainWindow => DwmSystemBackdropMainWindow,
            WindowBackdropKind.Transient => DwmSystemBackdropTransientWindow,
            _ => DwmSystemBackdropNone
        };
    }

    public static bool IsWindows11OrGreater() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    private static bool TrySetIntAttribute(IntPtr handle, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
