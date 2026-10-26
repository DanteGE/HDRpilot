namespace HdrPilot.Core.Artwork;

/// <summary>
/// Schalter der Regeln von <see cref="GameColorAnalysis"/>. Alle an im Betrieb; Tests schalten einzelne Regeln ab
/// (Gegenproben), ohne gemeinsamen Zustand zu ändern.
/// </summary>
internal sealed record GameColorOptions(
    bool SkinRule = true,
    bool FrameExclusion = true,
    bool FillOverOutline = true,
    bool HaloRule = true,
    bool Families = true,
    bool LogoDominance = true)
{
    public static GameColorOptions Default { get; } = new();
}

/// <summary>Farbfamilie einer Quelle: Mittel in OKLab (gewichtet nach Gewicht × Chroma), Anteil und Anteil am Rand.</summary>
/// <param name="Share">Anteil am Gewicht ohne Rahmen.</param>
/// <param name="EdgeShare">Anteil an den Randpixeln des Logos (0 bei Cover und Titelbild).</param>
/// <param name="Skin">Farbe im Bereich der Hauttöne.</param>
/// <param name="Qualifies">Erfüllt Chroma, Helligkeit und Mindestanteil.</param>
/// <param name="Score">Bewertung als Akzent (0, wenn die Familie kein Akzent sein darf).</param>
public sealed record ColorFamily(int Id, Oklab Mean, double Weight, double Share, double EdgeShare, bool Skin, bool Qualifies, double Score)
{
    public string Hex => ColorScience.ToRgba8(Mean).Hex;
}

/// <summary>Kennzahlen eines Laufs (für die Kostengrenzen der Tests).</summary>
public readonly record struct GameColorAnalysisStats(int Pixels, int Bins, int LongestArray, int Clusters, int Iterations);

/// <summary>Ergebnis einer Quelle (Logo, Cover oder Titelbild).</summary>
/// <param name="Best">Familie des Akzents; null, wenn keine taugt.</param>
/// <param name="Accent">Verfeinerter Akzent (OKLab); null ohne <paramref name="Best"/>.</param>
/// <param name="ChromaShare">Gewicht der tauglichen Familien geteilt durch das Gewicht ohne Rahmen (beim Leuchten: farbiges Leuchten geteilt durch den Körper).</param>
/// <param name="ViaHalo">Das Ergebnis stammt aus dem Leuchten um eine farblose Schrift.</param>
public sealed record SourceResult(
    GameColorKind Kind,
    IReadOnlyList<ColorFamily> Families,
    ColorFamily? Best,
    Oklab? Accent,
    double Confidence,
    double ChromaShare,
    bool ViaHalo,
    GameColorAnalysisStats Stats)
{
    public string? AccentHex => Accent is { } a ? ColorScience.ToRgba8(a).Hex : null;
}

/// <summary>
/// Spielfarben aus kleinen Fassungen von Logo, Cover und Titelbild (E94). Deterministisch und ohne WPF: Pixel in OKLab,
/// dünn besetzte Raster-Fächer (sortiert statt Wörterbuch), feste Startpunkte und gewichtetes k-Means, Farbfamilien nach
/// Farbton; Rahmen (dunkle oder graue Konturen), Hauttöne und kleine Punkte zählen nicht. Bei einem Logo gewinnt eine
/// Füllung gegen ihre farbige Kontur und das Leuchten um eine weiße Schrift. <see cref="Derive"/> wählt die Quelle des
/// Akzents (Vorrang des Logos) und die Kandidaten für Atmosphäre und Glanz. Eingepasst wird erst in der App.
/// </summary>
public static class GameColorAnalysis
{
    /// <summary>Fassung des Verfahrens; ein anderer Wert im Verzeichnis wird neu berechnet.</summary>
    public const int Version = 1;

    /// <summary>Größte Breite beim Dekodieren eines ungeschnittenen Logos.</summary>
    public const int LogoDecodeWidth = 384;

