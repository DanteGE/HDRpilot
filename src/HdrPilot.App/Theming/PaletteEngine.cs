using System.Windows.Media;

namespace HdrPilot.App.Theming;

/// <summary>
/// Alle Farben eines Themas, vorab berechnet aus Palette und hell/dunkel (E89, G10.1). Daraus entstehen die Ressourcen
/// (<see cref="ThemeResources"/>), das Tray- und Fenstersymbol und die Kontrast-Anzeige der Seite „Darstellung“.
/// Akzentflächen (<see cref="AccentFill1"/> bis <see cref="AccentFill3"/>) halten mit <see cref="OnAccent"/> mindestens 4,5:1,
/// <see cref="AccentText"/> und gedämpfter Text mindestens 4,5:1 auf Fläche und Hintergrund, Bedienelemente
/// (<see cref="Control1"/>, <see cref="Control2"/>, <see cref="FocusRing"/>, <see cref="SelectionRing"/>) mindestens 3:1.
/// </summary>
public sealed record ThemePalette(
    bool IsDark,
    Color C1,
    Color C2,
    Color C3,
    Color Ambient1,
    Color Ambient2,
    Color Ambient3,
    Color Background,
    Color BackgroundTop,
    Color BackgroundBottom,
    Color PanelReference,
    Color OnAccent,
    Color AccentFill1,
    Color AccentFill2,
    Color AccentFill3,
    Color AccentText,
    Color Control1,
    Color Control2,
    Color FocusRing,
    Color SelectionRing,
    Color SelectionFill,
    Color Text,
    Color TextSecondary,
    Color TextMuted,
    Color Success,
    Color Warning,
    Color Danger,
    Color Shine)
{
    /// <summary>Kontrast von <see cref="OnAccent"/> auf der schwächeren Seite des Akzentverlaufs (Kennzahl „Auf Akzent“).</summary>
    public double OnAccentContrast => Math.Min(ColorMath.Contrast(OnAccent, AccentFill1), ColorMath.Contrast(OnAccent, AccentFill2));

    /// <summary>Kontrast von <see cref="AccentText"/> auf der Kartenfläche (Kennzahl „Akzenttext“).</summary>
    public double AccentTextContrast => ColorMath.Contrast(AccentText, PanelReference);
}

/// <summary>
/// Rechnet die Palette in fertige Farben um (Formeln aus dem Prototyp v4: applyLook(), .app.dark/.app.light in app.css).
/// Reine Funktionen ohne WPF-Ressourcen, damit jede Regel mit Gegenprobe testbar ist.
/// </summary>
public static class PaletteEngine
{
    /// <summary>Dunkle Schrift auf hellen Akzenten (--on im Prototyp).</summary>
    public static readonly Color Ink = Color.FromRgb(0x0B, 0x0C, 0x12);

    /// <summary>Mindestkontrast für Text (WCAG AA).</summary>
    public const double TextContrast = 4.5;

    /// <summary>Mindestkontrast für Bedienelemente und Ränder (WCAG 1.4.11).</summary>
    public const double ControlContrast = 3.0;

    private static readonly Color Night = Color.FromRgb(0x05, 0x06, 0x0A);
    private static readonly Color Paper = Color.FromRgb(0xED, 0xEF, 0xF4);
    private static readonly Color DarkPanel = Color.FromRgb(0x14, 0x15, 0x1D);

    /// <summary>Alle Farben aus dem Zustand der Darstellung.</summary>
    public static ThemePalette Compute(AppearanceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Compute(state.Palette, state.IsDark, state.AutoTextOnAccent);
    }

