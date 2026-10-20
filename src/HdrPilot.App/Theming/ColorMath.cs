using System.Globalization;
using System.Windows.Media;

namespace HdrPilot.App.Theming;

/// <summary>
/// Farbrechnung für Palette, Themen und Bildfarben (E89), wie im Prototyp (app.js): sRGB-Mischung, OKLab/OKLCH
/// (color-mix in oklab, Mindest-Chroma), HSL und Kontrast nach WCAG. Alle Mischfarben werden damit vorab berechnet;
/// die Oberfläche bekommt nur fertige Farben. Reine Funktionen, Alphakanal bleibt unberührt, wo nicht anders gesagt.
/// </summary>
public static class ColorMath
{
    /// <summary>"#RRGGBB" oder "#AARRGGBB" (Groß- und Kleinschreibung egal).</summary>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s.StartsWith('#'))
        {
            s = s[1..];
        }

        if ((s.Length != 6 && s.Length != 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        color = s.Length == 6
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>Wie <see cref="TryParseHex"/>, wirft bei ungültigem Text.</summary>
    public static Color FromHex(string text) =>
        TryParseHex(text, out var c) ? c : throw new FormatException("Keine Farbe im Format #RRGGBB: " + text);

    /// <summary>"#RRGGBB" (ohne Alpha, Großbuchstaben).</summary>
    public static string ToHex(Color c) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    /// <summary>Relative Leuchtdichte nach WCAG 2 (0 schwarz bis 1 weiß), ohne Alpha.</summary>
    public static double RelativeLuminance(Color c) =>
        (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));

    /// <summary>Kontrastverhältnis nach WCAG 2 (1 bis 21), ohne Alpha.</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Mischung in sRGB (wie mix() im Prototyp): t = 0 ergibt <paramref name="a"/>, t = 1 ergibt <paramref name="b"/>.</summary>
    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(Lerp(a.A, b.A, t), Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
    }

    /// <summary>Mischung in OKLab (color-mix(in oklab, …)), Alpha linear.</summary>
    public static Color MixOklab(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var (l1, a1, b1) = ToOklab(a);
        var (l2, a2, b2) = ToOklab(b);
        var mixed = FromOklab(l1 + ((l2 - l1) * t), a1 + ((a2 - a1) * t), b1 + ((b2 - b1) * t));
        return Color.FromArgb(Lerp(a.A, b.A, t), mixed.R, mixed.G, mixed.B);
    }

    /// <summary>Gleiche Farbe mit Deckkraft 0–1.</summary>
    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), c.R, c.G, c.B);

    /// <summary>OKLab (L 0–1, a, b) einer sRGB-Farbe.</summary>
    public static (double L, double A, double B) ToOklab(Color c)
    {
        var r = Linear(c.R);
        var g = Linear(c.G);
        var b = Linear(c.B);
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
        return (
            (0.2104542553 * l) + (0.793617785 * m) - (0.0040720468 * s),
            (1.9779984951 * l) - (2.428592205 * m) + (0.4505937099 * s),
            (0.0259040371 * l) + (0.7827717662 * m) - (0.808675766 * s));
    }

    /// <summary>sRGB-Farbe (deckend) aus OKLab; außerhalb von sRGB wird je Kanal begrenzt.</summary>
    public static Color FromOklab(double l, double a, double b)
    {
        var lc = Math.Pow(l + (0.3963377774 * a) + (0.2158037573 * b), 3);
        var mc = Math.Pow(l - (0.1055613458 * a) - (0.0638541728 * b), 3);
        var sc = Math.Pow(l - (0.0894841775 * a) - (1.291485548 * b), 3);
        return Color.FromRgb(
            Gamma((4.0767416621 * lc) - (3.3077115913 * mc) + (0.2309699292 * sc)),
            Gamma((-1.2684380046 * lc) + (2.6097574011 * mc) - (0.3413193965 * sc)),
            Gamma((-0.0041960863 * lc) - (0.7034186147 * mc) + (1.707614701 * sc)));
    }

    /// <summary>Chroma in OKLCH (Abstand von Grau).</summary>
    public static double Chroma(Color c)
    {
        var (_, a, b) = ToOklab(c);
        return Math.Sqrt((a * a) + (b * b));
    }

    /// <summary>Mindest-Chroma in OKLCH (blasse Bildfarben kräftiger, wie minChroma() im Prototyp); Grau bleibt grau.</summary>
    public static Color MinChroma(Color c, double minimum)
    {
        var (l, a, b) = ToOklab(c);
        var chroma = Math.Sqrt((a * a) + (b * b));
        if (chroma >= minimum || chroma < 1e-4)
        {
            return c;
        }

        var k = minimum / chroma;
        var result = FromOklab(l, a * k, b * k);
        return Color.FromArgb(c.A, result.R, result.G, result.B);
    }

    /// <summary>
    /// Bringt <paramref name="c"/> auf mindestens <paramref name="target"/>:1 gegen <paramref name="background"/>, indem sie in
    /// Schritten von 10 % zu Weiß (<paramref name="towardsWhite"/>) bzw. Schwarz gemischt wird (readable() im Prototyp).
    /// </summary>
    public static Color Readable(Color c, Color background, bool towardsWhite, double target = 4.6)
    {
        var x = c;
        var end = towardsWhite ? Colors.White : Colors.Black;
        for (var i = 0; i < 30 && Contrast(x, background) < target; i++)
        {
            x = Mix(x, end, 0.1);
        }

        return x;
    }

    /// <summary>Farbton (0–360), Sättigung und Helligkeit (0–1).</summary>
    public static (double H, double S, double L) ToHsl(Color c)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
        return (h * 60, s, l);
    }

    /// <summary>Deckende Farbe aus Farbton (Grad, beliebig), Sättigung und Helligkeit (0–1).</summary>
    public static Color FromHsl(double h, double s, double l)
    {
        h = (((h % 360) + 360) % 360) / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        double F(double n)
        {
            var k = (n + (h * 12)) % 12;
            var a = s * Math.Min(l, 1 - l);
            return l - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }

        return Color.FromRgb(Byte(F(0) * 255), Byte(F(8) * 255), Byte(F(4) * 255));
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Gamma(double linear)
    {
        var c = linear <= 0.0031308 ? 12.92 * linear : (1.055 * Math.Pow(Math.Max(0, linear), 1 / 2.4)) - 0.055;
        return Byte(c * 255);
    }

    private static byte Lerp(byte a, byte b, double t) => Byte(a + ((b - a) * t));

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
