using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WitchDrawer.App.Infrastructure;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            bool flag => flag ? Visibility.Collapsed : Visibility.Visible,
            int count => count > 0 ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Visible
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

