using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>
/// „Windows Auto HDR“ je Spiel und die globale Notlösung in config.json (E88): ältere Dateien laden unverändert, beide
/// Werte überleben Speichern und Laden und sind unabhängig von „HDR automatisch“.
/// </summary>
public sealed class AutoHdrConfigTests
{
    private const string Path = @"C:\Users\Test\AppData\Roaming\HdrPilot\config.json";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    private ConfigService Service() => new(new ConfigStore(_fs, Path, _clock), _clock);

    [Fact]
    public void Config_from_before_auto_hdr_loads_with_both_options_off_and_keeps_its_other_values()
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
        Assert.False(service.Current.Settings.AutoHdrGlobalFallback);
        var game = service.Current.FindGame("Steam:570")!;
        Assert.False(game.AutoHdr);
        Assert.True(game.HdrAuto); // Gegenprobe: die alten Werte sind da
        Assert.False(game.RestoreAfter);
        Assert.Equal([@"D:\Steam\dota2.exe"], game.KnownExecutables);
        Assert.Equal(800, service.Current.Settings.PollIntervalMs);
        Assert.True(service.Current.Settings.HotkeyFallback);

        // Speichern einer anderen Änderung lässt alles andere stehen und schreibt beide Optionen als „aus“.
        service.Update(c => c.Settings.GracePeriodSeconds = 12);
        var reloaded = Service().Current;
        Assert.False(reloaded.FindGame("Steam:570")!.AutoHdr);
        Assert.True(reloaded.FindGame("Steam:570")!.HdrAuto);
        Assert.False(reloaded.Settings.AutoHdrGlobalFallback);
        Assert.Equal(12, reloaded.Settings.GracePeriodSeconds);
    }

    [Fact]
    public void Json_names_are_autoHdr_per_game_and_autoHdrGlobalFallback_in_the_settings()
    {
        var service = Service();
        service.Update(c =>
        {
            c.Settings.AutoHdrGlobalFallback = true;
            c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "a", Name = "A", AutoHdr = true });
        });

        var text = _fs.ReadAllText(Path);
        Assert.Contains("\"autoHdr\": true", text, StringComparison.Ordinal);
        Assert.Contains("\"autoHdrGlobalFallback\": true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"AutoHdr\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_options_survive_save_and_load_and_stay_independent_of_hdr_automatic()
    {
        Service().Update(c =>
        {
            c.Settings.AutoHdrGlobalFallback = true;
            c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "nur-auto-hdr", Name = "Nur Auto HDR", AutoHdr = true, HdrAuto = false });
            c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "nur-hdr", Name = "Nur HDR automatisch", AutoHdr = false, HdrAuto = true });
        });

        var reloaded = Service().Current;
        Assert.True(reloaded.Settings.AutoHdrGlobalFallback);
        Assert.True(reloaded.FindGame("Manual:nur-auto-hdr")!.AutoHdr);
        Assert.False(reloaded.FindGame("Manual:nur-auto-hdr")!.HdrAuto);
        Assert.False(reloaded.FindGame("Manual:nur-hdr")!.AutoHdr);
        Assert.True(reloaded.FindGame("Manual:nur-hdr")!.HdrAuto);
    }

    [Fact]
    public void Normalize_and_clone_leave_the_options_alone()
    {
        var config = new AppConfig();
        config.Settings.AutoHdrGlobalFallback = true;
        config.Games.Add(new GameConfig { Source = GameSource.Epic, SourceId = "x", Name = "X", AutoHdr = true });
        config.Games.Add(new GameConfig { Source = GameSource.Epic, SourceId = "y", Name = "Y", AutoHdr = false });

        ConfigStore.Normalize(config);
        var clone = config.Clone();

        Assert.True(clone.Settings.AutoHdrGlobalFallback);
        Assert.True(clone.FindGame("Epic:x")!.AutoHdr);
        Assert.False(clone.FindGame("Epic:y")!.AutoHdr);
    }

    [Fact]
    public void New_games_and_new_settings_start_with_both_options_off()
    {
        Assert.False(new GameConfig().AutoHdr);
        Assert.False(new AppSettings().AutoHdrGlobalFallback);
        var service = Service(); // neue Datei mit Standardwerten
        Assert.False(service.Current.Settings.AutoHdrGlobalFallback);
    }
}
