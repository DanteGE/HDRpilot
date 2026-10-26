using HdrPilot.Core.Artwork;
using static HdrPilot.Tests.Artwork.GeneratedArt;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// Spielfarben aus erzeugten Bildern (E94, G15.1): Quelle des Akzents, Farbton (OKLCH, ±6° zur Farbe des Erzeugers) und
/// Sicherheit; Gegenproben mit abgeschalteten Regeln (Haut, Rahmen, Füllung vor Kontur, Leuchten, Familien, Vorrang des
/// Logos) und Eigenschaften (deterministisch, Spiegelbild, Stabilität, steigende Sicherheit, Grenzen der Analyse).
/// </summary>
public sealed class GameColorAnalysisTests
{
    private static readonly LogoSpec G18Logo = new() { Fill = C("#FFFFFF"), Outline = 10, Emblem = (0.045, C("#E02020")) };

    [Fact]
    public void G1_red_logo_with_black_outline_wins_and_the_cover_orange_becomes_atmosphere()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#D62828"), Outline = 2 }), Cover(CoverScene.NightPortrait), null);

        Expect(r, GameColorSource.Logo, "#D62828", 0.9);
        Assert.Contains(r.Atmospheres, s => s.Source == GameColorSource.Cover && Near(s.Hex, "#F28C28"));
    }

    [Fact]
    public void G2_white_logo_gives_way_to_the_orange_title_of_the_cover_not_navy_or_skin()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#FFFFFF"), Outline = 2, Shadow = C("#000000") }), Cover(CoverScene.NightPortrait), null);

        Expect(r, GameColorSource.Cover, "#F28C28", 0.5);
    }

    [Fact]
    public void G3_gray_art_gives_no_colour()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#101010") }), Cover(CoverScene.Gray), Hero(HeroScene.Gray));

        Assert.Null(r.Accent);
        Assert.False(r.IsUsable);
        Assert.Equal(GameColorSource.None, r.AccentSource);
    }

    [Fact]
    public void G4_multicolour_logo_gives_blue_and_yellow_as_first_shine()
    {
        var r = Derive(G4(), Cover(CoverScene.Jungle), null);

        Expect(r, GameColorSource.Logo, "#1E64DC", 0.9);
        Assert.True(Near(r.Shines[0].Hex, "#FFD400"), r.ToString());
    }

    [Fact]
    public void G5_white_letters_with_orange_outline_give_orange()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#FFFFFF"), Outline = 3, OutlineColor = C("#FF7A00") }), Cover(CoverScene.Red), null);

        Expect(r, GameColorSource.Logo, "#FF7A00", 0.5);
    }

    [Fact]
    public void G6_yellow_fill_wins_against_thick_red_outline_and_red_is_the_first_secondary()
    {
        var r = Derive(G6(), null, null);

        Expect(r, GameColorSource.Logo, "#FFD000", 0.9);
        Assert.True(Near(r.Atmospheres[0].Hex, "#D01010"), r.ToString());
    }

    [Fact]
    public void G7_neon_cyan_logo_on_gray_cover_has_no_atmosphere_candidates()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#00F0FF") }), Cover(CoverScene.Gray), null);

        Expect(r, GameColorSource.Logo, "#00F0FF", 0.9);
        Assert.Empty(r.Atmospheres);
    }

    [Fact]
    public void G8_navy_logo_is_lifted_to_a_vivid_medium_blue()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#0B1F4D") }), null, null);

        Expect(r, GameColorSource.Logo, "#0B1F4D", 0.5);
        var mapped = ColorScience.Fit(Rgba8.Parse(r.Accent!), FitRole.Accent);
        Assert.True(ColorScience.Luminance(mapped) >= 0.28, mapped.Hex);
        Assert.True(ColorScience.ToOklab(mapped).C >= 0.14, mapped.Hex);
        Assert.True(ColorScience.HueDistance(ColorScience.ToOklab(mapped).H, Hue("#0B1F4D")) <= 2, mapped.Hex);
    }

    [Fact]
    public void G9_pastel_pink_with_white_outline_is_kept_and_mapped_with_enough_chroma()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#F7C6D9"), Outline = 2, OutlineColor = C("#FFFFFF") }), null, null);

        Expect(r, GameColorSource.Logo, "#F7C6D9", 0.5);
        Assert.True(ColorScience.ToOklab(ColorScience.Fit(Rgba8.Parse(r.Accent!), FitRole.Accent)).C >= 0.115);
    }

    [Fact]
    public void G10_beige_brand_logo_is_no_skin_on_a_logo()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#E3A46F") }), Cover(CoverScene.NightPortrait), null);

        Expect(r, GameColorSource.Logo, "#E3A46F", 0.9);
    }

    [Fact]
    public void G11_small_red_dot_on_white_logo_and_sepia_cover_give_no_colour()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#FFFFFF"), Emblem = (0.011, C("#E02020")) }), Cover(CoverScene.Sepia), null);

        Assert.Null(r.Accent);
        Assert.True(r.Confidence < GameColors.Accept);
    }

    [Fact]
    public void G12_sunset_hero_alone_is_the_source()
    {
        var r = Derive(null, null, Hero(HeroScene.Sunset));

        Assert.Equal(GameColorSource.Hero, r.AccentSource);
        Assert.True(r.IsUsable, r.ToString());
        Assert.InRange(Hue(r.Accent!), 15, 50);
    }

    [Fact]
    public void G13_white_logo_and_teal_cover_give_teal_from_the_cover()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#FFFFFF") }), Cover(CoverScene.TealSciFi), null);

        Expect(r, GameColorSource.Cover, "#00A3A3", 0.9);
    }

    [Fact]
    public void G14_white_lettering_with_cyan_halo_gives_cyan_through_the_halo_rule()
    {
        var logo = AnalyzeLogo(G14());
        var r = GameColorAnalysis.Derive(logo, null, null);

        Assert.True(logo!.ViaHalo);
        Expect(r, GameColorSource.Logo, "#00E5FF", 0.9);
    }

    [Fact]
    public void G15_slate_logo_is_too_gray()
    {
        var r = Derive(Logo(new LogoSpec { Fill = C("#6B7790") }), null, null);

        Assert.Null(r.Accent);
        Assert.True(r.Confidence < GameColors.Accept);
        Assert.True(r.Confidence > 0, "Die Sicherheit bleibt fürs Protokoll erhalten.");
    }

    [Fact]
    public void G16_white_logo_with_red_bar_of_15_percent_wins_by_chroma_share()
    {
        var logo = AnalyzeLogo(Logo(new LogoSpec { Bands = [(0.45, C("#FFFFFF")), (0.15, C("#E02020")), (0.40, C("#FFFFFF"))] }));
        var r = GameColorAnalysis.Derive(logo, AnalyzeCover(Cover(CoverScene.TealSciFi)), null);

        Assert.True(logo!.ChromaShare >= 0.10, logo.ChromaShare.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Expect(r, GameColorSource.Logo, "#E02020", 0.9);
    }

    [Fact]
    public void G17_big_face_cover_gives_the_orange_title_never_skin()
    {
        var cover = AnalyzeCover(Cover(CoverScene.BigFace));
        var r = GameColorAnalysis.Derive(null, cover, null);

        Expect(r, GameColorSource.Cover, "#F28C28", 0.5);
        Assert.Contains(cover!.Families, f => f.Skin && f.Share > 0.3);
    }

    [Fact]
    public void G18_red_emblem_inside_a_heavy_black_outline_wins_because_the_outline_is_a_frame()
    {
        var r = Derive(Logo(G18Logo), null, null);

        Expect(r, GameColorSource.Logo, "#E02020", 0.7);
    }

    [Fact]
    public void G18b_small_emblem_gives_way_to_a_confident_cover_of_another_hue()
    {
        var r = Derive(Logo(G18Logo), Cover(CoverScene.TealSciFi), null);

        Expect(r, GameColorSource.Cover, "#00A3A3", 0.9);
    }

    [Fact]
    public void G18c_small_emblem_wins_when_it_agrees_with_the_cover()
    {
        var r = Derive(Logo(G18Logo), Cover(CoverScene.RedOrange), null);

        Expect(r, GameColorSource.Logo, "#E02020", 0.7);
    }

    [Fact]
    public void G19_opaque_red_plate_with_white_text_gives_red()
    {
        var r = Derive(Logo(new LogoSpec { Plate = C("#D62828"), Fill = C("#FFFFFF") }), null, null);

        Expect(r, GameColorSource.Logo, "#D62828", 0.9);
    }

    [Fact]
    public void G20_opaque_black_box_with_yellow_text_gives_yellow()
    {
        var r = Derive(Logo(new LogoSpec { Plate = C("#000000"), Fill = C("#FFD000") }), null, null);

        Expect(r, GameColorSource.Logo, "#FFD000", 0.9);
    }

    [Fact]
    public void G21_empty_tiny_and_faint_images_give_nothing_without_exception()
    {
        var transparent = Solid(64, 32, new Rgba8(255, 0, 0, 0));
        var faint = Solid(64, 32, new Rgba8(255, 0, 0, 20));

        Assert.Null(GameColorAnalysis.PrepareLogo(transparent.Pixels, 64, 32));
        Assert.Null(GameColorAnalysis.Analyze(transparent.Pixels, 64, 32, GameColorKind.Logo));
        Assert.Null(GameColorAnalysis.Analyze(faint.Pixels, 64, 32, GameColorKind.Logo));
        Assert.Null(GameColorAnalysis.Analyze([], 0, 0, GameColorKind.Cover));
        Assert.Null(GameColorAnalysis.Analyze(new byte[3], 1, 1, GameColorKind.Hero));
        var one = GameColorAnalysis.Analyze(Solid(1, 1, C("#D62828")).Pixels, 1, 1, GameColorKind.Cover);
        Assert.NotNull(one);
        Assert.Null(GameColorAnalysis.Derive(null, null, null).Accent);
        Assert.Null(GameColorAnalysis.Derive(GameColorAnalysis.Analyze(faint.Pixels, 64, 32, GameColorKind.Logo), null, null).Accent);
    }

    [Fact]
    public void G22_gold_gradient_with_dark_extrusion_gives_gold_and_the_extrusion_is_a_frame()
    {
        var logo = AnalyzeLogo(G22());
        var r = GameColorAnalysis.Derive(logo, null, null);

        Expect(r, GameColorSource.Logo, "#D8B048", 0.9);
        Assert.DoesNotContain(logo!.Families, f => f.Mean.L < 0.30);
    }

    [Fact]
    public void G23_opaque_beige_plate_keeps_the_logo_rules()
    {
        var r = Derive(Logo(new LogoSpec { Plate = C("#E3A46F"), Fill = C("#FFFFFF") }), null, null);

        Expect(r, GameColorSource.Logo, "#E3A46F", 0.9);
    }

    // Gegenproben: jede abgeschaltete Regel ändert das Ergebnis.
    [Fact]
    public void Mutation_without_skin_rule_the_big_face_becomes_the_accent()
    {
        var options = new GameColorOptions(SkinRule: false);
        var r = GameColorAnalysis.Derive(null, AnalyzeCover(Cover(CoverScene.BigFace), options), null, options);
        var normal = GameColorAnalysis.Derive(null, AnalyzeCover(Cover(CoverScene.BigFace)), null);

        // Haut ist wenig bunt (Chroma unter 0,125), der Titel kräftig.
        Assert.True(r.IsUsable);
        Assert.True(ColorScience.ToOklab(Rgba8.Parse(r.Accent!)).C < 0.11, r.ToString());
        Assert.True(ColorScience.ToOklab(Rgba8.Parse(normal.Accent!)).C > 0.14, normal.ToString());
    }

    [Fact]
    public void Mutation_without_frame_exclusion_the_emblem_loses_confidence()
    {
        var options = new GameColorOptions(FrameExclusion: false);
        var logo = AnalyzeLogo(Logo(G18Logo), options);

        Assert.True(logo!.Confidence < GameColors.Accept, logo.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Mutation_without_fill_over_outline_the_red_outline_wins()
    {
        var options = new GameColorOptions(FillOverOutline: false);
        var r = GameColorAnalysis.Derive(AnalyzeLogo(G6(), options), null, null, options);

        Expect(r, GameColorSource.Logo, "#D01010", 0.9);
    }

    [Fact]
    public void Mutation_without_halo_rule_the_white_lettering_loses_against_a_red_cover()
    {
        // Mit Regel: das Leuchten ist farbig genug (Anteil ≥ 0,10) und das Logo gewinnt gegen das rote Cover.
        var normal = GameColorAnalysis.Derive(AnalyzeLogo(G14()), AnalyzeCover(Cover(CoverScene.Red)), null);
        Expect(normal, GameColorSource.Logo, "#00E5FF", 0.9);

        // Ohne Regel bleiben nur Mischpixel am Rand der Schrift: zu wenig Farbe, das Cover gewinnt.
        var options = new GameColorOptions(HaloRule: false);
        var logo = AnalyzeLogo(G14(), options);
        var r = GameColorAnalysis.Derive(logo, AnalyzeCover(Cover(CoverScene.Red), options), null, options);

        Assert.False(logo!.ViaHalo);
        Assert.True(logo.ChromaShare < 0.10, logo.ChromaShare.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(GameColorSource.Cover, r.AccentSource);
    }

    [Fact]
    public void Mutation_scored_on_raw_clusters_the_gold_gradient_is_not_stable()
    {
        var options = new GameColorOptions(Families: false);
        var baseline = Mapped(GameColorAnalysis.Derive(AnalyzeLogo(G22(), options), null, null, options));
        var noisy = Mapped(GameColorAnalysis.Derive(AnalyzeLogo(G22().Noise(8, 99), options), null, null, options));

        Assert.True(ColorScience.DeltaE(baseline, noisy) > 0.03, $"{baseline} {noisy}");
    }

    [Fact]
    public void Mutation_without_logo_dominance_the_small_emblem_wins_over_the_cover()
    {
        var options = new GameColorOptions(LogoDominance: false);
        var r = GameColorAnalysis.Derive(AnalyzeLogo(Logo(G18Logo), options), AnalyzeCover(Cover(CoverScene.TealSciFi), options), null, options);

        Assert.Equal(GameColorSource.Logo, r.AccentSource);
    }

    // Eigenschaften.
    [Fact]
    public void Analysis_is_deterministic()
    {
        var a = Derive(G4(), Cover(CoverScene.Jungle), Hero(HeroScene.Sunset));
        var b = Derive(G4(), Cover(CoverScene.Jungle), Hero(HeroScene.Sunset));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Mirror_image_gives_an_identical_result()
    {
        var a = Derive(G4(), Cover(CoverScene.NightPortrait), null);
        var b = Derive(G4().Mirror(), Cover(CoverScene.NightPortrait).Mirror(), null);

        Assert.Equal(a, b);
    }

    public static TheoryData<string> StabilityCases => ["G1", "G4", "G6", "G13", "G22"];

    [Theory]
    [MemberData(nameof(StabilityCases))]
    public void Noise_half_pixel_shift_and_90_percent_scale_keep_source_and_mapped_accent(string name)
    {
        var (logo, cover) = Case(name);
        GameColors Run(Func<ArtImage, ArtImage> t) => GameColorAnalysis.Derive(
            logo is null ? null : AnalyzeLogo(t(logo)), cover is null ? null : AnalyzeCover(t(cover)), null);

        var baseline = Run(x => x);
        Assert.True(baseline.IsUsable, baseline.ToString());
        foreach (var transform in new Func<ArtImage, ArtImage>[] { x => x.Noise(8, 99), x => x.ShiftScale(0.5, 1), x => x.ShiftScale(0, 0.9) })
        {
            var r = Run(transform);
            Assert.Equal(baseline.AccentSource, r.AccentSource);
            Assert.True(ColorScience.DeltaE(Mapped(baseline), Mapped(r)) <= 0.03, $"{baseline} / {r}");
        }
    }

    [Fact]
    public void Confidence_rises_monotonically_with_chroma()
    {
        var previous = -1.0;
        for (var c = 0.0; c <= 0.2001; c += 0.01)
        {
            var color = ColorScience.ToRgba8(ColorScience.FromLch(0.6, ColorScience.GamutClip(0.6, c, 30), 30));
            var logo = AnalyzeLogo(Logo(new LogoSpec { Fill = color }));
            var conf = logo?.Confidence ?? 0;
            Assert.True(conf >= previous - 1e-9, $"C {c:0.00}: {conf} < {previous}");
            previous = conf;
        }

        Assert.Equal(1, previous, 3);
    }

    [Fact]
    public void Analysis_stays_within_its_bounds()
    {
        var logo = ForAnalysis(G22());
        Assert.Equal(GameColorAnalysis.LogoLongSide, Math.Max(logo.Width, logo.Height));
        Assert.True(Math.Min(logo.Width, logo.Height) >= GameColorAnalysis.LogoShortSideMin);
        Assert.Equal((96, 8), GameColorAnalysis.LogoSize(4000, 10));
        Assert.Equal((32, 48), GameColorAnalysis.CoverSize(600, 900));
        Assert.Equal((40, 40), GameColorAnalysis.CoverSize(512, 512));
        Assert.Equal((48, 32), GameColorAnalysis.CoverSize(920, 430));

        var inputs = new[]
        {
            (Image: logo, Kind: GameColorKind.Logo),
            (Image: CoverForAnalysis(Cover(CoverScene.Jungle)), Kind: GameColorKind.Cover),
            (Image: HeroForAnalysis(Hero(HeroScene.Sunset)), Kind: GameColorKind.Hero),
            (Image: Solid(96, 96, C("#808080")).Noise(40, 3), Kind: GameColorKind.Cover),
        };
        foreach (var (image, kind) in inputs)
        {
            var r = GameColorAnalysis.Analyze(image.Pixels, image.Width, image.Height, kind)!;
            var pixels = image.Width * image.Height;
            Assert.True(pixels <= 96 * 96);
            Assert.True(r.Stats.Pixels <= pixels);
            Assert.True(r.Stats.LongestArray <= pixels);
            Assert.True(r.Stats.Bins <= pixels);
            Assert.True(r.Stats.Clusters <= (kind == GameColorKind.Logo ? 6 : 8));
            Assert.True(r.Stats.Iterations <= 12);
        }
    }

    internal static ArtImage G4() => Logo(new LogoSpec { Bands = [(0.5, C("#1E64DC")), (0.33, C("#FFD400")), (0.17, C("#E02020"))] });

    internal static ArtImage G6() => Logo(new LogoSpec { Fill = C("#FFD000"), Outline = 8, OutlineColor = C("#D01010") });

    internal static ArtImage G14() => Logo(new LogoSpec { Fill = C("#FFFFFF"), Halo = C("#00E5FF"), HaloRadius = 16 });

    internal static ArtImage G22() => Logo(new LogoSpec { Gradient = (C("#FFE680"), C("#B07A10")), Extrusion = C("#3A2405") });

    private static (ArtImage? Logo, ArtImage? Cover) Case(string name) => name switch
    {
        "G1" => (Logo(new LogoSpec { Fill = C("#D62828"), Outline = 2 }), Cover(CoverScene.NightPortrait)),
        "G4" => (G4(), Cover(CoverScene.Jungle)),
        "G6" => (G6(), null),
        "G13" => (Logo(new LogoSpec { Fill = C("#FFFFFF") }), Cover(CoverScene.TealSciFi)),
        "G22" => (G22(), null),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static GameColors Derive(ArtImage? logo, ArtImage? cover, ArtImage? hero) => GameColorAnalysis.Derive(
        logo is null ? null : AnalyzeLogo(logo), cover is null ? null : AnalyzeCover(cover), hero is null ? null : AnalyzeHero(hero));

    private static Rgba8 Mapped(GameColors r) => ColorScience.Fit(Rgba8.Parse(r.Accent!), FitRole.Accent);

    private static double Hue(string hex) => ColorScience.ToOklab(Rgba8.Parse(hex)).H;

    private static bool Near(string hex, string expected) => ColorScience.HueDistance(Hue(hex), Hue(expected)) <= 6;

    private static void Expect(GameColors r, GameColorSource source, string generatorColor, double minConfidence)
    {
        Assert.True(r.Accent is not null, r.ToString());
        Assert.Equal(source, r.AccentSource);
        Assert.True(Near(r.Accent!, generatorColor), $"{r} (erwartet Farbton von {generatorColor})");
        Assert.InRange(r.Confidence, minConfidence, 1);
        Assert.True(r.IsUsable);
    }
}
