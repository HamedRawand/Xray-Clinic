using System.Globalization;

namespace NasimXray.Client.Helpers;

// Single place that turns a DateTime into text for the active calendar (AppSession.CalendarType),
// shared by DateToCalendarConverter (XAML) and code-built UI text so they can't drift apart.
public static class CalendarFormat
{
    private static readonly PersianCalendar Pc = new();

    private static readonly string[] MonthNames =
    {
        "", "حمل", "ثور", "جوزا", "سرطان", "اسد", "سنبله",
        "میزان", "عقرب", "قوس", "جدی", "دلو", "حوت",
    };

    // kind: "date" (default) | "datetime" | "datetimesec" | "monthyear"
    public static string Format(DateTime dt, string kind = "date")
    {
        // API timestamps arrive as UTC; show them on the PC's clock so an exam done at 9pm Kabul time
        // isn't displayed as 4:30pm. Plain dates (date-picker values, DOB) are Unspecified and untouched.
        if (dt.Kind == DateTimeKind.Utc) dt = dt.ToLocalTime();

        if (AppSession.CalendarType == CalendarType.Shamsi)
        {
            int y = Pc.GetYear(dt), m = Pc.GetMonth(dt), d = Pc.GetDayOfMonth(dt);
            return kind switch
            {
                "datetime" => $"{y:0000}/{m:00}/{d:00}  {dt:HH:mm}",
                "datetimesec" => $"{y:0000}/{m:00}/{d:00} {dt:HH:mm:ss}",
                "monthyear" => $"{MonthNames[m]} {y}",
                _ => $"{y:0000}/{m:00}/{d:00}",
            };
        }

        return kind switch
        {
            "datetime" => dt.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture),
            "datetimesec" => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "monthyear" => dt.ToString("MMM yyyy", CultureInfo.InvariantCulture),
            _ => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }
}
