using HdrPilot.Core.Artwork;

namespace HdrPilot.Tests.Artwork;

/// <summary>Kennzeichen der eigenen Bildwahl (E89, „Bild wählen…“): nur schlichte Bild-Dateinamen, nie Pfade.</summary>
public sealed class ArtworkOverrideTests
{
    [Theory]
    [InlineData(null, ArtworkOverrideKind.Automatic, null)]
    [InlineData("gen", ArtworkOverrideKind.Generated, null)]
    [InlineData("alt:SplashScreen.png", ArtworkOverrideKind.Alternative, "SplashScreen.png")]
    [InlineData("custom:Steam_1091500_3F2A9C1B-hero.jpg", ArtworkOverrideKind.Custom, "Steam_1091500_3F2A9C1B-hero.jpg")]
    public void Valid_tokens_are_recognized(string? token, ArtworkOverrideKind kind, string? file)
    {
        Assert.True(ArtworkOverride.IsValid(token));
        Assert.Equal(kind, ArtworkOverride.KindOf(token));
        Assert.Equal(file, ArtworkOverride.FileOf(token));
    }

    [Theory]
    [InlineData("custom:..\\win.ini")]
    [InlineData("custom:C:\\Bilder\\a.png")]
    [InlineData("alt:sub/a.png")]
    [InlineData("custom:.versteckt.png")]
    [InlineData("custom:readme.txt")]
    [InlineData("GEN")]
    [InlineData("")]
    public void Paths_and_unknown_tokens_are_rejected(string token)
    {
        Assert.False(ArtworkOverride.IsValid(token));
        Assert.Equal(ArtworkOverrideKind.Automatic, ArtworkOverride.KindOf(token));
        Assert.Null(ArtworkOverride.FileOf(token));
    }

    [Fact]
    public void Builders_refuse_paths()
    {
        Assert.Equal("custom:a.png", ArtworkOverride.Custom("a.png"));
        Assert.Equal("alt:SplashScreen.png", ArtworkOverride.Alternative("SplashScreen.png"));
        Assert.Throws<ArgumentException>(() => ArtworkOverride.Custom(@"..\a.png"));
    }

    [Fact]
    public void Ambient_colors_from_name_are_stable_and_valid()
    {
        var a = AmbientColors.FromName("Baldur's Gate 3");
        var b = AmbientColors.FromName("Baldur's Gate 3");
        Assert.Equal(a, b);
        Assert.Matches("^#[0-9A-F]{6}$", a.Deep);
        Assert.Matches("^#[0-9A-F]{6}$", a.Glow);
        Assert.Matches("^#[0-9A-F]{6}$", a.Shine);
        Assert.NotEqual(AmbientColors.FromName("SWAT 3").Glow, a.Glow);
    }
}
