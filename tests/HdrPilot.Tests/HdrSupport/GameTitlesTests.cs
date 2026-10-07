using HdrPilot.Core.HdrSupport;

namespace HdrPilot.Tests.HdrSupport;

public sealed class GameTitlesTests
{
    [Theory]
    [InlineData("Avatar Frontiers of Pandora™", "Avatar Frontiers of Pandora")]
    [InlineData("Tom Clancy's Rainbow Six® Siege", "Tom Clancy's Rainbow Six Siege")]
    [InlineData("Baldur’s Gate 3", "Baldur's Gate 3")]
    [InlineData("Spider‑Man – Remastered", "Spider-Man - Remastered")]
    [InlineData("  Dead Island   2 ", "Dead Island 2")]
    [InlineData("©Studio „Spiel“", "Studio \"Spiel\"")]
    [InlineData("Line\tBreak\nGame", "Line Break Game")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Clean_strips_symbols_and_unifies_quotes_dashes_and_spaces(string? name, string expected)
    {
        Assert.Equal(expected, GameTitles.Clean(name));
    }

    [Fact]
    public void Normalize_ignores_case_and_compatibility_forms()
    {
        Assert.Equal(GameTitles.Normalize("DOOM Eternal"), GameTitles.Normalize("Doom Eternal"));
        Assert.Equal(GameTitles.Normalize("Final Fantasy Ⅱ"), GameTitles.Normalize("final fantasy II"));
        Assert.Equal(GameTitles.Normalize("Avatar: Frontiers of Pandora™"), GameTitles.Normalize("avatar: frontiers of pandora"));
        Assert.NotEqual(GameTitles.Normalize("Avatar Frontiers of Pandora"), GameTitles.Normalize("Avatar: Frontiers of Pandora"));
    }

    [Theory]
    [InlineData("Avatar Frontiers of Pandora™", "Avatar: Frontiers of Pandora", true)]
    [InlineData("Dying Light - The Beast", "Dying Light: The Beast", true)]
    [InlineData("Baldur’s Gate 3", "Baldur's Gate 3", true)]
    [InlineData("Doom", "Doom (2016)", false)]
    [InlineData("Half-Life 2", "Half-Life", false)]
    [InlineData("™", "®", false)] // leere Schlüssel sind nie gleich
    public void Loose_comparison_ignores_only_punctuation_symbols_and_case(string a, string b, bool equal)
    {
        Assert.Equal(equal, GameTitles.LooselyEqual(a, b));
    }

    [Fact]
    public void Variants_add_a_colon_form_for_spaced_dashes_only()
    {
        Assert.Equal(["Dying Light - The Beast", "Dying Light: The Beast"], GameTitles.Variants("Dying Light – The Beast"));
        Assert.Equal(["Half-Life 2"], GameTitles.Variants("Half-Life 2"));
        Assert.Equal(["Cyberpunk 2077"], GameTitles.Variants("Cyberpunk 2077™"));
    }

    [Theory]
    [InlineData("Game | Edition")]
    [InlineData("Game #1")]
    [InlineData("[Bracket]")]
    [InlineData("{Curly}")]
    [InlineData("100%20Orange")]
    [InlineData(":Leading colon")]
    [InlineData("   ")]
    public void Titles_that_the_wiki_rejects_are_never_queried(string name)
    {
        Assert.False(GameTitles.IsValidTitle(name));
        Assert.Empty(GameTitles.Variants(name));
    }

    [Fact]
    public void Long_titles_are_rejected_by_byte_length()
    {
        Assert.True(GameTitles.IsValidTitle(new string('a', 255)));
        Assert.False(GameTitles.IsValidTitle(new string('a', 256)));
        Assert.False(GameTitles.IsValidTitle(new string('ä', 128))); // 256 Byte in UTF-8
        Assert.True(GameTitles.IsValidTitle("100% Orange Juice")); // % ohne Hex-Ziffern ist erlaubt
    }
}
