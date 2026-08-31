using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WitchDrawer.App.Infrastructure;

public static class WindowMotion
{
    /// <summary>
    /// Mirrors the Windows "Animate controls and elements inside windows" preference.
    /// Keep decorative transitions opt-in so the app remains usable when users request
    /// reduced motion (and so tests can exercise the settled state without waiting on clocks).
    /// </summary>
    public static bool AreAnimationsEnabled => SystemParameters.ClientAreaAnimation;

    public static PopupAnimation PopupAnimation =>
        AreAnimationsEnabled ? PopupAnimation.Fade : PopupAnimation.None;

    public static void AnimateTranslateY(UIElement element, double to, int milliseconds)
    {
        ArgumentNullException.ThrowIfNull(element);

        var currentTransform = element.RenderTransform as TranslateTransform;
        var translateTransform = currentTransform switch
        {
            null => new TranslateTransform(),
            { IsFrozen: true } => currentTransform.CloneCurrentValue(),
            _ => currentTransform
        };

        if (!ReferenceEquals(translateTransform, currentTransform))
        {
            element.RenderTransform = translateTransform;
        }

        if (!AreAnimationsEnabled)
        {
            translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            translateTransform.Y = to;
            return;
        }

        translateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    public static void PopIn(Window window, double fromScale = 0.985, int milliseconds = 150)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.RenderTransformOrigin = new Point(0.5, 0.5);
        if (window.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            window.RenderTransform = scale;
        }

        window.Opacity = 0;
        scale.ScaleX = fromScale;
        scale.ScaleY = fromScale;

        if (!AreAnimationsEnabled)
        {
            window.BeginAnimation(UIElement.OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            window.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration)
        {
            EasingFunction = ease
        });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration)
        {
            EasingFunction = ease
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration)
        {
            EasingFunction = ease
        });
    }
}
