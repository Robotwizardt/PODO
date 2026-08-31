using System.Windows.Media;

namespace PaperTodo;

internal static class TodoTextColors
{
    public const string Red = "red";
    public const string Orange = "orange";
    public const string Green = "green";
    public const string Blue = "blue";
    public const string Purple = "purple";

    private static readonly Brush LightRed = Frozen(Color.FromRgb(156, 45, 45));
    private static readonly Brush LightOrange = Frozen(Color.FromRgb(138, 75, 8));
    private static readonly Brush LightGreen = Frozen(Color.FromRgb(38, 97, 58));
    private static readonly Brush LightBlue = Frozen(Color.FromRgb(36, 90, 146));
    private static readonly Brush LightPurple = Frozen(Color.FromRgb(103, 65, 163));
    private static readonly Brush DarkRed = Frozen(Color.FromRgb(255, 138, 128));
    private static readonly Brush DarkOrange = Frozen(Color.FromRgb(255, 180, 92));
    private static readonly Brush DarkGreen = Frozen(Color.FromRgb(123, 216, 143));
    private static readonly Brush DarkBlue = Frozen(Color.FromRgb(131, 185, 255));
    private static readonly Brush DarkPurple = Frozen(Color.FromRgb(196, 160, 255));

    public static string? Normalize(string? value) => value switch
    {
        Red or Orange or Green or Blue or Purple => value,
        _ => null
    };

    public static Brush BrushFor(string? value)
    {
        return (Normalize(value), Theme.IsDark) switch
        {
            (Red, false) => LightRed,
            (Orange, false) => LightOrange,
            (Green, false) => LightGreen,
            (Blue, false) => LightBlue,
            (Purple, false) => LightPurple,
            (Red, true) => DarkRed,
            (Orange, true) => DarkOrange,
            (Green, true) => DarkGreen,
            (Blue, true) => DarkBlue,
            (Purple, true) => DarkPurple,
            _ => Theme.TextBrush
        };
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
