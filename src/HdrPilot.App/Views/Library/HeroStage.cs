using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Titelbild der Bibliothek (Showcase Hero, E89): zwei Bildplätze, zwischen denen beim Spielwechsel überblendet wird,
/// langsamer Zoom (Ken Burns), Tiefenwirkung mit der Maus (nur über Transforms, 0,6 s), Lichtstrahlen und Lichtpunkte in
/// den Spielfarben, Abdunklungen für Kopfzeile und Text, Leuchten bei eingeschaltetem HDR und die Elemente des
/// HDR-Moments (Lichtstreifen, Lichtlinie, Blitz, Partikel). Ohne Titelbild zeigt ein Platz ein Ersatzbild aus Farbfeld und
/// dem Namen als Umriss-Muster. Keine Effekte (Blur, Schatten), keine Mischmodi, keine Deckkraft-Masken auf bewegten
/// Ebenen; Dauer-Animationen meldet <see cref="RegisterEffects"/> beim <see cref="MotionController"/> an.
/// </summary>
public sealed class HeroStage : Grid
{
    /// <summary>Überblenden beim Spielwechsel (Deckkraft), Verschieben/Zoomen dauert 1,2 s.</summary>
    public static readonly TimeSpan CrossfadeDuration = TimeSpan.FromSeconds(0.9);

    private static readonly Color Ink = Color.FromRgb(0x04, 0x05, 0x09);
    private static readonly Color WarmWhite = Color.FromRgb(0xFF, 0xF6, 0xE2);
    private static readonly IEasingFunction Settle = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
    private static readonly IEasingFunction Parallax = new QuinticEase { EasingMode = EasingMode.EaseOut };

