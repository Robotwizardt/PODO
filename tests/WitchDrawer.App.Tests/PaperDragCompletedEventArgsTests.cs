using System.Windows;
using PaperTodo;

namespace WitchDrawer.App.Tests;

public sealed class PaperDragCompletedEventArgsTests
{
    [Fact]
    public void PaperDragCompletedEventArgs_PreservesTheCapturedDropPoint()
    {
        var dropPoint = new Point(640, 360);
        var args = new PaperDragCompletedEventArgs(
            "paper-001",
            new Rect(500, 300, 180, 120),
            dropPoint);

        Assert.Equal(dropPoint, args.DropPoint);
        Assert.False(args.IsExplicitProjectUnlinkRequested);
    }

    [Fact]
    public void PaperDragCompletedEventArgs_PreservesExplicitUnlinkRequest()
    {
        var args = new PaperDragCompletedEventArgs(
            "paper-001",
            new Rect(500, 300, 180, 120),
            new Point(640, 360),
            isExplicitProjectUnlinkRequested: true);

        Assert.True(args.IsExplicitProjectUnlinkRequested);
    }
}
