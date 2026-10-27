using System.Text.Json;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>Neue Felder der Neugestaltung (E89): ältere Konfigurationen laden unverändert, neue Werte überleben das Speichern.</summary>
public sealed class ShowcaseConfigTests
{
    private const string Path = @"C:\cfg\config.json";

    private static ConfigStore Store(InMemoryFileSystem files) => new(files, Path, new FakeClock());

    [Fact]
    public void Old_config_without_new_fields_loads_with_defaults()
    {
        var files = new InMemoryFileSystem();
        files.AddFile(Path, """
            { "version": 1, "settings": { "librarySortOrder": "LastPlayed" },
              "games": [ { "source": "Steam", "sourceId": "1091500", "name": "Cyberpunk 2077", "hdrAuto": true } ] }
            """);

        var result = Store(files).Load();

        Assert.Null(result.Warning);
        var s = result.Config.Settings;
        Assert.Equal("LastPlayed", s.LibrarySortOrder);
        Assert.Null(s.LibraryView);
        Assert.Null(s.LibraryThumbnail);
        Assert.NotNull(s.Appearance);
        Assert.Null(s.Appearance.Theme);
        Assert.Null(s.Appearance.Preset);
        Assert.True(s.Appearance.AutoTextOnAccent);
        Assert.True(s.Appearance.BackgroundFollowsGame);
        Assert.True(s.Appearance.PauseWhilePlaying);
        Assert.True(s.Appearance.PauseInBackground);
        Assert.True(s.Appearance.RespectWindowsAnimations);
        Assert.False(s.Appearance.MinimizeToTrayAfterLaunch);
        Assert.False(s.Artwork.SteamGridDb);
        Assert.Null(result.Config.Games.Single().Artwork);
        Assert.True(result.Config.Games.Single().HdrAuto);
    }

    [Fact]
    public void New_fields_survive_save_and_load()
    {
        var files = new InMemoryFileSystem();
        var config = new AppConfig();
        config.Settings.LibraryView = "Grid";
        config.Settings.Appearance.Preset = "custom";
        config.Settings.Appearance.Accent = "#00E5FF";
        config.Settings.Appearance.EffectLevel = "Reduced";
        config.Settings.Appearance.PauseInBackground = false;
        config.Settings.Artwork.SteamGridDb = true;
        config.Games.Add(new GameConfig
        {
            Source = Core.Games.GameSource.Xbox,
            SourceId = "Hellraiser",
            Name = "Hellraiser",
            Artwork = new GameArtworkChoice { Hero = "alt:SplashScreen.png", Cover = ArtworkOverride.Generated },
        });

        Store(files).Save(config);
        var loaded = Store(files).Load().Config;

        Assert.Equal("Grid", loaded.Settings.LibraryView);
        Assert.Equal("custom", loaded.Settings.Appearance.Preset);
        Assert.Equal("#00E5FF", loaded.Settings.Appearance.Accent);
        Assert.Equal("Reduced", loaded.Settings.Appearance.EffectLevel);
        Assert.False(loaded.Settings.Appearance.PauseInBackground);
        Assert.True(loaded.Settings.Artwork.SteamGridDb);
        var artwork = loaded.Games.Single().Artwork;
        Assert.NotNull(artwork);
        Assert.Equal("alt:SplashScreen.png", artwork.Hero);
        Assert.Equal("gen", artwork.Cover);
        Assert.Null(artwork.Logo);
    }

    [Fact]
    public void Unknown_text_values_do_not_break_the_file()
    {
        // Text statt Aufzählung: Werte einer neueren Version machen die Datei nicht unlesbar.
        var files = new InMemoryFileSystem();
        files.AddFile(Path, """
            { "version": 1, "settings": { "libraryView": "Carousel", "appearance": { "theme": "Neon", "effectLevel": "Ultra" } }, "games": [] }
            """);

        var result = Store(files).Load();

        Assert.Null(result.Warning);
        Assert.Equal("Carousel", result.Config.Settings.LibraryView);
        Assert.Equal("Ultra", result.Config.Settings.Appearance.EffectLevel);
    }

