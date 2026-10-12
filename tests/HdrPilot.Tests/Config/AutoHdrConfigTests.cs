using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>
/// „Windows Auto HDR“ je Spiel in config.json (E88): ältere Dateien laden unverändert, der Wert überlebt Speichern und Laden
/// und ist unabhängig von „HDR automatisch“. Die frühere Einstellung „Notlösung“ gibt es nicht mehr: Ein Eintrag aus einer
/// Vorabversion stört nicht und verschwindet beim nächsten Speichern.
/// </summary>
public sealed class AutoHdrConfigTests
{
    private const string Path = @"C:\Users\Test\AppData\Roaming\HdrPilot\config.json";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    private ConfigService Service() => new(new ConfigStore(_fs, Path, _clock), _clock);

    [Fact]
    public void Config_from_before_auto_hdr_loads_with_the_option_off_and_keeps_its_other_values()
    {
        _fs.AddFile(Path, """
            {
              "version": 1,
              "settings": { "pollIntervalMs": 800, "hotkeyFallback": true },
              "games": [
                { "source": "Steam", "sourceId": "570", "name": "Dota 2", "hdrAuto": true, "restoreAfter": false,
                  "knownExecutables": [ "D:\\Steam\\dota2.exe" ] }
              ]
            }
            """);
        var service = Service();

        Assert.Null(service.LoadWarning);
        var game = service.Current.FindGame("Steam:570")!;
        Assert.False(game.AutoHdr);
        Assert.True(game.HdrAuto); // Gegenprobe: die alten Werte sind da
        Assert.False(game.RestoreAfter);
        Assert.Equal([@"D:\Steam\dota2.exe"], game.KnownExecutables);
        Assert.Equal(800, service.Current.Settings.PollIntervalMs);
        Assert.True(service.Current.Settings.HotkeyFallback);

        // Speichern einer anderen Änderung lässt alles andere stehen und schreibt die Option als „aus“.
        service.Update(c => c.Settings.GracePeriodSeconds = 12);
        var reloaded = Service().Current;
        Assert.False(reloaded.FindGame("Steam:570")!.AutoHdr);
        Assert.True(reloaded.FindGame("Steam:570")!.HdrAuto);
        Assert.Equal(12, reloaded.Settings.GracePeriodSeconds);
    }

    [Fact]
    public void Json_name_is_autoHdr_per_game()
    {
        var service = Service();
        service.Update(c => c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "a", Name = "A", AutoHdr = true }));

        var text = _fs.ReadAllText(Path);
        Assert.Contains("\"autoHdr\": true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"AutoHdr\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("autoHdrGlobalFallback", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Former_fallback_setting_from_a_preview_is_ignored_and_dropped_on_save()
    {
        _fs.AddFile(Path, """
            {
              "version": 1,
              "settings": { "autoHdrGlobalFallback": true, "gracePeriodSeconds": 7 },
              "games": [ { "source": "Manual", "sourceId": "a", "name": "A", "autoHdr": true } ]
            }
            """);
        var service = Service();

        Assert.Null(service.LoadWarning);
        Assert.Equal(7, service.Current.Settings.GracePeriodSeconds);
        Assert.True(service.Current.FindGame("Manual:a")!.AutoHdr);

        service.Update(c => c.Settings.GracePeriodSeconds = 8);
        Assert.DoesNotContain("autoHdrGlobalFallback", _fs.ReadAllText(Path), StringComparison.Ordinal);
    }

    [Fact]
    public void Option_survives_save_and_load_and_stays_independent_of_hdr_automatic()
    {
        Service().Update(c =>
        {
            c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "nur-auto-hdr", Name = "Nur Auto HDR", AutoHdr = true, HdrAuto = false });
            c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "nur-hdr", Name = "Nur HDR automatisch", AutoHdr = false, HdrAuto = true });
        });

        var reloaded = Service().Current;
        Assert.True(reloaded.FindGame("Manual:nur-auto-hdr")!.AutoHdr);
        Assert.False(reloaded.FindGame("Manual:nur-auto-hdr")!.HdrAuto);
        Assert.False(reloaded.FindGame("Manual:nur-hdr")!.AutoHdr);
        Assert.True(reloaded.FindGame("Manual:nur-hdr")!.HdrAuto);
    }

    [Fact]
    public void Normalize_and_clone_leave_the_option_alone()
    {
        var config = new AppConfig();
        config.Games.Add(new GameConfig { Source = GameSource.Epic, SourceId = "x", Name = "X", AutoHdr = true });
        config.Games.Add(new GameConfig { Source = GameSource.Epic, SourceId = "y", Name = "Y", AutoHdr = false });

        ConfigStore.Normalize(config);
        var clone = config.Clone();

        Assert.True(clone.FindGame("Epic:x")!.AutoHdr);
        Assert.False(clone.FindGame("Epic:y")!.AutoHdr);
    }

    [Fact]
    public void New_games_start_with_the_option_off()
    {
        Assert.False(new GameConfig().AutoHdr);
        var service = Service(); // neue Datei mit Standardwerten
        service.Update(c => c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "neu", Name = "Neu" }));
        Assert.False(Service().Current.FindGame("Manual:neu")!.AutoHdr);
    }
}
