using System.Globalization;
using System.Windows.Data;

namespace TalesAlarm.Views.Converters;

public sealed class TimerDisplayConverter : IValueConverter
{
    public static string Format(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(0L, (long)Math.Ceiling(remaining.TotalSeconds));
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is TimeSpan remaining ? Format(remaining) : string.Empty;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
