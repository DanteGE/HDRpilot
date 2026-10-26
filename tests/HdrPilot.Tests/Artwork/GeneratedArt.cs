using HdrPilot.Core.Artwork;

namespace HdrPilot.Tests.Artwork;

/// <summary>Erzeugtes Bild als BGRA (Alpha gerade).</summary>
internal sealed record ArtImage(byte[] Pixels, int Width, int Height)
{
    /// <summary>Spiegelbild (links ↔ rechts).</summary>
    public ArtImage Mirror()
    {
        var result = new byte[Pixels.Length];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                Array.Copy(Pixels, ((y * Width) + x) * 4, result, ((y * Width) + (Width - 1 - x)) * 4, 4);
            }
        }

        return new ArtImage(result, Width, Height);
    }

    /// <summary>Um einen Bruchteil eines Pixels verschoben und auf <paramref name="scale"/> verkleinert (gleiches Format).</summary>
    public ArtImage ShiftScale(double shift, double scale)
    {
        int tw = Math.Max(1, (int)Math.Round(Width * scale)), th = Math.Max(1, (int)Math.Round(Height * scale));
        return new ArtImage(PixelAnalysis.Resample(Pixels, Width, Height, shift, shift, Width, Height, tw, th), tw, th);
    }

    /// <summary>Rauschen ±<paramref name="amount"/> je Kanal (deterministisch), Alpha bleibt.</summary>
    public ArtImage Noise(int amount, uint seed)
    {
        var rng = new Lcg(seed);
        var result = (byte[])Pixels.Clone();
        for (var i = 0; i < result.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
            {
                result[i + c] = (byte)Math.Clamp(result[i + c] + rng.Next(-amount, amount), 0, 255);
            }
        }

        return new ArtImage(result, Width, Height);
    }
}

/// <summary>Aussehen eines erzeugten Logos (Schriftzug aus sechs Buchstaben mit Innenraum).</summary>
internal sealed record LogoSpec
{
    /// <summary>Füllung der Buchstaben; bei <see cref="LetterFills"/> je Buchstabe.</summary>
    public Rgba8 Fill { get; init; } = new(255, 255, 255);

    public Rgba8[]? LetterFills { get; init; }

    /// <summary>Senkrechter Verlauf der Füllung von oben nach unten (Gold-Metall).</summary>
    public (Rgba8 Top, Rgba8 Bottom)? Gradient { get; init; }

    /// <summary>Waagerechte Streifen je Buchstabe (Anteil von oben, Farbe), statt einer Füllung.</summary>
    public (double Share, Rgba8 Color)[]? Bands { get; init; }

    public int Outline { get; init; }

    public Rgba8 OutlineColor { get; init; } = new(0, 0, 0);

    public Rgba8? Shadow { get; init; }

    public Rgba8? Extrusion { get; init; }

    /// <summary>Leuchten: weichgezeichnete Form (Kastenfilter mit <see cref="HaloRadius"/>), Deckkraft höchstens 0,45.</summary>
    public Rgba8? Halo { get; init; }

    public int HaloRadius { get; init; } = 6;

    /// <summary>Gefüllter Kreis in der Mitte mit diesem Anteil an der Buchstabenfläche.</summary>
    public (double Share, Rgba8 Color)? Emblem { get; init; }

    /// <summary>Waagerechter Balken unter den Buchstaben mit diesem Anteil an der Buchstabenfläche.</summary>
    public (double Share, Rgba8 Color)? Bar { get; init; }

    /// <summary>Deckende Platte unter dem Schriftzug.</summary>
    public Rgba8? Plate { get; init; }
}

/// <summary>
/// Erzeugte Spielbilder für die Tests der Spielfarben (E94): Logos, Cover und Titelbilder aus einfachen Formen mit festem
/// Zufall (LCG). Nie echte Spielbilder. Verkleinert wird wie im Betrieb mit <see cref="PixelAnalysis.Resample"/>.
/// </summary>
internal static class GeneratedArt
{
    public const int LogoWidth = 384;
    public const int LogoHeight = 96;
    public const int LetterWidth = 44;
    public const int LetterHeight = 64;

