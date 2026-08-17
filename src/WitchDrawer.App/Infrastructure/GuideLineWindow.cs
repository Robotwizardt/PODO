using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Infrastructure;

public sealed class GuideLineWindow : Window
{
    private const double OverlayThickness = 4;

    private readonly bool _isVertical;
    private readonly Line _line;
    private HwndSource? _source;

    public GuideLineWindow(bool isVertical)
    {
        _isVertical = isVertical;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Width = OverlayThickness;
        Height = OverlayThickness;

        _line = new Line
        {
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }),
            SnapsToDevicePixels = true
        };

        // Dynamically reference active AccentBrush theme resource
        _line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");

        var canvas = new Canvas();
        canvas.Children.Add(_line);
        Content = canvas;
    }

    public void UpdateLine(double x1, double y1, double x2, double y2)
    {
        if (_isVertical)
        {
            var top = Math.Min(y1, y2);
            var height = Math.Max(1, Math.Abs(y2 - y1));

            Left = x1 - OverlayThickness / 2;
            Top = top;
            Width = OverlayThickness;
            Height = height;

            _line.X1 = OverlayThickness / 2;
            _line.X2 = OverlayThickness / 2;
            _line.Y1 = y1 <= y2 ? 0 : height;
            _line.Y2 = y1 <= y2 ? height : 0;
            return;
        }

        var left = Math.Min(x1, x2);
        var width = Math.Max(1, Math.Abs(x2 - x1));

        Left = left;
        Top = y1 - OverlayThickness / 2;
        Width = width;
        Height = OverlayThickness;

        _line.X1 = x1 <= x2 ? 0 : width;
        _line.X2 = x1 <= x2 ? width : 0;
        _line.Y1 = OverlayThickness / 2;
        _line.Y2 = OverlayThickness / 2;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        NonActivatingOverlayWindow.Configure(handle);
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
        base.OnClosed(e);
    }

    private static nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (NonActivatingOverlayWindow.IsNonClientHitTestMessage(message))
        {
            handled = true;
            return NonActivatingOverlayWindow.TransparentHitTestResult;
        }

        return nint.Zero;
    }
}
