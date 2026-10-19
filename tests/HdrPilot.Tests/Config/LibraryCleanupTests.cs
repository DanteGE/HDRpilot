using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>
/// Daten nicht installierter Spiele entfernen (E90, G11.1): nur nicht installierte Launcher-Spiele ohne Sitzung, geprüft beim
/// Schreiben (ein Einlesen dazwischen lässt alles stehen), was verloren geht, und dass ein wieder installiertes Spiel als neu
/// mit „HDR automatisch“ aus zurückkommt.
/// </summary>
public sealed class LibraryCleanupTests
{
    private const string Path = @"C:\Users\Test\AppData\Roaming\HdrPilot\config.json";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    private ConfigService Service() => new(new ConfigStore(_fs, Path, _clock), _clock);

    private static GameConfig Game(GameSource source, string id, string name, bool installed, bool hdrAuto = false) => new()
    {
        Source = source,
        SourceId = id,
        Name = name,
        InstallDir = $@"F:\Steam-Bibliothek\steamapps\common\{name}",
        IsInstalled = installed,
        IsNew = false,
        HdrAuto = hdrAuto,
    };

    private ConfigService WithGames(params GameConfig[] games)
    {
        var service = Service();
        service.Update(c => c.Games.AddRange(games));
        return service;
    }

    [Fact]
    public void LibraryCleanup_removes_only_not_installed_launcher_games()
    {
        var service = WithGames(
            Game(GameSource.Steam, "1", "Weg", installed: false, hdrAuto: true),
            Game(GameSource.Steam, "2", "Da", installed: true),
            Game(GameSource.Manual, "m1", "Eigenes ohne Ordner", installed: false),
            Game(GameSource.Epic, "e1", "Auch weg", installed: false));
        IReadOnlyList<GameConfig> removed = [];

        service.Update(c => removed = LibraryCleanup.RemoveNotInstalled(c, ["Steam:1", "Steam:2", "Manual:m1"]));

        Assert.Equal(["Steam:1"], removed.Select(g => g.Key));
        Assert.True(removed[0].HdrAuto); // die entfernten Einträge, wie sie waren (für das Aufräumen ihrer Dateien)
        Assert.Equal(["Steam:2", "Manual:m1", "Epic:e1"], service.Current.Games.Select(g => g.Key)); // nicht genannt: bleibt
    }

    [Fact]
    public void LibraryCleanup_keys_ignore_case_and_unknown_keys_change_nothing()
    {
        var service = WithGames(Game(GameSource.Steam, "1304930", "The Outlast Trials", installed: false));
        var before = _fs.ReadAllText(Path);
        IReadOnlyList<GameConfig> removed = [];

        service.Update(c => removed = LibraryCleanup.RemoveNotInstalled(c, ["Steam:404"]));
        Assert.Empty(removed);
        Assert.Equal(before, _fs.ReadAllText(Path));

        service.Update(c => removed = LibraryCleanup.RemoveNotInstalled(c, ["steam:1304930"]));
        Assert.Single(removed);
        Assert.Empty(service.Current.Games);
    }

    [Fact]
    public void LibraryCleanup_game_found_again_before_writing_keeps_everything()
    {
        // Die Rückfrage ist offen, ein Einlesen findet das Spiel wieder (Laufwerk angesteckt): erst beim Schreiben geprüft,
        // bleibt der Eintrag samt Einstellungen.
        var service = WithGames(Game(GameSource.Steam, "1", "Weg", installed: false, hdrAuto: true));
        var report = new LibraryScanReport(
        [
            new ScannerReport(new ScanResult { Source = GameSource.Steam, ScannerName = "Steam", LauncherDetected = true, Games =
            [
                new GameEntry { Source = GameSource.Steam, SourceId = "1", Name = "Weg", InstallDir = @"F:\Steam-Bibliothek\steamapps\common\Weg" },
            ] }, TimeSpan.Zero),
        ]);
        service.ApplyScan(report, _fs);
        IReadOnlyList<GameConfig> removed = [];

        service.Update(c => removed = LibraryCleanup.RemoveNotInstalled(c, ["Steam:1"]));

        Assert.Empty(removed);
        var game = Assert.Single(service.Current.Games);
        Assert.True(game.IsInstalled);
        Assert.True(game.HdrAuto);
    }

    [Fact]
    public void LibraryCleanup_running_or_prepared_session_keeps_the_game()
    {
        var service = WithGames(Game(GameSource.Steam, "1", "Läuft", installed: false), Game(GameSource.Steam, "2", "Ruht", installed: false));
        IReadOnlyList<GameConfig> removed = [];

        service.Update(c => removed = LibraryCleanup.RemoveNotInstalled(c, ["Steam:1", "Steam:2"], key => key == "Steam:1"));

        Assert.Equal(["Steam:2"], removed.Select(g => g.Key));
        Assert.Equal("Steam:1", Assert.Single(service.Current.Games).Key);
    }