    /// <summary>Fläche der sechs Buchstaben ohne Innenraum (Bezug für Emblem und Balken).</summary>
    public const int LetterArea = 6 * ((LetterWidth * LetterHeight) - (20 * 32));

    public static Rgba8 C(string hex) => Rgba8.Parse(hex);

    /// <summary>Schriftzug 384×96 auf durchsichtigem Grund: sechs Buchstaben 44×64 bei x = 20 + 58·i, y = 16, Innenraum 20×32.</summary>
    public static ArtImage Logo(LogoSpec spec)
    {
        const int w = LogoWidth, h = LogoHeight;
        var letters = new bool[w * h];
        var letterIndex = new int[w * h];
        for (var i = 0; i < 6; i++)
        {
            int x0 = 20 + (58 * i), y0 = 16;
            for (var y = y0; y < y0 + LetterHeight; y++)
            {
                for (var x = x0; x < x0 + LetterWidth; x++)
                {
                    var counter = x >= x0 + 12 && x < x0 + 32 && y >= y0 + 16 && y < y0 + 48;
                    if (!counter)
                    {
                        letters[(y * w) + x] = true;
                        letterIndex[(y * w) + x] = i;
                    }
                }
            }
        }

        var outlined = Dilate(letters, w, h, spec.Outline);
        var canvas = new double[w * h * 4]; // r, g, b, a (0–1, gerade)
        if (spec.Plate is { } plate)
        {
            Fill(canvas, w, h, (_, _) => (plate, 1.0));
        }

        if (spec.Halo is { } halo)
        {
            var blurred = Blur(outlined, w, h, spec.HaloRadius);
            Fill(canvas, w, h, (x, y) => (halo, Math.Min(0.45, blurred[(y * w) + x] * 0.45 / 0.5)));
        }

        if (spec.Extrusion is { } extrusion)
        {
            for (var d = 4; d >= 1; d--)
            {
                var dd = d;
                Fill(canvas, w, h, (x, y) => (extrusion, At(outlined, w, h, x - dd, y - dd) ? 1 : 0));
            }
        }

        if (spec.Shadow is { } shadow)
        {
            Fill(canvas, w, h, (x, y) => (shadow, At(outlined, w, h, x - 3, y - 3) ? 1 : 0));
        }

        if (spec.Outline > 0)
        {
            Fill(canvas, w, h, (x, y) => (spec.OutlineColor, outlined[(y * w) + x] ? 1 : 0));
        }

        Fill(canvas, w, h, (x, y) =>
        {
            var i = (y * w) + x;
            if (!letters[i])
            {
                return (default, 0);
            }

            if (spec.Bands is { } bands)
            {
                var t = (y - 16) / (double)LetterHeight;
                var acc = 0.0;
                foreach (var (share, color) in bands)
                {
                    acc += share;
                    if (t < acc)
                    {
                        return (color, 1);
                    }
                }

                return (bands[^1].Color, 1);
            }

            if (spec.Gradient is { } g)
            {
                var t = (y - 16) / (double)(LetterHeight - 1);
                return (Mix(g.Top, g.Bottom, t), 1);
            }

            return (spec.LetterFills is { } fills ? fills[letterIndex[i]] : spec.Fill, 1);
        });

        if (spec.Bar is { } bar)
        {
            // Balken über die ganze Breite des Schriftzugs, unter den Buchstaben (y ab 82).
            var height = Math.Max(1, (int)Math.Round(bar.Share * LetterArea / 334.0));
            Fill(canvas, w, h, (x, y) => (bar.Color, x >= 20 && x < 354 && y >= 82 && y < 82 + height ? 1 : 0));
        }

        if (spec.Emblem is { } emblem)
        {
            var r = Math.Sqrt(emblem.Share * LetterArea / Math.PI);
            Fill(canvas, w, h, (x, y) => (emblem.Color, Coverage(x, y, 192, 48, r, r)));
        }

        return ToImage(canvas, w, h);
    }

