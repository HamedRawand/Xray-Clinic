using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NasimXray.Client.Converters;

// Maps an ExamOrderStatus or InvoiceStatus value to a StatusBadge colour family.
public class StatusToBadgeKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Pending" => "Warn",
            "InProgress" => "Info",
            "Completed" => "Blue",
            "Reported" => "Ok",
            "Cancelled" => "Neutral",
            "Unpaid" => "Danger",
            "PartiallyPaid" => "Warn",
            "Paid" => "Ok",
            _ => "Neutral",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Visible when the value is null; with ConverterParameter="Invert", visible when it is NOT null.
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter as string == "Invert";
        var visible = (value is null) != invert;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
