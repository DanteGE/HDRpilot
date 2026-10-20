using System.Windows;
using System.Windows.Media;

namespace HdrPilot.App.Infrastructure.Effects;

/// <summary>
/// Zeichenfläche der Partikel des HDR-Moments: ein einziges <see cref="DrawingVisual"/>, je Bild neu gezeichnet, solange der
/// Moment läuft (höchstens 2,5 s); danach leer und ohne Bildtakt. Je Farbe ein vorab erzeugter, eingefrorener Lichthof
/// (weiß im Kern, dann die Farbe, außen durchsichtig); die Deckkraft je Partikel kommt über PushOpacity. Keine Effekte,
/// keine Mischmodi.
/// </summary>
public sealed class ParticleLayer : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private Brush[] _brushes = [];

    public ParticleLayer()
    {
        IsHitTestVisible = false;
        AddVisualChild(_visual);
    }

    /// <summary>Anzahl der zuletzt gezeichneten Partikel.</summary>
    public int Drawn { get; private set; }

    /// <summary>Farben des Moments (Spielfarben bzw. Schema); je Farbe ein Lichthof.</summary>
    public void SetColors(IReadOnlyList<Color> colors)
    {
        _brushes = colors.Count == 0 ? [Halo(Colors.White)] : colors.Select(Halo).ToArray();
    }

    public int ColorCount => Math.Max(1, _brushes.Length);

    public void Render(IReadOnlyList<Particle> particles, double fade)
    {
        if (_brushes.Length == 0)
        {
            SetColors([Colors.White]);
        }

        using var dc = _visual.RenderOpen();
        var count = 0;
        foreach (var p in particles)
        {
            var alpha = p.Alpha * fade;
            if (alpha <= 0.01)
            {
                continue;
            }

            var r = p.DrawRadius * 3;
            dc.PushOpacity(alpha);
            dc.DrawEllipse(_brushes[p.Color % _brushes.Length], null, new Point(p.X, p.Y), r, r);
            dc.Pop();
            count++;
        }

        Drawn = count;
    }

    public void Clear()
    {
        using (_visual.RenderOpen())
        {
        }

        Drawn = 0;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => index == 0 ? _visual : throw new ArgumentOutOfRangeException(nameof(index));

    private static Brush Halo(Color color)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Colors.White, 0),
                new GradientStop(color, 0.18),
                new GradientStop(Color.FromArgb(0x66, color.R, color.G, color.B), 0.45),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        brush.Freeze();
        return brush;
    }
}
