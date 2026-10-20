using System.Text.Json;
using System.Text.Json.Serialization;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>
/// Verträglichkeit (E91, G12.3): Eine Konfiguration mit einer Quelle, die diese Version nicht kennt (etwa aus einer späteren
/// Version), lädt ohne Warnung; alle übrigen Spiele und Einstellungen bleiben, nichts wird als „defekt“ gesichert, und der
/// fremde Eintrag geht beim Speichern nicht verloren. Gegenprobe: Mit dem bisherigen Konverter scheitert dieselbe Datei.
/// </summary>
public sealed class UnknownSourceConfigTests
{
    private const string ConfigPath = @"C:\Cfg\config.json";

    /// <summary>Datei einer späteren Version: Steam, eine unbekannte Quelle „Itch“ mit einem neuen Feld, GOG, eigene Einstellungen.</summary>
    private const string FutureConfig = """
        {
          "version": 1,
          "settings": { "gracePeriodSeconds": 25, "searchFolders": [ "E:\\Spiele" ], "hideUninstalledGames": true },
          "games": [
            { "source": "Steam", "sourceId": "570", "name": "Dota 2", "installDir": "D:\\Steam\\steamapps\\common\\dota 2 beta", "hdrAuto": true },
            { "source": "Itch", "sourceId": "celeste", "name": "Celeste", "installDir": "D:\\itch\\Celeste", "hdrAuto": true, "neuesFeld": { "a": [ 1, 2 ] } },
            { "source": "Gog", "sourceId": "1423049311", "name": "Cyberpunk 2077", "installDir": "D:\\GOG-Spiele\\Cyberpunk 2077", "restoreAfter": false }
          ]
        }
        """;

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    [Fact]
    public void UnknownSource_config_loads_without_warning_or_backup_and_keeps_everything_else()
    {
        _fs.AddFile(ConfigPath, FutureConfig);

        var result = new ConfigStore(_fs, ConfigPath, _clock).Load();

        Assert.Null(result.Warning);
        Assert.False(result.CreatedNew);
        Assert.Equal(["Steam:570", "Gog:1423049311"], result.Config.Games.Select(g => g.Key));
        Assert.True(result.Config.FindGame("Steam:570")!.HdrAuto);
        Assert.False(result.Config.FindGame("Gog:1423049311")!.RestoreAfter);
        Assert.Equal(25, result.Config.Settings.GracePeriodSeconds);
        Assert.Equal([@"E:\Spiele"], result.Config.Settings.SearchFolders);
        Assert.True(result.Config.Settings.HideUninstalledGames);
        var unknown = Assert.Single(result.Config.UnknownSourceGames);
        Assert.Equal("Itch", unknown.GetProperty("source").GetString());
        Assert.Empty(_fs.EnumerateFiles(@"C:\Cfg", "*.bak"));
    }

    [Fact]
    public void UnknownSource_entries_are_written_back_unchanged()
    {
        _fs.AddFile(ConfigPath, FutureConfig);
        var store = new ConfigStore(_fs, ConfigPath, _clock);

        store.Save(store.Load().Config);

        using var saved = JsonDocument.Parse(_fs.ReadAllText(ConfigPath));
        using var original = JsonDocument.Parse(FutureConfig);
        var games = saved.RootElement.GetProperty("games").EnumerateArray().ToList();
        Assert.Equal(3, games.Count);
        Assert.True(JsonElement.DeepEquals(original.RootElement.GetProperty("games")[1], games.Single(g => g.GetProperty("source").GetString() == "Itch")));
        Assert.Equal(["Steam", "Gog", "Itch"], games.Select(g => g.GetProperty("source").GetString()));
        Assert.Equal(25, saved.RootElement.GetProperty("settings").GetProperty("gracePeriodSeconds").GetInt32());
    }

