using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WitchDrawer.App.Infrastructure;

public sealed class NoteHeadingFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool isHeading && isHeading ? FontWeights.SemiBold : FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
