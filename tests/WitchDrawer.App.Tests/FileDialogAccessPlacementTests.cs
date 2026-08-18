using WitchDrawer.App.FileDialogAccess;

namespace WitchDrawer.App.Tests;

public sealed class FileDialogAccessPlacementTests
{
    [Fact]
    public void Calculate_PrefersRightThenLeftThenSafeInnerPlacement()
    {
        var workArea = new FileDialogScreenRect(0, 0, 1920, 1080);

        var right = FileDialogAccessPlacement.Calculate(
            new FileDialogScreenRect(300, 100, 900, 800),
            workArea,
            preferredWidth: 320,
            reservedFooterHeight: 88);
        Assert.Equal(FileDialogAccessSide.Right, right.Side);
        Assert.Equal(new FileDialogScreenRect(900, 100, 1220, 800), right.Bounds);

        var left = FileDialogAccessPlacement.Calculate(
            new FileDialogScreenRect(1500, 100, 1900, 800),
            workArea,
            preferredWidth: 320,
            reservedFooterHeight: 88);
        Assert.Equal(FileDialogAccessSide.Left, left.Side);
        Assert.Equal(new FileDialogScreenRect(1180, 100, 1500, 800), left.Bounds);

        var inner = FileDialogAccessPlacement.Calculate(
            new FileDialogScreenRect(100, 100, 1820, 900),
            workArea,
            preferredWidth: 320,
            reservedFooterHeight: 88);
        Assert.Equal(FileDialogAccessSide.Inner, inner.Side);
        Assert.Equal(new FileDialogScreenRect(1500, 100, 1820, 812), inner.Bounds);
    }
}
