using System.Text.Json;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

public sealed class ConfigTests
{
    private const string Path = @"C:\Users\Test\AppData\Roaming\HdrPilot\config.json";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    private ConfigService Service() => new(new ConfigStore(_fs, Path, _clock), _clock);

    [Fact]
    public void Missing_file_creates_defaults_with_version()
    {
        var service = Service();
        Assert.True(_fs.FileExists(Path));
        using var doc = JsonDocument.Parse(_fs.ReadAllText(Path));
        Assert.Equal(AppConfig.CurrentVersion, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(1000, service.Current.Settings.PollIntervalMs);
        Assert.Equal(10, service.Current.Settings.GracePeriodSeconds);
        Assert.True(service.Current.Settings.Notifications);
        Assert.False(service.Current.Settings.HotkeyFallback);
        Assert.False(service.Current.Settings.StartWithWindows);
        Assert.Empty(service.Current.Settings.TargetDisplays);
    }

    [Fact]
    public void Settings_and_game_settings_survive_restart()
    {
        var s1 = Service();
        s1.Update(c =>
        {
            c.Settings.PollIntervalMs = 500;
            c.Settings.GracePeriodSeconds = 25;
            c.Settings.SearchFolders = [@"E:\Spiele"];
            c.Settings.TargetDisplays = [@"\\?\DISPLAY#MSI#1"];
            c.Games.Add(new GameConfig { Source = GameSource.Epic, SourceId = "Fortnite", Name = "Fortnite", HdrAuto = true, RestoreAfter = false, Display = @"\\?\DISPLAY#MSI#1" });
        });

        var s2 = Service();
        Assert.Equal(500, s2.Current.Settings.PollIntervalMs);
        Assert.Equal(25, s2.Current.Settings.GracePeriodSeconds);
        Assert.Equal([@"E:\Spiele"], s2.Current.Settings.SearchFolders);
        var g = s2.Current.FindGame("Epic:Fortnite")!;
        Assert.True(g.HdrAuto);
        Assert.False(g.RestoreAfter);
        Assert.Equal(@"\\?\DISPLAY#MSI#1", g.Display);
    }

    [Fact]
    public void Broken_config_is_backed_up_and_defaults_used()
    {
        _fs.AddFile(Path, "{ \"version\": 1, \"games\": [ { kaputt");
        var service = Service();

        Assert.NotNull(service.LoadWarning);
        Assert.Contains("defekt", service.LoadWarning, StringComparison.Ordinal);
        Assert.Empty(service.Current.Games);
        Assert.Single(_fs.EnumerateFiles(System.IO.Path.GetDirectoryName(Path)!, "config.json.defekt-*.bak"));
    }

    [Fact]
    public void Newer_version_is_backed_up_before_overwrite()
    {
        _fs.AddFile(Path, "{ \"version\": 7, \"settings\": { \"pollIntervalMs\": 800 } }");
        var service = Service();
        Assert.Contains("neueren Version", service.LoadWarning, StringComparison.Ordinal);
        Assert.Equal(800, service.Current.Settings.PollIntervalMs);
        Assert.Single(_fs.EnumerateFiles(System.IO.Path.GetDirectoryName(Path)!, "config.json.v7-*.bak"));
    }

    [Fact]
    public void Values_are_clamped_and_duplicates_removed()
    {
        _fs.AddFile(Path, """
            { "version": 1, "settings": { "pollIntervalMs": 1, "gracePeriodSeconds": -5, "targetDisplays": null },
              "games": [ { "source": "Steam", "sourceId": "1", "name": "A" }, { "source": "Steam", "sourceId": "1", "name": "B" }, { "source": "Steam", "sourceId": "" } ] }
            """);
        var c = Service().Current;
        Assert.Equal(250, c.Settings.PollIntervalMs);
        Assert.Equal(0, c.Settings.GracePeriodSeconds);
        Assert.Empty(c.Settings.TargetDisplays);
        Assert.Equal("A", Assert.Single(c.Games).Name);
    }

    [Fact]
    public void Save_is_atomic_and_revision_increases()
    {
        var service = Service();
        var before = service.Revision;
        var changed = 0;
        service.Changed += (_, _) => changed++;
        service.Update(c => c.Settings.Notifications = false);
        Assert.Equal(before + 1, service.Revision);
        Assert.Equal(1, changed);
        Assert.False(Service().Current.Settings.Notifications);
    }

    [Fact]
    public void Current_snapshot_is_not_changed_by_later_updates()
    {
        var service = Service();
        var snapshot = service.Current;
        service.Update(c => c.Settings.GracePeriodSeconds = 42);
        Assert.Equal(10, snapshot.Settings.GracePeriodSeconds);
        Assert.Equal(42, service.Current.Settings.GracePeriodSeconds);
    }

    [Fact]
    public void Scan_merge_keeps_settings_by_key_marks_new_and_missing()
    {
        _fs.AddDirectory(@"D:\Manual\Game");
        var service = Service();
        service.Update(c =>
        {
            c.Games.Add(new GameConfig { Source = GameSource.Steam, SourceId = "1", Name = "Alt", InstallDir = @"D:\Old", HdrAuto = true, IsNew = false, RestoreAfter = false });
            c.Games.Add(new GameConfig { Source = GameSource.Steam, SourceId = "2", Name = "Weg", InstallDir = @"D:\Gone", HdrAuto = true, IsNew = false });
            c.Games.Add(new GameConfig { Source = GameSource.Gog, SourceId = "9", Name = "GOG", InstallDir = @"D:\GOG-Spiele", IsNew = false });
            c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "m1", Name = "Manuell", InstallDir = @"D:\Manual\Game", IsNew = false });
        });

