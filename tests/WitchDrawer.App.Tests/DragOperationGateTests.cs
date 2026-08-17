using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class DragOperationGateTests
{
    [Fact]
    public void TryEnter_BlocksReentryUntilExit()
    {
        var gate = new DragOperationGate();

        Assert.True(gate.TryEnter());
        Assert.True(gate.IsEntered);
        Assert.False(gate.TryEnter());

        gate.Exit();

        Assert.False(gate.IsEntered);
        Assert.True(gate.TryEnter());
    }
}
