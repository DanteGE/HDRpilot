using System.Globalization;

namespace HdrPilot.Core.Artwork;

/// <summary>Farbe mit 8 Bit je Kanal (sRGB, Alpha gerade, nicht vormultipliziert).</summary>
public readonly record struct Rgba8(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>„#RRGGBB“ (Großbuchstaben, ohne Alpha).</summary>
    public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    /// <summary>Farbe aus „#RRGGBB“; null bei ungültigem Text.</summary>
    public static Rgba8? TryParse(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
        {
            return null;
        }

        return new Rgba8((byte)((n >> 16) & 255), (byte)((n >> 8) & 255), (byte)(n & 255));
    }

    /// <summary>Wie <see cref="TryParse"/>, wirft bei ungültigem Text.</summary>
    public static Rgba8 Parse(string hex) => TryParse(hex) ?? throw new FormatException("Keine Farbe im Format #RRGGBB: " + hex);

    public override string ToString() => A == 255 ? Hex : string.Create(CultureInfo.InvariantCulture, $"#{A:X2}{R:X2}{G:X2}{B:X2}");
}

/// <summary>Farbe in OKLab (L 0–1, a, b) samt Chroma und Farbton (OKLCH, Grad 0–360).</summary>
public readonly record struct Oklab(double L, double A, double B)
{
    public double C => Math.Sqrt((A * A) + (B * B));

    public double H => ColorScience.Hue(A, B);
}

/// <summary>
/// Wertebereich einer Rolle für <see cref="ColorScience.Fit"/>: kleinste relative Leuchtdichte je Farbton, größte
/// Leuchtdichte, Chroma-Grenzen und die Mindest-Chroma beim Aufhellen dunkler Farben (0: keine).
/// </summary>
public sealed record FitRole(string Name, Func<double, double> YMin, double YMax, double CMin, double CMax, double LiftBoost)
{
    /// <summary>Akzent (E94): Gold und Zitrone höher, damit sie nicht oliv oder braun werden; Grün und Türkis tiefer.</summary>
    public static FitRole Accent { get; } = new(
        nameof(Accent),
        h => h switch
        {
            < 45 => 0.28,
            < 55 => 0.32,
            < 95 => 0.45,
            < 125 => 0.60,
            < 230 => 0.30,
            < 300 => 0.28,
            _ => 0.30,
        },
        0.75,
        0.12,
        0.24,
        0.15);

    /// <summary>Atmosphäre: dunkler als der Akzent, aber nie so dunkel, dass dunkle Schrift darauf unter 4,5:1 fällt.</summary>
    public static FitRole Atmosphere { get; } = new(nameof(Atmosphere), h => h is >= 55 and < 125 ? 0.30 : 0.21, 0.50, 0.12, 0.24, 0.15);

    /// <summary>Glanz im Tritonus.</summary>
    public static FitRole Shine { get; } = new(
        nameof(Shine),
        h => h switch
        {
            >= 55 and < 95 => 0.45,
            >= 95 and < 125 => 0.60,
            _ => 0.24,
        },
        0.70,
        0.08,
        0.24,
        0);
}

/// <summary>
/// Farbrechnung der Spielfarben (E94) mit Gleitkommazahlen und ohne WPF: sRGB ↔ OKLab/OKLCH, relative Leuchtdichte nach
/// WCAG, Begrenzung auf sRGB bei festem Farbton, Abstände, die „graue Mitte“ eines Verlaufs und das Einpassen einer Farbe in
/// einen Wertebereich (<see cref="Fit"/>). Reine Funktionen, deterministisch.
/// </summary>
public static class ColorScience
{
    private const int ClipSteps = 18;

    private static readonly double[] LinearTable = BuildLinearTable();

    /// <summary>sRGB-Kanal (0–255) linear (0–1), über eine Tabelle.</summary>
    public static double ToLinear(byte channel) => LinearTable[channel];