    /// <summary>Logo-Fassung wie im Betrieb (sichtbarer Teil, lange Seite 96 px).</summary>
    public static ArtImage ForAnalysis(ArtImage logo)
    {
        var (px, w, h) = GameColorAnalysis.PrepareLogo(logo.Pixels, logo.Width, logo.Height)!.Value;
        return new ArtImage(px, w, h);
    }

    /// <summary>Cover-Fassung wie im Betrieb.</summary>
    public static ArtImage CoverForAnalysis(ArtImage cover)
    {
        var (px, w, h) = GameColorAnalysis.PrepareCover(cover.Pixels, cover.Width, cover.Height)!.Value;
        return new ArtImage(px, w, h);
    }

    /// <summary>Titelbild-Fassung wie im Betrieb: 48 × <see cref="PixelAnalysis.AmbientHeight"/>, gestreckt.</summary>
    public static ArtImage HeroForAnalysis(ArtImage hero)
    {
        var th = PixelAnalysis.AmbientHeight(hero.Width, hero.Height);
        return new ArtImage(PixelAnalysis.Resample(hero.Pixels, hero.Width, hero.Height, 0, 0, hero.Width, hero.Height, 48, th), 48, th);
    }

    /// <summary>Cover 300×450 mit 2×2-Überabtastung und Rauschen ±3.</summary>
    public static ArtImage Cover(CoverScene scene, uint seed = 7) => Scene(300, 450, seed, (x, y) => CoverPixel(scene, x / 300.0, y / 450.0));

    /// <summary>Titelbild 960×320.</summary>
    public static ArtImage Hero(HeroScene scene, uint seed = 11) => Scene(960, 320, seed, (x, y) => HeroPixel(scene, x / 960.0, y / 320.0));

    /// <summary>Einfarbiges Bild (Gegenproben).</summary>
    public static ArtImage Solid(int width, int height, Rgba8 color)
    {
        var px = new byte[width * height * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = color.B;
            px[i + 1] = color.G;
            px[i + 2] = color.R;
            px[i + 3] = color.A;
        }

        return new ArtImage(px, width, height);
    }

    public static SourceResult? AnalyzeLogo(ArtImage logo) =>
        AnalyzeLogo(logo, null);

    internal static SourceResult? AnalyzeLogo(ArtImage logo, GameColorOptions? options)
    {
        var a = ForAnalysis(logo);
        return GameColorAnalysis.Analyze(a.Pixels, a.Width, a.Height, GameColorKind.Logo, options ?? GameColorOptions.Default);
    }

    internal static SourceResult? AnalyzeCover(ArtImage cover, GameColorOptions? options = null)
    {
        var a = CoverForAnalysis(cover);
        return GameColorAnalysis.Analyze(a.Pixels, a.Width, a.Height, GameColorKind.Cover, options ?? GameColorOptions.Default);
    }

    internal static SourceResult? AnalyzeHero(ArtImage hero, GameColorOptions? options = null)
    {
        var a = HeroForAnalysis(hero);
        return GameColorAnalysis.Analyze(a.Pixels, a.Width, a.Height, GameColorKind.Hero, options ?? GameColorOptions.Default);
    }

