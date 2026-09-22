using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NasimXray.Client.Converters;

public class InvoiceStatusToLocalizedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        var key = $"InvoiceStatus.{value}";
        return Application.Current.TryFindResource(key) as string ?? value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