    /// <summary>Linearer Kanal (beliebig) als sRGB 0–1, ohne Begrenzung.</summary>
    public static double ToGamma(double linear) =>
        linear <= 0.0031308 ? 12.92 * linear : (1.055 * Math.Pow(Math.Max(0, linear), 1 / 2.4)) - 0.055;

    /// <summary>Relative Leuchtdichte nach WCAG (0 schwarz bis 1 weiß), ohne Alpha.</summary>
    public static double Luminance(Rgba8 c) =>
        (0.2126 * ToLinear(c.R)) + (0.7152 * ToLinear(c.G)) + (0.0722 * ToLinear(c.B));

    /// <summary>Kontrastverhältnis nach WCAG (1 bis 21).</summary>
    public static double Contrast(Rgba8 a, Rgba8 b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static Oklab ToOklab(Rgba8 c) => ToOklab(ToLinear(c.R), ToLinear(c.G), ToLinear(c.B));

    /// <summary>OKLab aus linearem RGB.</summary>
    public static Oklab ToOklab(double r, double g, double b)
    {
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
        return new Oklab(
            (0.2104542553 * l) + (0.793617785 * m) - (0.0040720468 * s),
            (1.9779984951 * l) - (2.428592205 * m) + (0.4505937099 * s),
            (0.0259040371 * l) + (0.7827717662 * m) - (0.808675766 * s));
    }

    /// <summary>Lineares RGB (ohne Begrenzung) aus OKLab.</summary>
    public static (double R, double G, double B) ToLinearRgb(Oklab lab)
    {
        var l = Math.Pow(lab.L + (0.3963377774 * lab.A) + (0.2158037573 * lab.B), 3);
        var m = Math.Pow(lab.L - (0.1055613458 * lab.A) - (0.0638541728 * lab.B), 3);
        var s = Math.Pow(lab.L - (0.0894841775 * lab.A) - (1.291485548 * lab.B), 3);
        return (
            (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s),
            (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s),
            (-0.0041960863 * l) - (0.7034186147 * m) + (1.707614701 * s));
    }

    /// <summary>OKLab aus OKLCH (Farbton in Grad).</summary>
    public static Oklab FromLch(double l, double c, double h)
    {
        var rad = h * Math.PI / 180;
        return new Oklab(l, c * Math.Cos(rad), c * Math.Sin(rad));
    }

    /// <summary>Farbton (Grad, 0–360) aus a und b.</summary>
    public static double Hue(double a, double b)
    {
        var h = Math.Atan2(b, a) * 180 / Math.PI;
        return h < 0 ? h + 360 : h;
    }

    /// <summary>8-Bit-Farbe (deckend) aus OKLab, je Kanal begrenzt und gerundet.</summary>
    public static Rgba8 ToRgba8(Oklab lab, byte alpha = 255)
    {
        var (r, g, b) = ToLinearRgb(lab);
        return new Rgba8(Byte(ToGamma(r)), Byte(ToGamma(g)), Byte(ToGamma(b)), alpha);
    }

    /// <summary>True, wenn die Farbe in sRGB liegt (kleine Rechenungenauigkeit zugelassen).</summary>
    public static bool InGamut(double l, double c, double h)
    {
        const double eps = 1e-6;
        var (r, g, b) = ToLinearRgb(FromLch(l, c, h));
        return r >= -eps && r <= 1 + eps && g >= -eps && g <= 1 + eps && b >= -eps && b <= 1 + eps;
    }

    /// <summary>Größte Chroma ≤ <paramref name="c"/>, die bei festem L und Farbton in sRGB liegt (Halbierung, 18 Schritte).</summary>
    public static double GamutClip(double l, double c, double h)
    {
        if (l <= 0 || l >= 1)
        {
            return 0;
        }

        if (InGamut(l, c, h))
        {
            return c;
        }

        double lo = 0, hi = c;
        for (var i = 0; i < ClipSteps; i++)
        {
            var mid = (lo + hi) / 2;
            if (InGamut(l, mid, h))
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>Abstand zweier Farbtöne in Grad (0–180).</summary>
    public static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>Vorzeichenbehafteter kürzester Bogen von <paramref name="from"/> nach <paramref name="to"/> (−180 bis 180; bei genau 180 positiv).</summary>
    public static double SignedHueArc(double from, double to)
    {
        var d = (((to - from) % 360) + 360) % 360;
        return d > 180 ? d - 360 : d;
    }

    /// <summary>Weicher Übergang (smoothstep): 0 bis <paramref name="e0"/>, 1 ab <paramref name="e1"/>.</summary>
    public static double Smooth(double e0, double e1, double x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>Euklidischer Abstand in OKLab.</summary>
    public static double DeltaE(Oklab a, Oklab b)
    {
        double dl = a.L - b.L, da = a.A - b.A, db = a.B - b.B;
        return Math.Sqrt((dl * dl) + (da * da) + (db * db));
    }

    public static double DeltaE(Rgba8 a, Rgba8 b) => DeltaE(ToOklab(a), ToOklab(b));

    /// <summary>
    /// „Graue Mitte“ eines Verlaufs (WPF mischt in sRGB): Chroma der Mitte je Kanal (8 Bit) geteilt durch die mittlere Chroma
    /// der Enden. 1 heißt so bunt wie die Enden, klein heißt grau. Sind beide Enden fast grau, ist das Ergebnis 1.
    /// </summary>
    public static double Mud(Rgba8 c1, Rgba8 c2)
    {
        var mean = (ToOklab(c1).C + ToOklab(c2).C) / 2;
        if (mean < 1e-4)
        {
            return 1;
        }

        var mid = new Rgba8(Mid(c1.R, c2.R), Mid(c1.G, c2.G), Mid(c1.B, c2.B));
        return ToOklab(mid).C / mean;
    }

    /// <summary>
    /// Mischung in OKLCH: L und C linear, Farbton auf dem kürzeren Bogen (bei genau 180° in positiver Richtung); hat ein Ende
    /// fast keine Chroma (unter 0,02), gilt durchgehend der Farbton des anderen. Alpha linear; Ergebnis auf sRGB begrenzt und
    /// auf 8 Bit gerundet.
    /// </summary>
    public static Rgba8 Lerp(Rgba8 c1, Rgba8 c2, double t)
    {
        t = Math.Clamp(t, 0, 1);
        Oklab a = ToOklab(c1), b = ToOklab(c2);
        double ca = a.C, cb = b.C, ha = a.H, hb = b.H;
        if (ca < 0.02 && cb >= 0.02)
        {
            ha = hb;
        }
        else if (cb < 0.02 && ca >= 0.02)
        {
            hb = ha;
        }

        var l = a.L + ((b.L - a.L) * t);
        var c = ca + ((cb - ca) * t);
        var h = ha + (SignedHueArc(ha, hb) * t);
        h = ((h % 360) + 360) % 360;
        var alpha = Byte((c1.A + ((c2.A - c1.A) * t)) / 255.0);
        return ToRgba8(FromLch(l, GamutClip(l, c, h), h), alpha);
    }

    /// <summary>
    /// Bringt eine Farbe in den Wertebereich einer Rolle (E94): Ziel-Chroma = Chroma der Farbe, begrenzt auf
    /// <see cref="FitRole.CMin"/>–<see cref="FitRole.CMax"/> (dunkle Farben mindestens <see cref="FitRole.LiftBoost"/>), dann
    /// Suche über OKLCH-L bei festem Farbton (grob in Schritten von 0,01, fein in 0,001): Unter den Stufen, deren gerundete
    /// Farbe im Leuchtdichteband liegt und fast die höchste dort erreichbare Chroma hat, gewinnt die mit dem kleinsten
    /// <c>|L − L0| + 2·Chroma-Fehlbetrag</c>. Deckend.
    /// </summary>
    public static Rgba8 Fit(Rgba8 color, FitRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        var lab = ToOklab(color);
        double l0 = lab.L, c0 = lab.C, h = lab.H;
        var yMin = role.YMin(h);
        var target = Math.Clamp(c0, role.CMin, role.CMax);
        if (role.LiftBoost > 0 && Luminance(color) < yMin)
        {
            target = Math.Max(target, role.LiftBoost);
        }

        // Grob: L 0,30 bis 0,995 in Schritten von 0,01.
        var coarse = new List<FitStep>(70);
        for (var i = 0; i <= 69; i++)
        {
            var step = Step(Math.Min(0.995, 0.30 + (i * 0.01)), target, h, yMin, role.YMax);
            if (step.InBand)
            {
                coarse.Add(step);
            }
        }

        if (coarse.Count == 0)
        {
            return Fallback(l0, target, h, yMin, role.YMax);
        }

        var floor = Math.Min(role.CMin, coarse.Max(s => s.C)) - 0.003;
        var best = Pick(coarse, floor, l0, target);

        // Fein: ±0,01 um die beste grobe Stufe in Schritten von 0,001.
        var fine = new List<FitStep>(21);
        for (var i = -10; i <= 10; i++)
        {
            var l = Math.Round(best.L + (i * 0.001), 4);
            if (l < 0.30 || l > 0.995)
            {
                continue;
            }

            var step = Step(l, target, h, yMin, role.YMax);
            if (step.InBand)
            {
                fine.Add(step);
            }
        }

        var fineFloor = Math.Min(role.CMin, Math.Max(coarse.Max(s => s.C), fine.Count > 0 ? fine.Max(s => s.C) : 0)) - 0.003;
        var chosen = fine.Count > 0 && fine.Any(s => s.C >= fineFloor) ? Pick(fine, fineFloor, l0, target) : best;
        return chosen.Color;
    }

    private static FitStep Step(double l, double target, double h, double yMin, double yMax)
    {
        var c = GamutClip(l, target, h);
        var rgb = ToRgba8(FromLch(l, c, h));
        var y = Luminance(rgb);
        return new FitStep(l, ToOklab(rgb).C, rgb, y >= yMin && y <= yMax);
    }

    private static FitStep Pick(List<FitStep> steps, double floor, double l0, double target)
    {
        FitStep? best = null;
        var bestCost = double.MaxValue;
        foreach (var s in steps.OrderBy(s => s.L))
        {
            if (s.C < floor)
            {
                continue;
            }

            var cost = Math.Abs(s.L - l0) + (2 * Math.Max(0, target - s.C));
            if (cost < bestCost - 1e-12)
            {
                bestCost = cost;
                best = s;
            }
        }

        return best ?? steps[0];
    }

    /// <summary>Ersatz, falls keine Stufe im Band liegt: Halbierung über L bis zur Bandgrenze bei der Ziel-Chroma.</summary>
    private static Rgba8 Fallback(double l0, double target, double h, double yMin, double yMax)
    {
        double lo = 0.0, hi = 1.0;
        var wantMin = Luminance(ToRgba8(FromLch(l0, GamutClip(l0, target, h), h))) < yMin;
        var goal = wantMin ? yMin : yMax;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            var y = Luminance(ToRgba8(FromLch(mid, GamutClip(mid, target, h), h)));
            if (y < goal)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var l = wantMin ? hi : lo;
        return ToRgba8(FromLch(l, GamutClip(l, target, h), h));
    }

    private static byte Mid(byte a, byte b) => (byte)((a + b + 1) / 2);

    private static byte Byte(double v01) => (byte)Math.Clamp(Math.Floor((v01 * 255) + 0.5), 0, 255);

    private static double[] BuildLinearTable()
    {
        var table = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var v = i / 255.0;
            table[i] = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return table;
    }

    private readonly record struct FitStep(double L, double C, Rgba8 Color, bool InBand);
}
