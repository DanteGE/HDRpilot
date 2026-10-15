using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HdrPilot.Core.Artwork;

/// <summary>Kennzahlen der Helligkeit am unteren und oberen Rand eines Covers (Erkennung verschmierter Steam-Kapseln).</summary>
/// <param name="BottomVertical">Mittlere senkrechte Änderung der Zeilen 62–89 (von 90).</param>
/// <param name="BottomHorizontal">Mittlere waagerechte Änderung derselben Zeilen.</param>
/// <param name="BottomDeviation">Standardabweichung der Helligkeit derselben Zeilen.</param>
/// <param name="TopVertical">Mittlere senkrechte Änderung der Zeilen 0–8.</param>
/// <param name="TopHorizontal">Mittlere waagerechte Änderung der Zeilen 0–8.</param>
/// <param name="TopDeviation">Standardabweichung der Helligkeit der Zeilen 0–8.</param>
public readonly record struct SmearMetrics(
    double BottomVertical,
    double BottomHorizontal,
    double BottomDeviation,
    double TopVertical,
    double TopHorizontal,
    double TopDeviation)
{
    /// <summary>
    /// Von Steam aus dem Kopfbild erzeugte Kapsel: oben und unten senkrecht verschmiert (kaum senkrechte, aber waagerechte
    /// Änderung und Farbe). Flache Flächen (schwarz, einfarbig) gelten nicht als verschmiert. Schwellen wie in make-data.ps1.
    /// </summary>
    public bool IsSmeared =>
        BottomVertical < 1.4 && BottomHorizontal >= 1.6 * BottomVertical && BottomHorizontal < 6 && BottomDeviation > 10
        && TopVertical < 3.5 && TopDeviation > 3;

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{BottomVertical:0.0}/{BottomHorizontal:0.0}/{BottomDeviation:0} · oben {TopVertical:0.0}/{TopDeviation:0}");
}

/// <summary>Rechteck in Pixeln (Zuschnitt eines Logos).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Umgebungsfarben samt Farbfeld aus einem Bild (<see cref="PixelAnalysis.Ambient"/>).</summary>
/// <param name="Field">Farbfeld als BGRA (<see cref="FieldWidth"/> × <see cref="FieldHeight"/>, deckend).</param>
public sealed record AmbientResult(AmbientColors Colors, byte[] Field, int FieldWidth, int FieldHeight);

/// <summary>
/// Reine Pixel-Rechnungen der Spielbilder (E89) auf BGRA-Puffern (8 Bit je Kanal, Zeilen ohne Lücken), damit sie mit
/// erzeugten Bildern testbar sind. Die Formeln folgen dem Prototyp (<c>prototyp/app.js</c>: extract, field, mono, minChroma)
/// und make-data.ps1 (SmearMetrics); Dekodieren und Kodieren macht die App mit den Bilddecodern von Windows.
/// </summary>
public static partial class PixelAnalysis
{
    /// <summary>Breite der kleinen Fassung, aus der die Umgebungsfarben gelesen werden.</summary>
    public const int AmbientWidth = 48;

    /// <summary>Größe der verkleinerten Fassung eines Covers für die Erkennung verschmierter Kapseln.</summary>
    public const int SmearWidth = 60;

    public const int SmearHeight = 90;

    /// <summary>Höhe der kleinen Fassung zu einem Seitenverhältnis (12–48 px wie im Prototyp).</summary>
    public static int AmbientHeight(int width, int height) =>
        Math.Clamp(JsRound(AmbientWidth * (double)height / Math.Max(1, width)), 12, 48);