    /// <summary>Lange Seite der Logo-Fassung für die Analyse.</summary>
    public const int LogoLongSide = 96;

    /// <summary>Kurze Seite der Logo-Fassung mindestens.</summary>
    public const int LogoShortSideMin = 8;

    /// <summary>Breite, in der Cover und Titelbild dekodiert werden, bevor sie verkleinert werden.</summary>
    public const int DecodeWidth = 96;

    internal const int BodyAlpha = 128;
    internal const int HaloAlpha = 24;
    internal const int OpaqueAlpha = 250;
    internal const double RingWeight = 0.5;
    internal const int LSteps = 20;
    internal const int AbSteps = 32;
    internal const double LStep = 0.05;
    internal const double AbStep = 0.025;
    internal const double AbOrigin = -0.4;
    internal const double SeedMinimum = 0.002;
    internal const double SeedDistance = 0.05;
    internal const int LogoClusters = 6;
    internal const int ImageClusters = 8;
    internal const int LloydIterations = 12;
    internal const double MergeDistance = 0.045;
    internal const double FrameChroma = 0.05;
    internal const double FrameLightness = 0.30;
    internal const double FrameEdge = 0.5;
    internal const double FrameShare = 0.5;
    internal const double FamilyHue = 20;
    internal const double FamilyChromaRatio = 0.6;
    internal const double MinChroma = 0.035;
    internal const double LogoM = 0.02;
    internal const double ImageM = 0.03;
    internal const double LogoChromaShare = 0.10;
    internal const double LogoAgreement = 40;
    internal const double FillEdge = 0.5;
    internal const double FillShare = 0.35;
    internal const double FillOtherEdge = 0.2;
    internal const double FillChroma = 0.07;
    internal const double HaloChromaticBody = 0.10;
    internal const double HaloShare = 0.35;
    internal const double HaloChromatic = 0.5;
    internal const double SecondaryLogoShare = 0.15;

