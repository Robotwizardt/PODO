using System.Globalization;
using System.Windows;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class InverseBooleanToVisibilityConverterTests
{
    private readonly InverseBooleanToVisibilityConverter _converter = new();

    [Theory]
    [InlineData(0, Visibility.Collapsed)]
    [InlineData(1, Visibility.Visible)]
    [InlineData(8, Visibility.Visible)]
    public void Convert_CountShowsOnlyWhenItemsExist(int count, Visibility expected)
    {
        Assert.Equal(
            expected,
            _converter.Convert(count, typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(false, Visibility.Visible)]
    [InlineData(true, Visibility.Collapsed)]
    public void Convert_BooleanInvertsVisibility(bool value, Visibility expected)
    {
        Assert.Equal(
            expected,
            _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }
}
