using System.Globalization;
using System.Windows.Data;
using NasimXray.Shared;

namespace NasimXray.Client.Converters;

// decimal / decimal? -> "1,500.00 AFN" (blank for null). One-way display only.
public class MoneyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            decimal d => Currency.Format(d),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
