using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class PaperTodoHostTests
{
    [Theory]
    [InlineData("120.5,-45.25", 120.5, -45.25)]
    [InlineData("0,0", 0, 0)]
    public void TryParsePosition_ReadsPersistedDesktopCoordinates(
        string raw,
        double expectedLeft,
        double expectedTop)
    {
        var parsed = PaperTodoHost.TryParsePosition(raw, out var left, out var top);

        Assert.True(parsed);
        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not,a-position")]
    [InlineData("10")]
    [InlineData("NaN,5")]
    public void TryParsePosition_RejectsInvalidCoordinates(string? raw)
    {
        var parsed = PaperTodoHost.TryParsePosition(raw, out _, out _);

        Assert.False(parsed);
    }
}
