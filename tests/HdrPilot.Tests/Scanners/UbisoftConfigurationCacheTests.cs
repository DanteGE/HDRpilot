using System.Text;
using HdrPilot.Core.Games.Scanners.Ubisoft;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

/// <summary>Produkt-Cache von Ubisoft Connect: Protobuf-Hülle und YAML-Teilmenge.</summary>
public sealed class UbisoftConfigurationCacheTests
{
    [Fact]
    public void Cache_reads_wanted_products_and_skips_unknown_fields()
    {
        var data = new UbisoftCacheBuilder().AddFixture(420).AddUnknownField().AddFixture(1081).AddFixture(4740).Build();

        var yamls = UbisoftConfigurationCache.Read(data, new HashSet<uint> { 4740, 420, 9999 }, out var error);

        Assert.Null(error);
        Assert.Equal([420u, 4740u], yamls.Keys.Order());
        Assert.StartsWith("version: 2.0", yamls[4740], StringComparison.Ordinal);
    }

    [Fact]
    public void Cache_header_matches_measured_layout()
    {
        // Gemessen am Zielrechner: 0A C3 0C 08 65 10 2E 1A BC 0C "version: 2.0" (Produkt 101, Version 46).
        var data = new UbisoftCacheBuilder().Add(101, "version: 2.0\n" + new string('x', 1583), version: 46).Build();

        Assert.Equal(new byte[] { 0x0A, 0xC3, 0x0C, 0x08, 0x65, 0x10, 0x2E, 0x1A, 0xBC, 0x0C }, data[..10]);
        Assert.Single(UbisoftConfigurationCache.Read(data, new HashSet<uint> { 101 }, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Empty_cache_has_no_products_and_no_error()
    {
        Assert.Empty(UbisoftConfigurationCache.Read([], new HashSet<uint> { 1 }, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x0A, 0x7F, 0x08 })]
    [InlineData(new byte[] { 0x0B })]
    [InlineData(new byte[] { 0x00, 0x01 })]
    [InlineData(new byte[] { 0x0A, 0x02, 0x08, 0x80 })]
    public void Broken_cache_reports_error(byte[] data)
    {
        UbisoftConfigurationCache.Read(data, new HashSet<uint> { 1 }, out var error);

        Assert.NotNull(error);
        Assert.Contains("Byte", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Yaml_reads_fixture_structure()
    {
        var doc = UbisoftYaml.Parse(Fixture(4740));

        Assert.Equal("2.0", doc["version"]!.Text);
        Assert.Equal("l1", doc["root"]!["name"]!.Text);
        Assert.Equal("Avatar: Frontiers of Pandora™", doc["root"]!["sort_string"]!.Text);
        var online = (YamlSequence)doc["root"]!["start_game"]!["online"]!["executables"]!;
        var first = Assert.Single(online.Items);
        Assert.Equal("AFOP", first["shortcut_name"]!.Text);
        Assert.Equal("afop.exe", first["path"]!["relative"]!.Text);
        Assert.Equal("afop_plus.exe", first["trial"]!["relative"]!.Text);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\4740\InstallDir", first["working_directory"]!["register"]!.Text);
        Assert.Equal("", first["arguments"]!.Text);
        Assert.IsType<YamlSequence>(doc["root"]!["overlay_notifications"]);
        Assert.Equal("Аватар: Рубежи Пандоры", doc["localizations"]!["ru-RU"]!["l1"]!.Text);
    }

    [Fact]
    public void Yaml_reads_indented_sequences_comments_and_quoted_values()
    {
        var doc = UbisoftYaml.Parse(Fixture(420));

        var executables = (YamlSequence)doc["root"]!["start_game"]!["online"]!["executables"]!;
        Assert.Equal(2, executables.Items.Count);
        Assert.Equal("bin", executables.Items[0]["working_directory"]!["append"]!.Text);
        Assert.Equal("-uplay_overlay_no_ui", executables.Items[1]["arguments"]!.Text);
        Assert.Equal("Far Cry 05", doc["root"]!["sort_string"]!.Text);
        Assert.Equal("000000", doc["root"]!["background_color"]!.Text);
        Assert.Equal("Far Cry® 4", doc["localizations"]!["default"]!["GAMENAME"]!.Text);
        var addons = (YamlSequence)doc["root"]!["addons"]!;
        Assert.Equal(["1163", "1213"], addons.Items.Select(a => a["id"]!.Text));
    }

    [Theory]
    [InlineData("k: 'it''s'", "it's")]
    [InlineData("k: \"a\\\"b\\\\c\\u00e4\\x41\\n\"", "a\"b\\cäA\n")]
    [InlineData("k: wert # Kommentar", "wert")]
    [InlineData("k: https://example.com/#anker", "https://example.com/#anker")]
    [InlineData("k: /appid:{AppId}", "/appid:{AppId}")]
    [InlineData("k:", "")]
    [InlineData("\"quoted key\": 5", null)]
    [InlineData("k: 'nicht geschlossen", "nicht geschlossen")]
    public void Yaml_scalars(string yaml, string? expected)
    {
        var doc = UbisoftYaml.Parse(yaml);

        if (expected is null)
        {
            Assert.Equal("5", doc["quoted key"]!.Text);
        }
        else
        {
            Assert.Equal(expected, doc["k"]!.Text);
        }
    }

    [Fact]
    public void Yaml_skips_block_scalars_and_unexpected_lines()
    {
        const string yaml = """
            a: |
              zeile eins
              zeile zwei
            b: >-
              gefaltet
              weiter
            c: x
                unerwartet tief
            keine zuordnung
            d: [1, 2]
            e: {}
            ---
            f: ende
            """;

        var doc = UbisoftYaml.Parse(yaml);

        Assert.Equal("zeile eins\nzeile zwei", doc["a"]!.Text);
        Assert.Equal("gefaltet weiter", doc["b"]!.Text);
        Assert.Equal("x", doc["c"]!.Text);
        Assert.Equal("[1, 2]", doc["d"]!.Text);
        Assert.IsType<YamlMapping>(doc["e"]);
        Assert.Equal("ende", doc["f"]!.Text);
    }

    [Fact]
    public void Yaml_empty_and_nested_sequence_items()
    {
        const string yaml = """
            liste:
            -
              name: a
            - - innen
            - einfach
            - {}
            """;

        var items = ((YamlSequence)UbisoftYaml.Parse(yaml)["liste"]!).Items;

        Assert.Equal(4, items.Count);
        Assert.Equal("a", items[0]["name"]!.Text);
        Assert.Equal("innen", ((YamlSequence)items[1]).Items[0].Text);
        Assert.Equal("einfach", items[2].Text);
        Assert.IsType<YamlMapping>(items[3]);
    }

    [Fact]
    public void Yaml_too_deep_nesting_throws_format_exception()
    {
        var deep = string.Concat(Enumerable.Range(0, 100).Select(i => new string(' ', i * 2) + "k" + i + ":\n"));

        Assert.Throws<FormatException>(() => UbisoftYaml.Parse(deep));
    }

    [Fact]
    public void Product_config_resolves_names()
    {
        Assert.Equal("Avatar Frontiers of Pandora™", UbisoftProductConfig.FromYaml(Fixture(4740)).Name);
        Assert.Equal("Far Cry® 4", UbisoftProductConfig.FromYaml(Fixture(420)).Name);
        Assert.Equal("Assassin’s Creed Shadows", UbisoftProductConfig.FromYaml(Fixture(1081)).Name);
        Assert.Null(UbisoftProductConfig.FromYaml("root:\n  name: l1\n").Name);
        Assert.Null(UbisoftProductConfig.FromYaml("root:\n  name: ''\n").Name);
        Assert.Null(UbisoftProductConfig.FromYaml("").Name);
    }

    [Fact]
    public void Product_config_lists_executables_of_all_start_modes()
    {
        var afop = UbisoftProductConfig.FromYaml(Fixture(4740));
        Assert.Equal(2, afop.Executables.Count); // online und offline
        Assert.All(afop.Executables, e => Assert.Equal(new UbisoftExecutable("afop.exe", "afop_plus.exe", null), e));

        var fc4 = UbisoftProductConfig.FromYaml(Fixture(420));
        Assert.Equal(["FarCry4.exe", "IGE_WPF64.exe", "FarCry4.exe"], fc4.Executables.Select(e => e.RelativePath));
        Assert.All(fc4.Executables, e => Assert.Equal("bin", e.WorkingDirectoryAppend));

        Assert.Empty(UbisoftProductConfig.FromYaml(Fixture(1081)).Executables);
    }

    private static string Fixture(uint id) =>
        File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Ubisoft", id + ".yml"), Encoding.UTF8);
}
