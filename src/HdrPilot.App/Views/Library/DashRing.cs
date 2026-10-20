using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Gemeinsamer Takt des Auswahlrings (Prototyp „cm“ 2,6 s): ein einziger animierter Wert je Fenster, an den nur die
/// gewählte Kachel bindet. Läuft als Dauer-Effekt über den MotionController (höchstens 30 Bilder pro Sekunde, angehalten
/// im Hintergrund, entfernt beim Schließen).
/// </summary>
public sealed class RingClock : Animatable
{
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(RingClock), new PropertyMetadata(0.0));

    /// <summary>0–1: Lage des Strichs auf dem Umfang.</summary>
    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Ein Umlauf in 2,6 s, endlos.</summary>
    public Storyboard CreateStoryboard()
    {
        var board = new Storyboard();
        var spin = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(2.6)) { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(spin, this);
        Storyboard.SetTargetProperty(spin, new PropertyPath(PhaseProperty));
        board.Children.Add(spin);
        return board;
    }

    protected override Freezable CreateInstanceCore() => new RingClock();
}

/// <summary>
/// Gestrichelter Ring um die gewählte Kachel (Prototyp .rg): abgerundetes Rechteck mit einem Strich von 24 % des Umfangs
/// im Verlauf Akzent → Atmosphäre → Glanz, darunter ein breiter, halb durchsichtiger Lichtsaum. Die Lage kommt aus
/// <see cref="Phase"/>; gezeichnet wird nur, solange der Ring sichtbar ist.
/// </summary>
public sealed class DashRing : FrameworkElement
{
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(DashRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(DashRing), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(DashRing), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public DashRing()
    {
        IsHitTestVisible = false;
    }

    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Umfang eines abgerundeten Rechtecks.</summary>
    public static double Perimeter(double width, double height, double radius)
    {
        var r = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
        return (2 * (width + height)) - ((8 - (2 * Math.PI)) * r);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 4 || h <= 4)
        {
            return;
        }

        var rect = new Rect(1.5, 1.5, w - 3, h - 3);
        var radius = CornerRadius;
        var perimeter = Perimeter(rect.Width, rect.Height, radius);
        Draw(drawingContext, rect, radius, perimeter, 8, 0.3);
        Draw(drawingContext, rect, radius, perimeter, 2.5, 1);
    }

    private void Draw(DrawingContext dc, Rect rect, double radius, double perimeter, double thickness, double opacity)
    {
        var dash = 0.24 * perimeter / thickness;
        var gap = 0.76 * perimeter / thickness;
        var pen = new Pen(Stroke, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            DashStyle = new DashStyle([dash, gap], -Phase * perimeter / thickness),
        };
        pen.Freeze();
        dc.PushOpacity(opacity);
        dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
        dc.Pop();
    }
}
