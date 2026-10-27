namespace HdrPilot.Core.Artwork;

/// <summary>
/// Eingepasste Spielfarben: Akzent, Atmosphäre und (nur im Tritonus mit passendem Kandidaten oder Bogen) Glanz.
/// <see cref="Shine"/> null heißt: Glanz wie bei den Vorlagen aus der Atmosphäre ableiten (Duoton immer).
/// </summary>
/// <param name="AtmosphereDerived">Kein Kandidat taugte; die Atmosphäre ist um ±40° vom Akzent abgeleitet.</param>
public readonly record struct MappedGameColors(Rgba8 Accent, Rgba8 Atmosphere, Rgba8? Shine, bool AtmosphereDerived);

/// <summary>
/// Macht aus den rohen Spielfarben eine Palette wie eine automatisch erzeugte Vorlage (E94): Akzent im Wertebereich des
/// Akzents, Atmosphäre aus einem Kandidaten mit 30–120° Abstand ohne graue Mitte im Verlauf (sonst abgeleitet: warme Akzente
/// −40°, kühle +40°), Glanz im Tritonus aus einem Kandidaten oder auf einem Bogen von 50° hinter der Atmosphäre, ebenfalls ohne
/// graue Mitte (auch nicht zwischen Akzent und Glanz). Rein und deterministisch; die Kontrastrechnung macht die App.
/// </summary>
public static class GameColorMapping
{
    /// <summary>Mindestwert von <see cref="ColorScience.Mud"/> für Verläufe zwischen zwei Rollen.</summary>
    public const double MinMud = 0.70;

    /// <summary>Kleinster Abstand Akzent–Atmosphäre nach dem Drehen.</summary>
    public const double Spread = 32;

    /// <summary>Höchste Chroma (OKLCH) des ruhigen Schemas („Farben an, Hintergrund aus“).</summary>
    public const double CalmChroma = 0.03;

    /// <summary>
    /// Palette aus rohen Spielfarben; null, wenn sie nicht brauchbar sind (dann gilt das Farbschema).
    /// <paramref name="glowHint"/>: Leuchtfarbe des Titelbilds (beide Schalter an); Kandidaten, deren Farbton höchstens 45°
    /// davon liegt, kommen zuerst (der nächste zuerst).
    /// </summary>
    public static MappedGameColors? Map(GameColors? raw, bool tritone, Rgba8? glowHint = null)
    {
        if (raw is null || !raw.IsUsable || Rgba8.TryParse(raw.Accent) is not { } rawAccent)
        {
            return null;
        }

        var accent = ColorScience.Fit(rawAccent, FitRole.Accent);
        var accentLab = ColorScience.ToOklab(accent);
        var (atmosphere, derived) = Atmosphere(raw, accent, accentLab, glowHint);
        Rgba8? shine = tritone ? Shine(raw, accent, accentLab, atmosphere) : null;
        return new MappedGameColors(accent, atmosphere, shine, derived);
    }

    /// <summary>Gleiche Helligkeit und gleicher Farbton, Chroma höchstens <see cref="CalmChroma"/> (ruhiger Grund).</summary>
    public static Rgba8 Calm(Rgba8 color)
    {
        var lab = ColorScience.ToOklab(color);
        if (lab.C <= CalmChroma)
        {
            return color;
        }

        return ColorScience.ToRgba8(ColorScience.FromLch(lab.L, CalmChroma, lab.H), color.A);
    }

    /// <summary>Warme Farbtöne (Rot bis Gelb): die abgeleitete Atmosphäre liegt dann 40° davor, sonst 40° dahinter.</summary>
    public static bool Warm(double hue) => hue is >= 330 or < 110;