    private readonly Grid _stage = new() { Margin = new Thickness(-48, -36, -48, -36) };
    private readonly TranslateTransform _stageShift = new();
    private readonly Grid _kenBurns = new() { RenderTransformOrigin = new Point(0.62, 0.42) };
    private readonly ScaleTransform _kbScale = new(1.02, 1.02);
    private readonly TranslateTransform _kbShift = new();
    private readonly HeroSlot[] _slots = [new(), new()];
    private readonly Rectangle _veil = new() { Fill = Frozen(new SolidColorBrush(Color.FromArgb(56, 6, 8, 14))), IsHitTestVisible = false };
    private readonly Canvas _rays = new() { IsHitTestVisible = false, Opacity = 0.55 };
    private readonly TranslateTransform _raysShift = new();
    private readonly Canvas _bokeh = new() { IsHitTestVisible = false };
    private readonly TranslateTransform _bokehShift = new();
    private readonly Grid _bloom = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Rectangle _bloomCore = new();
    private readonly Rectangle _bloomHalo = new();
    private readonly Grid _scrim = new() { IsHitTestVisible = false };
    private readonly Rectangle _scrimTop = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly Rectangle _scrimLeft = new();
    private readonly Rectangle _scrimRight = new();
    private readonly Rectangle _scrimBottom = new() { VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Rectangle _flash = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Canvas _sweep = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Rectangle _sweepBand = new() { Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly Grid _sweepLine = new() { Opacity = 0, Height = SweepLineGlow, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly Rectangle _sweepHalo = new();
    private readonly Rectangle _sweepCore = new() { Height = 3, VerticalAlignment = VerticalAlignment.Center };
    private readonly ParticleLayer _particles = new() { Margin = new Thickness(0, ParticleTop, 0, 0) };
    private readonly List<Rectangle> _rayBars = [];
    private readonly List<Ellipse> _dots = [];

    private int _current;
    private string? _shownKey;
    private ImageSource? _shownImage;
    private LibraryViewMode _view = LibraryViewMode.List;
    private bool _dark = true;
    private bool _hdr;
    private AmbientPalette _palette = AmbientPalette.Resolve(null, new PaletteColors(Colors.Orange, Colors.DeepPink, Colors.Pink), followGame: false, dark: true);
    private Color _accent = Color.FromRgb(0xFF, 0x8A, 0x3D);
    private Color _shine = Color.FromRgb(0xFF, 0x94, 0xB9);
    private Point _light = new(0.62, 0.45);

    /// <summary>Partikel erst unterhalb der Kopfzeile (nie über Titelleiste und Fensterknöpfen).</summary>
    public const double ParticleTop = 96;

    /// <summary>
    /// Höhe der Lichtlinie samt Schein (Prototyp: 3-px-Linie mit box-shadow 22 px): ein Verlauf statt eines Schattens, damit
    /// die Linie wie Licht wirkt und nicht wie eine harte Kante quer durchs Bild.
    /// </summary>
    private const double SweepLineGlow = 44;

    public HeroStage()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;

        _stage.RenderTransform = _stageShift;
        var kb = new TransformGroup();
        kb.Children.Add(_kbScale);
        kb.Children.Add(_kbShift);
        _kenBurns.RenderTransform = kb;
        foreach (var slot in _slots)
        {
            _kenBurns.Children.Add(slot.Root);
        }

        _stage.Children.Add(_kenBurns);
        _stage.Children.Add(_veil);
        Children.Add(_stage);

        BuildRays();
        BuildBokeh();
        _rays.RenderTransform = _raysShift;
        _bokeh.RenderTransform = _bokehShift;
        Children.Add(_rays);
        Children.Add(_bokeh);

        _bloom.Children.Add(_bloomHalo);
        _bloom.Children.Add(_bloomCore);
        Children.Add(_bloom);

        _scrimRight.HorizontalAlignment = HorizontalAlignment.Stretch;
        _scrim.Children.Add(_scrimLeft);
        _scrim.Children.Add(_scrimRight);
        _scrim.Children.Add(_scrimTop);
        _scrim.Children.Add(_scrimBottom);
        Children.Add(_scrim);
        Children.Add(_flash);

        _sweepBand.RenderTransform = new TranslateTransform();
        _sweepBand.LayoutTransform = new SkewTransform(-20, 0);
        _sweepLine.RenderTransform = new ScaleTransform(1, 1);
        _sweepLine.Children.Add(_sweepHalo);
        _sweepLine.Children.Add(_sweepCore);
        _sweep.Children.Add(_sweepBand);
        _sweep.Children.Add(_sweepLine);
        Children.Add(_sweep);
        Children.Add(_particles);

        SizeChanged += (_, _) => Relayout();
        ApplyColors();
        ApplyScrims();
    }

    /// <summary>Schlüssel des gezeigten Spiels (Tests, Bildschirmfotos).</summary>
    public string? ShownKey => _shownKey;

    /// <summary>Gerade gezeigtes Titelbild (null: Ersatzbild).</summary>
    public ImageSource? ShownImage => _shownImage;

    /// <summary>Bildplatz, der gerade sichtbar ist (0 oder 1).</summary>
    public int CurrentSlot => _current;

    public ParticleLayer Particles => _particles;

    /// <summary>Spitzlicht (Mitte von Blitz, Leuchten und Partikeln) in Pixeln des Titelbereichs.</summary>
    public Point Light => new(_light.X * Math.Max(1, ActualWidth), _light.Y);

    /// <summary>
    /// Spiel zeigen: das neue Bild kommt in den freien Platz und wird eingeblendet (0,9 s, dazu 70 px Weg und Zoom 1,04 → 1,
    /// 1,2 s), der alte gleitet hinaus. <paramref name="transition"/> 0: sofort. Gleiches Spiel mit neuem Bild (etwa nach
    /// „Bild wählen…“): im selben Ablauf.
    /// </summary>
    public void Show(HeroViewModel? hero, GameArtwork? artwork, TimeSpan transition)
    {
        var image = artwork?.Hero?.Image;
        var key = hero?.Key;
        var shownFallback = _slots[_current].Generated is not null;
        if (string.Equals(key, _shownKey, StringComparison.Ordinal) && ReferenceEquals(image, _shownImage) && (image is not null || shownFallback || hero is null))
        {
            // Schon zu sehen (dasselbe Bild oder dasselbe Ersatzbild); nur die helle Fassung kann nachkommen (HDR an, Moment).
            _slots[_current].SetBoost(artwork?.HeroBoost?.Image, ActualWidth + 96, ActualHeight + 72, _view);
            return;
        }

        _shownKey = key;
        _shownImage = image;
        var next = _slots[1 - _current];
        var previous = _slots[_current];
        next.Fill(hero, artwork, ActualHeight, _view);
        next.Layout(ActualWidth + 96, ActualHeight + 72, _view);
        _current = 1 - _current;

        if (transition <= TimeSpan.Zero)
        {
            next.Root.BeginAnimation(OpacityProperty, null);
            next.Shift.BeginAnimation(TranslateTransform.XProperty, null);
            next.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            next.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            previous.Root.BeginAnimation(OpacityProperty, null);
            next.Root.Opacity = 1;
            next.Shift.X = 0;
            next.Zoom.ScaleX = next.Zoom.ScaleY = 1;
            previous.Root.Opacity = 0;
            previous.Clear();
            Panel.SetZIndex(next.Root, 1);
            Panel.SetZIndex(previous.Root, 0);
            SetBoost(next, _hdr, TimeSpan.Zero);
            return;
        }

        Panel.SetZIndex(next.Root, 1);
        Panel.SetZIndex(previous.Root, 0);
        var fade = transition < CrossfadeDuration ? transition : CrossfadeDuration;
        var move = transition < CrossfadeDuration ? transition : TimeSpan.FromSeconds(1.2);
        var full = transition >= CrossfadeDuration;
        next.Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, fade));
        if (full)
        {
            next.Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(70, 0, move) { EasingFunction = Settle });
            var zoom = new DoubleAnimation(1.04, 1, move) { EasingFunction = Settle };
            next.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            next.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
            previous.Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-70, move) { EasingFunction = Settle });
        }

        var outFade = new DoubleAnimation(0, fade);
        outFade.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_slots[_current], previous))
            {
                previous.Clear(); // Bild freigeben, sobald es ausgeblendet ist
            }
        };
        previous.Root.BeginAnimation(OpacityProperty, outFade);
        SetBoost(next, _hdr, TimeSpan.Zero);
    }

    /// <summary>Ansicht: Bildausschnitt (Liste/Raster 50 % 28 %, Regal 50 % 35 %) und Abdunklung.</summary>
    public void SetView(LibraryViewMode view)
    {
        if (_view == view)
        {
            return;
        }

        _view = view;
        ApplyScrims();
        Relayout();
    }

    /// <summary>Thema und Umgebungsfarben (Strahlen, Lichtpunkte, Leuchten, Abdunklung zum Fenstergrund hin).</summary>
    public void SetPalette(AmbientPalette palette, PaletteColors scheme, bool dark)
    {
        _palette = palette;
        _accent = scheme.Accent;
        _shine = scheme.Shine;
        var themeChanged = _dark != dark;
        _dark = dark;
        ApplyColors();
        ApplyScrims();
        if (themeChanged)
        {
            Relayout();
        }
    }

    /// <summary>Lage des Spitzlichts (Pixel im Titelbereich), Stärke nach gemessener Helligkeit (0–1).</summary>
    public void SetLight(double x, double y, double highlight)
    {
        var w = Math.Max(1, ActualWidth);
        var h = Math.Max(1, ActualHeight);
        _light = new Point(Math.Clamp(x / w, 0, 1), y);
        var center = new Point(_light.X, Math.Clamp(y / h, 0, 1));
        var core = Math.Clamp((highlight * 0.85) - 0.05, 0.14, 0.7);
        var halo = Math.Clamp(0.18 + (highlight * 0.32), 0.18, 0.46);
        _bloomCore.Fill = Frozen(Radial(center, 0.14, 0.34, [(Color.FromArgb((byte)(core * 255), WarmWhite.R, WarmWhite.G, WarmWhite.B), 0), (Color.FromArgb(0, WarmWhite.R, WarmWhite.G, WarmWhite.B), 0.7)]));
        _bloomHalo.Fill = Frozen(Radial(center, 0.40, 0.70, [(AmbientPalette.Alpha(_palette.A3, halo), 0), (AmbientPalette.Alpha(_palette.A3, 0), 0.72)]));
        var flash = ColorMath.Mix(_palette.A3, Colors.White, 0.5);
        _flash.Fill = Frozen(Radial(center, 0.44, 0.80, [(AmbientPalette.Alpha(flash, 0.78), 0), (AmbientPalette.Alpha(flash, 0), 0.7)]));
        Canvas.SetTop(_sweepLine, y - (SweepLineGlow / 2));
    }

    /// <summary>HDR an: helle Fassung und Leuchten einblenden, Schleier und gedämpfte Strahlen weg (Übergang nach Stufe).</summary>
    public void SetHdr(bool on, TimeSpan transition)
    {
        _hdr = on;
        Animate(_veil, OpacityProperty, on ? 0 : 1, Scale(transition, 1.1));
        Animate(_bloom, OpacityProperty, on ? 1 : 0, Scale(transition, 1.2));
        Animate(_rays, OpacityProperty, on ? 1 : 0.55, Scale(transition, 0.8));
        SetBoost(_slots[_current], on, Scale(transition, 1.2));
    }

    /// <summary>Tiefenwirkung: Zeiger relativ zur Mitte (−1…1); Bild −14/−9 px, Strahlen −24/−12, Lichtpunkte −40/−22 (0,6 s).</summary>
    public void SetPointer(double px, double py, TimeSpan duration)
    {
        px = Math.Clamp(px, -1, 1);
        py = Math.Clamp(py, -1, 1);
        Animate(_stageShift, TranslateTransform.XProperty, -14 * px, duration, Parallax);
        Animate(_stageShift, TranslateTransform.YProperty, -9 * py, duration, Parallax);
        Animate(_raysShift, TranslateTransform.XProperty, -24 * px, duration, Parallax);
        Animate(_raysShift, TranslateTransform.YProperty, -12 * py, duration, Parallax);
        Animate(_bokehShift, TranslateTransform.XProperty, -40 * px, duration, Parallax);
        Animate(_bokehShift, TranslateTransform.YProperty, -22 * py, duration, Parallax);
    }

    /// <summary>Dauer-Effekte anmelden: langsamer Zoom (28 s), Lichtstrahlen (14 s), Lichtpunkte (11–19 s).</summary>
    public void RegisterEffects(MotionController controller)
    {
        controller.Register("KenBurns", this, CreateKenBurns);
        controller.Register("Rays", this, CreateRays);
        controller.Register("Bokeh", this, CreateBokeh);
    }

    /// <summary>Elemente des HDR-Moments (der leuchtende Rand gehört dem Fenster; die helle Fassung erst beim Abspielen).</summary>
    public HdrMomentParts MomentParts(FrameworkElement edgeGlow) =>
        new(this, _sweepBand, _sweepLine, _flash, edgeGlow, _particles, CurrentBoostLayer);

    /// <summary>Ebene der hellen Fassung im sichtbaren Bildplatz (null, solange der Platz nichts zeigt).</summary>
    public FrameworkElement? CurrentBoostLayer() => _slots[_current].Boost.Background is null ? null : _slots[_current].Boost;

    /// <summary>Gerade gezeigte helle Fassung (null: keine, dann nur warmes Licht über dem Bild).</summary>
    public ImageSource? ShownBoost => _slots[_current].BoostImage;

    /// <summary>Lage und Farben für den nächsten HDR-Moment.</summary>
    public HdrMomentScene MomentScene(IReadOnlyList<Color> particleColors)
    {
        var w = Math.Max(1, ActualWidth);
        var h = Math.Max(1, ActualHeight - ParticleTop);
        return new HdrMomentScene(w, h, _light.X * w, _light.Y - ParticleTop, Math.Max(80, w * 0.38), particleColors);
    }

    private static TimeSpan Scale(TimeSpan transition, double seconds) =>
        transition <= TimeSpan.Zero ? TimeSpan.Zero : transition < TimeSpan.FromSeconds(seconds) ? transition : TimeSpan.FromSeconds(seconds);

    private static void Animate(IAnimatable target, DependencyProperty property, double to, TimeSpan duration, IEasingFunction? ease = null)
    {
        if (duration <= TimeSpan.Zero)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, to);
            return;
        }

        target.BeginAnimation(property, new DoubleAnimation(to, duration) { EasingFunction = ease ?? new SineEase { EasingMode = EasingMode.EaseInOut } }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void SetBoost(HeroSlot slot, bool on, TimeSpan duration) => Animate(slot.Boost, OpacityProperty, on && slot.Boost.Background is not null ? 1 : 0, duration);

    private void Relayout()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        foreach (var slot in _slots)
        {
            slot.Layout(w + 96, h + 72, _view);
        }

        // Lichtstrahlen: 160 px breit, über die ganze Höhe, bei 28 %, 47 % und 66 % der Breite.
        double[] at = [0.28, 0.47, 0.66];
        for (var i = 0; i < _rayBars.Count; i++)
        {
            Canvas.SetLeft(_rayBars[i], w * at[i]);
            Canvas.SetTop(_rayBars[i], -160);
            _rayBars[i].Height = Math.Max(1100, h + 400);
        }

        for (var i = 0; i < _dots.Count; i++)
        {
            Canvas.SetLeft(_dots[i], w * (0.18 + (((i * 37) % 60) / 100.0)));
            Canvas.SetTop(_dots[i], h * (0.30 + (((i * 23) % 55) / 100.0)));
        }

        _sweepBand.Width = Math.Max(80, w * 0.38);
        _sweepBand.Height = h * 1.6;
        Canvas.SetTop(_sweepBand, -0.3 * h);
        Canvas.SetLeft(_sweepBand, 0);
        _sweepLine.Width = w * 1.2;
        Canvas.SetLeft(_sweepLine, -0.1 * w);
        _sweep.Clip = new RectangleGeometry(new Rect(0, 36, w, Math.Max(0, h - 36)));
        _scrimTop.Height = Math.Min(h, _view == LibraryViewMode.Shelf ? 220 : 150);
        _scrimBottom.Height = Math.Min(h, _view == LibraryViewMode.Shelf ? h * 0.46 : 72);
    }

    private void ApplyColors()
    {
        var a3 = _palette.A3;
        foreach (var bar in _rayBars)
        {
            bar.Fill = Frozen(new LinearGradientBrush(
                [new GradientStop(AmbientPalette.Alpha(a3, 0), 0), new GradientStop(AmbientPalette.Alpha(a3, 0.32), 0.5), new GradientStop(AmbientPalette.Alpha(a3, 0), 1)],
                new Point(0, 0.5),
                new Point(1, 0.5)));
        }

        var dotBrush = Frozen(new RadialGradientBrush(
        [
            new GradientStop(ColorMath.Mix(a3, Colors.White, 0.2), 0),
            new GradientStop(AmbientPalette.Alpha(a3, 0.3), 0.45),
            new GradientStop(AmbientPalette.Alpha(a3, 0), 0.7),
        ]));
        foreach (var dot in _dots)
        {
            dot.Fill = dotBrush;
        }

        var gl = ColorMath.Mix(ColorMath.Mix(_shine, Colors.White, 0.4), Colors.White, 0.08);
        _sweepBand.Fill = Frozen(new LinearGradientBrush(
        [
            new GradientStop(AmbientPalette.Alpha(a3, 0), 0),
            new GradientStop(AmbientPalette.Alpha(a3, 0.4), 0.26),
            new GradientStop(AmbientPalette.Alpha(gl, 0.92), 0.5),
            new GradientStop(AmbientPalette.Alpha(_accent, 0.38), 0.74),
            new GradientStop(AmbientPalette.Alpha(_accent, 0), 1),
        ], new Point(0, 0.5), new Point(1, 0.5)));
        _sweepCore.Fill = Frozen(new LinearGradientBrush(
        [
            new GradientStop(AmbientPalette.Alpha(a3, 0), 0),
            new GradientStop(a3, 0.22),
            new GradientStop(Colors.White, 0.5),
            new GradientStop(_accent, 0.78),
            new GradientStop(AmbientPalette.Alpha(_accent, 0), 1),
        ], new Point(0, 0.5), new Point(1, 0.5)));

        // Schein um die Linie: flache Ellipse, in der Mitte am hellsten, zu den Enden und nach oben und unten hin weich.
        _sweepHalo.Fill = Frozen(new RadialGradientBrush(
        [
            new GradientStop(AmbientPalette.Alpha(ColorMath.Mix(a3, Colors.White, 0.25), 0.55), 0),
            new GradientStop(AmbientPalette.Alpha(a3, 0.22), 0.45),
            new GradientStop(AmbientPalette.Alpha(a3, 0), 1),
        ]));
        SetLight(_light.X * Math.Max(1, ActualWidth), _light.Y, _palette.Highlight);
    }

    /// <summary>Abdunklung (Prototyp .scrim): oben für die Kopfzeile, links für den Text, unten zum Inhalt, rechts leicht.</summary>
    private void ApplyScrims()
    {
        var bg = _palette.Background;
        var shelf = _view == LibraryViewMode.Shelf;
        _scrimTop.Fill = Frozen(Vertical(
            (AmbientPalette.Alpha(Ink, _dark ? (shelf ? 0.78 : 0.74) : 0.7), 0),
            (AmbientPalette.Alpha(Ink, _dark ? 0.2 : (shelf ? 0.15 : 0.18)), shelf ? 150.0 / 220 : 104.0 / 150),
            (AmbientPalette.Alpha(Ink, 0), 1)));
        if (_dark)
        {
            _scrimLeft.Fill = Frozen(Horizontal(
                (bg, 0),
                (AmbientPalette.Alpha(bg, shelf ? 0.82 : 0.78), shelf ? 0.20 : 0.18),
                (AmbientPalette.Alpha(bg, shelf ? 0.25 : 0.20), 0.46),
                (AmbientPalette.Alpha(bg, 0), 0.62)));
            _scrimBottom.Fill = Frozen(Vertical(
                (AmbientPalette.Alpha(bg, 0), 0),
                (AmbientPalette.Alpha(bg, shelf ? 0.6 : 0.55), shelf ? 0.6 : 50.0 / 72),
                (bg, 1)));
        }
        else
        {
            _scrimLeft.Fill = Frozen(Horizontal(
                (AmbientPalette.Alpha(Ink, shelf ? 0.82 : 0.84), 0),
                (AmbientPalette.Alpha(Ink, shelf ? 0.45 : 0.6), 0.30),
                (AmbientPalette.Alpha(Ink, shelf ? 0.1 : 0.24), shelf ? 0.55 : 0.50),
                (AmbientPalette.Alpha(Ink, 0), 0.66)));
            _scrimBottom.Fill = Frozen(Vertical((AmbientPalette.Alpha(bg, 0), shelf ? 0.9 : 0.66), (bg, 1)));
        }

        _scrimRight.Fill = Frozen(Horizontal((AmbientPalette.Alpha(bg, 0), 0.70), (AmbientPalette.Alpha(bg, (shelf ? 0.45 : 1) * 0.55), 1)));
        Relayout();
    }

    private void BuildRays()
    {
        for (var i = 0; i < 3; i++)
        {
            var bar = new Rectangle { Width = 160, Height = 1100, Opacity = 0, RenderTransformOrigin = new Point(0, 0) };
            var group = new TransformGroup();
            group.Children.Add(new SkewTransform(-24, 0));
            group.Children.Add(new TranslateTransform());
            bar.RenderTransform = group;
            _rayBars.Add(bar);
            _rays.Children.Add(bar);
        }
    }

    private void BuildBokeh()
    {
        double[] sizes = [4, 10, 22];
        for (var i = 0; i < 16; i++)
        {
            var size = sizes[i % 3];
            var dot = new Ellipse { Width = size, Height = size, Opacity = 0, RenderTransform = new TranslateTransform() };
            _dots.Add(dot);
            _bokeh.Children.Add(dot);
        }
    }

    private Storyboard CreateKenBurns()
    {
        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var duration = new Duration(TimeSpan.FromSeconds(28));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        AddTo(board, new DoubleAnimation(1.02, 1.12, duration) { EasingFunction = ease }, _kbScale, ScaleTransform.ScaleXProperty);
        AddTo(board, new DoubleAnimation(1.02, 1.12, duration) { EasingFunction = ease }, _kbScale, ScaleTransform.ScaleYProperty);
        AddTo(board, new DoubleAnimation(0, -0.016 * Math.Max(1, ActualWidth + 96), duration) { EasingFunction = ease }, _kbShift, TranslateTransform.XProperty);
        AddTo(board, new DoubleAnimation(0, -0.012 * Math.Max(1, ActualHeight + 72), duration) { EasingFunction = ease }, _kbShift, TranslateTransform.YProperty);
        return board;
    }

    private Storyboard CreateRays()
    {
        var board = new Storyboard();
        double[] offsets = [0, 4.5, 9];
        for (var i = 0; i < _rayBars.Count; i++)
        {
            var cycle = TimeSpan.FromSeconds(14);
            var opacity = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = cycle, BeginTime = TimeSpan.FromSeconds(-offsets[i]) };
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.75, KeyTime.FromPercent(0.35), new SineEase()));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromPercent(0.65)));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase()));
            AddTo(board, opacity, _rayBars[i], OpacityProperty);
            var move = new DoubleAnimation(0, 140, cycle) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-offsets[i]), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            AddTo(board, move, ((TransformGroup)_rayBars[i].RenderTransform).Children[1], TranslateTransform.XProperty);
        }

        return board;
    }

    private Storyboard CreateBokeh()
    {
        var board = new Storyboard();
        double[] peak = [0.95, 0.6, 0.35];
        for (var i = 0; i < _dots.Count; i++)
        {
            var cycle = TimeSpan.FromSeconds(11 + ((i % 5) * 2));
            var begin = TimeSpan.FromSeconds(-(i * 1.7));
            var opacity = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = cycle, BeginTime = begin };
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(peak[i % 3], KeyTime.FromPercent(0.25), new SineEase()));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(peak[i % 3], KeyTime.FromPercent(0.70)));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase()));
            AddTo(board, opacity, _dots[i], OpacityProperty);
            var dx = (i % 2 == 1 ? -1 : 1) * (20 + ((i * 7) % 40));
            var shift = (TranslateTransform)_dots[i].RenderTransform;
            AddTo(board, new DoubleAnimation(0, dx, cycle) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin }, shift, TranslateTransform.XProperty);
            AddTo(board, new DoubleAnimation(0, -160, cycle) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin }, shift, TranslateTransform.YProperty);
        }

        return board;
    }

    private static void AddTo(Storyboard board, Timeline animation, DependencyObject target, DependencyProperty property)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        board.Children.Add(animation);
    }

    private static LinearGradientBrush Vertical(params (Color Color, double Offset)[] stops) =>
        new(new GradientStopCollection(stops.Select(s => new GradientStop(s.Color, s.Offset))), new Point(0.5, 0), new Point(0.5, 1));

    private static LinearGradientBrush Horizontal(params (Color Color, double Offset)[] stops) =>
        new(new GradientStopCollection(stops.Select(s => new GradientStop(s.Color, s.Offset))), new Point(0, 0.5), new Point(1, 0.5));

    private static RadialGradientBrush Radial(Point center, double radiusX, double radiusY, (Color Color, double Offset)[] stops) =>
        new(new GradientStopCollection(stops.Select(s => new GradientStop(s.Color, s.Offset))))
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = radiusX,
            RadiusY = radiusY,
        };

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>Ein Bildplatz: Titelbild, helle Fassung darüber, oder das Ersatzbild.</summary>
    private sealed class HeroSlot
    {
        public HeroSlot()
        {
            var group = new TransformGroup();
            group.Children.Add(Zoom);
            group.Children.Add(Shift);
            Root.RenderTransform = group;
            Root.RenderTransformOrigin = new Point(0.5, 0.5);
            Root.Children.Add(Art);
            Root.Children.Add(Boost);
            RenderOptions.SetBitmapScalingMode(Art, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(Boost, BitmapScalingMode.HighQuality);
        }

        public Grid Root { get; } = new() { Opacity = 0, IsHitTestVisible = false };

        public Border Art { get; } = new();

        public Border Boost { get; } = new() { Opacity = 0 };

        /// <summary>
        /// Ohne vorab berechnete helle Fassung (Bilddienst): warmes Licht über dem Bild, am Spitzlicht am stärksten. WPF hat
        /// keine Helligkeitsfilter; so wirkt das Bild bei HDR an trotzdem heller (Prototyp: brightness 1,18).
        /// </summary>
        private static Brush Lift { get; } = Frozen(new RadialGradientBrush(
            [
                new GradientStop(Color.FromArgb(0x47, 0xFF, 0xF6, 0xE2), 0),
                new GradientStop(Color.FromArgb(0x1F, 0xFF, 0xF6, 0xE2), 0.5),
                new GradientStop(Color.FromArgb(0x0A, 0xFF, 0xF6, 0xE2), 1),
            ])
        {
            Center = new Point(0.6, 0.38),
            GradientOrigin = new Point(0.6, 0.38),
            RadiusX = 0.75,
            RadiusY = 0.9,
        });

        public ScaleTransform Zoom { get; } = new(1, 1);

        public TranslateTransform Shift { get; } = new();

        public Grid? Generated { get; private set; }

        /// <summary>Vorab berechnete helle Fassung in diesem Platz (null: warmes Licht statt ihrer).</summary>
        public ImageSource? BoostImage { get; private set; }

        private ImageSource? Image { get; set; }

        public void Fill(HeroViewModel? hero, GameArtwork? artwork, double height, LibraryViewMode view)
        {
            Clear();
            Image = artwork?.Hero?.Image;
            if (Image is not null)
            {
                Art.Background = new ImageBrush(Image) { Stretch = Stretch.Fill };
                BoostImage = artwork?.HeroBoost?.Image;
                Boost.Background = BoostImage is { } boost ? new ImageBrush(boost) { Stretch = Stretch.Fill } : Lift;
                return;
            }

            if (hero is null)
            {
                return;
            }

            // Ersatzbild (Prototyp genBanner): Farbfeld, Abdunklung, Name als Umriss-Muster in zwei Reihen.
            Generated = new Grid { ClipToBounds = true };
            var field = artwork?.Field?.Image is { } fieldImage
                ? (Brush)new ImageBrush(fieldImage) { Stretch = Stretch.Fill }
                : LibraryArt.Field(hero.Ambient);
            var fieldLayer = new Border { Background = field, Margin = new Thickness(-24) };
            RenderOptions.SetBitmapScalingMode(fieldLayer, BitmapScalingMode.Linear);
            Generated.Children.Add(fieldLayer);
            Generated.Children.Add(new Border
            {
                Background = Frozen(new RadialGradientBrush(
                    [new GradientStop(Color.FromArgb(0, 5, 6, 10), 0), new GradientStop(Color.FromArgb(107, 5, 6, 10), 0.75)])
                {
                    Center = new Point(0.74, 0.4),
                    GradientOrigin = new Point(0.74, 0.4),
                    RadiusX = 0.6,
                    RadiusY = 1.1,
                }),
            });
            Generated.Children.Add(new Border
            {
                Background = Frozen(new LinearGradientBrush(
                    [new GradientStop(Color.FromArgb(128, 5, 6, 10), 0), new GradientStop(Color.FromArgb(31, 5, 6, 10), 0.55), new GradientStop(Color.FromArgb(0, 5, 6, 10), 1)],
                    new Point(0, 0.5),
                    new Point(1, 0.5))),
            });
            Generated.Children.Add(Pattern(hero.Name, height, view));
            Root.Children.Insert(0, Generated);
            Boost.Background = Lift;
        }

        /// <summary>
        /// Helle Fassung nachträglich einsetzen (kommt erst, wenn HDR an ist oder der Moment spielt); die Deckkraft der Ebene
        /// bleibt, wie Übergang oder Moment sie gerade führen. Ohne Titelbild (Ersatzbild) oder ohne neue Fassung: nichts.
        /// </summary>
        public void SetBoost(ImageSource? boost, double width, double height, LibraryViewMode view)
        {
            if (boost is null || Image is null || ReferenceEquals(boost, BoostImage))
            {
                return;
            }

            BoostImage = boost;
            Boost.Background = new ImageBrush(boost) { Stretch = Stretch.Fill };
            Layout(width, height, view);
        }

        public void Clear()
        {
            Art.Background = null;
            Boost.Background = null;
            BoostImage = null;
            Image = null;
            if (Generated is not null)
            {
                Root.Children.Remove(Generated);
                Generated = null;
            }
        }

        /// <summary>Bildausschnitt wie object-fit: cover, waagerecht mittig, senkrecht bei 28 % (Regal 35 %).</summary>
        public void Layout(double width, double height, LibraryViewMode view)
        {
            if (Image is BitmapSource bitmap && Art.Background is ImageBrush brush)
            {
                var crop = CoverCrop.Compute(bitmap.PixelWidth, bitmap.PixelHeight, width, height, 0.5, view == LibraryViewMode.Shelf ? 0.35 : 0.28);
                brush.Viewbox = crop;
                if (Boost.Background is ImageBrush boost)
                {
                    boost.Viewbox = crop;
                }
            }
        }

        /// <summary>Name in Großbuchstaben als Umriss (FormattedText → Geometrie), gedreht, links ausgeblendet.</summary>
        private static FrameworkElement Pattern(string name, double height, LibraryViewMode view)
        {
            var size = Math.Clamp(height * (view == LibraryViewMode.Shelf ? 0.3 : 0.52), 110, 190);
            var typeface = new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var text = (name ?? string.Empty).ToUpper(CultureInfo.CurrentCulture);
            var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
            for (var row = 0; row < 2; row++)
            {
                var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, 1.0);
                var geometry = formatted.BuildGeometry(new Point(row == 0 ? 0 : -1.6 * size, row * size * 0.86));
                geometry.Freeze();
                var alpha = row == 0 ? 26 : 15;
                var stroke = Frozen(new LinearGradientBrush(
                    [new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb((byte)alpha, 255, 255, 255), 0.3), new GradientStop(Color.FromArgb((byte)alpha, 255, 255, 255), 1)],
                    new Point(0, 0.5),
                    new Point(1, 0.5)));
                canvas.Children.Add(new Path { Data = geometry, Stroke = stroke, StrokeThickness = 1.5 });
            }

            var host = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            var wrapper = new Canvas { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            wrapper.SizeChanged += (_, e) =>
            {
                Canvas.SetLeft(canvas, e.NewSize.Width * 0.3);
                Canvas.SetTop(canvas, (e.NewSize.Height / 2) - (size * 0.9));
            };
            canvas.RenderTransform = new RotateTransform(-6);
            wrapper.Children.Add(canvas);
            host.Children.Add(wrapper);
            return host;
        }
    }
}