    [Fact]
    public void LibraryCleanup_reinstalled_game_comes_back_as_new_with_hdr_auto_off()
    {
        var game = Game(GameSource.Steam, "1304930", "The Outlast Trials", installed: false, hdrAuto: true);
        game.AutoHdr = true;
        game.DetectedExecutables = [@"C:\Spiele\OutlastTrials\Binaries\Win64\TOTClient-Win64-Shipping.exe"];
        game.LastPlayed = new DateTimeOffset(2026, 10, 17, 20, 0, 0, TimeSpan.Zero);
        var service = WithGames(game);
        service.Update(c => LibraryCleanup.RemoveNotInstalled(c, ["Steam:1304930"]));
        Assert.Empty(service.Current.Games);

        var report = new LibraryScanReport(
        [
            new ScannerReport(new ScanResult { Source = GameSource.Steam, ScannerName = "Steam", LauncherDetected = true, Games =
            [
                new GameEntry { Source = GameSource.Steam, SourceId = "1304930", Name = "The Outlast Trials", InstallDir = @"C:\Spiele\OutlastTrials" },
            ] }, TimeSpan.Zero),
        ]);
        service.ApplyScan(report, _fs);

        var back = Assert.Single(service.Current.Games);
        Assert.True(back.IsNew);
        Assert.True(back.IsInstalled);
        Assert.False(back.HdrAuto);
        Assert.False(back.AutoHdr);
        Assert.Empty(back.DetectedExecutables);
        Assert.Null(back.LastPlayed);
    }

    [Fact]
    public void LibraryCleanup_loss_names_every_own_setting()
    {
        var game = Game(GameSource.Steam, "1", "Voll", installed: false, hdrAuto: true);
        game.AutoHdr = true;
        game.Display = @"\\?\DISPLAY#MSI#1";
        game.RestoreAfter = false;
        game.DetectedExecutables = [@"F:\a.exe", @"F:\b.exe", " "];
        game.ExtraExecutables = [@"C:\Tools\ReShade.exe"];
        game.ExcludedExecutables = ["Crash.exe", "Bench.exe", "Patch.exe"];
        game.CustomIcon = CustomIcons.FileNameFor(game.Key);
        game.Artwork = new GameArtworkChoice { Hero = "custom:Steam_1_00000000-hero.jpg", Logo = "gen", Cover = "custom:Steam_1_00000000-cover.jpg" };
        game.LastPlayed = new DateTimeOffset(2026, 10, 17, 7, 50, 0, TimeSpan.Zero);

        var loss = LibraryCleanup.LossOf(game);

        Assert.Equal(new SettingsLoss(true, true, true, true, 2, 1, 3, true, 2, 1, game.LastPlayed), loss);
        Assert.False(loss.IsEmpty);
    }

    [Fact]
    public void LibraryCleanup_loss_of_a_game_with_only_a_chosen_image_is_not_empty()
    {
        // Ein anderes Launcher-Bild per „Bild wählen…“ ist eine eigene Wahl ohne eigene Datei: Sie geht mit dem Eintrag verloren.
        var game = Game(GameSource.Steam, "2", "Nur Bildwahl", installed: false);
        game.Artwork = new GameArtworkChoice { Hero = "alt:library_hero_2.jpg" };

        var loss = LibraryCleanup.LossOf(game);

        Assert.Equal((0, 1), (loss.CustomImages, loss.ChosenImages));
        Assert.False(loss.IsEmpty);
    }

    [Fact]
    public void LibraryCleanup_loss_of_an_untouched_game_is_empty()
    {
        var loss = LibraryCleanup.LossOf(Game(GameSource.Steam, "4320630", "Antivirus Survivors 2003 Professional Demo", installed: false));

        Assert.True(loss.IsEmpty);
        Assert.Equal(new SettingsLoss(false, false, false, false, 0, 0, 0, false, 0, 0, null), loss);
    }

    [Fact]
    public void LibraryCleanup_counts_all_not_installed_but_offers_only_launcher_games_by_name()
    {
        var config = new AppConfig
        {
            Games =
            [
                Game(GameSource.Steam, "3", "ROLLA", installed: false),
                Game(GameSource.Steam, "1", "Da", installed: true),
                Game(GameSource.Manual, "m1", "Eigenes ohne Ordner", installed: false),
                Game(GameSource.Steam, "2", "VEIN Demo", installed: false),
                Game(GameSource.Epic, "e", "antivirus", installed: false),
            ],
        };

        Assert.Equal(4, LibraryCleanup.CountNotInstalled(config));
        Assert.Equal(["antivirus", "ROLLA", "VEIN Demo"], LibraryCleanup.Removable(config).Select(g => g.Name));
        Assert.False(LibraryCleanup.IsRemovable(config.Games[1]));
        Assert.False(LibraryCleanup.IsRemovable(config.Games[2]));
        Assert.True(LibraryCleanup.IsRemovable(config.Games[0]));
    }
}
