using HdrPilot.App.Services;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Erkennung von Standardsymbolen (E86): feste Liste mit den gemessenen Fingerabdrücken, gleicher Hash oder
/// Wahrnehmungsabstand ≤ 8. Positiv mit Symbolen, die nach den echten Fingerabdrücken gezeichnet sind, negativ mit echten
/// Spielsymbolen der Messung (Boltgun teilt sein Symbol mit zwei weiteren Einträgen und ist trotzdem keins).
/// </summary>
public sealed class GenericIconDetectionTests : IDisposable
{
    private readonly IconFixtures _files = new();

    public static TheoryData<string> Drawn => ["EasyAntiCheat", "UnrealU", "Godot4"];

    public void Dispose() => _files.Dispose();

    [Theory]
    [MemberData(nameof(Drawn))]
    public void Generic_icon_drawn_after_a_real_fingerprint_is_detected(string name)
    {
        var (hash, color, art) = Art(name);
        var reference = KnownGenericIcons.All.Single(k => k.Fingerprint.Hash == hash).Fingerprint;
        var drawn = IconFingerprint.Of(GenericIconArt.Render(art, color));

        Assert.InRange(drawn.DistanceTo(reference), 0, GenericIconDetector.MaxDistance);
        Assert.True(GenericIconDetector.Default.IsGeneric(drawn));
    }

    [Theory]
    [MemberData(nameof(Drawn))]
    public void Generic_icon_with_the_same_shape_but_other_shading_is_not_detected(string name)
    {
        var (_, color, art) = Art(name);

        Assert.False(GenericIconDetector.Default.IsGeneric(IconFingerprint.Of(GenericIconArt.Render(GenericIconArt.Inverted(art), color))));
    }

    [Fact]
    public void Generic_icon_in_an_ico_file_is_detected_by_the_reader()
    {
        var (_, color, art) = GenericIconArt.UnrealU;
        var generic = _files.Ico("unreal.ico", IconFixtures.Image(48, 48, GenericIconArt.Render(art, color)), IconFixtures.Image(256, 256, new byte[256 * 256 * 4]));
        var normal = _files.Ico("spiel.ico", IconFixtures.Colorful(48, 48), IconFixtures.Colorful(256, 256));

        var genericIcon = new IconReader().Read(generic);
        var normalIcon = new IconReader().Read(normal);

        Assert.True(genericIcon!.IsGeneric);
        Assert.False(genericIcon.IsGood); // 256 px groß, aber ein Standardsymbol
        Assert.False(normalIcon!.IsGeneric);
        Assert.True(normalIcon.IsGood);
    }

    [Fact]
    public void Generic_icon_detection_ignores_real_game_icons_from_the_measurement()
    {
        // Gemessen an der Bibliothek: Boltgun (drei Einträge, ein echtes Symbol) Abstand 68, Necrosmith als ähnlichstes
        // echtes Symbol Abstand 29 (zu Godot 4).
        var boltgun = new IconFingerprint("9AB9014EEFE9C33E", 0x1F384E4E5E1E9FDE, 0x3C0119FD787CFCD0, 0xFFFFFFFFFFFFFFFF, 113, 98, 94);
        var necrosmith = new IconFingerprint("56801CD93BC8D497", 0xE80F312B230713D4, 0x003C72FE7E7E2400, 0xFFFFFFFFFFFFFFFF, 139, 128, 184);

        Assert.False(GenericIconDetector.Default.IsGeneric(boltgun));
        Assert.False(GenericIconDetector.Default.IsGeneric(necrosmith));
        Assert.Equal(68, KnownGenericIcons.All.Min(k => k.Fingerprint.DistanceTo(boltgun)));
        Assert.Equal(29, KnownGenericIcons.All.Min(k => k.Fingerprint.DistanceTo(necrosmith)));
    }

    [Fact]
    public void Generic_icon_list_holds_the_measured_fingerprints()
    {
        string[] measured = ["BFF8A9D4CC3574F3", "21704A055F2F2AF1", "D970DE3C3B3B97F1", "CAFA9E41B8D0CFCE", "94DA36B9F4063577", "D30DAE223B9DC3FB", "F9F095E9485D704F", "89AEAB8B670448CD"];
        var hashes = KnownGenericIcons.All.Select(k => k.Fingerprint.Hash).ToList();

        Assert.All(measured, h => Assert.Contains(h, hashes));
        Assert.Contains("2322153F433979FB", hashes); // Unreal 3 (UDK)
        Assert.Contains("8E2BA1E166E6995D", hashes); // Windows-Programmsymbol
        Assert.All(KnownGenericIcons.All, k =>
        {
            Assert.NotEqual(0UL, k.Fingerprint.Mask); // echte Muster, keine Platzhalter
            Assert.Equal(0, k.Fingerprint.DistanceTo(k.Fingerprint));
            Assert.True(GenericIconDetector.Default.IsGeneric(k.Fingerprint));
        });
    }

    [Fact]
    public void Generic_icon_fingerprint_ignores_the_color_of_transparent_pixels()
    {
        var a = GenericIconArt.Render(GenericIconArt.Godot4.Art, GenericIconArt.Godot4.Color);
        var b = (byte[])a.Clone();
        for (var i = 0; i < b.Length; i += 4)
        {
            if (b[i + 3] == 0)
            {
                b[i] = 200; // Farbe unter voller Transparenz
            }
        }

        var normalizedB = IconFingerprint.NormalizedPixels(IconFixtures.Image(48, 48, b))!;

        Assert.Equal(IconFingerprint.Of(a).Hash, IconFingerprint.Of(normalizedB).Hash);
        Assert.Null(IconFingerprint.Of(IconFixtures.Colorful(32, 32))); // nur 48 px werden verglichen
    }

    [Fact]
    public void Generic_icon_detection_with_own_list_matches_exact_hash_and_near_variants()
    {
        var reference = IconFingerprint.Of(GenericIconArt.Render(GenericIconArt.UnrealU.Art, GenericIconArt.UnrealU.Color));
        var detector = new GenericIconDetector([reference]);
        var variant = GenericIconArt.Render(GenericIconArt.UnrealU.Art, GenericIconArt.UnrealU.Color);
        variant[((10 * 48) + 10) * 4] ^= 0x40; // ein Pixel anders: anderer Hash, gleiche Wahrnehmung

        Assert.True(detector.IsGeneric(reference));
        Assert.NotEqual(reference.Hash, IconFingerprint.Of(variant).Hash);
        Assert.True(detector.IsGeneric(IconFingerprint.Of(variant)));
        Assert.False(detector.IsGeneric(IconFingerprint.Of(GenericIconArt.Render(GenericIconArt.Godot4.Art, GenericIconArt.Godot4.Color))));
    }

    private static (string Hash, (int R, int G, int B) Color, string[] Art) Art(string name) => name switch
    {
        "EasyAntiCheat" => GenericIconArt.EasyAntiCheat,
        "UnrealU" => GenericIconArt.UnrealU,
        _ => GenericIconArt.Godot4,
    };
}