    private static (Rgba8 Color, double Alpha) CoverPixel(CoverScene scene, double u, double v)
    {
        switch (scene)
        {
            case CoverScene.NightPortrait:
                {
                    // Navy-Himmel, Gesicht (Hautton) in der Mitte, orangefarbenes Titelband (4 % der Fläche, zwei Zeilen der
                    // 48 Zeilen hohen Fassung) unten.
                    if (v is >= 40 / 48.0 and < 42 / 48.0)
                    {
                        return (C("#F28C28"), 1);
                    }

                    if (Ellipse(u, v, 0.5, 0.45, 0.16, 0.14))
                    {
                        return (C("#D9A07A"), 1);
                    }

                    return (Mix(C("#0B1F4D"), C("#060D24"), v), 1);
                }

            case CoverScene.BigFace:
                {
                    if (v is >= 42 / 48.0 and < 44 / 48.0)
                    {
                        return (C("#F28C28"), 1);
                    }

                    if (Ellipse(u, v, 0.5, 0.45, 0.35, 0.35))
                    {
                        return (Mix(C("#E0AC86"), C("#C68E6A"), v), 1);
                    }

                    // Einfarbiger dunkler Grund: ein Cluster, schwerer als das Gesicht.
                    return (C("#1E1E24"), 1);
                }

            case CoverScene.Jungle:
                {
                    var leaf = Math.Sin(u * 23) * Math.Cos(v * 17);
                    return (leaf > 0.3 ? C("#2E8B3A") : leaf < -0.3 ? C("#1B5E20") : C("#4CAF50"), 1);
                }

            case CoverScene.TealSciFi:
                {
                    if (v > 0.7)
                    {
                        return (C("#0A1416"), 1);
                    }

                    return (Mix(C("#00A3A3"), C("#006E73"), v), 1);
                }

            case CoverScene.Blue:
                return (Mix(C("#2F5BD8"), C("#1A3A9E"), v), 1);

            case CoverScene.Sepia:
                return (Mix(C("#9C8B74"), C("#5E5244"), v), 1);

            case CoverScene.Gray:
                return (Mix(C("#9A9A9A"), C("#3A3A3A"), v), 1);

            case CoverScene.Red:
                return (Mix(C("#C62828"), C("#7F1414"), v), 1);

            case CoverScene.RedOrange:
                // Rotorange, Farbton innerhalb von 40° zu #D62828.
                return (Mix(C("#E2502A"), C("#A8341A"), v), 1);

            default:
                throw new ArgumentOutOfRangeException(nameof(scene));
        }
    }

    private static (Rgba8 Color, double Alpha) HeroPixel(HeroScene scene, double u, double v) => scene switch
    {
        HeroScene.Sunset => v < 0.6 ? (Mix(C("#5B2A86"), C("#F26B3A"), v / 0.6), 1) : (Mix(C("#3A1A3A"), C("#1A0F1F"), (v - 0.6) / 0.4), 1),
        HeroScene.BlueSky => (Mix(C("#3D8FE0"), C("#9CC9F2"), v), 1),
        HeroScene.Gray => (Mix(C("#808080"), C("#303030"), v), 1),
        _ => throw new ArgumentOutOfRangeException(nameof(scene)),
    };