    [Fact]
    public void UnknownSource_entries_survive_updates_clones_and_external_changes()
    {
        _fs.AddFile(ConfigPath, FutureConfig);
        var service = new ConfigService(new ConfigStore(_fs, ConfigPath, _clock), _clock);

        service.Update(c => c.Settings.GracePeriodSeconds = 30);
        Assert.Single(service.Current.Clone().UnknownSourceGames);
        Assert.Contains("\"celeste\"", _fs.ReadAllText(ConfigPath), StringComparison.Ordinal);

        // Ein anderer Prozess ändert die Datei: neu gelesen, der fremde Eintrag bleibt, und das nächste Speichern behält ihn.
        _fs.AddFile(ConfigPath, FutureConfig.Replace("\"gracePeriodSeconds\": 25", "\"gracePeriodSeconds\": 40", StringComparison.Ordinal));
        Assert.True(service.ReloadIfChangedOnDisk());
        service.UpdateGame("Steam:570", g => g.HdrAuto = false);

        var text = _fs.ReadAllText(ConfigPath);
        Assert.Contains("\"neuesFeld\"", text, StringComparison.Ordinal);
        Assert.Equal(40, service.Current.Settings.GracePeriodSeconds);
        Assert.Equal(["Steam:570", "Gog:1423049311"], service.Current.Games.Select(g => g.Key));
        Assert.Empty(_fs.EnumerateFiles(@"C:\Cfg", "*.bak"));
    }

    [Fact]
    public void UnknownSource_counter_check_the_previous_converter_rejects_the_same_file()
    {
        // Gegenprobe: genau die Optionen bis 1.4.0 (nur JsonStringEnumConverter). Damit scheitert die Datei als Ganzes, und
        // ConfigStore sicherte sie als „defekt“ und begänne mit leerer Bibliothek.
        var previous = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppConfig>(FutureConfig, previous));

        // So liest 1.4.0 „Quiver“: Name fehlt in seiner Aufzählung, also scheitert die ganze Datei (E91, README).
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OldGame>("""{ "source": "Quiver" }""", previous));
        Assert.Equal(2, JsonSerializer.Deserialize<AppConfig>(FutureConfig, ConfigJson.Options)!.Games.Count);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("99")]
    [InlineData("\"\"")]
    [InlineData("{ \"name\": \"Itch\" }")]
    public void UnknownSource_values_that_are_no_known_source_are_kept_as_well(string source)
    {
        _fs.AddFile(ConfigPath, FutureConfig.Replace("\"source\": \"Itch\"", "\"source\": " + source, StringComparison.Ordinal));
        var store = new ConfigStore(_fs, ConfigPath, _clock);

        var result = store.Load();
        store.Save(result.Config);

        Assert.Null(result.Warning);
        Assert.Equal(2, result.Config.Games.Count);
        Assert.Single(result.Config.UnknownSourceGames);
        Assert.Contains("\"celeste\"", _fs.ReadAllText(ConfigPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"games\": [ { \"source\": \"Steam\", \"sourceId\": \"570\", \"hdrAuto\": \"ja\" } ] }")]
    [InlineData("{ \"version\": 1, \"games\": [ { \"source\": \"Itch\", \"sourceId\": \"a\" }, ")]
    public void UnknownSource_tolerance_does_not_hide_other_damage(string text)
    {
        _fs.AddFile(ConfigPath, text);

        var result = new ConfigStore(_fs, ConfigPath, _clock).Load();

        Assert.StartsWith("Konfiguration war defekt", result.Warning, StringComparison.Ordinal);
        Assert.Single(_fs.EnumerateFiles(@"C:\Cfg", "config.json.defekt-*.bak"));
    }

    [Fact]
    public void UnknownSource_does_not_include_quiver_which_this_version_knows()
    {
        _fs.AddFile(ConfigPath, FutureConfig.Replace("\"source\": \"Itch\", \"sourceId\": \"celeste\"", "\"source\": \"Quiver\", \"sourceId\": \"Black-BlackXBERecomp\"", StringComparison.Ordinal));

        var config = new ConfigStore(_fs, ConfigPath, _clock).Load().Config;

        Assert.Empty(config.UnknownSourceGames);
        Assert.Equal(GameSource.Quiver, config.FindGame("Quiver:Black-BlackXBERecomp")!.Source);
    }

    /// <summary>Die Quellen von 1.4.0 (vor Quiver).</summary>
    private enum OldGameSource
    {
        Steam,
        Epic,
        Xbox,
        Gog,
        Ubisoft,
        EA,
        BattleNet,
        Manual,
    }

    private sealed record OldGame(OldGameSource Source);
}
