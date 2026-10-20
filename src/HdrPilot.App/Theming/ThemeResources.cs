using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HdrPilot.App.Theming;

/// <summary>
/// Erzeugt aus einer <see cref="ThemePalette"/> das Ressourcen-Wörterbuch mit allen <see cref="ThemeKeys"/> (E89, G10.1):
/// Farben als <see cref="Color"/>, Pinsel eingefroren, Schriften. Werte und Verläufe wie im Prototyp v4 (app.css .app.dark
/// und .app.light); was dort color-mix ist, ist hier vorab gerechnet. Für den weichen Farbwechsel entsteht auf Wunsch eine
/// Fassung mit nicht eingefrorenen Pinseln, die einmalig von den alten zu den neuen Farben überblendet (keine Bildschleife
/// im Code, nur WPF-Animationen mit Ende).
/// </summary>
public static class ThemeResources
{
    private static readonly FontFamily DisplayFont = new("Bahnschrift, Segoe UI");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");

    /// <summary>Markiert ein erzeugtes Wörterbuch (zum Wiederfinden in Application.Resources).</summary>
    public const string MarkerKey = "Hp.Theme.Generated";

    /// <summary>Alle Schlüssel mit eingefrorenen Werten.</summary>
    public static ResourceDictionary Build(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var d = new ResourceDictionary { [MarkerKey] = palette };
        var p = palette;
        var dark = p.IsDark;

        // ---- Farben ----
        d[ThemeKeys.ColorC1] = p.C1;
        d[ThemeKeys.ColorC2] = p.C2;
        d[ThemeKeys.ColorC3] = p.C3;
        d[ThemeKeys.ColorOnAccent] = p.OnAccent;
        d[ThemeKeys.ColorAccentText] = p.AccentText;
        d[ThemeKeys.ColorBackground] = p.Background;
        d[ThemeKeys.ColorAmbient1] = p.Ambient1;
        d[ThemeKeys.ColorAmbient2] = p.Ambient2;
        d[ThemeKeys.ColorAmbient3] = p.Ambient3;
        d[ThemeKeys.ColorAccent1] = p.C1;
        d[ThemeKeys.ColorAccent2] = p.C2;
        d[ThemeKeys.ColorAccent3] = p.C3;
        d[ThemeKeys.ColorGlow] = p.C2;
        d[ThemeKeys.ColorShadow] = dark ? Colors.Black : Rgb(0x6B, 0x71, 0x89);

        // ---- Hintergrund ----
        d[ThemeKeys.BrushWindowBackground] = Linear(new Point(0, 0), new Point(1, 1), (p.BackgroundTop, 0), (p.Background, 0.55), (p.BackgroundBottom, 1));
        d[ThemeKeys.BrushBackgroundGlow] = Radial(new Point(0.8, 0), 0.6, 0.5, ColorMath.WithAlpha(p.C2, dark ? 0.18 : 0.16));
        d[ThemeKeys.BrushBackgroundGlow2] = Radial(new Point(0, 1), 0.5, 0.5, ColorMath.WithAlpha(p.C1, dark ? 0.14 : 0.12));
        d[ThemeKeys.BrushTitleBar] = Solid(Colors.Transparent);

        // ---- Flächen und Linien (--s1..3, --ln, --ln2, --pnl, --mn, --glass) ----
        d[ThemeKeys.BrushSurface] = Solid(dark ? Argb(0xED, 0x13, 0x14, 0x1C) : Argb(0xF2, 0xFF, 0xFF, 0xFF));
        d[ThemeKeys.BrushSurfaceHover] = Solid(dark ? Argb(0x1C, 0xFF, 0xFF, 0xFF) : Argb(0x0D, 0x11, 0x13, 0x20));
        d[ThemeKeys.BrushSurfaceSelected] = Solid(dark ? Argb(0x24, 0xFF, 0xFF, 0xFF) : Colors.White);
        d[ThemeKeys.BrushSurfaceInput] = Solid(dark ? Argb(0x0E, 0xFF, 0xFF, 0xFF) : Argb(0xB8, 0xFF, 0xFF, 0xFF));
        d[ThemeKeys.BrushSurface1] = Solid(dark ? Argb(0x0E, 0xFF, 0xFF, 0xFF) : Argb(0xB8, 0xFF, 0xFF, 0xFF));
        d[ThemeKeys.BrushSurface2] = Solid(dark ? Argb(0x1C, 0xFF, 0xFF, 0xFF) : Colors.White);
        d[ThemeKeys.BrushSurface3] = Solid(dark ? Argb(0x29, 0xFF, 0xFF, 0xFF) : Colors.White);
        var line = dark ? Argb(0x17, 0xFF, 0xFF, 0xFF) : Argb(0x1A, 0x11, 0x13, 0x20);
        var lineStrong = dark ? Argb(0x29, 0xFF, 0xFF, 0xFF) : Argb(0x2E, 0x11, 0x13, 0x20);
        d[ThemeKeys.BrushBorder] = Solid(line);
        d[ThemeKeys.BrushBorderStrong] = Solid(lineStrong);
        d[ThemeKeys.BrushLine] = Solid(line);
        d[ThemeKeys.BrushLineStrong] = Solid(lineStrong);
        d[ThemeKeys.BrushPanel] = dark
            ? Linear(new Point(0, 0), new Point(0, 1), (Argb(0xED, 0x13, 0x14, 0x1C), 0), (Argb(0xF2, 0x0A, 0x0B, 0x10), 1))
            : Linear(new Point(0, 0), new Point(0, 1), (Argb(0xF2, 0xFF, 0xFF, 0xFF), 0), (Argb(0xF7, 0xF7, 0xF8, 0xFB), 1));
        d[ThemeKeys.BrushPanelHighlight] = Solid(dark ? Argb(0x24, 0xFF, 0xFF, 0xFF) : Colors.White);
        d[ThemeKeys.BrushMenu] = Solid(dark ? Argb(0xFA, 0x10, 0x11, 0x18) : Argb(0xFC, 0xFF, 0xFF, 0xFF));
        d[ThemeKeys.BrushGlass] = Solid(dark ? Argb(0xA8, 0x0C, 0x0D, 0x13) : Argb(0xCC, 0xFF, 0xFF, 0xFF));

        // ---- Text ----
        d[ThemeKeys.BrushText] = Solid(p.Text);
        d[ThemeKeys.BrushTextSecondary] = Solid(p.TextSecondary);
        d[ThemeKeys.BrushTextMuted] = Solid(p.TextMuted);
        d[ThemeKeys.BrushTextOnAccent] = Solid(p.OnAccent);
        d[ThemeKeys.BrushTextOnArt] = Solid(Rgb(0xF5, 0xF6, 0xFA));
        d[ThemeKeys.BrushTextOnArtSecondary] = Solid(Rgb(0xC9, 0xCD, 0xDB));

        // ---- Akzent (Flächen unter Text: Akzentflächen; Bedienelemente: 3:1-Fassung) ----
        d[ThemeKeys.BrushAccent] = Linear(new Point(0, 0.32), new Point(1, 0.68), (p.AccentFill1, 0), (p.AccentFill2, 1));
        d[ThemeKeys.BrushAccentShine] = Linear(new Point(0, 0.32), new Point(1, 0.68), (p.AccentFill1, 0), (p.AccentFill3, 1));
        d[ThemeKeys.BrushAccentSolid] = Solid(p.AccentText);
        d[ThemeKeys.BrushAccentText] = Solid(p.AccentText);
        d[ThemeKeys.BrushAccentSoft] = Solid(ColorMath.WithAlpha(p.C1, 0.22));
        d[ThemeKeys.BrushAccentStrong] = Solid(ColorMath.WithAlpha(p.C1, 0.62));
        d[ThemeKeys.BrushAtmosphereSoft] = Solid(ColorMath.WithAlpha(p.C2, 0.55));
        d[ThemeKeys.BrushShine] = Solid(p.Shine);
        d[ThemeKeys.BrushSelectionFill] = Solid(p.SelectionFill);
        d[ThemeKeys.BrushSelectionRing] = Solid(p.SelectionRing);
        d[ThemeKeys.BrushFocusRing] = Solid(p.FocusRing);
        d[ThemeKeys.BrushHdrOn] = Linear(new Point(0, 0), new Point(1, 1), (p.AccentFill1, 0), (p.AccentFill2, 0.55), (p.AccentFill3, 1));
        d[ThemeKeys.BrushHdrOff] = Solid(dark ? Argb(0x24, 0xFF, 0xFF, 0xFF) : Argb(0x24, 0x11, 0x13, 0x20));
        d[ThemeKeys.BrushToggleOn] = Linear(new Point(0, 0.32), new Point(1, 0.68), (p.Control1, 0), (p.Control2, 1));
        d[ThemeKeys.BrushToggleOff] = Solid(dark ? Rgb(0xC9, 0xCC, 0xD8) : Colors.White);
        d[ThemeKeys.BrushToggleThumb] = Solid(Colors.White);

        // ---- Zustände ----
        d[ThemeKeys.BrushSuccess] = Solid(p.Success);
        d[ThemeKeys.BrushWarning] = Solid(p.Warning);
        d[ThemeKeys.BrushDanger] = Solid(p.Danger);
        d[ThemeKeys.BrushSuccessSoft] = Solid(Argb(0x29, 0x3D, 0xDC, 0x84));
        d[ThemeKeys.BrushWarningSoft] = Solid(Argb(0x2E, 0xFF, 0xB0, 0x20));
        d[ThemeKeys.BrushDangerSoft] = Solid(Argb(0x29, 0xFF, 0x4D, 0x4D));

        // ---- Quellen (Punkte vor dem Namen); Epic, Manuell und Quiver im hellen Thema dunkler (sonst auf Weiß unsichtbar) ----
        d[ThemeKeys.BrushSourceSteam] = Solid(Rgb(0x66, 0xC0, 0xF4));
        d[ThemeKeys.BrushSourceEpic] = Solid(dark ? Rgb(0xD7, 0xDA, 0xE3) : Rgb(0x8A, 0x8F, 0xA5));
        d[ThemeKeys.BrushSourceXbox] = Solid(Rgb(0x3F, 0xB9, 0x50));
        d[ThemeKeys.BrushSourceGog] = Solid(Rgb(0xB4, 0x5C, 0xFF));
        d[ThemeKeys.BrushSourceUbisoft] = Solid(Rgb(0x4F, 0x8B, 0xFF));
        d[ThemeKeys.BrushSourceEA] = Solid(Rgb(0xFF, 0x5A, 0x5A));
        d[ThemeKeys.BrushSourceBattleNet] = Solid(Rgb(0x27, 0xB8, 0xFF));
        d[ThemeKeys.BrushSourceManual] = Solid(dark ? Rgb(0xC8, 0xCC, 0xD8) : Rgb(0x8A, 0x8F, 0xA5));
        d[ThemeKeys.BrushSourceQuiver] = Solid(dark ? Rgb(0xF4, 0x72, 0xB6) : Rgb(0xD0, 0x40, 0x8C));

        // ---- Schriften (Windows-Schriften, solange keine OFL-Schrift eingebettet ist) ----
        d[ThemeKeys.FontDisplay] = DisplayFont;
        d[ThemeKeys.FontText] = TextFont;
        d[ThemeKeys.FontMono] = MonoFont;
        return d;
    }

