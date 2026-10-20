using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Umgebungslicht hinter der ganzen Bibliothek (Prototyp .ambient): Fenstergrund in den Spielfarben, darüber das weiche
/// Farbfeld des Titelbilds (Steam library_hero_blur bzw. ein winziges Bild, gestreckt mit linearer Skalierung, also
/// ohne Weichzeichner), drei radiale Leuchtflächen und ein Verlauf zum Grund hin. Farbwechsel beim Spielwechsel als
/// einmalige Farbanimation (1,4 s; Stufe „Reduziert“ 0,3 s, „Aus“ sofort). Keine Dauer-Animation.
/// </summary>
public sealed class AmbientBackdrop : Grid
{
    /// <summary>Dauer des Farbwechsels bei „Voll“.</summary>
    public static readonly TimeSpan ColorDuration = TimeSpan.FromSeconds(1.4);

    private readonly SolidColorBrush _background = new(Color.FromRgb(0x12, 0x0B, 0x12));
    private readonly Image[] _fields = [new(), new()];
    private readonly RadialGradientBrush _glowLow = Glow(new Point(0.12, 1.0), 0.70, 0.55);
    private readonly RadialGradientBrush _glowRight = Glow(new Point(0.96, 0.92), 0.60, 0.50);
    private readonly RadialGradientBrush _glowMid = Glow(new Point(0.60, 0.40), 0.50, 0.40);
    private readonly LinearGradientBrush _veil = new()
    {
        StartPoint = new Point(0.5, 0),
        EndPoint = new Point(0.5, 1),
        GradientStops = { new GradientStop(Colors.Transparent, 0.30), new GradientStop(Colors.Transparent, 0.75), new GradientStop(Colors.Transparent, 1) },
    };

    private int _field;
    private bool _dark = true;
    private ImageSource? _fieldImage;

    public AmbientBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Background = _background;
        foreach (var image in _fields)
        {
            image.Stretch = Stretch.Fill;
            image.Opacity = 0;
            image.Margin = new Thickness(-60);
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
            Children.Add(image);
        }

        Children.Add(new Rectangle { Fill = _glowLow });
        Children.Add(new Rectangle { Fill = _glowRight });
        Children.Add(new Rectangle { Fill = _glowMid });
        Children.Add(new Rectangle { Fill = _veil });
    }

    /// <summary>Aktueller Fenstergrund (Tests, Abdunklungen).</summary>
    public Color BackgroundColor => _background.Color;

    /// <summary>Zuletzt gezeigtes Farbfeld (null: keins).</summary>
    public ImageSource? FieldImage => _fieldImage;

    /// <summary>Farben anwenden; <paramref name="field"/> ist das weiche Farbfeld des Spiels (null: keins oder „Hintergrund folgt dem Spiel“ aus).</summary>
    public void Apply(AmbientPalette palette, ImageSource? field, bool dark, TimeSpan transition)
    {
        _dark = dark;
        To(_background, SolidColorBrush.ColorProperty, palette.Background, transition);
        Stops(_glowLow, AmbientPalette.Alpha(palette.A2, 0.34), transition);
        Stops(_glowRight, AmbientPalette.Alpha(palette.A3, 0.22), transition);
        Stops(_glowMid, AmbientPalette.Alpha(palette.A1, 0.30), transition);

        // Verlauf zum Grund: oben frei, unten der Fenstergrund (hell kräftiger, damit Texte ruhig bleiben).
        var bg = palette.Background;
        To(_veil.GradientStops[0], GradientStop.ColorProperty, AmbientPalette.Alpha(bg, 0), transition);
        To(_veil.GradientStops[1], GradientStop.ColorProperty, AmbientPalette.Alpha(bg, dark ? 0.70 : 0.82), transition);
        To(_veil.GradientStops[2], GradientStop.ColorProperty, bg, transition);
        _veil.GradientStops[0].Offset = dark ? 0.30 : 0.25;
        _veil.GradientStops[1].Offset = dark ? 0.75 : 0.62;

        if (!ReferenceEquals(field, _fieldImage))
        {
            _fieldImage = field;
            var next = _fields[1 - _field];
            var current = _fields[_field];
            next.Source = field;
            _field = 1 - _field;
            Fade(next, field is null ? 0 : FieldOpacity, transition);
            Fade(current, 0, transition, () =>
            {
                if (!ReferenceEquals(_fields[_field], current))
                {
                    current.Source = null;
                }
            });
        }
        else if (field is not null)
        {
            Fade(_fields[_field], FieldOpacity, transition);
        }
    }

    private double FieldOpacity => _dark ? 0.5 : 0.36;

    private static RadialGradientBrush Glow(Point center, double radiusX, double radiusY) => new()
    {
        Center = center,
        GradientOrigin = center,
        RadiusX = radiusX,
        RadiusY = radiusY,
        GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0.7) },
    };

    private static void Stops(GradientBrush brush, Color inner, TimeSpan transition)
    {
        To(brush.GradientStops[0], GradientStop.ColorProperty, inner, transition);
        To(brush.GradientStops[1], GradientStop.ColorProperty, AmbientPalette.Alpha(inner, 0), transition);
    }

    private static void To(Animatable target, DependencyProperty property, Color color, TimeSpan transition)
    {
        if (transition <= TimeSpan.Zero)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, color);
            return;
        }

        target.BeginAnimation(property, new ColorAnimation(color, transition) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Fade(UIElement element, double to, TimeSpan transition, Action? done = null)
    {
        if (transition <= TimeSpan.Zero)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            done?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(to, transition);
        if (done is not null)
        {
            animation.Completed += (_, _) => done();
        }

        element.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
