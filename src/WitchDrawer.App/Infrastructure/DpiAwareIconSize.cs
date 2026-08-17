namespace WitchDrawer.App.Infrastructure;

public static class DpiAwareIconSize
{
    public const int MinimumSourcePixelSize = 8;
    public const int MaximumSourcePixelSize = 256;

    private const double PixelatedSourceSizeDip = 16;
    private static readonly int[] NativeSourceSizes = [32, 48, 64, 96, 128, 256];

    public static int Calculate(
        double displayWidthDip,
        double displayHeightDip,
        double dpiScaleX,
        double dpiScaleY,
        bool isPixelated)
    {
        var widthDip = NormalizePositive(displayWidthDip, 16);
        var heightDip = NormalizePositive(displayHeightDip, widthDip);
        var scaleX = NormalizePositive(dpiScaleX, 1);
        var scaleY = NormalizePositive(dpiScaleY, 1);

        var targetWidthDip = isPixelated
            ? Math.Min(widthDip, PixelatedSourceSizeDip)
            : widthDip;
        var targetHeightDip = isPixelated
            ? Math.Min(heightDip, PixelatedSourceSizeDip)
            : heightDip;
        var targetPhysicalPixels = Math.Max(targetWidthDip * scaleX, targetHeightDip * scaleY);
        if (targetPhysicalPixels >= MaximumSourcePixelSize)
        {
            return MaximumSourcePixelSize;
        }

        var targetPixels = Math.Max(
            (int)Math.Round(targetPhysicalPixels, MidpointRounding.AwayFromZero),
            MinimumSourcePixelSize);
        if (isPixelated)
        {
            return targetPixels;
        }

        // Asking Windows Shell for an arbitrary small size such as 20 px can make
        // it enlarge a 16 px icon before WPF sees it. Select the next common native
        // frame instead, then let WPF perform one high-quality downscale.
        foreach (var sourceSize in NativeSourceSizes)
        {
            if (targetPixels <= sourceSize)
            {
                return sourceSize;
            }
        }

        return MaximumSourcePixelSize;
    }

    private static double NormalizePositive(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }
}
