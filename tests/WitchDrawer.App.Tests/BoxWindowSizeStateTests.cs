using System.Globalization;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Tests;

public sealed class BoxWindowSizeStateTests
{
    [Fact]
    public void WindowSize_RoundTripsWithInvariantDecimalFormat()
    {
        var size = new BoxWindowSizeState(420.5, 280.25);

        Assert.Equal("420.5,280.25", size.Serialize());
        Assert.Equal(size, BoxWindowSizeState.Parse(size.Serialize()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad,280")]
    [InlineData("NaN,280")]
    [InlineData("420,Infinity")]
    [InlineData("420")]
    public void WindowSize_RejectsInvalidPersistenceValues(string? raw)
    {
        Assert.Null(BoxWindowSizeState.Parse(raw));
    }

    [Fact]
    public void WindowSize_NormalizesBelowMinimumDimensions()
    {
        var size = BoxWindowSizeState.Normalize(12, 8);

        Assert.Equal(BoxWindowSizeState.MinimumWidth, size.Width);
        Assert.Equal(BoxWindowSizeState.MinimumHeight, size.Height);
    }

    [Fact]
    public void WindowSize_UsesInvariantFormatRegardlessOfCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("420.5,280.25", new BoxWindowSizeState(420.5, 280.25).Serialize());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void WindowSizeSettingKey_IsStablePerBox()
    {
        var boxId = Guid.Parse("8d4f1f55-32f9-4de1-a4bb-96c93bf1e8dd");

        Assert.Equal(
            "BoxWindowSize:8d4f1f5532f94de1a4bb96c93bf1e8dd",
            BoxViewModel.GetWindowSizeSettingKey(boxId));
    }
}
