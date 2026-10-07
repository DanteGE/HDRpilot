using HdrPilot.Core.Games.Scanners.Steam;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

/// <summary>Valve-KeyValues-Textformat: Escapes, Tokens, Kommentare, Bedingungen und defekte Dateien.</summary>
public sealed class SteamKeyValuesParserTests
{
    [Fact]
    public void Parses_libraryfolders_fixture_from_target_machine_format()
    {
        var text = File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Steam", "libraryfolders.vdf"));

        var folders = KeyValuesParser.Parse(text).Child("libraryfolders")!;

        Assert.Equal(["0", "1", "2"], folders.Children.Select(c => c.Key));
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders.Child("0")!.GetString("path"));
        Assert.Equal(@"D:\Steam-Bibliothek", folders.Child("1")!.GetString("path"));
        Assert.Equal("", folders.Child("0")!.GetString("label"));
        Assert.Equal("61237584", folders.Child("1")!.Child("apps")!.GetString("9990120"));
        Assert.True(folders.Child("2")!.Child("apps")!.IsBlock);
        Assert.Empty(folders.Child("2")!.Child("apps")!.Children);
    }

    [Fact]
    public void Parses_appmanifest_fixture_with_nested_blocks()
    {
        var text = File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Steam", "appmanifest_228980.acf"));

        var state = KeyValuesParser.Parse(text).Child("AppState")!;

        Assert.Equal("228980", state.GetString("appid"));
        Assert.Equal("Steamworks Common Redistributables", state.GetString("name"));
        Assert.Equal("4", state.GetString("StateFlags"));
        Assert.Equal(@"_CommonRedist\vcredist\2005\installscript.vdf", state.Child("InstallScripts")!.GetString("228981"));
    }

    [Theory]
    [InlineData(@"""k"" ""a\\b""", @"a\b")]
    [InlineData(@"""k"" ""sag \""hallo\""""", "sag \"hallo\"")]
    [InlineData(@"""k"" ""zeile1\nzeile2""", "zeile1\nzeile2")]
    [InlineData(@"""k"" ""a\tb""", "a\tb")]
    [InlineData(@"""k"" ""D:\Spielordner\Neu""", @"D:\Spielordner\Neu")] // unbekannte Escapes bleiben stehen
    [InlineData(@"""k"" ""Spiel™ Ä""", "Spiel™ Ä")]
    [InlineData("k ohneAnfuehrung", "ohneAnfuehrung")]
    [InlineData("k \"\"", "")]
    public void Strings_and_escapes_are_resolved(string text, string expected) =>
        Assert.Equal(expected, KeyValuesParser.Parse(text).GetString("k"));

    [Fact]
    public void Comments_unquoted_tokens_and_conditions_are_tolerated()
    {
        const string text = """
            // Kommentar am Anfang
            "Root"   // Kommentar hinter dem Schlüssel
            {
                unquoted    wert        [$WIN32]
                "bedingt"   "ja"        [!$X360]
                "block"     [$WIN64]
                {
                    "x"     "1"         // Kommentar
                }
                "Doppelt"   "erster"
                "doppelt"   "zweiter"
            }
            """;

        var root = KeyValuesParser.Parse(text).Child("root")!;

        Assert.Equal("wert", root.GetString("unquoted"));
        Assert.Equal("ja", root.GetString("bedingt"));
        Assert.Equal("1", root.Child("block")!.GetString("x"));
        Assert.Equal("erster", root.GetString("DOPPELT"));
        Assert.Equal(5, root.Children.Count);
    }

    [Fact]
    public void Value_of_block_is_null_and_children_of_string_are_empty()
    {
        var root = KeyValuesParser.Parse("\"a\" { \"b\" \"c\" }");

        Assert.Null(root.Child("a")!.Value);
        Assert.Null(root.GetString("a"));
        Assert.Empty(root.Child("a")!.Child("b")!.Children);
        Assert.Null(root.Child("fehlt"));
    }

    [Theory]
    [InlineData("\"AppState\"\n{\n\t\"appid\"\t\t\"1018", "Zeichenkette ab Zeile 3")]
    [InlineData("\"AppState\"\n{\n\t\"appid\"\t\t\"9990120\"\n", "Block ab Zeile 2")]
    [InlineData("\"AppState\"\n{\n}\n}\n", "schließende Klammer")]
    [InlineData("\"AppState\"\n{\n\t\"appid\"\n}\n", "Wert für \"appid\" fehlt")]
    [InlineData("\"AppState\"", "Wert für \"AppState\" fehlt")]
    [InlineData("{ \"a\" \"b\" }", "Block ohne Schlüssel")]
    [InlineData("\"a\" \"b\" [$WIN32", "Bedingung")]
    [InlineData("\"a\" \"b\0c\"", "Nullzeichen")]
    [InlineData("\"a\" b\u0001c", "Steuerzeichen 0x01")]
    public void Broken_input_throws_with_line_information(string text, string expected)
    {
        var ex = Assert.Throws<KeyValuesException>(() => KeyValuesParser.Parse(text));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Zeile", ex.Message, StringComparison.Ordinal);
        Assert.True(ex.Line >= 1);
    }

    [Fact]
    public void Broken_too_deep_nesting_throws_instead_of_overflowing_the_stack()
    {
        var text = string.Concat(Enumerable.Repeat("\"a\" { ", 200)) + string.Concat(Enumerable.Repeat("} ", 200));

        Assert.Throws<KeyValuesException>(() => KeyValuesParser.Parse(text));
    }

    [Fact]
    public void Empty_input_gives_empty_root()
    {
        Assert.Empty(KeyValuesParser.Parse("").Children);
        Assert.Empty(KeyValuesParser.Parse("\uFEFF  // nur Kommentar\n").Children);
    }
}