        var report = new LibraryScanReport(
        [
            new ScannerReport(new ScanResult { Source = GameSource.Steam, ScannerName = "Steam", LauncherDetected = true, Games =
            [
                new GameEntry { Source = GameSource.Steam, SourceId = "1", Name = "Neuer Name", InstallDir = @"D:\New", LaunchCommand = "steam://rungameid/1" },
                new GameEntry { Source = GameSource.Steam, SourceId = "3", Name = "Brandneu", InstallDir = @"D:\Three", LaunchCommand = "steam://rungameid/3" },
            ] }, TimeSpan.Zero),
            new ScannerReport(new ScanResult { Source = GameSource.Gog, ScannerName = "GOG", Errors = [new ScanError("Scanner abgestürzt: X")] }, TimeSpan.Zero),
        ]);

        var summary = service.ApplyScan(report, _fs);
        var c = service.Current;

        var g1 = c.FindGame("Steam:1")!;
        Assert.Equal("Neuer Name", g1.Name);
        Assert.Equal(@"D:\New", g1.InstallDir);
        Assert.True(g1.HdrAuto);
        Assert.False(g1.RestoreAfter);
        Assert.False(g1.IsNew);

        var g3 = c.FindGame("Steam:3")!;
        Assert.True(g3.IsNew);
        Assert.False(g3.HdrAuto);

        Assert.False(c.FindGame("Steam:2")!.IsInstalled);
        Assert.True(c.FindGame("Steam:2")!.HdrAuto); // bleibt mit Einstellungen erhalten
        Assert.True(c.FindGame("Gog:9")!.IsInstalled); // Scanner abgestürzt: nicht als verschwunden markieren
        Assert.True(c.FindGame("Manual:m1")!.IsInstalled);
        Assert.Equal(new MergeSummary(1, 1, 1, 0), summary);
    }

    [Fact]
    public void Session_state_store_roundtrip_and_broken_file()
    {
        var store = new FileSessionStateStore(_fs, @"C:\Users\Test\AppData\Local\HdrPilot\session-state.json");
        Assert.Null(store.Load());
        store.Save(new SessionStateData { Displays = [new DisplayClaimState { MonitorDevicePath = "p", FriendlyName = "n", WasHdrOn = true, Games = ["Steam:1"] }] });
        var loaded = store.Load()!;
        Assert.True(loaded.Displays.Single().WasHdrOn);
        Assert.Equal(["Steam:1"], loaded.Displays.Single().Games);

        _fs.AddFile(@"C:\Users\Test\AppData\Local\HdrPilot\session-state.json", "\0\0kaputt");
        Assert.Null(store.Load());
        store.Delete();
        Assert.False(_fs.FileExists(@"C:\Users\Test\AppData\Local\HdrPilot\session-state.json"));
    }

    [Fact]
    public void App_paths_split_roaming_config_and_local_data()
    {
        var paths = AppPaths.Default(new FakeEnvironment());
        Assert.Equal(@"C:\Users\Test\AppData\Roaming\HdrPilot\config.json", paths.ConfigFile);
        Assert.Equal(@"C:\Users\Test\AppData\Local\HdrPilot\session-state.json", paths.SessionStateFile);
        Assert.Equal(@"C:\Users\Test\AppData\Local\HdrPilot\logs", paths.LogDirectory);
    }
}
