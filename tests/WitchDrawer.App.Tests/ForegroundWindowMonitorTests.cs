using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class ForegroundWindowMonitorTests
{
    [Theory]
    [InlineData("Progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("Shell_TrayWnd", false)]
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData(null, false)]
    public void IsDesktopWindowClass_RecognizesOnlyDesktopShellClasses(
        string? className,
        bool expected)
    {
        Assert.Equal(expected, ForegroundWindowMonitor.IsDesktopWindowClass(className));
    }
}
