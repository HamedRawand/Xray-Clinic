using System.Globalization;
using System.Windows.Data;
using NasimXray.Client.Helpers;

namespace NasimXray.Client.Converters;

[ValueConversion(typeof(DateTime), typeof(string))]
public sealed class DateToCalendarConverter : IValueConverter, IMultiValueConverter
{
    // ConverterParameter: null/"date" | "datetime" | "datetimesec" | "monthyear"
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        DateTime dt = value switch
        {
            DateTime d => d,
            DateTimeOffset o => o.LocalDateTime,
            _ => DateTime.MinValue,
        };
        if (dt == DateTime.MinValue) return string.Empty;

        return CalendarFormat.Format(dt, (parameter as string ?? "date").ToLowerInvariant());
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    // values[0] = DateTime? (the date), values[1] = IsShamsi (change trigger only, so the text
    // re-renders live when the user switches calendars).
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        DateTime? dt = values[0] is DateTime d ? d : null;
        return dt.HasValue ? Convert((object)dt.Value, targetType, parameter, culture) : string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
