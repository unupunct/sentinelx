using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SentinelX.App.Controls;

/// <summary>
/// Animated 0-100 circular arc gauge. Score changes animate through an internal
/// AnimatedScore property (WPF can't directly animate a Geometry, so the arc is recomputed
/// and redrawn on every animation tick instead).
/// </summary>
public partial class ScoreGaugeControl : UserControl
{
    private const double Radius = 90;
    private const double CenterX = 100;
    private const double CenterY = 100;

    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score), typeof(int), typeof(ScoreGaugeControl), new PropertyMetadata(0, OnScoreChanged));

    private static readonly DependencyProperty AnimatedScoreProperty = DependencyProperty.Register(
        nameof(AnimatedScore), typeof(double), typeof(ScoreGaugeControl),
        new PropertyMetadata(0.0, OnAnimatedScoreChanged));

    public ScoreGaugeControl()
    {
        InitializeComponent();
        Redraw(0);
    }

    public int Score
    {
        get => (int)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    private double AnimatedScore
    {
        get => (double)GetValue(AnimatedScoreProperty);
        set => SetValue(AnimatedScoreProperty, value);
    }

    private static void OnScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScoreGaugeControl)d;
        var animation = new DoubleAnimation
        {
            To = (int)e.NewValue,
            Duration = TimeSpan.FromMilliseconds(700),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        control.BeginAnimation(AnimatedScoreProperty, animation);
    }

    private static void OnAnimatedScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ScoreGaugeControl)d).Redraw((double)e.NewValue);
    }

    private void Redraw(double score)
    {
        ScoreText.Text = ((int)Math.Round(score)).ToString();
        ArcPath.Data = BuildArcGeometry(score);
        ArcPath.Stroke = BuildScoreBrush(score);
    }

    private static Geometry BuildArcGeometry(double score)
    {
        // Clamp away from the exact 0/360 degenerate cases where ArcSegment can't render.
        var clamped = Math.Clamp(score, 0.1, 99.9);
        var sweepAngle = clamped / 100.0 * 360.0;
        var center = new Point(CenterX, CenterY);
        var startPoint = PointOnCircle(center, Radius, -90);
        var endPoint = PointOnCircle(center, Radius, -90 + sweepAngle);
        var isLargeArc = sweepAngle > 180;

        var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            endPoint, new Size(Radius, Radius), 0, isLargeArc, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private Brush BuildScoreBrush(double score)
    {
        var key = score switch
        {
            >= 80 => "AccentGreenBrush",
            >= 50 => "AccentOrangeBrush",
            _ => "AccentRedBrush"
        };

        return (Brush)FindResource(key);
    }
}
