using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>Einstellungen der Spielebibliothek (Sortierung, Spaltenbreite): ältere und neuere config.json bleiben lesbar.</summary>
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
