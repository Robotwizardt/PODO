using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PaperTodo;

internal static class TodoPinIcon
{
    private const string HeadPathData =
        "M 7.5,4.25 H 16.5 V 5.75 H 15.5 V 12.05 L 17.6,14.15 V 15.35 H 6.4 V 14.15 L 8.5,12.05 V 5.75 H 7.5 Z";
    private const string NeedlePathData =
        "M 10.85,15.35 H 13.15 V 22.1 L 12,23.25 L 10.85,22.1 Z";

    public static FrameworkElement Create(
        Brush foreground,
        double size,
        string accessibleName)
    {
        var canvas = new Canvas
        {
            Width = 24,
            Height = 24,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(HeadPathData),
            Fill = foreground,
            Stroke = foreground,
            StrokeThickness = 2.15,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            SnapsToDevicePixels = true
        });
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(NeedlePathData),
            Fill = foreground,
            SnapsToDevicePixels = true
        });

        var indicator = new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            ToolTip = accessibleName
        };
        System.Windows.Automation.AutomationProperties.SetName(
            indicator,
            accessibleName);
        return indicator;
    }
}