    /// <summary>Größe der Logo-Fassung: lange Seite 96 px, kurze Seite mindestens 8 px.</summary>
    public static (int Width, int Height) LogoSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return (LogoLongSide, LogoShortSideMin);
        }

        return width >= height
            ? (LogoLongSide, Math.Clamp((int)Math.Round(LogoLongSide * (double)height / width), LogoShortSideMin, LogoLongSide))
            : (Math.Clamp((int)Math.Round(LogoLongSide * (double)width / height), LogoShortSideMin, LogoLongSide), LogoLongSide);
    }

    /// <summary>Größe der Cover-Fassung: 32×48 (Hochformat), 40×40 (Seitenverhältnis 0,83–1,2) oder 48×32.</summary>
    public static (int Width, int Height) CoverSize(int width, int height)
    {
        var aspect = width / (double)Math.Max(1, height);
        return aspect < 0.83 ? (32, 48) : aspect <= 1.2 ? (40, 40) : (48, 32);
    }

    /// <summary>
    /// Logo-Fassung für die Analyse: sichtbarer Teil (<see cref="PixelAnalysis.OpaqueBounds"/>) im Speicher ausgeschnitten und
    /// mit vormultipliziertem Alpha auf <see cref="LogoSize"/> umgerechnet. Null, wenn nichts sichtbar ist.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height)? PrepareLogo(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return null;
        }

        if (PixelAnalysis.OpaqueBounds(bgra, width, height) is not { } r)
        {
            return null;
        }

        var (tw, th) = LogoSize(r.Width, r.Height);
        return (PixelAnalysis.Resample(bgra, width, height, r.X, r.Y, r.Width, r.Height, tw, th), tw, th);
    }

    /// <summary>Cover-Fassung für die Analyse (<see cref="CoverSize"/>), flächengemittelt.</summary>
    public static (byte[] Pixels, int Width, int Height)? PrepareCover(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return null;
        }

        var (tw, th) = CoverSize(width, height);
        return (PixelAnalysis.Resample(bgra, width, height, 0, 0, width, height, tw, th), tw, th);
    }

    /// <summary>Liest die Farbfamilien einer Quelle. Null, wenn kein Pixel deckend genug ist.</summary>
    public static SourceResult? Analyze(ReadOnlySpan<byte> bgra, int width, int height, GameColorKind kind) =>
        Analyze(bgra, width, height, kind, GameColorOptions.Default);

    internal static SourceResult? Analyze(ReadOnlySpan<byte> bgra, int width, int height, GameColorKind kind, GameColorOptions options)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return null;
        }

        var logo = kind == GameColorKind.Logo;
        var transparentLogo = logo && !IsOpaque(bgra, width * height);
        var n = width * height;
        var body = new Samples(n);
        var halo = transparentLogo ? new Samples(n) : null;
        double bodyWeight = 0, chromaticBody = 0, haloWeight = 0, chromaticHalo = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var o = ((y * width) + x) * 4;
                int alpha = bgra[o + 3];
                if (alpha < HaloAlpha || (alpha < BodyAlpha && halo is null))
                {
                    continue;
                }

                var lab = ColorScience.ToOklab(
                    ColorScience.ToLinear(bgra[o + 2]), ColorScience.ToLinear(bgra[o + 1]), ColorScience.ToLinear(bgra[o]));
                var chromatic = lab.C >= MinChroma;
                if (alpha < BodyAlpha)
                {
                    var hw = alpha / 255.0;
                    halo!.Add(lab, hw, ring: false);
                    haloWeight += hw;
                    chromaticHalo += chromatic ? hw : 0;
                    continue;
                }

                var ring = transparentLogo && IsRing(bgra, width, height, x, y);
                var w = alpha / 255.0 * (ring ? RingWeight : 1);
                body.Add(lab, w, ring);
                bodyWeight += w;
                chromaticBody += chromatic ? w : 0;
            }
        }

        if (halo is not null && options.HaloRule && bodyWeight > 0
            && chromaticBody < HaloChromaticBody * bodyWeight
            && haloWeight >= HaloShare * bodyWeight
            && chromaticHalo >= HaloChromatic * haloWeight)
        {
            // Weiße Schrift mit farbigem Leuchten: das Leuchten ist die Markenfarbe (ohne Rand- und Rahmenregeln).
            var fromHalo = Run(halo, kind, logo, edges: false, options);
            if (fromHalo is not null)
            {
                return fromHalo with { ChromaShare = Math.Round(chromaticHalo / bodyWeight, 4), ViaHalo = true };
            }
        }

        return Run(body, kind, logo, edges: transparentLogo, options);
    }

    /// <summary>
    /// Wählt den Akzent aus den Quellen (E94): Das Logo gewinnt, wenn es sicher ist und farbig genug (mindestens ein Zehntel
    /// farbige Fläche), zum Cover bzw. Titelbild passt (höchstens 40° Farbtonabstand) oder diese nicht sicher sind; sonst das
    /// Cover, sonst das Titelbild. Dazu bis zu drei Kandidaten für Atmosphäre und Glanz aus den übrigen Familien.
    /// </summary>
    public static GameColors Derive(SourceResult? logo, SourceResult? cover, SourceResult? hero) =>
        Derive(logo, cover, hero, GameColorOptions.Default);

    internal static GameColors Derive(SourceResult? logo, SourceResult? cover, SourceResult? hero, GameColorOptions options)
    {
        var reference = new[] { cover, hero }.FirstOrDefault(r => r is { Accent: not null } && r.Confidence >= GameColors.Accept);
        var logoWins = logo is { Accent: not null } && logo.Confidence >= GameColors.Accept
            && (!options.LogoDominance
                || logo.ChromaShare >= LogoChromaShare
                || reference is null
                || ColorScience.HueDistance(logo.Accent.Value.H, reference.Accent!.Value.H) <= LogoAgreement);
        var source = logoWins ? logo : reference;
        if (source is null || source.Accent is not { } accent || source.Best is not { } accentFamily)
        {
            var max = new[] { logo, cover, hero }.Max(r => r?.Confidence ?? 0);
            return new GameColors(null, GameColorSource.None, Math.Round(max, 2), [], []);
        }

        var accentSource = SourceOf(source.Kind);
        var accentHue = accent.H;

        // Kandidaten aus allen Familien außer der des Akzents.
        var candidates = new List<Candidate>();
        void Collect(SourceResult? result, double weight, bool secondaryLogo)
        {
            if (result is null)
            {
                return;
            }

            foreach (var f in result.Families)
            {
                if (ReferenceEquals(result, source) && f.Id == accentFamily.Id)
                {
                    continue;
                }

                candidates.Add(new Candidate(f, SourceOf(result.Kind), weight, secondaryLogo));
            }
        }

        if (logoWins)
        {
            Collect(logo, 1.2, secondaryLogo: true);
        }

        Collect(cover, 1.0, secondaryLogo: false);
        Collect(hero, 0.8, secondaryLogo: false);

        var atmospheres = candidates
            .Where(c => c.Family.Mean.C >= 0.07 && c.Family.Mean.L is >= 0.30 and <= 0.92
                && (c.SecondaryLogo ? c.Family.Share >= SecondaryLogoShare * accentFamily.Share : c.Family.Share >= 0.01))
            .Select(c => (c, d: ColorScience.HueDistance(c.Family.Mean.H, accentHue)))
            .Where(t => t.d is >= 12 and <= 120)
            .Select(t => (t.c, score: t.c.Weight * Math.Sqrt(t.c.Family.Share) * ColorScience.Smooth(0.015, 0.045, t.c.Family.Share)
                * Math.Min(t.c.Family.Mean.C, 0.2) * Separation(t.d) * (t.c.Family.Skin ? 0.5 : 1)))
            .Where(t => t.score > 0.012)
            .OrderByDescending(t => t.score)
            .Take(3)
            .Select(t => t.c)
            .ToList();
        var firstAtmosphere = atmospheres.Count > 0 ? atmospheres[0] : null;

        var shines = candidates
            .Where(c => !ReferenceEquals(c, firstAtmosphere)
                && c.Family.Mean.C >= 0.04 && c.Family.Mean.L >= 0.62
                && (c.SecondaryLogo ? c.Family.Share >= SecondaryLogoShare * accentFamily.Share : c.Family.Share >= 0.005)
                && ColorScience.HueDistance(c.Family.Mean.H, accentHue) >= 25)
            .Select(c => (c, score: c.Weight * Math.Sqrt(c.Family.Share) * ColorScience.Smooth(0.01, 0.03, c.Family.Share)
                * Math.Min(c.Family.Mean.C, 0.2) * c.Family.Mean.L * (c.Family.Skin ? 0.5 : 1)))
            .Where(t => t.score > 0)
            .OrderByDescending(t => t.score)
            .Take(3)
            .Select(t => t.c)
            .ToList();

        return new GameColors(
            ColorScience.ToRgba8(accent).Hex,
            accentSource,
            Math.Round(source.Confidence, 2),
            [.. atmospheres.Select(c => new GameSwatch(c.Family.Hex, c.Source))],
            [.. shines.Select(c => new GameSwatch(c.Family.Hex, c.Source))]);
    }

    /// <summary>Gewicht des Abstands Akzent–Atmosphäre: 12–25° steigend, 25–90° voll, 90–120° fallend.</summary>
    internal static double Separation(double d) => d switch
    {
        < 12 => 0,
        < 25 => 0.5 + (0.5 * (d - 12) / 13),
        <= 90 => 1,
        <= 120 => 1 - (0.5 * (d - 90) / 30),
        _ => 0,
    };

    private static GameColorSource SourceOf(GameColorKind kind) => kind switch
    {
        GameColorKind.Logo => GameColorSource.Logo,
        GameColorKind.Cover => GameColorSource.Cover,
        _ => GameColorSource.Hero,
    };

    private static bool IsOpaque(ReadOnlySpan<byte> bgra, int pixels)
    {
        for (var i = 0; i < pixels; i++)
        {
            if (bgra[(i * 4) + 3] < OpaqueAlpha)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Randpixel eines Logos: am Bildrand oder mit einem Nachbarn (4er-Nachbarschaft) unter der Körper-Deckkraft.</summary>
    private static bool IsRing(ReadOnlySpan<byte> bgra, int width, int height, int x, int y)
    {
        if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
        {
            return true;
        }

        return bgra[((((y - 1) * width) + x) * 4) + 3] < BodyAlpha
            || bgra[((((y + 1) * width) + x) * 4) + 3] < BodyAlpha
            || bgra[((((y * width) + x - 1) * 4) + 3)] < BodyAlpha
            || bgra[((((y * width) + x + 1) * 4) + 3)] < BodyAlpha;
    }

    private static SourceResult? Run(Samples samples, GameColorKind kind, bool logo, bool edges, GameColorOptions options)
    {
        var bins = Bin(samples);
        if (bins.Count == 0 || bins.Total <= 0)
        {
            return null;
        }

        var k = logo ? LogoClusters : ImageClusters;
        var (clusters, iterations) = Cluster(bins, k);
        double total = bins.Total, totalRing = bins.TotalRing;

        // Rahmen: dunkle oder graue Kontur, die am Rand des Logos liegt, aber nicht die Hauptfläche ist.
        var frameWeight = 0.0;
        if (edges && options.FrameExclusion && totalRing > 0)
        {
            foreach (var c in clusters)
            {
                var edge = EdgeShare(c.Ring, c.Weight, totalRing);
                if ((c.Mean.C < FrameChroma || c.Mean.L < FrameLightness) && edge >= FrameEdge && c.Weight / total < FrameShare)
                {
                    c.Frame = true;
                    frameWeight += c.Weight;
                }
            }
        }

        var rest = Math.Max(1e-12, total - frameWeight);
        var heaviest = clusters.Where(c => !c.Frame).OrderByDescending(c => c.Weight).ThenBy(c => c.Index).FirstOrDefault();

        // Farbfamilien: Cluster mit Chroma, nach Gewicht; gleicher Farbton (≤ 20°) und ähnliche Chroma (≥ 0,6) gehören zusammen.
        var groups = new List<List<ClusterData>>();
        foreach (var c in clusters.Where(c => !c.Frame && c.Mean.C >= MinChroma).OrderByDescending(c => c.Weight).ThenBy(c => c.Index))
        {
            List<ClusterData>? home = null;
            if (options.Families)
            {
                foreach (var g in groups)
                {
                    var mean = FamilyMean(g);
                    double cmin = Math.Min(mean.C, c.Mean.C), cmax = Math.Max(mean.C, c.Mean.C);
                    if (ColorScience.HueDistance(mean.H, c.Mean.H) <= FamilyHue && cmin / cmax >= FamilyChromaRatio)
                    {
                        home = g;
                        break;
                    }
                }
            }

            if (home is null)
            {
                groups.Add([c]);
            }
            else
            {
                home.Add(c);
            }
        }

        var m = logo ? LogoM : ImageM;
        var families = new List<ColorFamily>(groups.Count);
        for (var id = 0; id < groups.Count; id++)
        {
            var g = groups[id];
            var mean = FamilyMean(g);
            var weight = g.Sum(c => c.Weight);
            var share = weight / rest;
            var edge = edges && totalRing > 0 ? EdgeShare(g.Sum(c => c.Ring), weight, totalRing) : 0;
            var skin = mean.H is >= 40 and <= 80 && mean.C is >= 0.04 and <= 0.125 && mean.L is >= 0.40 and <= 0.92;
            var qualifies = mean.C >= MinChroma && mean.L is >= 0.22 and <= 0.97 && share >= 0.25 * m;
            var eligible = qualifies && !(skin && !logo && options.SkinRule);
            var score = 0.0;
            if (eligible)
            {
                var distinct = logo || heaviest is null
                    ? 1
                    : 0.5 + (0.5 * Math.Clamp(ColorScience.DeltaE(mean, heaviest.Mean) / 0.12, 0, 1));
                score = Math.Sqrt(share) * ColorScience.Smooth(0.5 * m, 1.5 * m, share) * Math.Min(mean.C, 0.22)
                    * ColorScience.Smooth(0.22, 0.40, mean.L) * (1 - (0.5 * ColorScience.Smooth(0.90, 0.97, mean.L))) * distinct;
            }

            families.Add(new ColorFamily(id, mean, weight, share, edge, skin, qualifies, score));
        }

        var best = families.Where(f => f.Score > 0)
            .OrderByDescending(f => f.Score).ThenByDescending(f => f.Weight).ThenBy(f => f.Id)
            .FirstOrDefault();

        // Füllung vor Kontur: Besitzt der beste den Rand und gibt es eine kaum am Rand liegende Füllung, gewinnt die Füllung.
        if (best is not null && edges && options.FillOverOutline && best.EdgeShare >= FillEdge)
        {
            var fill = families
                .Where(f => f.Id != best.Id && f.Score > 0 && f.Mean.C >= FillChroma && f.Share >= FillShare * best.Share
                    && f.EdgeShare <= FillOtherEdge)
                .OrderByDescending(f => f.Score).ThenBy(f => f.Id)
                .FirstOrDefault();
            best = fill ?? best;
        }

        Oklab? accent = null;
        var confidence = 0.0;
        if (best is not null)
        {
            accent = Refine(bins, groups[best.Id], best.Mean);
            confidence = ColorScience.Smooth(0.03, 0.07, accent.Value.C) * ColorScience.Smooth(0.5 * m, 2 * m, best.Share);
        }

        var chromaShare = families.Where(f => f.Qualifies).Sum(f => f.Weight) / rest;
        var stats = new GameColorAnalysisStats(samples.Count, bins.Count, samples.Capacity, clusters.Count, iterations);
        return new SourceResult(kind, families, best, accent, Math.Round(confidence, 4), Math.Round(chromaShare, 4), false, stats);
    }

    /// <summary>
    /// Randanteil: der größere von „Anteil am ganzen Rand des Logos“ (eine Kontur trägt fast den ganzen Rand) und „Anteil der
    /// eigenen Fläche, der am Rand liegt“ (dünne Kontur, Schatten oder Kante liegen fast ganz am Rand).
    /// </summary>
    private static double EdgeShare(double ring, double weight, double totalRing) =>
        Math.Max(ring / totalRing, weight > 0 ? ring / weight : 0);

    /// <summary>Mittel einer Familie, gewichtet nach Gewicht × Chroma.</summary>
    private static Oklab FamilyMean(List<ClusterData> group)
    {
        double sw = 0, l = 0, a = 0, b = 0;
        foreach (var c in group)
        {
            var w = c.Weight * Math.Max(c.Mean.C, 1e-6);
            sw += w;
            l += w * c.Mean.L;
            a += w * c.Mean.A;
            b += w * c.Mean.B;
        }

        return new Oklab(l / sw, a / sw, b / sw);
    }

    /// <summary>Akzent: Mittel (Gewicht × Chroma) der Fächer der Familie mit gleichem Farbton und mindestens 0,6-facher Chroma.</summary>
    private static Oklab Refine(Bins bins, List<ClusterData> group, Oklab mean)
    {
        double sw = 0, l = 0, a = 0, b = 0;
        foreach (var c in group)
        {
            foreach (var i in c.Members)
            {
                var lab = bins.Mean(i);
                var chroma = lab.C;
                if (ColorScience.HueDistance(lab.H, mean.H) > FamilyHue || chroma < FamilyChromaRatio * mean.C)
                {
                    continue;
                }

                var w = bins.Weight[i] * chroma;
                sw += w;
                l += w * lab.L;
                a += w * lab.A;
                b += w * lab.B;
            }
        }

        return sw > 0 ? new Oklab(l / sw, a / sw, b / sw) : mean;
    }

    /// <summary>Dünn besetzte Fächer: Schlüssel je Pixel, sortiert, gleiche Schlüssel aufsummiert (in Schlüsselreihenfolge).</summary>
    private static Bins Bin(Samples s)
    {
        var count = s.Count;
        var order = new int[count];
        var keys = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
            keys[i] = s.Key[i];
        }

        Array.Sort(keys, order);
        var bins = new Bins(count);
        var j = 0;
        while (j < count)
        {
            var key = keys[j];
            double w = 0, l = 0, a = 0, b = 0, ring = 0;
            for (; j < count && keys[j] == key; j++)
            {
                var p = order[j];
                w += s.W[p];
                l += s.WL[p];
                a += s.WA[p];
                b += s.WB[p];
                ring += s.Ring[p];
            }

            if (w > 0)
            {
                bins.Add(w, l / w, a / w, b / w, ring);
            }
        }

        return bins;
    }

    private static (List<ClusterData> Clusters, int Iterations) Cluster(Bins bins, int k)
    {
        var total = bins.Total;
        var seeds = new List<Oklab>(k);
        var first = 0;
        for (var i = 1; i < bins.Count; i++)
        {
            if (bins.Weight[i] > bins.Weight[first])
            {
                first = i;
            }
        }

        seeds.Add(bins.Mean(first));
        while (seeds.Count < k)
        {
            var bestIndex = -1;
            var bestScore = 0.0;
            for (var i = 0; i < bins.Count; i++)
            {
                if (bins.Weight[i] < SeedMinimum * total)
                {
                    continue;
                }

                var lab = bins.Mean(i);
                var d = seeds.Min(s => ColorScience.DeltaE(s, lab));
                if (d < SeedDistance)
                {
                    continue;
                }

                var score = Math.Sqrt(bins.Weight[i] / total) * d * d;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            seeds.Add(bins.Mean(bestIndex));
        }

        // Gewichtetes k-Means (Lloyd), höchstens 12 Durchgänge; Gleichstand geht an den kleineren Index.
        var centroids = seeds.ToArray();
        var assign = new int[bins.Count];
        Array.Fill(assign, -1);
        var iterations = 0;
        for (var it = 0; it < LloydIterations; it++)
        {
            iterations++;
            var changed = false;
            for (var i = 0; i < bins.Count; i++)
            {
                var lab = bins.Mean(i);
                var nearest = 0;
                var nd = double.MaxValue;
                for (var c = 0; c < centroids.Length; c++)
                {
                    var d = ColorScience.DeltaE(centroids[c], lab);
                    if (d < nd)
                    {
                        nd = d;
                        nearest = c;
                    }
                }

                if (assign[i] != nearest)
                {
                    assign[i] = nearest;
                    changed = true;
                }
            }

            for (var c = 0; c < centroids.Length; c++)
            {
                double sw = 0, l = 0, a = 0, b = 0;
                for (var i = 0; i < bins.Count; i++)
                {
                    if (assign[i] == c)
                    {
                        var w = bins.Weight[i];
                        sw += w;
                        l += w * bins.L[i];
                        a += w * bins.A[i];
                        b += w * bins.B[i];
                    }
                }

                if (sw > 0)
                {
                    centroids[c] = new Oklab(l / sw, a / sw, b / sw);
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var clusters = new List<ClusterData>(centroids.Length);
        for (var c = 0; c < centroids.Length; c++)
        {
            var data = new ClusterData(c);
            for (var i = 0; i < bins.Count; i++)
            {
                if (assign[i] == c)
                {
                    data.Absorb(i, bins);
                }
            }

            if (data.Weight > 0)
            {
                clusters.Add(data);
            }
        }

        // Nahe Cluster zusammenlegen; der schwerere nimmt den anderen auf.
        while (true)
        {
            int ia = -1, ib = -1;
            var nearest = MergeDistance;
            for (var a = 0; a < clusters.Count; a++)
            {
                for (var b = a + 1; b < clusters.Count; b++)
                {
                    var d = ColorScience.DeltaE(clusters[a].Mean, clusters[b].Mean);
                    if (d < nearest)
                    {
                        nearest = d;
                        ia = a;
                        ib = b;
                    }
                }
            }

            if (ia < 0)
            {
                break;
            }

            var (keep, drop) = clusters[ia].Weight >= clusters[ib].Weight ? (clusters[ia], clusters[ib]) : (clusters[ib], clusters[ia]);
            foreach (var i in drop.Members)
            {
                keep.Absorb(i, bins);
            }

            clusters.Remove(drop);
        }

        return (clusters, iterations);
    }

    private sealed record Candidate(ColorFamily Family, GameColorSource Source, double Weight, bool SecondaryLogo);

    /// <summary>Pixel einer Quelle: Fächer-Schlüssel und Gewichte in Feldern von der Größe der Pixelzahl.</summary>
    private sealed class Samples(int capacity)
    {
        public int Capacity { get; } = capacity;

        public int Count { get; private set; }

        public int[] Key { get; } = new int[capacity];

        public double[] W { get; } = new double[capacity];

        public double[] WL { get; } = new double[capacity];

        public double[] WA { get; } = new double[capacity];

        public double[] WB { get; } = new double[capacity];

        public double[] Ring { get; } = new double[capacity];

        public void Add(Oklab lab, double w, bool ring)
        {
            var l = Math.Clamp((int)Math.Floor(lab.L / LStep), 0, LSteps - 1);
            var a = Math.Clamp((int)Math.Floor((lab.A - AbOrigin) / AbStep), 0, AbSteps - 1);
            var b = Math.Clamp((int)Math.Floor((lab.B - AbOrigin) / AbStep), 0, AbSteps - 1);
            var i = Count++;
            Key[i] = (l * 1024) + (a * 32) + b;
            W[i] = w;
            WL[i] = w * lab.L;
            WA[i] = w * lab.A;
            WB[i] = w * lab.B;
            Ring[i] = ring ? w : 0;
        }
    }

    /// <summary>Besetzte Fächer in Schlüsselreihenfolge: Gewicht, Mittel und Randgewicht.</summary>
    private sealed class Bins(int capacity)
    {
        public int Count { get; private set; }

        public double Total { get; private set; }

        public double TotalRing { get; private set; }

        public double[] Weight { get; } = new double[capacity];

        public double[] L { get; } = new double[capacity];

        public double[] A { get; } = new double[capacity];

        public double[] B { get; } = new double[capacity];

        public double[] RingWeight { get; } = new double[capacity];

        public Oklab Mean(int i) => new(L[i], A[i], B[i]);

        public void Add(double w, double l, double a, double b, double ring)
        {
            var i = Count++;
            Weight[i] = w;
            L[i] = l;
            A[i] = a;
            B[i] = b;
            RingWeight[i] = ring;
            Total += w;
            TotalRing += ring;
        }
    }

    private sealed class ClusterData(int index)
    {
        private double _l;
        private double _a;
        private double _b;

        public int Index { get; } = index;

        public List<int> Members { get; } = [];

        public double Weight { get; private set; }

        public double Ring { get; private set; }

        public bool Frame { get; set; }

        public Oklab Mean => Weight > 0 ? new Oklab(_l / Weight, _a / Weight, _b / Weight) : default;

        public void Absorb(int bin, Bins bins)
        {
            var w = bins.Weight[bin];
            Members.Add(bin);
            Weight += w;
            Ring += bins.RingWeight[bin];
            _l += w * bins.L[bin];
            _a += w * bins.A[bin];
            _b += w * bins.B[bin];
        }
    }
}
