using System.Globalization;
using System.Windows.Data;

namespace WitchDrawer.App.Infrastructure;

public sealed class NoteHeadingFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool isHeading && isHeading ? 15d : 11.5d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
