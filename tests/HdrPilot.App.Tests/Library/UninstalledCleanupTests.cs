using HdrPilot.App.Services;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Aufräumen nach „Einstellungen entfernen…“ (E90, G11.1) auf einem Attrappen-Dateisystem voller Dateien, die nie
/// verschwinden dürfen (Spiel, Launcher, Steam, andere Spiele, Zwischenspeicher): Gelöscht werden nur die eigene PNG, die
/// eigenen Bilder und, nur mit Häkchen, die exakt passenden Verknüpfungen samt ihrer eigenen .ico; der Eintrag in
/// icon-cache.json verschwindet. Dazu die Prüfung des Laufwerks vor der Rückfrage.
/// </summary>
public sealed class UninstalledCleanupTests
{
    private const string Key = "Steam:9990160";
    private const string OtherKey = "Steam:9990110";
    private const string Data = @"C:\Users\Test\AppData\Local\HDRpilot";
    private const string Icons = Data + @"\icons";
    private const string CustomArt = Data + @"\art\custom";
    private const string Desktop = @"C:\Users\Test\Desktop";
    private const string Programs = @"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs";
    private const string StartMenu = Programs + @"\HDRpilot";
    private const string HdrPilotExe = @"C:\Users\Test\AppData\Local\Programs\HDRpilot\HDRpilot.exe";
    private const string PublicDesktop = @"C:\Users\Public\Desktop";
    private const string QuickLaunch = @"C:\Users\Test\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch";
    private const string GameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Nachtprobe";

    private readonly RecordingFileSystem _fs = new();
    private readonly Dictionary<string, ShortcutInfo> _links = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _brokenLinks = new(StringComparer.OrdinalIgnoreCase);
    private readonly FakeIcons _icons = new();
    private readonly FakeArtworkService _artwork = new();

    private static string OwnPng => Path.Combine(Icons, CustomIcons.FileNameFor(Key));

    private static string OwnIco => Path.ChangeExtension(OwnPng, ".ico");

    private static string Stem(string key) => ArtworkFiles.Stem(key);

    /// <summary>Alles, was es auf der Platte gibt: eigene Dateien des Spiels und vieles, was bleiben muss.</summary>
    private static readonly string[] OwnFiles =
    [
        OwnPng,
        Path.Combine(CustomArt, ArtworkFiles.CustomFileName(Key, ArtworkKind.Hero)),
        Path.Combine(CustomArt, ArtworkFiles.CustomFileName(Key, ArtworkKind.Logo)),
        Path.Combine(CustomArt, ArtworkFiles.CustomFileName(Key, ArtworkKind.Cover)),
    ];

    private static readonly string[] OtherFiles =
    [
        OwnIco, // nur mit Häkchen und gelöschter Verknüpfung, die sie zeigt
        Path.Combine(Icons, CustomIcons.FileNameFor(OtherKey)),
        Path.ChangeExtension(Path.Combine(Icons, CustomIcons.FileNameFor(OtherKey)), ".ico"),
        Path.Combine(CustomArt, ArtworkFiles.CustomFileName(OtherKey, ArtworkKind.Hero)),
        Data + @"\art\gen\" + ArtworkFiles.GeneratedFileName(Key, ArtworkKind.Hero), // Zwischenspeicher: räumt der Bilddienst auf
        Data + @"\art\index.json",
        Data + @"\icon-cache.json",
        Data + @"\config.json",
        GameDir + @"\NPClient.exe",
        GameDir + @"\Nachtprobe.ico",
        @"C:\Program Files (x86)\Steam\steamapps\appmanifest_9990110.acf",
        @"C:\Program Files (x86)\Steam\appcache\librarycache\9990160\library_hero.jpg",
        @"C:\Program Files (x86)\Steam\steam\games\a1b2c3.ico",
        HdrPilotExe,
        Desktop + @"\Sternenwacht 2 (HDR).lnk",
        Desktop + @"\Steam.lnk",
    ];

