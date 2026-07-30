using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SentinelX.App.Converters;

/// <summary>
/// Visible when the bound count is greater than zero; pass ConverterParameter="Invert" to
/// flip that (useful for "no issues found" placeholders that show only when a list is empty).
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var isVisible = invert ? count == 0 : count > 0;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
