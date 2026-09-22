using System.Globalization;
using System.Windows.Data;
using NasimXray.Client.Helpers;

namespace NasimXray.Client.Converters;

[ValueConversion(typeof(DateTime), typeof(string))]
public sealed class DateToCalendarConverter : IValueConverter, IMultiValueConverter
{
    private static readonly PersianCalendar _pc = new();

    private static readonly string[] _monthNames =
    {
        "", "حمل", "ثور", "جوزا", "سرطان", "اسد", "سنبله",
        "میزان", "عقرب", "قوس", "جدی", "دلو", "حوت",
    };

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

        var param = (parameter as string ?? "date").ToLowerInvariant();

        if (AppSession.CalendarType == CalendarType.Shamsi)
        {
            int y = _pc.GetYear(dt), m = _pc.GetMonth(dt), d = _pc.GetDayOfMonth(dt);
            return param switch
            {
                "datetime" => $"{y:0000}/{m:00}/{d:00}  {dt:HH:mm}",
                "datetimesec" => $"{y:0000}/{m:00}/{d:00} {dt:HH:mm:ss}",
                "monthyear" => $"{_monthNames[m]} {y}",
                _ => $"{y:0000}/{m:00}/{d:00}",
            };
        }

        return param switch
        {
            "datetime" => dt.ToString("dd/MM/yyyy  HH:mm"),
            "datetimesec" => dt.ToString("dd/MM/yyyy HH:mm:ss"),
            "monthyear" => dt.ToString("MMM yyyy"),
            _ => dt.ToString("dd/MM/yyyy"),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    // values[0] = DateTime? (SelectedDate), values[1] = IsShamsi (change trigger only)
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        DateTime? dt = values[0] is DateTime d ? d : null;
        return dt.HasValue ? Convert((object)dt.Value, targetType, parameter, culture) : string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