    /// <summary>
    /// Wie <see cref="Build(ThemePalette)"/>, aber Pinsel nicht eingefroren und mit einmaligen Farbanimationen von
    /// <paramref name="from"/> zu <paramref name="to"/> (je Pinsel bzw. Verlaufsstopp eine ColorAnimation). Farben (Color)
    /// und Schriften gelten sofort. Danach ersetzt der Aufrufer das Wörterbuch durch die eingefrorene Fassung.
    /// </summary>
    public static ResourceDictionary BuildTransition(ThemePalette from, ThemePalette to, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var start = Build(from);
        var target = Build(to);
        if (duration <= TimeSpan.Zero)
        {
            return target;
        }

        var ease = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });
        var result = new ResourceDictionary();
        foreach (var key in target.Keys)
        {
            var value = target[key];
            result[key] = (start[key], value) switch
            {
                (SolidColorBrush a, SolidColorBrush b) when a.Color != b.Color => Animate(a, b, duration, ease),
                (GradientBrush a, GradientBrush b) when a.GetType() == b.GetType() && a.GradientStops.Count == b.GradientStops.Count => Animate(a, b, duration, ease),
                _ => value,
            };
        }

        return result;
    }

    /// <summary>True, wenn ein Wörterbuch noch laufende Farbanimationen enthält (Prüfung: danach alles eingefroren).</summary>
    public static bool HasRunningTransitions(ResourceDictionary dictionary) =>
        dictionary.Values.OfType<Brush>().Any(b => !b.IsFrozen);

    private static SolidColorBrush Animate(SolidColorBrush from, SolidColorBrush to, TimeSpan duration, IEasingFunction ease)
    {
        var brush = new SolidColorBrush(from.Color) { Opacity = to.Opacity };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(to.Color, duration, ease));
        return brush;
    }

    private static GradientBrush Animate(GradientBrush from, GradientBrush to, TimeSpan duration, IEasingFunction ease)
    {
        var brush = to.CloneCurrentValue();
        for (var i = 0; i < brush.GradientStops.Count; i++)
        {
            var stop = brush.GradientStops[i];
            var target = stop.Color;
            stop.Color = from.GradientStops[i].Color;
            if (stop.Color != target)
            {
                stop.BeginAnimation(GradientStop.ColorProperty, ColorTo(target, duration, ease));
            }
        }

        return brush;
    }

    private static ColorAnimation ColorTo(Color to, TimeSpan duration, IEasingFunction ease)
    {
        var animation = new ColorAnimation(to, new Duration(duration)) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        Timeline.SetDesiredFrameRate(animation, Infrastructure.Effects.EffectPolicy.OneShotFrameRate);
        return animation;
    }

    private static SolidColorBrush Solid(Color c) => Freeze(new SolidColorBrush(c));

    private static LinearGradientBrush Linear(Point start, Point end, params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return Freeze(brush);
    }

    /// <summary>Weiches Leuchten (radial-gradient(… at x y, farbe, transparent 70 %)).</summary>
    private static RadialGradientBrush Radial(Point center, double radiusX, double radiusY, Color color)
    {
        var brush = new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = radiusX, RadiusY = radiusY };
        brush.GradientStops.Add(new GradientStop(color, 0));
        brush.GradientStops.Add(new GradientStop(ColorMath.WithAlpha(color, color.A / 255.0 * 0.45), 0.4));
        brush.GradientStops.Add(new GradientStop(ColorMath.WithAlpha(color, 0), 0.7));
        return Freeze(brush);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
