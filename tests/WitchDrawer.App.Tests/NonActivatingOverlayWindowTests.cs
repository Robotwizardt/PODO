using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class NonActivatingOverlayWindowTests
{
    [Fact]
    public void ApplyRequiredExtendedStyles_AddsOverlayStylesAndRemovesAppWindow()
    {
        const long layered = 0x00080000;
        const long appWindow = 0x00040000;
        const long transparent = 0x00000020;
        const long toolWindow = 0x00000080;
        const long noActivate = 0x08000000;

        var result = NonActivatingOverlayWindow
            .ApplyRequiredExtendedStyles((nint)(layered | appWindow))
            .ToInt64();

        Assert.NotEqual(0, result & layered);
        Assert.NotEqual(0, result & transparent);
        Assert.NotEqual(0, result & toolWindow);
        Assert.NotEqual(0, result & noActivate);
        Assert.Equal(0, result & appWindow);
    }

    [Theory]
    [InlineData(NonActivatingOverlayWindow.NonClientHitTestMessage, true)]
    [InlineData(0x0200, false)]
    [InlineData(0x0112, false)]
    public void IsNonClientHitTestMessage_RecognizesOnlyHitTesting(
        int message,
        bool expected)
    {
        Assert.Equal(
            expected,
            NonActivatingOverlayWindow.IsNonClientHitTestMessage(message));
    }
}