    private static (Rgba8 Color, bool Derived) Atmosphere(GameColors raw, Rgba8 accent, Oklab accentLab, Rgba8? glowHint)
    {
        var candidates = raw.Atmospheres
            .Select(s => Rgba8.TryParse(s.Hex))
            .OfType<Rgba8>()
            .Select(c => ColorScience.Fit(c, FitRole.Atmosphere))
            .ToList();
        if (glowHint is { } hint)
        {
            var hintHue = ColorScience.ToOklab(hint).H;
            candidates = [.. candidates
                .Select((c, i) => (c, i, d: ColorScience.HueDistance(ColorScience.ToOklab(c).H, hintHue)))
                .OrderBy(t => t.d <= 45 ? 0 : 1)
                .ThenBy(t => t.d <= 45 ? t.d : 0)
                .ThenBy(t => t.i)
                .Select(t => t.c)];
        }

        foreach (var candidate in candidates)
        {
            var lab = ColorScience.ToOklab(candidate);
            var d = ColorScience.HueDistance(accentLab.H, lab.H);
            if (d is < 12 or > 120 || ColorScience.Mud(accent, candidate) < MinMud)
            {
                continue;
            }

            if (d >= 30)
            {
                return (candidate, false);
            }

            // Zu nah am Akzent: auf genau 32° auf der eigenen Seite drehen und neu einpassen.
            var side = ColorScience.SignedHueArc(accentLab.H, lab.H) >= 0 ? 1 : -1;
            var rotated = FitAt(lab.L, lab.C, accentLab.H + (side * Spread), FitRole.Atmosphere);
            if (ColorScience.HueDistance(accentLab.H, ColorScience.ToOklab(rotated).H) >= 30 && ColorScience.Mud(accent, rotated) >= MinMud)
            {
                return (rotated, false);
            }
        }

        return (DeriveAtmosphere(accent), true);
    }

    /// <summary>
    /// Abgeleitete Atmosphäre: warme Akzente 40° davor, kühle 40° dahinter, Chroma mindestens 0,12, etwas dunkler; ergibt der
    /// Verlauf eine graue Mitte (<see cref="ColorScience.Mud"/> unter <paramref name="minMud"/>), in Schritten von 10° näher an
    /// den Akzent, aber nie unter <see cref="Spread"/>.
    /// </summary>
    public static Rgba8 DeriveAtmosphere(Rgba8 accent, double minMud = MinMud)
    {
        var accentLab = ColorScience.ToOklab(accent);
        var direction = Warm(accentLab.H) ? -1 : 1;
        var c = Math.Max(accentLab.C, FitRole.Atmosphere.CMin);
        var l = accentLab.L - 0.08;
        for (var spread = 40.0; ; spread = Math.Max(Spread, spread - 10))
        {
            var derived = FitAt(l, c, accentLab.H + (direction * spread), FitRole.Atmosphere);
            if (ColorScience.Mud(accent, derived) >= minMud || spread <= Spread)
            {
                return derived;
            }
        }
    }

    private static Rgba8? Shine(GameColors raw, Rgba8 accent, Oklab accentLab, Rgba8 atmosphere)
    {
        var atmosphereLab = ColorScience.ToOklab(atmosphere);
        bool Fits(Rgba8 shine)
        {
            var h = ColorScience.ToOklab(shine).H;
            var toAccent = ColorScience.HueDistance(accentLab.H, h);
            var toAtmosphere = ColorScience.HueDistance(atmosphereLab.H, h);
            return toAccent is >= 25 and <= 150 && toAtmosphere is >= 25 and <= 90
                && ColorScience.Mud(atmosphere, shine) >= MinMud && ColorScience.Mud(accent, shine) >= MinMud;
        }

        foreach (var swatch in raw.Shines)
        {
            if (Rgba8.TryParse(swatch.Hex) is { } c && ColorScience.Fit(c, FitRole.Shine) is var shine && Fits(shine))
            {
                return shine;
            }
        }

        // Bogen: 50° hinter der Atmosphäre (vom Akzent aus gesehen), hell und kräftig.
        var side = ColorScience.SignedHueArc(accentLab.H, atmosphereLab.H) >= 0 ? 1 : -1;
        var arc = FitAt(0.80, 0.20, atmosphereLab.H + (side * 50), FitRole.Shine);
        return Fits(arc) ? arc : null;
    }

    private static Rgba8 FitAt(double l, double c, double h, FitRole role)
    {
        h = ((h % 360) + 360) % 360;
        l = Math.Clamp(l, 0.05, 0.98);
        return ColorScience.Fit(ColorScience.ToRgba8(ColorScience.FromLch(l, ColorScience.GamutClip(l, c, h), h)), role);
    }
}