    /// <summary>Alle Farben aus Palette, Thema und der Wahl „Textfarbe auf Akzentflächen automatisch“.</summary>
    public static ThemePalette Compute(PaletteColors palette, bool dark, bool autoTextOnAccent)
    {
        var c1 = Opaque(palette.Accent);
        var c2 = Opaque(palette.Atmosphere);
        var c3 = Opaque(palette.Shine);

        // Umgebung aus dem Schema (applyAmbient() ohne Spielfarben): a1 tief, a2 Atmosphäre, a3 Glanz.
        var a1 = SchemeAmbient(c2);
        var background = dark ? ColorMath.MixOklab(Night, a1, 0.34) : ColorMath.MixOklab(Paper, c2, 0.09);
        var top = dark ? ColorMath.MixOklab(Night, a1, 0.48) : ColorMath.MixOklab(Paper, c2, 0.13);
        var bottom = dark ? ColorMath.MixOklab(Night, a1, 0.22) : ColorMath.MixOklab(Paper, c2, 0.05);
        var panel = dark ? DarkPanel : Colors.White;
        Color[] surfaces = [panel, background, top, bottom];

        var on = OnAccentFor(c1, c2, autoTextOnAccent);
        var lighten = on == Ink;
        var fill1 = FillFor(c1, on, lighten);
        var fill2 = FillFor(c2, on, lighten);
        var fill3 = FillFor(c3, on, lighten);

        var control1 = ReadableOnAll(c1, surfaces, dark, ControlContrast);
        var control2 = ReadableOnAll(c2, surfaces, dark, ControlContrast);

        var text = dark ? Color.FromRgb(0xF4, 0xF5, 0xFA) : Color.FromRgb(0x12, 0x13, 0x1A);
        var secondary = ReadableOnAll(dark ? Color.FromRgb(0xAE, 0xB3, 0xC6) : Color.FromRgb(0x4C, 0x52, 0x68), surfaces, dark, TextContrast);
        var muted = ReadableOnAll(dark ? Color.FromRgb(0x80, 0x85, 0x9B) : Color.FromRgb(0x66, 0x6C, 0x80), surfaces, dark, TextContrast);

        return new ThemePalette(
            dark,
            c1,
            c2,
            c3,
            a1,
            c2,
            c3,
            background,
            top,
            bottom,
            panel,
            on,
            fill1,
            fill2,
            fill3,
            AccentText: ReadableOnAll(c1, surfaces, dark, 4.6),
            control1,
            control2,
            FocusRing: control1,
            SelectionRing: control1,
            SelectionFill: ColorMath.WithAlpha(c1, 0.14),
            text,
            secondary,
            muted,
            Success: ReadableOnAll(dark ? Color.FromRgb(0x5B, 0xE3, 0x9B) : Color.FromRgb(0x17, 0x82, 0x4D), surfaces, dark, TextContrast),
            Warning: ReadableOnAll(dark ? Color.FromRgb(0xFF, 0xC4, 0x53) : Color.FromRgb(0x94, 0x60, 0x0A), surfaces, dark, TextContrast),
            Danger: ReadableOnAll(dark ? Color.FromRgb(0xFF, 0x80, 0x80) : Color.FromRgb(0xC6, 0x28, 0x28), surfaces, dark, TextContrast),
            Shine: ColorMath.Mix(c3, Colors.White, 0.4));
    }

    /// <summary>Tiefe Umgebungsfarbe aus der Atmosphäre (a1 im Prototyp: 82 % Nacht).</summary>
    public static Color SchemeAmbient(Color atmosphere) => ColorMath.Mix(Opaque(atmosphere), Night, 0.82);

    /// <summary>
    /// Textfarbe auf Akzentflächen: automatisch die dunkle Schrift oder Weiß, je nachdem, was auf der schwächeren Seite des
    /// Verlaufs Akzent → Atmosphäre mehr Kontrast hat; ohne Automatik immer Weiß.
    /// </summary>
    public static Color OnAccentFor(Color accent, Color atmosphere, bool automatic)
    {
        if (!automatic)
        {
            return Colors.White;
        }

        var dark = Math.Min(ColorMath.Contrast(accent, Ink), ColorMath.Contrast(atmosphere, Ink));
        var white = Math.Min(ColorMath.Contrast(accent, Colors.White), ColorMath.Contrast(atmosphere, Colors.White));
        return dark >= white ? Ink : Colors.White;
    }

    /// <summary>
    /// Akzentfläche unter Text in <paramref name="onAccent"/>: unverändert, wenn sie 4,5:1 schon erreicht; sonst in Schritten
    /// zu Weiß (dunkle Schrift) bzw. Schwarz (weiße Schrift) gemischt. Die Textregel bleibt, nur die Fläche weicht aus.
    /// </summary>
    public static Color FillFor(Color color, Color onAccent, bool lighten) =>
        ColorMath.Contrast(color, onAccent) >= TextContrast ? color : ColorMath.Readable(color, onAccent, lighten, TextContrast);

    /// <summary>
    /// Bringt <paramref name="color"/> auf mindestens <paramref name="target"/>:1 gegen jede der <paramref name="backgrounds"/>
    /// (Mischen in 5-%-Schritten zu Weiß bzw. Schwarz, höchstens 60 Schritte). Schon lesbare Farben bleiben unverändert.
    /// </summary>
    public static Color ReadableOnAll(Color color, IReadOnlyList<Color> backgrounds, bool towardsWhite, double target)
    {
        ArgumentNullException.ThrowIfNull(backgrounds);
        var x = Opaque(color);
        var end = towardsWhite ? Colors.White : Colors.Black;
        for (var i = 0; i < 60 && MinContrast(x, backgrounds) < target; i++)
        {
            x = ColorMath.Mix(x, end, 0.05);
        }

        return x;
    }

    /// <summary>Kleinster Kontrast einer Farbe gegen mehrere Hintergründe.</summary>
    public static double MinContrast(Color color, IReadOnlyList<Color> backgrounds)
    {
        var min = double.MaxValue;
        foreach (var bg in backgrounds)
        {
            min = Math.Min(min, ColorMath.Contrast(color, bg));
        }

        return min;
    }

    /// <summary>Note nach WCAG für die Kennzeichen der Seite „Darstellung“: AAA ab 7, AA ab 4,5, „AA groß“ ab 3.</summary>
    public static ContrastGrade Grade(double ratio) => ratio switch
    {
        >= 7 => ContrastGrade.Aaa,
        >= TextContrast => ContrastGrade.Aa,
        >= ControlContrast => ContrastGrade.AaLarge,
        _ => ContrastGrade.TooLow,
    };

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);
}

/// <summary>Kontraststufe nach WCAG 2.</summary>
public enum ContrastGrade
{
    TooLow,
    AaLarge,
    Aa,
    Aaa,
}
