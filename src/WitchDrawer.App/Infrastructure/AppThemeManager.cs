using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// Owns semantic application brushes. Views should consume these resources instead of choosing
/// a per-screen color; replacing a resource keeps theme changes live through DynamicResource.
/// </summary>
public static class AppThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Glass;
    private static bool _useTransparentCrystalBoxes;

    private static readonly IReadOnlyDictionary<string, string> TransparentCrystalBoxColors =
        new Dictionary<string, string>
        {
            ["WindowFallbackBrush"] = "#FFF7FBFF",
            ["AppBackgroundBrush"] = "#78FFFFFF",
            ["PanelBrush"] = "#66FFFFFF",
            ["PanelAltBrush"] = "#4DF2F2F7",
            ["BorderBrushSoft"] = "#29FFFFFF",
            ["TextPrimaryBrush"] = "#1D1D1F",
            ["TextMutedBrush"] = "#6E6E73",
            ["AccentBrush"] = "#0071E3",
            ["AccentHoverBrush"] = "#0068D1",
            ["AccentPressedBrush"] = "#0059B3",
            ["AccentSoftBrush"] = "#2E0071E3",
            ["SelectionBackgroundBrush"] = "#2E0071E3",
            ["SelectionForegroundBrush"] = "#0071E3",
            ["OnAccentBrush"] = "#FFFFFFFF",
            ["GlassSurfaceBrush"] = "#70FFFFFF",
            ["AcrylicSurfaceBrush"] = "#B8FFFFFF",
            ["DrawerSecondarySurfaceBrush"] = "#70F5F5F7",
            ["ProjectAttachmentChevronBrush"] = "#FF2563EB",
            ["GlassInnerBrush"] = "#24FFFFFF",
            ["AcrylicInnerBrush"] = "#5CFFFFFF",
            ["GlassStrokeBrush"] = "#3DFFFFFF",
            ["AcrylicStrokeBrush"] = "#78FFFFFF",
            ["GlassHighlightBrush"] = "#A6FFFFFF",
            ["GlassControlBrush"] = "#52FFFFFF",
            ["AcrylicHoverBrush"] = "#80FFFFFF",
            ["AmbientCyanBrush"] = "#3D24B6F6",
            ["AmbientVioletBrush"] = "#352E5BFF",
            ["AmbientPeachBrush"] = "#2BF59E9E",
            ["FocusRingBrush"] = "#FF2563EB",
            ["PositiveBrush"] = "#34C759",
            ["PositiveSoftBrush"] = "#2634C759",
            ["DangerBrush"] = "#FF3B30",
            ["DangerSoftBrush"] = "#26FF3B30",
            ["HoverBrush"] = "#52FFFFFF",
            ["CardShadowBrush"] = "#18000000",
            ["DropZoneBrush"] = "#33FFFFFF",
            ["WindowOverlayBrush"] = "#24FFFFFF",
            ["ScrimBrush"] = "#30000000"
        };

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static event EventHandler<bool>? CrystalBoxTransparencyChanged;

    /// <summary>Raised when Windows high-contrast mode changes while the app is running.</summary>
    public static event EventHandler<bool>? HighContrastChanged;

    public static AppTheme CurrentTheme => _currentTheme;

    public static bool UseTransparentCrystalBoxes => _useTransparentCrystalBoxes;

    public static bool IsHighContrast => SystemParameters.HighContrast;

    static AppThemeManager()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public static void Apply(AppTheme theme)
    {
        _currentTheme = theme;
        ApplyMotionResources();

        if (SystemParameters.HighContrast)
        {
            ApplyHighContrastResources();
        }
        else if (theme == AppTheme.Glass)
        {
            ApplyColors(
                ("ControlCenterSurfaceBrush", "#B80F1B2B"),
                ("WindowFallbackBrush", "#FF101A2A"),
                ("AppBackgroundBrush", "#A80B1422"),
                ("PanelBrush", "#B81A2A3D"),
                ("PanelAltBrush", "#6D1E3249"),
                ("BorderBrushSoft", "#29FFFFFF"),
                ("TextPrimaryBrush", "#FFF4F8FF"),
                ("TextMutedBrush", "#FFB5C4D9"),
                ("AccentBrush", "#FF54B8FF"),
                ("AccentHoverBrush", "#FF7BC8FF"),
                ("AccentPressedBrush", "#FF2D9AEA"),
                ("AccentSoftBrush", "#3D54B8FF"),
                ("SelectionBackgroundBrush", "#3D54B8FF"),
                ("SelectionForegroundBrush", "#FFBFE4FF"),
                ("OnAccentBrush", "#FF07101D"),
                ("GlassSurfaceBrush", "#941A2A3D"),
                ("AcrylicSurfaceBrush", "#CC16283D"),
                ("DrawerSecondarySurfaceBrush", "#8C0C1526"),
                ("ProjectAttachmentChevronBrush", "#FF8E7CFF"),
                ("GlassInnerBrush", "#14FFFFFF"),
                ("AcrylicInnerBrush", "#3AFFFFFF"),
                ("GlassStrokeBrush", "#29FFFFFF"),
                ("AcrylicStrokeBrush", "#52FFFFFF"),
                ("GlassHighlightBrush", "#70FFFFFF"),
                ("GlassControlBrush", "#36203C5A"),
                ("AcrylicHoverBrush", "#4CFFFFFF"),
                ("AmbientCyanBrush", "#5A22D3EE"),
                ("AmbientVioletBrush", "#462E52FF"),
                ("AmbientPeachBrush", "#3AF59E9E"),
                ("FocusRingBrush", "#FFBFE4FF"),
                ("PositiveBrush", "#FF30D158"),
                ("PositiveSoftBrush", "#2630D158"),
                ("DangerBrush", "#FFFF6B6B"),
                ("DangerSoftBrush", "#26FF453A"),
                ("HoverBrush", "#36203C5A"),
                ("CardShadowBrush", "#88000000"),
                ("DropZoneBrush", "#361D6C90"),
                ("WindowOverlayBrush", "#6507101D"),
                ("ScrimBrush", "#99000000"));
        }
        else if (theme == AppTheme.Crystal)
        {
            ApplyColors(
                ("ControlCenterSurfaceBrush", "#D8F7FBFF"),
                ("WindowFallbackBrush", "#FFF0F8FC"),
                ("AppBackgroundBrush", "#B8EAF7FD"),
                ("PanelBrush", "#C7FFFFFF"),
                ("PanelAltBrush", "#90F1F7FD"),
                ("BorderBrushSoft", "#7AFFFFFF"),
                ("TextPrimaryBrush", "#FF17263D"),
                ("TextMutedBrush", "#FF53647C"),
                ("AccentBrush", "#FF2563EB"),
                ("AccentHoverBrush", "#FF1D4ED8"),
                ("AccentPressedBrush", "#FF1E40AF"),
                ("AccentSoftBrush", "#3D72B8FF"),
                ("SelectionBackgroundBrush", "#3D72B8FF"),
                ("SelectionForegroundBrush", "#FF174A9A"),
                ("OnAccentBrush", "#FFFFFFFF"),
                ("GlassSurfaceBrush", "#B8FFFFFF"),
                ("AcrylicSurfaceBrush", "#D6FFFFFF"),
                ("DrawerSecondarySurfaceBrush", "#96F4F8FD"),
                ("ProjectAttachmentChevronBrush", "#FF2563EB"),
                ("GlassInnerBrush", "#66FFFFFF"),
                ("AcrylicInnerBrush", "#99FFFFFF"),
                ("GlassStrokeBrush", "#78FFFFFF"),
                ("AcrylicStrokeBrush", "#A6FFFFFF"),
                ("GlassHighlightBrush", "#E6FFFFFF"),
                ("GlassControlBrush", "#B8FFFFFF"),
                ("AcrylicHoverBrush", "#CCFFFFFF"),
                ("AmbientCyanBrush", "#4A18B9FF"),
                ("AmbientVioletBrush", "#382E5BFF"),
                ("AmbientPeachBrush", "#2BF59E9E"),
                ("FocusRingBrush", "#FF2563EB"),
                ("PositiveBrush", "#FF197A37"),
                ("PositiveSoftBrush", "#2634C759"),
                ("DangerBrush", "#FFC62828"),
                ("DangerSoftBrush", "#26FF3B30"),
                ("HoverBrush", "#99FFFFFF"),
                ("CardShadowBrush", "#26001A36"),
                ("DropZoneBrush", "#66EAF7FF"),
                ("WindowOverlayBrush", "#30001A36"),
                ("ScrimBrush", "#30001A36"));
        }
        else
        {
            ApplyColors(
                ("ControlCenterSurfaceBrush", "#D6F3F8FD"),
                ("WindowFallbackBrush", "#FFF4FAFC"),
                ("AppBackgroundBrush", "#C7F1F6FB"),
                ("PanelBrush", "#D6FFFFFF"),
                ("PanelAltBrush", "#A6F4F8FD"),
                ("BorderBrushSoft", "#70FFFFFF"),
                ("TextPrimaryBrush", "#FF16263D"),
                ("TextMutedBrush", "#FF53647C"),
                ("AccentBrush", "#FF2563EB"),
                ("AccentHoverBrush", "#FF1D4ED8"),
                ("AccentPressedBrush", "#FF1E40AF"),
                ("AccentSoftBrush", "#3D72B8FF"),
                ("SelectionBackgroundBrush", "#3D72B8FF"),
                ("SelectionForegroundBrush", "#FF174A9A"),
                ("OnAccentBrush", "#FFFFFFFF"),
                ("GlassSurfaceBrush", "#B8FFFFFF"),
                ("AcrylicSurfaceBrush", "#D6FFFFFF"),
                ("DrawerSecondarySurfaceBrush", "#B8F4F8FD"),
                ("ProjectAttachmentChevronBrush", "#FF2563EB"),
                ("GlassInnerBrush", "#72F4F8FD"),
                ("AcrylicInnerBrush", "#B8F4F8FD"),
                ("GlassStrokeBrush", "#70FFFFFF"),
                ("AcrylicStrokeBrush", "#A6FFFFFF"),
                ("GlassHighlightBrush", "#BFFFFFFF"),
                ("GlassControlBrush", "#B8F4F8FD"),
                ("AcrylicHoverBrush", "#CCFFFFFF"),
                ("AmbientCyanBrush", "#3D24B6F6"),
                ("AmbientVioletBrush", "#352E5BFF"),
                ("AmbientPeachBrush", "#2BF59E9E"),
                ("FocusRingBrush", "#FF2563EB"),
                ("PositiveBrush", "#FF197A37"),
                ("PositiveSoftBrush", "#EAF8EE"),
                ("DangerBrush", "#FFC62828"),
                ("DangerSoftBrush", "#FFF0EF"),
                ("HoverBrush", "#80FFFFFF"),
                ("CardShadowBrush", "#26001A36"),
                ("DropZoneBrush", "#66EAF7FF"),
                ("WindowOverlayBrush", "#30001A36"),
                ("ScrimBrush", "#30001A36"));
        }

        ThemeChanged?.Invoke(null, theme);
    }

    public static void SetCrystalBoxTransparency(bool enabled)
    {
        if (_useTransparentCrystalBoxes == enabled)
        {
            return;
        }

        _useTransparentCrystalBoxes = enabled;
        CrystalBoxTransparencyChanged?.Invoke(null, enabled);
    }

    public static void ApplyDesktopBoxResources(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (var key in TransparentCrystalBoxColors.Keys)
        {
            resources.Remove(key);
        }

        // High contrast intentionally never opts into translucent per-window overrides.
        if (IsHighContrast || _currentTheme != AppTheme.Crystal || !_useTransparentCrystalBoxes)
        {
            return;
        }

        foreach (var (key, color) in TransparentCrystalBoxColors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    public static void ApplyToWindow(Window window) =>
        ApplyToWindow(window, WindowBackdropKind.Automatic);

    public static void ApplyToWindow(Window window, WindowBackdropKind backdropKind)
    {
        ArgumentNullException.ThrowIfNull(window);

        var nativeBackdropApplied = WindowBackdropManager.TryApply(window, _currentTheme, backdropKind);

        var resources = Application.Current?.Resources;
        var fallbackBrush = resources?["WindowFallbackBrush"] as Brush
            ?? resources?["AppBackgroundBrush"] as Brush
            ?? SystemColors.WindowBrush;
        var foregroundBrush = resources?["TextPrimaryBrush"] as Brush
            ?? SystemColors.WindowTextBrush;

        // Layered windows cannot expose a DWM backdrop and therefore keep their transparent
        // chrome. Normal windows use a transparent client surface only when the requested
        // native backdrop can actually paint behind it; Windows 10, Moe and failed-DWM paths
        // retain an opaque fallback instead of exposing an undefined desktop colour.
        window.Background = window.AllowsTransparency || nativeBackdropApplied
            ? Brushes.Transparent
            : fallbackBrush;
        window.Foreground = foregroundBrush;
    }

    private static void ApplyHighContrastResources()
    {
        ApplyBrushes(
            ("ControlCenterSurfaceBrush", SystemColors.WindowBrush),
            ("WindowFallbackBrush", SystemColors.WindowBrush),
            ("AppBackgroundBrush", SystemColors.WindowBrush),
            ("PanelBrush", SystemColors.ControlBrush),
            ("PanelAltBrush", SystemColors.ControlBrush),
            ("BorderBrushSoft", SystemColors.WindowTextBrush),
            ("TextPrimaryBrush", SystemColors.WindowTextBrush),
            ("TextMutedBrush", SystemColors.GrayTextBrush),
            ("AccentBrush", SystemColors.HighlightBrush),
            ("AccentHoverBrush", SystemColors.HighlightBrush),
            ("AccentPressedBrush", SystemColors.HighlightBrush),
            ("AccentSoftBrush", SystemColors.HighlightBrush),
            ("SelectionBackgroundBrush", SystemColors.HighlightBrush),
            ("SelectionForegroundBrush", SystemColors.HighlightTextBrush),
            ("OnAccentBrush", SystemColors.HighlightTextBrush),
            ("GlassSurfaceBrush", SystemColors.ControlBrush),
            ("AcrylicSurfaceBrush", SystemColors.ControlBrush),
            ("DrawerSecondarySurfaceBrush", SystemColors.ControlBrush),
            ("ProjectAttachmentChevronBrush", SystemColors.HighlightBrush),
            ("GlassInnerBrush", SystemColors.ControlBrush),
            ("AcrylicInnerBrush", SystemColors.ControlBrush),
            ("GlassStrokeBrush", SystemColors.WindowTextBrush),
            ("AcrylicStrokeBrush", SystemColors.WindowTextBrush),
            ("GlassHighlightBrush", SystemColors.WindowTextBrush),
            ("GlassControlBrush", SystemColors.ControlBrush),
            ("AcrylicHoverBrush", SystemColors.ControlBrush),
            ("AmbientCyanBrush", SystemColors.ControlBrush),
            ("AmbientVioletBrush", SystemColors.ControlBrush),
            ("AmbientPeachBrush", SystemColors.ControlBrush),
            ("FocusRingBrush", SystemColors.HighlightBrush),
            ("PositiveBrush", SystemColors.WindowTextBrush),
            ("PositiveSoftBrush", SystemColors.ControlBrush),
            ("DangerBrush", SystemColors.WindowTextBrush),
            ("DangerSoftBrush", SystemColors.ControlBrush),
            ("HoverBrush", SystemColors.ControlBrush),
            ("CardShadowBrush", Brushes.Transparent),
            ("DropZoneBrush", SystemColors.ControlBrush),
            ("WindowOverlayBrush", SystemColors.WindowBrush),
            ("ScrimBrush", SystemColors.WindowBrush));
    }

    private static void ApplyColors(params (string Key, string Color)[] colors)
    {
        foreach (var (key, color) in colors)
        {
            SetColor(key, color);
        }
    }

    private static void ApplyBrushes(params (string Key, Brush Brush)[] brushes)
    {
        foreach (var (key, brush) in brushes)
        {
            SetBrush(key, brush);
        }
    }

    private static void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.HighContrast)
            or nameof(SystemParameters.ClientAreaAnimation)
            or null))
        {
            return;
        }

        var highContrastChanged = e.PropertyName is nameof(SystemParameters.HighContrast) or null;
        var highContrast = SystemParameters.HighContrast;
        Apply(_currentTheme);
        if (highContrastChanged)
        {
            HighContrastChanged?.Invoke(null, highContrast);
        }
    }

    private static void ApplyMotionResources()
    {
        var duration = WindowMotion.AreAnimationsEnabled
            ? new Duration(TimeSpan.FromMilliseconds(180))
            : new Duration(TimeSpan.Zero);
        var fastDuration = WindowMotion.AreAnimationsEnabled
            ? new Duration(TimeSpan.FromMilliseconds(120))
            : new Duration(TimeSpan.Zero);
        SetResource("MotionDuration", duration);
        SetResource("MotionDurationFast", fastDuration);
    }

    private static void SetColor(string key, string color)
    {
        SetBrush(key, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
    }

    private static void SetBrush(string key, Brush brush)
    {
        if (Application.Current is not null)
        {
            Application.Current.Resources[key] = brush;
        }
    }

    private static void SetResource(string key, object value)
    {
        if (Application.Current is not null)
        {
            Application.Current.Resources[key] = value;
        }
    }
}
