using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class DpiAwareIconSizeTests
{
    [Theory]
    [InlineData(14, 1.0, false, 32)]
    [InlineData(20, 1.0, false, 32)]
    [InlineData(20, 1.25, false, 32)]
    [InlineData(30, 1.5, false, 48)]
    [InlineData(44, 2.0, false, 96)]
    public void Calculate_SelectsTheNextNativeSourceSizeForOneTimeDownscaling(
        double displaySizeDip,
        double dpiScale,
        bool isPixelated,
        int expected)
    {
        var actual = DpiAwareIconSize.Calculate(
            displaySizeDip,
            displaySizeDip,
            dpiScale,
            dpiScale,
            isPixelated);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(44, 1.0, 16)]
    [InlineData(44, 1.5, 24)]
    [InlineData(44, 2.0, 32)]
    public void Calculate_PixelatedIconsKeepAStableLogicalSourceGrid(
        double displaySizeDip,
        double dpiScale,
        int expected)
    {
        var actual = DpiAwareIconSize.Calculate(
            displaySizeDip,
            displaySizeDip,
            dpiScale,
            dpiScale,
            isPixelated: true);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Calculate_UsesTheLargerAxisAndFallsBackForInvalidDpi()
    {
        var actual = DpiAwareIconSize.Calculate(
            displayWidthDip: 19,
            displayHeightDip: 30,
            dpiScaleX: double.NaN,
            dpiScaleY: 1.5,
            isPixelated: false);

        Assert.Equal(48, actual);
    }

    [Fact]
    public void Calculate_CapsVeryLargeRequests()
    {
        var actual = DpiAwareIconSize.Calculate(
            displayWidthDip: 500,
            displayHeightDip: 500,
            dpiScaleX: 3,
            dpiScaleY: 3,
            isPixelated: false);

        Assert.Equal(256, actual);
    }

    [Fact]
    public void Calculate_CapsExtremeFiniteInputsWithoutOverflow()
    {
        var actual = DpiAwareIconSize.Calculate(
            displayWidthDip: double.MaxValue,
            displayHeightDip: double.MaxValue,
            dpiScaleX: 1,
            dpiScaleY: 1,
            isPixelated: false);

        Assert.Equal(256, actual);
    }
}
