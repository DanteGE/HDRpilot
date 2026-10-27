using System.Windows.Media;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Theming;

/// <summary>
/// Farbschema aus den Farben eines Spiels (E94, „Farben folgen dem Spiel“): wie eine automatisch erzeugte Vorlage. Einpassen
/// in die Wertebereiche macht <see cref="GameColorMapping"/>; danach läuft die Palette durch dieselbe Kontrastrechnung wie die
/// Vorlagen (<see cref="PaletteEngine"/>), gegen die Flächen des Schemas und den hellsten bzw. dunkelsten möglichen Grundton
/// der Bibliothek. Im hellen Thema werden abgedunkelte Gelb- und Goldtöne zu Bernstein statt Oliv. Graue Spiele bekommen
/// genau das Farbschema (<see cref="Scheme"/>). Reine Funktionen.
/// </summary>
public static class GamePalette
{
    /// <summary>Farbton (OKLCH), zu dem abgedunkeltes Gelb und Gold im hellen Thema wandert (wie die Warnfarbe #94600A).</summary>
    public const double AmberHue = 75;

    private static readonly Color Night = Color.FromRgb(0x05, 0x06, 0x0A);
    private static readonly Color Paper = Color.FromRgb(0xED, 0xEF, 0xF4);
    private static readonly Lazy<Color[]> DarkBounds = new(() => ComputeBounds(dark: true));
    private static readonly Lazy<Color[]> LightBounds = new(() => ComputeBounds(dark: false));

    /// <summary>Palette aus rohen Spielfarben im Modus des Nutzers; null: keine brauchbare Farbe, es gilt das Schema.</summary>
    public static PaletteColors? Map(GameColors? raw, PaletteMode mode, Color? glowHint = null)
    {
        Rgba8? hint = glowHint is { } g ? ToRgba(g) : null;
        if (GameColorMapping.Map(raw, mode == PaletteMode.Tritone, hint) is not { } mapped)
        {
            return null;
        }

        var atmosphere = ToColor(mapped.Atmosphere);
        return new PaletteColors(ToColor(mapped.Accent), atmosphere, mapped.Shine is { } shine ? ToColor(shine) : PalettePresets.DerivedShine(atmosphere));
    }

    /// <summary>
    /// Alle Farben einer Spielpalette: Flächen aus dem Schema, Kontrast zusätzlich gegen den möglichen Grundton der Bibliothek,
    /// im hellen Thema die Bernstein-Regel. <paramref name="dark"/> kommt vom Fenster (nicht aus <paramref name="state"/>).
    /// </summary>
    public static ThemePalette Compute(PaletteColors accents, AppearanceState state, bool dark)
    {
        ArgumentNullException.ThrowIfNull(state);
        var bounds = LibraryBackgroundBounds(dark);
        var palette = PaletteEngine.Compute(accents, dark, state.AutoTextOnAccent, state.Palette.Atmosphere, bounds);
        return dark ? palette : Amber(palette, accents, Surfaces(palette, bounds));
    }

