using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SentinelX.Core.Models;

namespace SentinelX.App.Converters;

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is Severity severity
            ? severity switch
            {
                Severity.Critical => "AccentRedBrush",
                Severity.Warning => "AccentOrangeBrush",
                _ => "AccentGreenBrush"
            }
            : "FgSecondaryBrush";

        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