    private static ArtImage Scene(int width, int height, uint seed, Func<double, double, (Rgba8 Color, double Alpha)> pixel)
    {
        var rng = new Lcg(seed);
        var px = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double r = 0, g = 0, b = 0;
                for (var sy = 0; sy < 2; sy++)
                {
                    for (var sx = 0; sx < 2; sx++)
                    {
                        var (c, _) = pixel(x + 0.25 + (sx * 0.5), y + 0.25 + (sy * 0.5));
                        r += c.R;
                        g += c.G;
                        b += c.B;
                    }
                }

                var o = ((y * width) + x) * 4;
                px[o] = (byte)Math.Clamp((int)Math.Round(b / 4) + rng.Next(-3, 3), 0, 255);
                px[o + 1] = (byte)Math.Clamp((int)Math.Round(g / 4) + rng.Next(-3, 3), 0, 255);
                px[o + 2] = (byte)Math.Clamp((int)Math.Round(r / 4) + rng.Next(-3, 3), 0, 255);
                px[o + 3] = 255;
            }
        }

        return new ArtImage(px, width, height);
    }

    private static bool Ellipse(double u, double v, double cx, double cy, double rx, double ry)
    {
        double dx = (u - cx) / rx, dy = (v - cy) / ry;
        return (dx * dx) + (dy * dy) <= 1;
    }

    /// <summary>Deckung eines Pixels durch eine Ellipse (4×4 Stichproben).</summary>
    private static double Coverage(int x, int y, double cx, double cy, double rx, double ry)
    {
        var hits = 0;
        for (var j = 0; j < 4; j++)
        {
            for (var i = 0; i < 4; i++)
            {
                double dx = (x + 0.125 + (i * 0.25) - cx) / rx, dy = (y + 0.125 + (j * 0.25) - cy) / ry;
                hits += (dx * dx) + (dy * dy) <= 1 ? 1 : 0;
            }
        }

        return hits / 16.0;
    }

    private static Rgba8 Mix(Rgba8 a, Rgba8 b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgba8((byte)Math.Round(a.R + ((b.R - a.R) * t)), (byte)Math.Round(a.G + ((b.G - a.G) * t)), (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    private static bool At(bool[] mask, int w, int h, int x, int y) => x >= 0 && y >= 0 && x < w && y < h && mask[(y * w) + x];

    private static bool[] Dilate(bool[] mask, int w, int h, int n)
    {
        if (n <= 0)
        {
            return mask;
        }

        var result = new bool[mask.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                for (var dy = -n; dy <= n && !result[(y * w) + x]; dy++)
                {
                    for (var dx = -n; dx <= n; dx++)
                    {
                        if (At(mask, w, h, x + dx, y + dy))
                        {
                            result[(y * w) + x] = true;
                            break;
                        }
                    }
                }
            }
        }

        return result;
    }

    private static double[] Blur(bool[] mask, int w, int h, int r)
    {
        var result = new double[mask.Length];
        var area = (2 * r) + 1;
        area *= area;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var sum = 0;
                for (var dy = -r; dy <= r; dy++)
                {
                    for (var dx = -r; dx <= r; dx++)
                    {
                        sum += At(mask, w, h, x + dx, y + dy) ? 1 : 0;
                    }
                }

                result[(y * w) + x] = sum / (double)area;
            }
        }

        return result;
    }

    /// <summary>Malt eine Ebene „über“ die bisherigen (gerades Alpha).</summary>
    private static void Fill(double[] canvas, int w, int h, Func<int, int, (Rgba8 Color, double Alpha)> layer)
    {
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (c, a) = layer(x, y);
                if (a <= 0)
                {
                    continue;
                }

                var o = ((y * w) + x) * 4;
                var da = canvas[o + 3];
                var oa = a + (da * (1 - a));
                for (var k = 0; k < 3; k++)
                {
                    var src = (k == 0 ? c.R : k == 1 ? c.G : c.B) / 255.0;
                    canvas[o + k] = ((src * a) + (canvas[o + k] * da * (1 - a))) / oa;
                }

                canvas[o + 3] = oa;
            }
        }
    }

    private static ArtImage ToImage(double[] canvas, int w, int h)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            px[(i * 4) + 2] = (byte)Math.Round(canvas[(i * 4)] * 255);
            px[(i * 4) + 1] = (byte)Math.Round(canvas[(i * 4) + 1] * 255);
            px[i * 4] = (byte)Math.Round(canvas[(i * 4) + 2] * 255);
            px[(i * 4) + 3] = (byte)Math.Round(canvas[(i * 4) + 3] * 255);
        }

        return new ArtImage(px, w, h);
    }
}

internal enum CoverScene
{
    NightPortrait,
    Jungle,
    TealSciFi,
    Blue,
    Sepia,
    Gray,
    BigFace,
    Red,
    RedOrange,
}

internal enum HeroScene
{
    Sunset,
    BlueSky,
    Gray,
}

/// <summary>Fester Zufall (lineare Kongruenz), damit erzeugte Bilder überall gleich sind.</summary>
internal sealed class Lcg(uint seed)
{
    private uint _state = seed == 0 ? 1u : seed;

    /// <summary>Ganze Zahl in [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public int Next(int min, int max)
    {
        _state = unchecked((_state * 1664525u) + 1013904223u);
        return min + (int)((_state >> 8) % (uint)(max - min + 1));
    }
}
