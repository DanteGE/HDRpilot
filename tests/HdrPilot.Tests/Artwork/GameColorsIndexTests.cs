using System.Text;
using System.Text.Json;
using HdrPilot.Core.Artwork;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// Spielfarben im Verzeichnis der Bilder (E94, G15.1): <c>colors</c> hin und zurück, alte Verzeichnisse ohne Farben, eigene
/// Fassung des Verfahrens, Zurücksetzen bei geändertem Bildstand, unbekannte Werte verwerfen nie das ganze Verzeichnis,
/// höchstens 260 Bytes je Spiel und Gleichheit nach Wert.
/// </summary>
public sealed class GameColorsIndexTests
{
    private const string IndexPath = @"C:\Data\art\index.json";

    private static readonly GameColors Full = new(
        "#D62828",
        GameColorSource.Logo,
        0.97,
        [new GameSwatch("#F28C28", GameColorSource.Cover), new GameSwatch("#7C3AED", GameColorSource.Hero), new GameSwatch("#2EC4B6", GameColorSource.Logo)],
        [new GameSwatch("#FFD400", GameColorSource.Logo), new GameSwatch("#FFE8A0", GameColorSource.Cover), new GameSwatch("#9CC9F2", GameColorSource.Hero)]);

    [Fact]
    public void Colors_round_trip_through_the_index()
    {
        var fs = new InMemoryFileSystem();
        var index = new ArtworkIndex(fs, IndexPath);
        var data = new ArtworkIndexData();
        data.Games["Steam:1"] = new ArtworkIndexEntry { Signature = "A", Colors = IndexedGameColors.From(Full) };
        data.Games["Steam:2"] = new ArtworkIndexEntry { Signature = "B", Colors = IndexedGameColors.From(GameColors.None) };
        index.Save(data);

        var text = fs.ReadAllText(IndexPath);
        var loaded = index.Load();

        Assert.Contains("\"colors\":{\"v\":1,\"acc\":\"#D62828\",\"src\":\"Logo\",\"conf\":0.97,\"atm\":[{\"c\":\"#F28C28\",\"s\":\"Cover\"}", text, StringComparison.Ordinal);
        Assert.Equal(Full, loaded.Games["Steam:1"].Colors!.ToColors());
        Assert.Equal(GameColors.None, loaded.Games["Steam:2"].Colors!.ToColors());
        Assert.DoesNotContain("\"acc\":null", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Old_index_without_colors_loads_with_null()
    {
        var fs = new InMemoryFileSystem().AddFile(IndexPath, "{\"version\":1,\"games\":{\"Steam:1\":{\"signature\":\"A\",\"logo\":\"x.png\"}}}");

        var entry = new ArtworkIndex(fs, IndexPath).Load().Games["Steam:1"];

        Assert.Equal("x.png", entry.Logo);
        Assert.Null(entry.Colors);
    }

    [Fact]
    public void Other_algorithm_version_counts_as_missing()
    {
        var stored = IndexedGameColors.From(Full);
        Assert.Equal(GameColorAnalysis.Version, stored.Version);
        Assert.NotNull(stored.ToColors());

        stored.Version = GameColorAnalysis.Version + 1;
        Assert.Null(stored.ToColors());
        stored.Version = 0;
        Assert.Null(stored.ToColors());
    }

    [Fact]
    public void Reset_of_derived_data_clears_the_colors_but_keeps_steamgriddb()
    {
        var entry = new ArtworkIndexEntry { Signature = "A", Colors = IndexedGameColors.From(Full), SteamGridDb = new IndexedSteamGridDb { GameId = 7 } };

        entry.ResetDerived("B");

        Assert.Null(entry.Colors);
        Assert.Equal(7, entry.SteamGridDb!.GameId);
    }

    [Fact]
    public void Index_format_version_stays_one()
    {
        // Eine neue Fassung würde das Verzeichnis jedes Nutzers verwerfen; die Farben haben eine eigene.
        Assert.Equal(1, ArtworkIndex.CurrentVersion);
    }

    [Fact]
    public void Unknown_source_strings_never_discard_the_index()
    {
        const string json = "{\"version\":1,\"steamGridDbLastRun\":\"2026-10-15T08:00:00+00:00\",\"games\":{"
            + "\"Steam:1\":{\"signature\":\"A\",\"colors\":{\"v\":1,\"acc\":\"#D62828\",\"src\":\"Banner\",\"conf\":0.9}},"
            + "\"Steam:2\":{\"signature\":\"B\",\"colors\":{\"v\":1,\"acc\":\"#D62828\",\"src\":\"Cover\",\"conf\":0.9,"
            + "\"atm\":[{\"c\":\"#F28C28\",\"s\":\"Wolke\"},{\"c\":\"kein\",\"s\":\"Hero\"},{\"c\":\"#7C3AED\",\"s\":\"Hero\"}]}},"
            + "\"Steam:3\":{\"signature\":\"C\",\"colors\":{\"v\":1,\"acc\":\"#D62828\",\"src\":\"None\",\"conf\":0.9}}}}";
        var fs = new InMemoryFileSystem().AddFile(IndexPath, json);

        var data = new ArtworkIndex(fs, IndexPath).Load();

        Assert.NotNull(data.SteamGridDbLastRun);
        Assert.Equal(3, data.Games.Count);
        Assert.Null(data.Games["Steam:1"].Colors!.ToColors()); // unbekannte Herkunft mit Akzent: neu berechnen
        Assert.Null(data.Games["Steam:3"].Colors!.ToColors()); // Akzent ohne Herkunft passt nicht zusammen
        var second = data.Games["Steam:2"].Colors!.ToColors()!;
        Assert.Equal(GameColorSource.Cover, second.AccentSource);
        Assert.Equal(new[] { new GameSwatch("#7C3AED", GameColorSource.Hero) }, second.Atmospheres); // unbekannte und ungültige Kandidaten fallen weg
    }

    [Fact]
    public void Entry_with_three_and_three_candidates_stays_within_260_bytes()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var longest = Full with { AccentSource = GameColorSource.Cover, Confidence = 0.55 };
        var json = JsonSerializer.Serialize(IndexedGameColors.From(longest), options);

        Assert.True(Encoding.UTF8.GetByteCount(json) <= 260, $"{Encoding.UTF8.GetByteCount(json)} Bytes: {json}");
    }

    [Fact]
    public void Game_colors_compare_by_value_including_the_lists()
    {
        var copy = new GameColors(Full.Accent, Full.AccentSource, Full.Confidence, [.. Full.Atmospheres], [.. Full.Shines]);

        Assert.Equal(Full, copy);
        Assert.Equal(Full.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(Full, copy with { Shines = [.. Full.Shines.Take(2)] });
        Assert.NotEqual(Full, copy with { Atmospheres = [.. Full.Atmospheres.Reverse()] });
        Assert.NotEqual(Full, copy with { Confidence = 0.96 });
        Assert.NotEqual(Full, copy with { Accent = "#D62829" });
        Assert.False(Full.Equals(null));
        Assert.True(Full.IsUsable);
        Assert.False((Full with { Confidence = 0.49 }).IsUsable);
        Assert.False(GameColors.None.IsUsable);
    }
}
