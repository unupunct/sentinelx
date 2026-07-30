using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SentinelX.App.Controls;

/// <summary>
/// Hand-rolled line chart over a bound value sequence — no charting library needed for a
/// single scrolling series. Redraws whenever the bound collection changes or the control
/// is resized.
/// </summary>
public partial class SparklineControl : UserControl
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable<double>), typeof(SparklineControl),
        new PropertyMetadata(null, OnValuesChanged));

    public SparklineControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SparklineControl)d;

        if (e.OldValue is INotifyCollectionChanged oldNotifying)
        {
            oldNotifying.CollectionChanged -= control.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newNotifying)
        {
            newNotifying.CollectionChanged += control.OnCollectionChanged;
        }

        control.Redraw();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var values = Values?.ToList();
        if (values is null || values.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            Line.Points = new PointCollection();
            return;
        }

        var max = Math.Max(values.Max(), 1);
        var min = Math.Min(values.Min(), 0);
        var range = Math.Max(max - min, 1);

        var points = new PointCollection(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var x = i / (double)(values.Count - 1) * ActualWidth;
            var y = ActualHeight - (values[i] - min) / range * ActualHeight;
            points.Add(new Point(x, y));
        }

        Line.Points = points;
    }
}