    /// <summary>Genau die Palette der App (ohne Grundton der Bibliothek): graue Spiele, Schalter aus, Einbau der Ebene.</summary>
    public static ThemePalette Scheme(AppearanceState state, bool dark)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PaletteEngine.Compute(state.Palette, dark, state.AutoTextOnAccent);
    }

    /// <summary>Ruhiges Schema („Farben an, Hintergrund aus“): je Farbe Chroma höchstens 0,03, Helligkeit und Farbton bleiben.</summary>
    public static PaletteColors Calm(PaletteColors scheme) =>
        new(CalmColor(scheme.Accent), CalmColor(scheme.Atmosphere), CalmColor(scheme.Shine));

    /// <summary>
    /// Hellster (dunkles Thema) bzw. dunkelster (helles Thema) mögliche Grundton der Bibliothek (<see cref="AmbientPalette"/>),
    /// einmal aus allen Eingaben berechnet, die das Fenster bekommen kann: Bildfarben in den Grenzen von
    /// <see cref="PixelAnalysis"/>, Farben aus dem Namen, Mischungen grauer Bilder mit Schema- und Spielfarben, Grund jedes
    /// Schemas (Vorlagen, eigene Farben, ruhige Fassungen).
    /// </summary>
    public static IReadOnlyList<Color> LibraryBackgroundBounds(bool dark) => dark ? DarkBounds.Value : LightBounds.Value;

    /// <summary>Grenzen für das Thema schon berechnet (dann kostet <see cref="Compute"/> nur noch die Kontrastrechnung).</summary>
    public static bool BoundsReady(bool dark) => (dark ? DarkBounds : LightBounds).IsValueCreated;

    /// <summary>Berechnet die Grenzen vorab auf einem Hintergrund-Thread (Schalter an, Farbprobe der Einstellungen).</summary>
    public static Task PrepareAsync() => Task.Run(() =>
    {
        _ = DarkBounds.Value;
        _ = LightBounds.Value;
    });

    /// <summary>Alle Grundtöne, die in die Grenze eingehen, über <see cref="AmbientPalette.Resolve"/> (für Tests).</summary>
    public static IEnumerable<Color> SampledBackgrounds(bool dark)
    {
        var schemes = SchemeAtmospheres().ToList();
        var mixes = schemes.Concat(BandAtmospheres()).ToList();
        foreach (var (deep, glow) in ArtInputs())
        {
            var art = new AmbientColors(deep, glow, glow, 0.5, 0.5, 0.5, AmbientDullness.None);
            yield return AmbientPalette.Resolve(art, PalettePresets.Default.Colors, followGame: true, dark).Background;
            foreach (var atmosphere in mixes)
            {
                var scheme = new PaletteColors(atmosphere, atmosphere, atmosphere);
                yield return AmbientPalette.Resolve(art with { Dullness = AmbientDullness.Gray }, scheme, followGame: true, dark).Background;
            }
        }

        foreach (var atmosphere in schemes)
        {
            var scheme = new PaletteColors(atmosphere, atmosphere, atmosphere);
            yield return AmbientPalette.Resolve(null, scheme, followGame: false, dark).Background;
        }
    }

    /// <summary>Bernstein-Regel: Akzenttext und Bedienfarben mit Farbton 80–115°, die abgedunkelt werden mussten, bei 75°.</summary>
    private static ThemePalette Amber(ThemePalette p, PaletteColors accents, IReadOnlyList<Color> surfaces)
    {
        var accentText = AmberOf(accents.Accent, p.AccentText, surfaces, 4.6);
        var control1 = AmberOf(accents.Accent, p.Control1, surfaces, PaletteEngine.ControlContrast);
        var control2 = AmberOf(accents.Atmosphere, p.Control2, surfaces, PaletteEngine.ControlContrast);
        return p with { AccentText = accentText, Control1 = control1, Control2 = control2, FocusRing = control1, SelectionRing = control1 };
    }

    private static Color AmberOf(Color source, Color engine, IReadOnlyList<Color> surfaces, double target)
    {
        var src = ColorScience.ToOklab(ToRgba(source));
        if (src.H is < 80 or >= 115 || src.C < 0.02 || Opaque(source) == engine)
        {
            return engine;
        }

        // Größte Helligkeit bei 75°, die den Kontrast noch erreicht (Halbierung; Chroma der Quelle, auf sRGB begrenzt).
        Color At(double l) => ToColor(ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, src.C, AmberHue), AmberHue)));
        double lo = 0, hi = src.L;
        if (PaletteEngine.MinContrast(At(lo + 0.02), surfaces) < target)
        {
            return engine;
        }

        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            if (PaletteEngine.MinContrast(At(mid), surfaces) >= target)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var result = At(lo);
        return PaletteEngine.MinContrast(result, surfaces) >= target ? result : engine;
    }

    /// <summary>Flächen, gegen die die Engine Kontrast gehalten hat (Karte, Grund oben und unten, Grenzen der Bibliothek).</summary>
    private static Color[] Surfaces(ThemePalette p, IReadOnlyList<Color> bounds) =>
        [p.PanelReference, p.Background, p.BackgroundTop, p.BackgroundBottom, .. bounds];

    /// <summary>
    /// Wie <see cref="SampledBackgrounds"/>, aber nur die Formel des Grundtons (dunkel: 34 % a1 auf #05060A, hell: 9 % a2
    /// auf #EDEFF4) und jede Eingabe einmal: einmalig etwa 0,1 s statt einer Sekunde.
    /// </summary>
    private static Color[] ComputeBounds(bool dark)
    {
        var schemes = SchemeAtmospheres().ToList();
        var mixes = schemes.Concat(BandAtmospheres()).ToList();
        var inputs = ArtInputs().Select(p => LibraryArt.Parse(dark ? p.Deep : p.Glow)).Distinct().ToList();
        Color? best = null;
        var bestY = 0.0;
        void Consider(Color tone)
        {
            var background = dark ? ColorMath.MixOklab(Night, tone, 0.34) : ColorMath.MixOklab(Paper, tone, 0.09);
            var y = ColorMath.RelativeLuminance(background);
            if (best is null || (dark ? y > bestY : y < bestY))
            {
                best = Opaque(background);
                bestY = y;
            }
        }

        foreach (var tone in inputs)
        {
            Consider(tone);
            foreach (var atmosphere in mixes)
            {
                Consider(dark ? ColorMath.MixOklab(tone, ColorMath.Mix(atmosphere, Night, 0.8), 0.5) : ColorMath.MixOklab(tone, atmosphere, 0.5));
            }
        }

        foreach (var atmosphere in schemes)
        {
            Consider(dark ? ColorMath.Mix(atmosphere, Night, 0.82) : atmosphere);
        }

        return [best!.Value];
    }

    /// <summary>a1/a2-Paare aus Bildern (Grenzen von <see cref="PixelAnalysis.Ambient"/>) und aus dem Namen.</summary>
    private static IEnumerable<(string Deep, string Glow)> ArtInputs()
    {
        for (var i = 0; i < 36; i++)
        {
            var h = i * 10.0;
            foreach (var s1 in new[] { 0.2, 0.5, 0.75 })
            {
                var deep = PixelAnalysis.HslToHex(h, s1, 0.15);
                foreach (var s2 in new[] { 0.45, 0.7, 0.95 })
                {
                    foreach (var l2 in new[] { 0.46, 0.53, 0.60 })
                    {
                        yield return (deep, PixelAnalysis.HslToHex(h, s2, l2));
                    }
                }
            }

            yield return (PixelAnalysis.HslToHex(h, 0.55, 0.16), PixelAnalysis.HslToHex(h + 18, 0.78, 0.55));
        }
    }

    /// <summary>Atmosphären aller Schemata: Vorlagen, gesättigte eigene Farben, Weiß, Schwarz, Grau, jeweils auch ruhig.</summary>
    private static IEnumerable<Color> SchemeAtmospheres()
    {
        var list = new List<Color>();
        foreach (var preset in PalettePresets.All)
        {
            list.AddRange([preset.Colors.Accent, preset.Colors.Atmosphere, preset.Colors.Shine]);
        }

        for (var i = 0; i < 36; i++)
        {
            list.Add(ColorMath.FromHsl(i * 10, 1, 0.5));
        }

        list.AddRange([Colors.White, Colors.Black, Color.FromRgb(0x80, 0x80, 0x80)]);
        return list.Concat(list.Select(CalmColor));
    }

    /// <summary>Spielatmosphären an den Rändern ihres Bands (Leuchtdichte 0,21 und 0,50) für 36 Farbtöne.</summary>
    private static IEnumerable<Color> BandAtmospheres()
    {
        for (var i = 0; i < 36; i++)
        {
            foreach (var y in new[] { 0.21, 0.50 })
            {
                yield return ToColor(AtLuminance(i * 10.0, 0.15, y));
            }
        }
    }

    private static Rgba8 AtLuminance(double h, double c, double y)
    {
        double lo = 0, hi = 1;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            var color = ColorScience.ToRgba8(ColorScience.FromLch(mid, ColorScience.GamutClip(mid, c, h), h));
            if (ColorScience.Luminance(color) < y)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return ColorScience.ToRgba8(ColorScience.FromLch(hi, ColorScience.GamutClip(hi, c, h), h));
    }

    private static Color CalmColor(Color c)
    {
        var calm = GameColorMapping.Calm(ToRgba(c));
        return Color.FromArgb(c.A, calm.R, calm.G, calm.B);
    }

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

    internal static Rgba8 ToRgba(Color c) => new(c.R, c.G, c.B, c.A);

    internal static Color ToColor(Rgba8 c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
