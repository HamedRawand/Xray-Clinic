using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NasimXray.Client.Converters;

// Visible when the bound enum value's name is in the comma-separated ConverterParameter
// (e.g. ConverterParameter="Pending,InProgress"), otherwise Collapsed.
public class StatusVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var allowed = (parameter as string ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return value is not null && allowed.Contains(value.ToString()) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
