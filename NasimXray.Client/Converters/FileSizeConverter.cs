using System.Globalization;
using System.Windows.Data;

namespace NasimXray.Client.Converters;

public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes) return string.Empty;

        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:0.#} {units[unitIndex]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