    [Fact]
    public void Invalid_artwork_tokens_fall_back_to_automatic()
    {
        // Eine von Hand geänderte Datei darf nie auf fremde Dateien zeigen (Pfade, „..“).
        var files = new InMemoryFileSystem();
        files.AddFile(Path, """
            { "version": 1, "settings": {}, "games": [
              { "source": "Steam", "sourceId": "1", "name": "A", "artwork": { "hero": "custom:..\\..\\win.ini", "logo": "custom:C:\\x.png", "cover": "alt:SplashScreen.png" } },
              { "source": "Steam", "sourceId": "2", "name": "B", "artwork": { "hero": "irgendwas" } } ] }
            """);

        var games = Store(files).Load().Config.Games;

        Assert.Null(games[0].Artwork!.Hero);
        Assert.Null(games[0].Artwork!.Logo);
        Assert.Equal("alt:SplashScreen.png", games[0].Artwork!.Cover);
        Assert.Null(games[1].Artwork); // nichts Gültiges mehr: Abschnitt entfällt
    }

    [Fact]
    public void Toolbar_state_is_expanded_in_old_configs_and_survives_saving_without_touching_other_settings()
    {
        // Ältere Konfiguration ohne den Zustand der Werkzeugleiste: ausgeklappt, alles andere bleibt, wie es war.
        var files = new InMemoryFileSystem();
        files.AddFile(Path, """
            { "version": 1, "settings": { "libraryView": "Grid", "libraryHeroRatio": 0.4, "libraryPaneRatio": 0.3, "librarySortOrder": "LastPlayed",
              "appearance": { "effectLevel": "Reduced" } }, "games": [] }
            """);
        var store = Store(files);
        var config = store.Load().Config;
        Assert.False(config.Settings.LibraryToolbarCollapsed);

        // Eingeklappt gespeichert und wieder geladen: der Zustand und alle übrigen Werte.
        config.Settings.LibraryToolbarCollapsed = true;
        store.Save(config);
        var loaded = Store(files).Load().Config.Settings;
        Assert.True(loaded.LibraryToolbarCollapsed);
        Assert.Equal("Grid", loaded.LibraryView);
        Assert.Equal(0.4, loaded.LibraryHeroRatio);
        Assert.Equal(0.3, loaded.LibraryPaneRatio);
        Assert.Equal("LastPlayed", loaded.LibrarySortOrder);
        Assert.Equal("Reduced", loaded.Appearance.EffectLevel);
        Assert.Contains("\"libraryToolbarCollapsed\": true", files.ReadAllText(Path), StringComparison.Ordinal);
    }

    [Fact]
    public void Null_sections_are_repaired()
    {
        var files = new InMemoryFileSystem();
        files.AddFile(Path, """{ "version": 1, "settings": { "appearance": null, "artwork": null }, "games": [] }""");

        var s = Store(files).Load().Config.Settings;

        Assert.NotNull(s.Appearance);
        Assert.NotNull(s.Artwork);
        Assert.Contains("\"appearance\"", JsonSerializer.Serialize(new AppConfig(), ConfigJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void Colors_follow_game_is_off_in_old_configs()
    {
        var files = new InMemoryFileSystem();
        files.AddFile(Path, """
            { "version": 1, "settings": { "appearance": { "preset": "royal", "backgroundFollowsGame": false } }, "games": [] }
            """);

        var appearance = Store(files).Load().Config.Settings.Appearance;

        Assert.False(appearance.ColorsFollowGame);
        Assert.False(appearance.BackgroundFollowsGame);
        Assert.False(new AppearanceSettings().ColorsFollowGame);
    }

    [Fact]
    public void Colors_follow_game_survives_saving_and_standard_turns_it_off()
    {
        var files = new InMemoryFileSystem();
        var config = new AppConfig();
        config.Settings.Appearance.ColorsFollowGame = true;

        Store(files).Save(config);
        var text = files.ReadAllText(Path);
        var loaded = Store(files).Load().Config;

        Assert.Contains("\"colorsFollowGame\": true", text, StringComparison.Ordinal);
        Assert.True(loaded.Settings.Appearance.ColorsFollowGame);
        Assert.True(loaded.Settings.Appearance.BackgroundFollowsGame); // unabhängig voneinander
        loaded.Settings.Appearance = new AppearanceSettings(); // „Standard“ der Seite „Darstellung“
        Assert.False(loaded.Settings.Appearance.ColorsFollowGame);
    }
}
