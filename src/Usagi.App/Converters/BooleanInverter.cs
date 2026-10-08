using System.Globalization;
using System.Windows.Data;

namespace Usagi.App.Converters;

public sealed class BooleanInverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