    /// <summary>
    /// Umgebungsfarben (Portierung von <c>extract()</c>): Farb-Buckets mit 3 Bit je Kanal, gewichtet nach Anzahl und Sättigung;
    /// a1 dunkler Grundton, a2 Leuchtfarbe, a3 helle Zweitfarbe mit anderem Farbton. Blasse oder sehr dunkle Bilder bekommen
    /// eine Mindest-Chroma (OKLCH 0,05/0,08/0,08); fast graue werden als <see cref="AmbientDullness.Gray"/> markiert (beim
    /// Anwenden zur Hälfte mit der Schemafarbe gemischt). Bei einem Titelbild (<paramref name="isScene"/>) zusätzlich die
    /// hellste 3×3-Stelle zwischen 22 % und 86 % der Breite. Null, wenn kein Pixel deckend genug ist.
    /// </summary>
    public static AmbientResult? Ambient(ReadOnlySpan<byte> bgra, int width, int height, bool isScene)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return null;
        }

        var buckets = new Dictionary<int, Bucket>();
        var order = new List<int>();
        double ar = 0, ag = 0, ab = 0;
        var an = 0;
        var luma = new double[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var o = ((y * width) + x) * 4;
                int b = bgra[o], g = bgra[o + 1], r = bgra[o + 2], a = bgra[o + 3];
                if (a < 120)
                {
                    continue;
                }

                ar += r;
                ag += g;
                ab += b;
                an++;
                luma[(y * width) + x] = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
                var (_, s, l) = RgbToHsl(r, g, b);
                var key = ((r >> 5) << 6) | ((g >> 5) << 3) | (b >> 5);
                if (!buckets.TryGetValue(key, out var e))
                {
                    e = new Bucket();
                    buckets[key] = e;
                    order.Add(key);
                }

                e.N++;
                e.R += r;
                e.G += g;
                e.B += b;
                e.S += s * (l > 0.18 && l < 0.82 ? 1 : 0.3);
            }
        }

        if (an == 0)
        {
            return null;
        }

        double best = -1, bx = 0.62, by = 0.4;
        if (isScene)
        {
            var x0 = (int)Math.Floor(width * 0.22);
            var x1 = (int)Math.Floor(width * 0.86);
            for (var y = 1; y < height - 1; y++)
            {
                for (var x = Math.Max(1, x0); x < Math.Min(width - 1, x1); x++)
                {
                    double sum = 0;
                    for (var j = -1; j <= 1; j++)
                    {
                        for (var i = -1; i <= 1; i++)
                        {
                            sum += luma[((y + j) * width) + x + i];
                        }
                    }

                    if (sum > best)
                    {
                        best = sum;
                        bx = x / (double)width;
                        by = y / (double)height;
                    }
                }
            }
        }

        var highlight = isScene ? Math.Clamp(best / 9 / 255, 0, 1) : 0.55;
        var list = order.Select(k =>
        {
            var e = buckets[k];
            double r = e.R / e.N, g = e.G / e.N, b = e.B / e.N;
            var (h, s, l) = RgbToHsl(r, g, b);
            return new Swatch(h, s, l, e.N * (0.15 + (e.S / e.N)) * (0.4 + s));
        }).OrderByDescending(c => c.Weight).ToList();

        var v1 = list.FirstOrDefault(c => c.S > 0.22 && c.L > 0.14 && c.L < 0.86) ?? list[0];
        var v2 = list.FirstOrDefault(c => !ReferenceEquals(c, v1) && c.S > 0.18 && c.L > 0.14 && HueDistance(c.H, v1.H) > 28) ?? v1;
        var (ah, aS, _) = RgbToHsl(ar / an, ag / an, ab / an);
        var dullness = v1.S < 0.08 ? AmbientDullness.Gray : (v1.S < 0.25 || v1.L < 0.12) ? AmbientDullness.Pale : AmbientDullness.None;
        var a1 = HslToHex(v1.S > 0.25 ? v1.H : ah, Math.Clamp(Math.Max(aS, v1.S) * 0.85, 0.2, 0.75), 0.15);
        var a2 = HslToHex(v1.H, Math.Clamp(v1.S * 1.15, 0.45, 0.95), Math.Clamp(v1.L, 0.46, 0.6));
        var a3 = HslToHex(v2.H, Math.Clamp(v2.S * 1.1, 0.5, 1), Math.Clamp(v2.L + 0.22, 0.68, 0.82));
        if (dullness == AmbientDullness.Pale)
        {
            a1 = MinChroma(a1, 0.05);
            a2 = MinChroma(a2, 0.08);
            a3 = MinChroma(a3, 0.08);
        }

        var colors = new AmbientColors(a1, a2, a3, Math.Round(bx, 3), Math.Round(by, 3), Math.Round(highlight, 2), dullness);
        int fieldWidth = isScene ? 8 : 6, fieldHeight = isScene ? 3 : 6;
        return new AmbientResult(colors, Field(bgra, width, height, fieldWidth, fieldHeight, a1, isScene ? 1 : 1.3), fieldWidth, fieldHeight);
    }

    /// <summary>
    /// Farbfeld (Portierung von <c>field()</c>): die kleine Fassung über der Grundfarbe flächengemittelt auf
    /// <paramref name="fieldWidth"/> × <paramref name="fieldHeight"/> verkleinert (Titelbild 8×3, Symbol 6×6 mit 1,3-facher
    /// Vergrößerung), dann je Pixel Sättigung ×1,25 und Helligkeit ×0,82 (0,04–0,7). Gestreckt mit linearer Filterung ergibt
    /// das eine weiche Fläche ohne Weichzeichner.
    /// </summary>
    public static byte[] Field(ReadOnlySpan<byte> bgra, int width, int height, int fieldWidth, int fieldHeight, string baseHex, double zoom = 1)
    {
        var (br, bg, bb) = HexToRgb(baseHex);
        var scale = Math.Max(fieldWidth / (double)width, fieldHeight / (double)height) * zoom;
        var ox = (fieldWidth - (width * scale)) / 2;
        var oy = (fieldHeight - (height * scale)) / 2;
        var result = new byte[fieldWidth * fieldHeight * 4];
        for (var ty = 0; ty < fieldHeight; ty++)
        {
            for (var tx = 0; tx < fieldWidth; tx++)
            {
                double sx0 = (tx - ox) / scale, sx1 = (tx + 1 - ox) / scale, sy0 = (ty - oy) / scale, sy1 = (ty + 1 - oy) / scale;
                var area = (sx1 - sx0) * (sy1 - sy0);
                double pr = 0, pg = 0, pb = 0, covered = 0;
                for (var sy = Math.Max(0, (int)Math.Floor(sy0)); sy < Math.Min(height, (int)Math.Ceiling(sy1)); sy++)
                {
                    var wy = Math.Min(sy + 1, sy1) - Math.Max(sy, sy0);
                    for (var sx = Math.Max(0, (int)Math.Floor(sx0)); sx < Math.Min(width, (int)Math.Ceiling(sx1)); sx++)
                    {
                        var w = (Math.Min(sx + 1, sx1) - Math.Max(sx, sx0)) * wy;
                        var o = ((sy * width) + sx) * 4;
                        var a = bgra[o + 3] / 255.0 * w;
                        pr += bgra[o + 2] * a;
                        pg += bgra[o + 1] * a;
                        pb += bgra[o] * a;
                        covered += a;
                    }
                }

                var rest = Math.Max(0, area - covered);
                var (h, s, l) = RgbToHsl((pr + (br * rest)) / area, (pg + (bg * rest)) / area, (pb + (bb * rest)) / area);
                var (r, g, b) = HexToRgb(HslToHex(h, Math.Clamp(s * 1.25, 0, 1), Math.Clamp(l * 0.82, 0.04, 0.7)));
                var t = ((ty * fieldWidth) + tx) * 4;
                result[t] = (byte)b;
                result[t + 1] = (byte)g;
                result[t + 2] = (byte)r;
                result[t + 3] = 255;
            }
        }

        return result;
    }

    /// <summary>
    /// Ausschnitt eines Bildes (<paramref name="sx"/>, <paramref name="sy"/>, <paramref name="sw"/> × <paramref name="sh"/> in
    /// Quellpixeln, auch gebrochen) auf genau <paramref name="targetWidth"/> × <paramref name="targetHeight"/> umgerechnet:
    /// beim Verkleinern flächengemittelt, beim Vergrößern bilinear, jeweils mit vormultipliziertem Alpha (keine dunklen
    /// Ränder an durchsichtigen Kanten). Außerhalb des Bildes ist es durchsichtig.
    /// </summary>
    public static byte[] Resample(ReadOnlySpan<byte> bgra, int width, int height, double sx, double sy, double sw, double sh, int targetWidth, int targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0 || sw <= 0 || sh <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Größen müssen positiv sein.");
        }

        var result = new byte[targetWidth * targetHeight * 4];
        double scaleX = sw / targetWidth, scaleY = sh / targetHeight;
        var upscale = scaleX < 1 || scaleY < 1;
        for (var ty = 0; ty < targetHeight; ty++)
        {
            for (var tx = 0; tx < targetWidth; tx++)
            {
                double a = 0, r = 0, g = 0, b = 0, total;
                if (upscale)
                {
                    // Bilinear an der Pixelmitte.
                    var fx = sx + ((tx + 0.5) * scaleX) - 0.5;
                    var fy = sy + ((ty + 0.5) * scaleY) - 0.5;
                    int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                    double dx = fx - x0, dy = fy - y0;
                    total = 0;
                    for (var j = 0; j <= 1; j++)
                    {
                        for (var i = 0; i <= 1; i++)
                        {
                            var w = (i == 0 ? 1 - dx : dx) * (j == 0 ? 1 - dy : dy);
                            int px = Math.Clamp(x0 + i, 0, width - 1), py = Math.Clamp(y0 + j, 0, height - 1);
                            Accumulate(bgra, width, px, py, w, ref a, ref r, ref g, ref b);
                            total += w;
                        }
                    }
                }
                else
                {
                    double x0 = sx + (tx * scaleX), x1 = x0 + scaleX, y0 = sy + (ty * scaleY), y1 = y0 + scaleY;
                    total = (x1 - x0) * (y1 - y0);
                    for (var py = Math.Max(0, (int)Math.Floor(y0)); py < Math.Min(height, (int)Math.Ceiling(y1)); py++)
                    {
                        var wy = Math.Min(py + 1, y1) - Math.Max(py, y0);
                        for (var px = Math.Max(0, (int)Math.Floor(x0)); px < Math.Min(width, (int)Math.Ceiling(x1)); px++)
                        {
                            Accumulate(bgra, width, px, py, (Math.Min(px + 1, x1) - Math.Max(px, x0)) * wy, ref a, ref r, ref g, ref b);
                        }
                    }
                }

                var o = ((ty * targetWidth) + tx) * 4;
                var alpha = total > 0 ? a / total : 0;
                if (a > 0)
                {
                    result[o] = ToByte(b / a);
                    result[o + 1] = ToByte(g / a);
                    result[o + 2] = ToByte(r / a);
                }

                result[o + 3] = ToByte(alpha);
            }
        }

        return result;
    }

    /// <summary>
    /// Kennzahlen eines auf 60×90 verkleinerten Covers (wie make-data.ps1): unteres Band Zeilen 62–89, oberes Band 0–8, je
    /// mittlere senkrechte und waagerechte Helligkeitsänderung und Standardabweichung.
    /// </summary>
    public static SmearMetrics Smear(ReadOnlySpan<byte> bgra, int width = SmearWidth, int height = SmearHeight)
    {
        if (width != SmearWidth || height != SmearHeight || bgra.Length < width * height * 4)
        {
            throw new ArgumentException("Erwartet wird ein Bild mit 60 × 90 Pixeln.", nameof(bgra));
        }

        var luma = new double[width * height];
        for (var i = 0; i < width * height; i++)
        {
            luma[i] = (0.2126 * bgra[(i * 4) + 2]) + (0.7152 * bgra[(i * 4) + 1]) + (0.0722 * bgra[i * 4]);
        }

        var bottom = Band(luma, width, 62, 89);
        var top = Band(luma, width, 0, 8);
        return new SmearMetrics(bottom.V, bottom.H, bottom.Deviation, top.V, top.H, top.Deviation);
    }

    /// <summary>Mittlere Farbigkeit (größter minus kleinster Kanal, 0–255): Schwarzweiß-Splash einer Engine liegt nahe 0.</summary>
    public static double MeanChroma(ReadOnlySpan<byte> bgra)
    {
        var count = bgra.Length / 4;
        if (count == 0)
        {
            return 0;
        }

        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            int b = bgra[i * 4], g = bgra[(i * 4) + 1], r = bgra[(i * 4) + 2];
            sum += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        }

        return sum / count;
    }

    /// <summary>True, wenn kein Pixel durchsichtig ist (Deckkraft mindestens 250, wie make-data.ps1).</summary>
    public static bool IsOpaque(ReadOnlySpan<byte> bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] < 250)
            {
                return false;
            }
        }

        return bgra.Length >= 4;
    }

    /// <summary>
    /// Sichtbarer Teil eines Logos: Rahmen aller Pixel mit Deckkraft über <paramref name="alphaThreshold"/>, um
    /// <paramref name="padding"/> Pixel erweitert und auf das Bild begrenzt. Null, wenn kein Pixel sichtbar ist.
    /// </summary>
    public static PixelRect? OpaqueBounds(ReadOnlySpan<byte> bgra, int width, int height, int alphaThreshold = 8, int padding = 2)
    {
        int x0 = width, y0 = height, x1 = -1, y1 = -1;
        for (var y = 0; y < height; y++)
        {
            var row = bgra.Slice(y * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                if (row[(x * 4) + 3] > alphaThreshold)
                {
                    x0 = Math.Min(x0, x);
                    x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y);
                    y1 = Math.Max(y1, y);
                }
            }
        }

        if (x1 < 0)
        {
            return null;
        }

        x0 = Math.Max(0, x0 - padding);
        y0 = Math.Max(0, y0 - padding);
        x1 = Math.Min(width - 1, x1 + padding);
        y1 = Math.Min(height - 1, y1 + padding);
        return new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>
    /// Helle „HDR“-Fassung eines Bildes, je Pixel wie CSS <c>brightness(1.22) contrast(1.14) saturate(1.42)</c> (in dieser
    /// Reihenfolge, nach jedem Schritt auf 0–1 begrenzt). Ändert den Puffer an Ort und Stelle; Deckkraft bleibt.
    /// </summary>
    public static void Boost(Span<byte> bgra, double brightness = 1.22, double contrast = 1.14, double saturation = 1.42)
    {
        var lut = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var c = Math.Min(1, i / 255.0 * brightness);
            lut[i] = Math.Clamp(((c - 0.5) * contrast) + 0.5, 0, 1);
        }

        var s = saturation;
        double rr = 0.213 + (0.787 * s), rg = 0.715 - (0.715 * s), rb = 0.072 - (0.072 * s);
        double gr = 0.213 - (0.213 * s), gg = 0.715 + (0.285 * s), gb = 0.072 - (0.072 * s);
        double br = 0.213 - (0.213 * s), bg = 0.715 - (0.715 * s), bb = 0.072 + (0.928 * s);
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            double b = lut[bgra[i]], g = lut[bgra[i + 1]], r = lut[bgra[i + 2]];
            bgra[i + 2] = ToByte((rr * r) + (rg * g) + (rb * b));
            bgra[i + 1] = ToByte((gr * r) + (gg * g) + (gb * b));
            bgra[i] = ToByte((br * r) + (bg * g) + (bb * b));
        }
    }

    /// <summary>
    /// Monogramm wie im Prototyp (<c>mono()</c>): Genitiv-s und Zeichen wie ™®©:'"!?.,-–—()[] fallen weg, Bindestriche zählen
    /// nicht als Wort; erste Buchstaben der ersten beiden Wörter, bei nur einem Wort dessen erste zwei („Baldur's Gate 3“ →
    /// „BG“, „SWAT 3 - Tactical …“ → „S3“).
    /// </summary>
    public static string Monogram(string? name)
    {
        var text = Possessive().Replace(name ?? string.Empty, string.Empty);
        text = Separators().Replace(text, " ");
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w != "-" && w.Any(c => char.IsAsciiLetterOrDigit(c) || "ÄÖÜäöü".Contains(c)))
            .ToList();
        var first = words.Count > 0 ? words[0] : "?";
        var builder = new StringBuilder(2);
        builder.Append(first[0]);
        if (words.Count > 1)
        {
            builder.Append(words[1][0]);
        }
        else if (first.Length > 1)
        {
            builder.Append(first[1]);
        }

        return builder.ToString().ToUpper(CultureInfo.InvariantCulture);
    }

    /// <summary>HSL eines Farbwerts (Kanäle 0–255): Farbton in Grad (0–360), Sättigung und Helligkeit 0–1.</summary>
    public static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        r /= 255;
        g /= 255;
        b /= 255;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2;
        double h = 0, s = 0;
        if (max != min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
            h *= 60;
        }

        return (h, s, l);
    }

    /// <summary>„#RRGGBB“ aus HSL (Farbton in Grad, beliebig; Sättigung und Helligkeit 0–1), gerundet wie im Prototyp.</summary>
    public static string HslToHex(double h, double s, double l)
    {
        h = (((h % 360) + 360) % 360) / 360;
        double F(double n)
        {
            var k = (n + (h * 12)) % 12;
            var a = s * Math.Min(l, 1 - l);
            return l - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }

        return Hex(F(0) * 255, F(8) * 255, F(4) * 255);
    }

    /// <summary>Kanäle 0–255 aus „#RRGGBB“ (ungültig: schwarz).</summary>
    public static (int R, int G, int B) HexToRgb(string hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
        {
            return (0, 0, 0);
        }

        return ((n >> 16) & 255, (n >> 8) & 255, n & 255);
    }

    /// <summary>„#RRGGBB“ (Großbuchstaben) aus Kanälen 0–255, gerundet und begrenzt.</summary>
    public static string Hex(double r, double g, double b)
    {
        static int C(double v) => (int)Math.Clamp(Math.Floor(v + 0.5), 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{C(r):X2}{C(g):X2}{C(b):X2}");
    }

    /// <summary>OKLab einer Farbe (L, a, b).</summary>
    public static (double L, double A, double B) ToOklab(string hex)
    {
        var (r8, g8, b8) = HexToRgb(hex);
        double r = SrgbToLinear(r8), g = SrgbToLinear(g8), b = SrgbToLinear(b8);
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
        return ((0.2104542553 * l) + (0.793617785 * m) - (0.0040720468 * s),
            (1.9779984951 * l) - (2.428592205 * m) + (0.4505937099 * s),
            (0.0259040371 * l) + (0.7827717662 * m) - (0.808675766 * s));
    }

    /// <summary>„#RRGGBB“ aus OKLab (außerhalb von sRGB begrenzt).</summary>
    public static string FromOklab(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + (0.3963377774 * a) + (0.2158037573 * b), 3);
        var m = Math.Pow(lightness - (0.1055613458 * a) - (0.0638541728 * b), 3);
        var s = Math.Pow(lightness - (0.0894841775 * a) - (1.291485548 * b), 3);
        return Hex(
            LinearToSrgb((4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s)),
            LinearToSrgb((-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s)),
            LinearToSrgb((-0.0041960863 * l) - (0.7034186147 * m) + (1.707614701 * s)));
    }

    /// <summary>Chroma (OKLCH) einer Farbe.</summary>
    public static double Chroma(string hex)
    {
        var (_, a, b) = ToOklab(hex);
        return Math.Sqrt((a * a) + (b * b));
    }

    /// <summary>Farbe mit mindestens dieser Chroma (OKLCH, Farbton und Helligkeit bleiben); Grau (Chroma fast 0) bleibt grau.</summary>
    public static string MinChroma(string hex, double minimum)
    {
        var (l, a, b) = ToOklab(hex);
        var c = Math.Sqrt((a * a) + (b * b));
        if (c >= minimum || c < 1e-4)
        {
            return hex;
        }

        var k = minimum / c;
        return FromOklab(l, a * k, b * k);
    }

    /// <summary>Rundung wie JavaScript Math.round (halbe Werte nach oben).</summary>
    internal static int JsRound(double value) => (int)Math.Floor(value + 0.5);

    private static double SrgbToLinear(int c)
    {
        var v = c / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static double LinearToSrgb(double c) => 255 * (c <= 0.0031308 ? 12.92 * c : (1.055 * Math.Pow(Math.Max(0, c), 1 / 2.4)) - 0.055);

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Floor((v * 255) + 0.5), 0, 255);

    private static void Accumulate(ReadOnlySpan<byte> bgra, int width, int x, int y, double weight, ref double a, ref double r, ref double g, ref double b)
    {
        var o = ((y * width) + x) * 4;
        var alpha = bgra[o + 3] / 255.0 * weight;
        a += alpha;
        r += bgra[o + 2] / 255.0 * alpha;
        g += bgra[o + 1] / 255.0 * alpha;
        b += bgra[o] / 255.0 * alpha;
    }

    private static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b);
        return Math.Min(d, 360 - d);
    }

    private static (double V, double H, double Deviation) Band(double[] luma, int width, int y0, int y1)
    {
        double v = 0, h = 0, s = 0, s2 = 0;
        int nv = 0, nh = 0, n = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = luma[(y * width) + x];
                s += a;
                s2 += a * a;
                n++;
                if (y < y1)
                {
                    v += Math.Abs(luma[((y + 1) * width) + x] - a);
                    nv++;
                }

                if (x < width - 1)
                {
                    h += Math.Abs(luma[(y * width) + x + 1] - a);
                    nh++;
                }
            }
        }

        var mean = s / n;
        return (v / nv, h / nh, Math.Sqrt(Math.Max(0, (s2 / n) - (mean * mean))));
    }

    [GeneratedRegex(@"['’]s\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Possessive();

    [GeneratedRegex(@"[™®©:’'""!?.,\-–—()\[\]]", RegexOptions.CultureInvariant)]
    private static partial Regex Separators();

    private sealed class Bucket
    {
        public int N;
        public double R;
        public double G;
        public double B;
        public double S;
    }

    private sealed record Swatch(double H, double S, double L, double Weight);
}