    public UninstalledCleanupTests()
    {
        foreach (var file in OwnFiles.Concat(OtherFiles))
        {
            _fs.Inner.AddFile(file, "x");
        }

        _links[Desktop + @"\Sternenwacht 2 (HDR).lnk"] = Link("--run \"" + OtherKey + "\"", OtherIco);
        _links[Desktop + @"\Steam.lnk"] = new ShortcutInfo(@"C:\Program Files (x86)\Steam\steam.exe", string.Empty, string.Empty, string.Empty, 0, "Steam");
    }

    private static string OtherIco => Path.ChangeExtension(Path.Combine(Icons, CustomIcons.FileNameFor(OtherKey)), ".ico");

    private static ShortcutInfo Link(string arguments, string icon) => new(HdrPilotExe, arguments, Path.GetDirectoryName(HdrPilotExe)!, icon, 0, "Mit HDR starten");

    private static GameConfig Game() => new() { Source = GameSource.Steam, SourceId = "9990160", Name = "NACHTPROBE", InstallDir = GameDir, IsInstalled = false, HdrAuto = true };

    /// <summary>Die Konfiguration nach dem Entfernen: das andere Spiel mit eigenem Symbol und eigenem Titelbild.</summary>
    private static AppConfig Remaining(params GameConfig[] extra)
    {
        var other = new GameConfig
        {
            Source = GameSource.Steam,
            SourceId = "9990110",
            Name = "STERNENWACHT 2",
            InstallDir = @"C:\Spiele\Sternenwacht 2",
            IsInstalled = true,
            CustomIcon = CustomIcons.FileNameFor(OtherKey),
            Artwork = new GameArtworkChoice { Hero = ArtworkOverride.Custom(ArtworkFiles.CustomFileName(OtherKey, ArtworkKind.Hero)) },
        };
        return new AppConfig { Games = [other, .. extra] };
    }

    /// <summary>Verknüpfung auf der Attrappe anlegen (Datei und gelesener Inhalt).</summary>
    private void AddLink(string path, string arguments, string icon)
    {
        _fs.Inner.AddFile(path, "lnk");
        _links[path] = Link(arguments, icon);
    }

    private ShortcutService Shortcuts() => new(
        Desktop,
        Programs,
        () => HdrPilotExe,
        fileSystem: _fs,
        read: path => _brokenLinks.Contains(path) ? throw new System.Runtime.InteropServices.COMException("Verknüpfung beschädigt")
            : _links.TryGetValue(path, out var info) && _fs.FileExists(path) ? info : throw new FileNotFoundException(path),
        otherShortcutDirectories: [PublicDesktop, QuickLaunch]);

    private UninstalledGameCleanup Cleanup(ShortcutService? shortcuts = null) => new(_fs, Icons, CustomArt, _icons, _artwork, shortcuts ?? Shortcuts());

