using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>
/// Einstellungen der Spielebibliothek (Sortierung, Spaltenbreite, Höhe des Titelbereichs): ältere und neuere config.json
/// bleiben lesbar.
/// </summary>
public sealed class LibraryViewSettingsTests
{
    private const string Path = @"C:\Users\Test\AppData\Roaming\HdrPilot\config.json";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    private ConfigService Service() => new(new ConfigStore(_fs, Path, _clock), _clock);

    [Fact]
    public void Config_without_the_new_settings_loads_with_defaults_and_is_not_rewritten_with_them()
    {
        _fs.AddFile(Path, """
            {
              "version": 1,
              "settings": { "pollIntervalMs": 800, "targetDisplays": [] },
              "games": [ { "source": "Steam", "sourceId": "570", "name": "Dota 2", "hdrAuto": true } ]
            }
            """);
        var service = Service();

        Assert.Null(service.LoadWarning);
        Assert.Null(service.Current.Settings.LibrarySortOrder);
        Assert.Null(service.Current.Settings.LibraryDetailsRatio);
        Assert.Equal(800, service.Current.Settings.PollIntervalMs);

        // Speichern ohne Änderung der neuen Werte: sie tauchen in der Datei nicht auf (gleiche Datei für ältere Versionen).
        service.Update(c => c.Settings.GracePeriodSeconds = 12);
        var text = _fs.ReadAllText(Path);
        Assert.DoesNotContain("librarySortOrder", text, StringComparison.Ordinal);
        Assert.DoesNotContain("libraryDetailsRatio", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_settings_survive_a_restart()
    {
        Service().Update(c =>
        {
            c.Settings.LibrarySortOrder = "LastPlayed";
            c.Settings.LibraryDetailsRatio = 0.412;
        });

        var text = _fs.ReadAllText(Path);
        Assert.Contains("\"librarySortOrder\": \"LastPlayed\"", text, StringComparison.Ordinal);
        Assert.Contains("\"libraryDetailsRatio\": 0.412", text, StringComparison.Ordinal);

        var reloaded = Service().Current.Settings;
        Assert.Equal("LastPlayed", reloaded.LibrarySortOrder);
        Assert.Equal(0.412, reloaded.LibraryDetailsRatio);
    }

    [Fact]
    public void Config_without_the_hero_height_loads_unchanged_and_the_hero_height_survives_a_restart()
    {
        // Konfiguration von vor dem Ziehgriff (Wunsch des Nutzers 2026-10-16): alles andere bleibt, wie es ist.
        _fs.AddFile(Path, """
            {
              "version": 1,
              "settings": { "pollIntervalMs": 800, "libraryPaneRatio": 0.31, "libraryView": "Grid", "librarySortOrder": "LastPlayed" },
              "games": [ { "source": "Steam", "sourceId": "570", "name": "Dota 2", "hdrAuto": true } ]
            }
            """);
        var service = Service();

        Assert.Null(service.LoadWarning);
        var settings = service.Current.Settings;
        Assert.Null(settings.LibraryHeroRatio);
        Assert.Equal((800, 0.31, "Grid", "LastPlayed"), (settings.PollIntervalMs, settings.LibraryPaneRatio, settings.LibraryView, settings.LibrarySortOrder));
        Assert.True(Assert.Single(service.Current.Games).HdrAuto);

        // Ohne gemerkte Höhe taucht das Feld beim Speichern nicht auf.
        service.Update(c => c.Settings.GracePeriodSeconds = 12);
        Assert.DoesNotContain("libraryHeroRatio", _fs.ReadAllText(Path), StringComparison.Ordinal);

        // Gemerkt: steht in der Datei und kommt nach einem Neustart unverändert zurück, die übrigen Werte auch.
        service.Update(c => c.Settings.LibraryHeroRatio = 0.4584);
        Assert.Contains("\"libraryHeroRatio\": 0.4584", _fs.ReadAllText(Path), StringComparison.Ordinal);
        var reloaded = Service().Current;
        Assert.Equal(0.4584, reloaded.Settings.LibraryHeroRatio);
        Assert.Equal((800, 0.31, "Grid", 12), (reloaded.Settings.PollIntervalMs, reloaded.Settings.LibraryPaneRatio, reloaded.Settings.LibraryView, reloaded.Settings.GracePeriodSeconds));
        Assert.Equal("Dota 2", Assert.Single(reloaded.Games).Name);

        // Zurück zum Standard: Feld entfällt wieder.
        service.Update(c => c.Settings.LibraryHeroRatio = null);
        Assert.DoesNotContain("libraryHeroRatio", _fs.ReadAllText(Path), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_without_the_hero_heights_of_grid_and_shelf_loads_unchanged_and_they_survive_a_restart()
    {
        // Konfiguration von 1.5.0 (Griff nur in der Liste): Höhe der Liste, Aufteilung, Ansicht und alles andere bleiben.
        _fs.AddFile(Path, """
            {
              "version": 1,
              "settings": { "pollIntervalMs": 800, "libraryHeroRatio": 0.4584, "libraryPaneRatio": 0.31, "libraryView": "Shelf", "libraryToolbarCollapsed": true },
              "games": [ { "source": "Steam", "sourceId": "570", "name": "Dota 2", "hdrAuto": true } ]
            }
            """);
        var service = Service();

        Assert.Null(service.LoadWarning);
        var settings = service.Current.Settings;
        Assert.Null(settings.LibraryGridHeroRatio);
        Assert.Null(settings.LibraryShelfHeroRatio);
        Assert.Equal((800, 0.4584, 0.31, "Shelf", true), (settings.PollIntervalMs, settings.LibraryHeroRatio, settings.LibraryPaneRatio, settings.LibraryView, settings.LibraryToolbarCollapsed));
        Assert.True(Assert.Single(service.Current.Games).HdrAuto);

        // Ohne gemerkte Höhe tauchen die Felder beim Speichern nicht auf.
        service.Update(c => c.Settings.GracePeriodSeconds = 12);
        var text = _fs.ReadAllText(Path);
        Assert.DoesNotContain("libraryGridHeroRatio", text, StringComparison.Ordinal);
        Assert.DoesNotContain("libraryShelfHeroRatio", text, StringComparison.Ordinal);
        Assert.Contains("\"libraryHeroRatio\": 0.4584", text, StringComparison.Ordinal);

        // Gemerkt: stehen in der Datei und kommen nach einem Neustart unverändert zurück, die übrigen Werte auch.
        service.Update(c =>
        {
            c.Settings.LibraryGridHeroRatio = 0.2712;
            c.Settings.LibraryShelfHeroRatio = 0.55;
        });
        text = _fs.ReadAllText(Path);
        Assert.Contains("\"libraryGridHeroRatio\": 0.2712", text, StringComparison.Ordinal);
        Assert.Contains("\"libraryShelfHeroRatio\": 0.55", text, StringComparison.Ordinal);
        var reloaded = Service().Current.Settings;
        Assert.Equal((0.4584, 0.2712, 0.55), (reloaded.LibraryHeroRatio, reloaded.LibraryGridHeroRatio, reloaded.LibraryShelfHeroRatio));
        Assert.Equal((800, 0.31, "Shelf", true, 12), (reloaded.PollIntervalMs, reloaded.LibraryPaneRatio, reloaded.LibraryView, reloaded.LibraryToolbarCollapsed, reloaded.GracePeriodSeconds));

        // Zurück zum Standard: Felder entfallen wieder.
        service.Update(c =>
        {
            c.Settings.LibraryGridHeroRatio = null;
            c.Settings.LibraryShelfHeroRatio = null;
        });
        text = _fs.ReadAllText(Path);
        Assert.DoesNotContain("libraryGridHeroRatio", text, StringComparison.Ordinal);
        Assert.DoesNotContain("libraryShelfHeroRatio", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_sort_order_from_a_newer_version_does_not_break_the_file()
    {
        _fs.AddFile(Path, """
            { "version": 1, "settings": { "librarySortOrder": "Playtime", "libraryDetailsRatio": 0.5 }, "games": [] }
            """);
        var service = Service();

        Assert.Null(service.LoadWarning); // keine Sicherung als „defekt“
        Assert.Equal("Playtime", service.Current.Settings.LibrarySortOrder); // bleibt erhalten; die Oberfläche nimmt dann „Name“
        Assert.Equal(0.5, service.Current.Settings.LibraryDetailsRatio);
        Assert.Empty(_fs.EnumerateFiles(System.IO.Path.GetDirectoryName(Path)!, "*.bak"));
    }
}
