using System.Globalization;

namespace WitchDrawer.App.ViewModels;

/// <summary>
/// 用户直接调整后的收纳盒窗口尺寸。
/// 窗口首次显示仍由内容自适应，只有用户实际调整过的宽高才会保存。
/// </summary>
public sealed record BoxWindowSizeState(double Width, double Height)
{
    public const double MinimumWidth = 180;
    public const double MinimumHeight = 120;

    public string Serialize() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Width:R},{Height:R}");

    public static BoxWindowSizeState? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(
                parts[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var width)
            || !double.TryParse(
                parts[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var height)
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || width <= 0
            || height <= 0)
        {
            return null;
        }

        return Normalize(width, height);
    }

    public static BoxWindowSizeState Normalize(double width, double height) =>
        new(
            NormalizeDimension(width, MinimumWidth),
            NormalizeDimension(height, MinimumHeight));

    private static double NormalizeDimension(double value, double minimum) =>
        double.IsFinite(value) && value > 0
            ? Math.Max(minimum, value)
            : minimum;
}
