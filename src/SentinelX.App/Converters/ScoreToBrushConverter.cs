using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SentinelX.App.Converters;

/// <summary>
/// Maps a 0-100 health score to the Healthy/Warning/Critical accent brush (green ≥80,
/// orange 50-79, red &lt;50).
/// </summary>
public sealed class ScoreToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var score = value is int i ? i : 0;
        var key = score switch
        {
            >= 80 => "AccentGreenBrush",
            >= 50 => "AccentOrangeBrush",
            _ => "AccentRedBrush"
        };

        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
