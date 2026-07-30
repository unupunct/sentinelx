using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SentinelX.App.Converters;

/// <summary>
/// Maps a bool to one of two named theme brushes, given as "TrueKey|FalseKey" in
/// ConverterParameter (e.g. "AccentGreenBrush|FgSecondaryBrush"). Defaults to
/// AccentGreenBrush/FgSecondaryBrush when no parameter is supplied.
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isTrue = value is bool b && b;
        var keys = (parameter as string)?.Split('|') ?? new[] { "AccentGreenBrush", "FgSecondaryBrush" };
        var key = isTrue ? keys[0] : keys.Length > 1 ? keys[1] : keys[0];
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