    [Fact]
    public void Uninstalled_cleanup_deletes_only_the_own_icon_and_images_and_forgets_the_cache_entry()
    {
        var result = Cleanup().DeleteData(Game(), [], Remaining());

        Assert.Equal(OwnFiles.Order(StringComparer.OrdinalIgnoreCase), _fs.Deleted.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(OwnFiles.Length, result.Deleted.Count);
        Assert.Empty(result.Failed);
        Assert.All(OtherFiles, f => Assert.True(_fs.FileExists(f), "gelöscht: " + f));
        Assert.Equal([Key], _icons.ForgottenGames);
        Assert.Equal([Key], _artwork.Forgotten);
        Assert.Empty(_fs.Written); // keine Datei neu geschrieben (icon-cache.json schreibt der Symboldienst)
    }

    [Fact]
    public void Uninstalled_cleanup_with_checkbox_deletes_the_exact_shortcuts_and_only_their_own_ico()
    {
        AddLink(Desktop + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        AddLink(StartMenu + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        AddLink(Desktop + @"\Nachtprobe mit Spielsymbol.lnk", "--run \"" + Key + "\"", GameDir + @"\Nachtprobe.ico"); // Symbol im Spielordner
        AddLink(Desktop + @"\Nachtprobe Test.lnk", "--run \"" + Key + "\" --config-dir \"D:\\Test\"", HdrPilotExe); // Testablage: nicht genau
        var shortcuts = Shortcuts();
        var found = shortcuts.FindFor(Key);
        Assert.Equal(3, found.Count);

        var result = Cleanup(shortcuts).DeleteData(Game(), found, Remaining());

        string[] expected = [.. OwnFiles, OwnIco, Desktop + @"\NACHTPROBE (HDR).lnk", StartMenu + @"\NACHTPROBE (HDR).lnk", Desktop + @"\Nachtprobe mit Spielsymbol.lnk"];
        Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), _fs.Deleted.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(3, result.DeletedShortcuts);
        Assert.True(_fs.FileExists(GameDir + @"\Nachtprobe.ico")); // Spieldatei bleibt, auch wenn eine Verknüpfung sie zeigte
        Assert.True(_fs.FileExists(Desktop + @"\Nachtprobe Test.lnk"));
        Assert.All(OtherFiles.Where(f => !string.Equals(f, OwnIco, StringComparison.OrdinalIgnoreCase)), f => Assert.True(_fs.FileExists(f), "gelöscht: " + f));
    }

    [Fact]
    public void Uninstalled_cleanup_keeps_the_ico_while_a_shortcut_showing_it_could_not_be_deleted()
    {
        AddLink(Desktop + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        AddLink(StartMenu + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        _fs.Inner.FailDeleting(StartMenu + @"\NACHTPROBE (HDR).lnk", new IOException("gesperrt"));
        var shortcuts = Shortcuts();

        var result = Cleanup(shortcuts).DeleteData(Game(), shortcuts.FindFor(Key), Remaining());

        Assert.Equal(1, result.DeletedShortcuts);
        Assert.Equal([StartMenu + @"\NACHTPROBE (HDR).lnk"], result.Failed);
        Assert.True(_fs.FileExists(OwnIco));
    }

    [Fact]
    public void Uninstalled_cleanup_without_checkbox_keeps_shortcuts_and_their_ico()
    {
        AddLink(Desktop + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);

        var result = Cleanup().DeleteData(Game(), [], Remaining());

        Assert.Equal(0, result.DeletedShortcuts);
        Assert.True(_fs.FileExists(Desktop + @"\NACHTPROBE (HDR).lnk"));
        Assert.True(_fs.FileExists(OwnIco));
    }

    [Fact]
    public void Uninstalled_cleanup_never_deletes_files_a_hand_edited_config_names()
    {
        // Eine von Hand geänderte config.json nennt Symbol und Bilder eines anderen Spiels: Die bleiben.
        foreach (var own in OwnFiles)
        {
            _fs.Inner.DeleteFile(own);
        }

        var game = Game();
        game.CustomIcon = CustomIcons.FileNameFor(OtherKey);
        game.Artwork = new GameArtworkChoice { Hero = ArtworkOverride.Custom(ArtworkFiles.CustomFileName(OtherKey, ArtworkKind.Hero)) };

        var result = Cleanup().DeleteData(game, [], Remaining());

        Assert.Empty(result.Deleted);
        Assert.Empty(_fs.Deleted);
        Assert.True(_fs.FileExists(Path.Combine(Icons, CustomIcons.FileNameFor(OtherKey))));
        Assert.True(_fs.FileExists(Path.Combine(CustomArt, ArtworkFiles.CustomFileName(OtherKey, ArtworkKind.Hero))));
    }

    [Fact]
    public void Uninstalled_cleanup_deletes_a_shortcut_only_if_it_still_matches_and_lies_in_its_folder()
    {
        AddLink(Desktop + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        AddLink(StartMenu + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        var shortcuts = Shortcuts();
        var found = shortcuts.FindFor(Key);

        // Zwischen Rückfrage und Löschen: die Startmenü-Verknüpfung zeigt jetzt auf ein anderes Spiel; dazu eine passende
        // Verknüpfung außerhalb von Desktop und Startmenü-Ordner, die jemand der Liste untergeschoben hat.
        _links[StartMenu + @"\NACHTPROBE (HDR).lnk"] = Link("--run \"" + OtherKey + "\"", OtherIco);
        AddLink(@"C:\Users\Test\Documents\Nachtprobe (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        var deleted = shortcuts.Delete(Key, [.. found, new GameShortcut(@"C:\Users\Test\Documents\Nachtprobe (HDR).lnk", OwnIco)]);

        Assert.Equal([Desktop + @"\NACHTPROBE (HDR).lnk"], deleted.Select(d => d.Path));
        Assert.True(_fs.FileExists(StartMenu + @"\NACHTPROBE (HDR).lnk"));
        Assert.True(_fs.FileExists(@"C:\Users\Test\Documents\Nachtprobe (HDR).lnk"));
    }

    /// <summary>Nicht installiertes Spiel mit diesem Installationsordner (und auf Wunsch gemerkten EXEs).</summary>
    private static GameConfig Uninstalled(string installDir, string id = "9990160", GameSource source = GameSource.Steam, params string[] detected) =>
        new() { Source = source, SourceId = id, Name = "Spiel " + id, InstallDir = installDir, IsInstalled = false, DetectedExecutables = [.. detected] };

    [Theory]
    [InlineData(@"Q:\SteamLibrary\steamapps\common\Spiel", "Q:")]
    [InlineData(@"q:/Spiele/Spiel", "Q:")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Spiel", null)]
    [InlineData("", null)]
    [InlineData(@"Spiele\relativ", null)]
    public async Task Uninstalled_missing_drive_is_named_only_for_a_disconnected_drive_letter(string installDir, string? expected)
    {
        _fs.Inner.AddDirectory(@"C:\");

        var trace = await Cleanup().CheckInstallAsync(Uninstalled(installDir));

        Assert.Equal(expected is null ? InstallTrace.None : InstallTrace.MissingDrive(expected), trace);
    }

    [Fact]
    public void Uninstalled_icon_service_forgets_the_icon_cache_entry_and_keeps_the_others()
    {
        var cacheFile = Data + @"\icon-cache.json";
        var cache = new IconChoiceCache(_fs, cacheFile);
        cache.Set(Key, new IconChoice("abc", GameDir + @"\NPClient.exe", IconOrigin.Executable, "1:2", 256, true, DateTimeOffset.UnixEpoch));
        cache.Set(OtherKey, new IconChoice("def", null, IconOrigin.Executable, null, 0, false, DateTimeOffset.UnixEpoch));
        cache.Flush();

        var service = new GameIconService(_fs, iconDirectory: Icons, choiceCacheFile: cacheFile);
        service.ForgetGame(Key);

        var reread = new IconChoiceCache(_fs, cacheFile);
        Assert.Null(reread.Get(Key));
        Assert.NotNull(reread.Get(OtherKey));
        Assert.Empty(_fs.Deleted); // nur der Eintrag, keine Datei
    }

    [Theory]
    [InlineData("Unterordner")]
    [InlineData("Gemeinsamer Desktop")]
    [InlineData("Taskleiste")]
    [InlineData("Testinstanz")]
    public void Uninstalled_cleanup_keeps_the_ico_while_a_shortcut_elsewhere_still_shows_it(string place)
    {
        // Eine Verknüpfung, die „Einstellungen entfernen…“ nicht löscht (nicht direkt auf dem Desktop oder im Startmenü-Ordner
        // „HDRpilot“, oder nicht genau die Argumente), zeigt dieselbe .ico: Sie bleibt, sonst stünde dort ein leeres Symbol.
        AddLink(Desktop + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        var elsewhere = place switch
        {
            "Gemeinsamer Desktop" => PublicDesktop + @"\NACHTPROBE (HDR).lnk",
            "Taskleiste" => QuickLaunch + @"\User Pinned\TaskBar\NACHTPROBE (HDR).lnk",
            "Testinstanz" => Desktop + @"\Nachtprobe Test.lnk",
            _ => Desktop + @"\Spiele\NACHTPROBE (HDR).lnk",
        };
        AddLink(elsewhere, "--run \"" + Key + "\"" + (place == "Testinstanz" ? " --config-dir \"D:\\Test\"" : string.Empty), OwnIco);
        var shortcuts = Shortcuts();
        var found = shortcuts.FindFor(Key);
        Assert.Equal([Desktop + @"\NACHTPROBE (HDR).lnk"], found.Select(f => f.Path));

        var result = Cleanup(shortcuts).DeleteData(Game(), found, Remaining());

        Assert.Equal(1, result.DeletedShortcuts);
        Assert.Empty(result.Failed);
        Assert.True(_fs.FileExists(elsewhere));
        Assert.True(_fs.FileExists(OwnIco));
        Assert.DoesNotContain(OwnIco, _fs.Deleted, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uninstalled_cleanup_keeps_own_files_that_a_remaining_entry_still_names()
    {
        // Von Hand geänderte config.json: Ein verbliebenes Spiel zeigt Symbol und Titelbild des entfernten als seine. Die
        // beiden Dateien bleiben; Logo und Cover nennt niemand mehr, sie fallen.
        var borrower = new GameConfig
        {
            Source = GameSource.Epic,
            SourceId = "leiht",
            Name = "Leiht sich Bilder",
            InstallDir = @"C:\Spiele\Leiht",
            IsInstalled = true,
            CustomIcon = CustomIcons.FileNameFor(Key),
            Artwork = new GameArtworkChoice { Hero = ArtworkOverride.Custom(ArtworkFiles.CustomFileName(Key, ArtworkKind.Hero)) },
        };

        var result = Cleanup().DeleteData(Game(), [], Remaining(borrower));

        Assert.True(_fs.FileExists(OwnPng));
        Assert.True(_fs.FileExists(OwnFiles[1]));
        Assert.Equal(new[] { OwnFiles[2], OwnFiles[3] }.Order(StringComparer.OrdinalIgnoreCase), result.Deleted.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(result.Failed);
        Assert.Equal([Key], _icons.ForgottenGames); // vergessen wird trotzdem: Der Eintrag ist weg
    }

    [Fact]
    public async Task Uninstalled_install_check_counts_programs_not_leftover_folders()
    {
        // Prüfung (E90): Ein deinstalliertes Spiel hinterlässt oft einen Ordner mit Spielständen, Einstellungen und
        // Protokollen (KLIPPE Demo: Klippe\Saved\Config, Logs, SaveGames, .sentry-native, keine EXE). Das ist keine Spur; eine EXE
        // im Ordner (auch tief wie bei Unreal) schon. Gesucht wird nur begrenzt tief.
        _fs.Inner.AddDirectory(@"C:\");
        _fs.Inner.AddFile(@"C:\Spiele\Noch da\Spiel.exe", "x");
        _fs.Inner.AddFile(@"C:\Spiele\Unreal\Klippe\Binaries\Win64\KlippeDemo-Win64-Test.exe", "x");
        _fs.Inner.AddFile(@"C:\Spiele\Reste\Engine\Saved\Config\Windows\Manifest.ini", "x")
            .AddFile(@"C:\Spiele\Reste\Klippe\Saved\Config\Windows\Engine.ini", "x")
            .AddFile(@"C:\Spiele\Reste\Klippe\Saved\Logs\Klippe.log", "x")
            .AddFile(@"C:\Spiele\Reste\Klippe\Saved\SaveGames\steam_autocloud.vdf", "x")
            .AddFile(@"C:\Spiele\Reste\Klippe\.sentry-native\settings.dat", "x");
        _fs.Inner.AddFile(@"C:\Spiele\Zu tief\a\b\c\d\e\Spiel.exe", "x"); // fünf Ebenen: wird nicht mehr gesucht
        _fs.Inner.AddDirectory(@"C:\Spiele\Leer");
        _fs.Inner.AddFile(@"\\nas\spiele\Netz\Spiel.exe", "x");

        var traces = await Cleanup().CheckInstallsAsync(
        [
            Uninstalled(@"C:\Spiele\Noch da"), Uninstalled(@"C:\Spiele\Unreal"), Uninstalled(@"C:\Spiele\Reste"), Uninstalled(@"C:\Spiele\Zu tief"),
            Uninstalled(@"C:\Spiele\Leer"), Uninstalled(@"C:\Spiele\Fehlt"), Uninstalled(@"\\nas\spiele\Netz"), Uninstalled(@"\\nas\spiele\Fehlt"),
            Uninstalled(@"Q:\Spiele\Weg"), Uninstalled(@"q:\Anderes"), Uninstalled(string.Empty), Uninstalled(@"Spiele\relativ"),
        ]);

        var program = new InstallTrace(InstallSign.Program);
        Assert.Equal(
            [
                program, program, InstallTrace.None, InstallTrace.None,
                InstallTrace.None, InstallTrace.None, program, InstallTrace.None,
                InstallTrace.MissingDrive("Q:"), InstallTrace.MissingDrive("Q:"), InstallTrace.None, InstallTrace.None,
            ],
            traces);
        Assert.False(InstallTrace.None.MaybeInstalled);
        Assert.True(program.MaybeInstalled);
    }

    [Fact]
    public async Task Uninstalled_install_check_counts_a_remembered_exe_that_still_exists()
    {
        // Restordner ohne EXE, aber eine gemerkte EXE (auch außerhalb des Ordners) ist noch da: vielleicht noch installiert.
        // Gemerkte EXEs, die fehlen oder auf einem nicht verbundenen Laufwerk liegen, und bloße Dateinamen zählen nicht.
        _fs.Inner.AddDirectory(@"C:\");
        _fs.Inner.AddFile(@"C:\Spiele\Reste\Klippe\Saved\Logs\Klippe.log", "x");
        _fs.Inner.AddFile(@"C:\Werkzeuge\Starter.exe", "x");
        var withExe = Uninstalled(@"C:\Spiele\Reste", detected: [@"C:\Spiele\Reste\KlippeDemo.exe", @"C:\Werkzeuge\Starter.exe"]);
        var gone = Uninstalled(@"C:\Spiele\Reste", detected: [@"C:\Spiele\Reste\KlippeDemo.exe", @"Z:\Weg\Spiel.exe"]);
        var extra = Uninstalled(@"C:\Spiele\Fehlt");
        extra.ExtraExecutables = ["Starter.exe", @"C:\Werkzeuge\Starter.exe"];
        var nameOnly = Uninstalled(@"C:\Spiele\Fehlt");
        nameOnly.ExtraExecutables = ["Starter.exe"];

        var traces = await Cleanup().CheckInstallsAsync([withExe, gone, extra, nameOnly]);

        Assert.Equal([new InstallTrace(InstallSign.Program), InstallTrace.None, new InstallTrace(InstallSign.Program), InstallTrace.None], traces);
    }

    [Fact]
    public async Task Uninstalled_install_check_counts_a_steam_manifest_in_the_library()
    {
        // Steam führt das Spiel noch (etwa mitten in einem Update): appmanifest_<ID>.acf in steamapps der Bibliothek. Nur für
        // Steam, nur mit Ziffern als ID und nur für Ordner unter steamapps\common.
        _fs.Inner.AddDirectory(@"C:\");
        _fs.Inner.AddFile(@"D:\Steam-Bibliothek\steamapps\appmanifest_9990140.acf", "x");
        _fs.Inner.AddFile(@"D:\Steam-Bibliothek\steamapps\common\KlippeDemo\Klippe\Saved\Logs\Klippe.log", "x");
        _fs.Inner.AddFile(@"D:\Steam-Bibliothek\steamapps\appmanifest_..\x.acf", "x");
        _fs.Inner.AddFile(@"D:\Spielordner\appmanifest_9990140.acf", "x");

        var traces = await Cleanup().CheckInstallsAsync(
        [
            Uninstalled(@"D:\Steam-Bibliothek\steamapps\common\KlippeDemo", "9990140"),
            Uninstalled(@"D:\Steam-Bibliothek\steamapps\common\Anderes", "9990160"),
            Uninstalled(@"D:\Steam-Bibliothek\steamapps\common\KlippeDemo", "9990140", GameSource.Epic),
            Uninstalled(@"D:\Steam-Bibliothek\steamapps\common\X", @"..\x"),
            Uninstalled(@"D:\Spielordner\KlippeDemo", "9990140"),
        ]);

        Assert.Equal([new InstallTrace(InstallSign.SteamManifest), InstallTrace.None, InstallTrace.None, InstallTrace.None, InstallTrace.None], traces);
    }

    [Fact]
    public async Task Uninstalled_install_check_without_an_answer_warns_instead_of_counting_as_gone()
    {
        // Prüfung (E90): Was nicht rechtzeitig antwortet (Laufwerk, Netzwerkfreigabe, Ordner) oder nicht lesbar ist, ist nicht
        // „keine Spur“, sondern ungeklärt: Ein getrenntes Netzlaufwerk bekommt die Warnung. Alle zusammen warten höchstens eine
        // Frist (nicht je Laufwerk und Ordner eine).
        var slow = new HangingFileSystem(_fs.Inner, @"M:\", @"\\nas\spiele\", @"C:\Spiele\Hängt");
        _fs.Inner.AddDirectory(@"C:\").AddDirectory(@"M:\").AddDirectory(@"\\nas\spiele").AddFile(@"C:\Spiele\Hängt\Saved\Logs\Spiel.log", "x");
        var denied = new DenyingFileSystem(slow, @"C:\Spiele\Gesperrt");
        _fs.Inner.AddFile(@"C:\Spiele\Gesperrt\Saved\Spiel.sav", "x");
        var cleanup = new UninstalledGameCleanup(denied, Icons, CustomArt, _icons, _artwork, Shortcuts());
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var traces = await cleanup.CheckInstallsAsync(
                [Uninstalled(@"M:\Spiele\A"), Uninstalled(@"\\nas\spiele\B"), Uninstalled(@"C:\Spiele\Hängt"), Uninstalled(@"\\offline\spiele\C"), Uninstalled(@"C:\Spiele\Gesperrt")]);
            watch.Stop();

            Assert.All(traces, t => Assert.Equal(new InstallTrace(InstallSign.Unchecked), t));
            Assert.True(traces[0].MaybeInstalled);
            Assert.InRange(watch.Elapsed, UninstalledGameCleanup.CheckTimeout * 0.9, UninstalledGameCleanup.CheckTimeout * 1.75);
        }
        finally
        {
            slow.Release();
        }
    }

    [Fact]
    public async Task Uninstalled_install_check_steps_share_one_deadline()
    {
        // Eine anlaufende Platte antwortet nach 1,5 s, ihr Ordner danach gar nicht: Die Prüfung endet mit der einen Frist
        // (2 s ab Beginn), nicht mit einer neuen Frist je Schritt (1,5 s + 2 s).
        var slow = new HangingFileSystem(_fs.Inner, @"S:\Spiele\A");
        slow.Slow[@"S:\"] = TimeSpan.FromSeconds(1.5);
        _fs.Inner.AddFile(@"S:\Spiele\A\Spiel.exe", "x");
        var cleanup = new UninstalledGameCleanup(slow, Icons, CustomArt, _icons, _artwork, Shortcuts());
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var trace = await cleanup.CheckInstallAsync(Uninstalled(@"S:\Spiele\A"));
            watch.Stop();

            Assert.Equal(new InstallTrace(InstallSign.Unchecked), trace);
            Assert.InRange(watch.Elapsed, UninstalledGameCleanup.CheckTimeout * 0.9, UninstalledGameCleanup.CheckTimeout * 1.4);
        }
        finally
        {
            slow.Release();
        }
    }

    [Fact]
    public async Task Uninstalled_install_check_limits_parallel_access_and_shares_one_deadline()
    {
        // Zehn Spiele in Ordnern, die nicht antworten (eine langsame Platte): Höchstens MaxParallelChecks Zugriffe hängen
        // zugleich (jeder hält einen Thread), und nach einer gemeinsamen Frist steht die Antwort fest, statt je Ordner zu warten.
        var folders = Enumerable.Range(1, 10).Select(i => @"C:\Spiele\Langsam " + i).ToArray();
        var slow = new HangingFileSystem(_fs.Inner, folders);
        _fs.Inner.AddDirectory(@"C:\");
        foreach (var folder in folders)
        {
            _fs.Inner.AddFile(folder + @"\Spiel.exe", "x");
        }

        var cleanup = new UninstalledGameCleanup(slow, Icons, CustomArt, _icons, _artwork, Shortcuts());
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var traces = await cleanup.CheckInstallsAsync([.. folders.Select(f => Uninstalled(f))]);
            watch.Stop();

            Assert.All(traces, t => Assert.Equal(InstallSign.Unchecked, t.Sign));
            Assert.InRange(slow.MaxWaiting, 1, UninstalledGameCleanup.MaxParallelChecks);
            Assert.InRange(watch.Elapsed, UninstalledGameCleanup.CheckTimeout * 0.9, UninstalledGameCleanup.CheckTimeout * 1.75);
        }
        finally
        {
            slow.Release();
        }
    }

    [Theory]
    [InlineData("nicht lesbarer Ordner")]
    [InlineData("verknüpfter Ordner")]
    [InlineData("nicht lesbare Verknüpfung")]
    public void Uninstalled_cleanup_keeps_the_ico_when_it_cannot_see_every_shortcut(string obstacle)
    {
        // E90 „nicht lesbar: bleibt“: Lässt sich nicht ganz klären, ob noch eine Verknüpfung die .ico zeigt (ein Unterordner des
        // Startmenüs ohne Leserecht, eine Junction auf dem gemeinsamen Desktop, der nicht gefolgt wird, eine beschädigte .lnk in
        // der Taskleiste), bleibt sie; sonst stünde dort vielleicht ein leeres Symbol.
        AddLink(Desktop + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        switch (obstacle)
        {
            case "nicht lesbarer Ordner":
                _fs.CompletelyFails[Programs] = new UnauthorizedAccessException("Zugriff verweigert: " + Programs + @"\Gesperrt");
                break;
            case "verknüpfter Ordner":
                _fs.CompletelyFails[PublicDesktop] = new IOException("Verknüpfter Ordner, nicht verfolgt: " + PublicDesktop + @"\Spiele");
                break;
            default:
                _fs.Inner.AddFile(QuickLaunch + @"\User Pinned\TaskBar\Kaputt.lnk", "lnk");
                _brokenLinks.Add(QuickLaunch + @"\User Pinned\TaskBar\Kaputt.lnk");
                break;
        }

        var shortcuts = Shortcuts();
        var result = Cleanup(shortcuts).DeleteData(Game(), shortcuts.FindFor(Key), Remaining());

        Assert.Equal(1, result.DeletedShortcuts);
        Assert.True(_fs.FileExists(OwnIco));
        Assert.DoesNotContain(OwnIco, _fs.Deleted, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uninstalled_cleanup_deletes_the_ico_when_another_shortcut_vanished_meanwhile()
    {
        // Gegenprobe: Eine Verknüpfung, die zwischen Auflisten und Lesen verschwand, zeigt nichts mehr; die .ico fällt.
        AddLink(Desktop + @"\NACHTPROBE (HDR).lnk", "--run \"" + Key + "\"", OwnIco);
        _fs.Inner.AddFile(Desktop + @"\Gerade geloescht.lnk", "lnk"); // gelistet, Lesen meldet „nicht gefunden“
        var shortcuts = Shortcuts();

        var result = Cleanup(shortcuts).DeleteData(Game(), shortcuts.FindFor(Key), Remaining());

        Assert.Equal(1, result.DeletedShortcuts);
        Assert.False(_fs.FileExists(OwnIco));
        Assert.Contains(OwnIco, _fs.Deleted, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Attrappen-Dateisystem, das Löschen und Schreiben mitschreibt.</summary>
    private sealed class RecordingFileSystem : IFileSystem
    {
        public InMemoryFileSystem Inner { get; } = new();

        /// <summary>Vollständiges Auflisten dieser Ordner scheitert (nicht lesbarer oder verknüpfter Ordner darunter).</summary>
        public Dictionary<string, Exception> CompletelyFails { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> EnumerateFilesCompletely(string directory, string searchPattern) =>
            CompletelyFails.TryGetValue(directory, out var failure) ? throw failure : ((IFileSystem)Inner).EnumerateFilesCompletely(directory, searchPattern);

        public List<string> Deleted { get; } = [];

        public List<string> Written { get; } = [];

        public bool FileExists(string path) => Inner.FileExists(path);

        public bool DirectoryExists(string path) => Inner.DirectoryExists(path);

        public string ReadAllText(string path) => Inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => Inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) => Inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) => Inner.EnumerateDirectories(directory, searchPattern, recursive);

        public void CreateDirectory(string path) => Inner.CreateDirectory(path);

        public void WriteAllTextAtomic(string path, string contents)
        {
            Written.Add(path);
            Inner.WriteAllTextAtomic(path, contents);
        }

        public void DeleteFile(string path)
        {
            Inner.DeleteFile(path);
            Deleted.Add(path);
        }

        public IReadOnlyList<string> GetFixedDriveRoots() => Inner.GetFixedDriveRoots();
    }
}
