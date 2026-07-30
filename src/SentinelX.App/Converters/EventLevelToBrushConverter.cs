using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SentinelX.Infrastructure.EventLog;

namespace SentinelX.App.Converters;

public sealed class EventLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is EventEntryLevel level
            ? level switch
            {
                EventEntryLevel.Critical => "AccentRedBrush",
                EventEntryLevel.Error => "AccentRedBrush",
                EventEntryLevel.Warning => "AccentOrangeBrush",
                _ => "AccentBlueBrush"
            }
            : "FgSecondaryBrush";

        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
