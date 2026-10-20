using System.Text;
using System.Text.RegularExpressions;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Core.Games.Scanners.Quiver;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Scanners;

/// <summary>
/// Tests des Quiver-Scanners (E91) gegen nachgebaute Quiver-Ordner im Speicher mit erfundenen Pfaden. Die Beispieldateien
/// unter Fixtures\Quiver folgen den Dateien von Quiver 3.5.1 am Zielrechner (apps.json mit „Black“, settings.json,
/// current\sq.version, Velopack-Protokoll); die Pfade darin sind erfunden.
/// </summary>
public sealed class QuiverScannerTests
{
    private const string Root = @"D:\Spielordner\QuiverLauncher-win-Portable";
    private const string Apps = Root + @"\Apps";
    private const string BlackDir = Apps + @"\Black-BlackXBERecomp";
    private const string BlackKey = "Quiver:Black-BlackXBERecomp";
    private const string LocalAppData = @"C:\Users\Test\AppData\Local";
    private const string VelopackLog = LocalAppData + @"\velopack\velopack_QuiverLauncher.log";
    private const string IconUrl = "https://cdn2.steamgriddb.com/grid/df6f08b0faeb3094ca257cafd49dbcd9.png";

    /// <summary>Name der Datei, die Quiver 3.5.1 am Zielrechner für das Cover von „Black“ angelegt hat (gemessen, WebP, 480 × 480).</summary>
    private const string BlackCacheName = "8C8FC6677FB84678288E4615E4E470A5";

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13];

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeEnvironment _env = new();
    private readonly ListLogger<QuiverScanner> _log = new();
    private ReadOnlyRegistry? _lastRegistry;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Fehlender Launcher ----

    [Fact]
    public void Missing_launcher_returns_empty_without_errors()
    {
        var result = Scan();

        Assert.Equal(GameSource.Quiver, result.Source);
        Assert.Equal("Quiver Launcher", result.ScannerName);
        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Quiver_folder_without_library_files_is_no_hit()
    {
        // Frisch entpackt, nie gestartet: Kennzeichen da, aber weder apps.json noch settings.json.
        _fs.AddFixture(@"Quiver\sq.version", Root + @"\current\sq.version");

        var result = Scan(Learned(Root));

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    // ---- Ordner finden ----

    [Fact]
    public void Quiver_learned_folder_yields_black_started_directly_from_its_folder()
    {
        AddRoot();
        AddBlack();

        var result = Scan(Learned(Root));

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal(BlackKey, game.Key);
        Assert.Equal("Black-BlackXBERecomp", game.SourceId);
        Assert.Equal("Black", game.Name);
        Assert.Equal(BlackDir, game.InstallDir);
        Assert.Equal(BlackDir + @"\BLACK.exe", game.LaunchCommand);
        Assert.Equal(BlackDir, game.LaunchWorkingDirectory);
        Assert.Null(game.LaunchArguments);
        Assert.Equal([BlackDir + @"\BLACK.exe"], game.Executables);
        Assert.Null(game.InstallKind);
        Assert.True(_log.Contains(LogLevel.Information, "(gelernt, Aufbau Velopack)"));
    }

    [Fact]
    public void Quiver_folder_is_found_in_the_velopack_log()
    {
        AddRoot();
        AddBlack();
        _fs.AddFixture(@"Quiver\velopack_QuiverLauncher.log", VelopackLog);

        var result = Scan();

        Assert.Equal(BlackKey, Assert.Single(result.Games).Key);
        Assert.True(_log.Contains(LogLevel.Information, Root + " gefunden (Velopack-Protokoll"));
    }

    [Fact]
    public void Quiver_foreign_velopack_program_is_no_hit()
    {
        // Ein anderes Programm mit Velopack, sogar mit apps.json daneben, im Protokoll genannt: kein Quiver-Ordner.
        const string foreign = @"D:\Werkzeuge\AnderesProgramm";
        _fs.AddFixture(@"Quiver\sq.version.fremd", foreign + @"\current\sq.version");
        _fs.AddFixture(@"Quiver\apps.json", foreign + @"\apps.json");
        _fs.AddFile(foreign + @"\Apps\Black-BlackXBERecomp\BLACK.exe", "MZ");
        _fs.AddFile(VelopackLog, $"[update:1] [INFO] About to launch: '\"{foreign.Replace(@"\", @"\\")}\\\\current\\\\AnderesProgramm.exe\"'\n");

        var result = Scan(new ScanOptions { QuiverFolders = [foreign], SearchFolders = [@"D:\Werkzeuge"] });

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Quiver_folder_is_found_among_the_children_of_a_search_folder()
    {
        AddRoot();
        AddBlack();

        var result = Scan(new ScanOptions { SearchFolders = [@"D:\Spielordner"] });

        Assert.Equal(BlackKey, Assert.Single(result.Games).Key);
        Assert.True(_log.Contains(LogLevel.Information, "(Suchordner"));
    }

    [Fact]
    public void Quiver_folder_in_downloads_is_found_by_the_search()
    {
        const string root = @"C:\Users\Test\Downloads\QuiverLauncher-win-Portable";
        AddRoot(root);
        AddBlack(root + @"\Apps\Black-BlackXBERecomp");

        var result = Scan();

        Assert.Equal(root + @"\Apps\Black-BlackXBERecomp", Assert.Single(result.Games).InstallDir);
        Assert.True(_log.Contains(LogLevel.Information, "(Suche, Aufbau Velopack)"));
    }

    [Fact]
    public void Quiver_downloads_folder_comes_from_the_known_folder_in_the_registry()
    {
        // Downloads nach E:\ verschoben (festes Laufwerk; die Suche über die Laufwerke reicht zwei Ebenen, Quiver liegt in der
        // dritten): nur über den bekannten Ordner zu finden.
        _fs.FixedDriveRoots.Add(@"E:\");
        _registry.Set(RegistryHiveKind.CurrentUser, QuiverRootFinder.UserShellFoldersKeyPath, QuiverRootFinder.DownloadsValueName, @"E:\Ablage\Downloads");
        const string root = @"E:\Ablage\Downloads\Quiver";
        AddRoot(root);
        AddBlack(root + @"\Apps\Black-BlackXBERecomp");

        var result = Scan();

        Assert.Equal(BlackKey, Assert.Single(result.Games).Key);
        Assert.Contains((RegistryHiveKind.CurrentUser, QuiverRootFinder.UserShellFoldersKeyPath, RegistryViewKind.Default), _lastRegistry!.Opened);
    }

    [Fact]
    public void Quiver_folders_in_localappdata_and_from_an_uninstall_entry_are_found()
    {
        const string installed = LocalAppData + @"\QuiverLauncher";
        const string other = @"F:\Werkzeuge\Quiver";
        AddRoot(installed, Apps1("Spiel-Eins"));
        _fs.AddFile(installed + @"\Apps\Spiel-Eins\Eins.exe", "MZ");
        AddRoot(other, Apps1("Spiel-Zwei"));
        _fs.AddFile(other + @"\Apps\Spiel-Zwei\Zwei.exe", "MZ");
        _registry.Set(RegistryHiveKind.CurrentUser, QuiverRootFinder.UninstallKeyPath, "InstallLocation", other + @"\");

        var result = Scan();

        Assert.Equal(["Quiver:Spiel-Eins", "Quiver:Spiel-Zwei"], result.Games.Select(g => g.Key).Order(StringComparer.Ordinal));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Quiver_old_2_4_layout_is_read()
    {
        const string root = @"D:\Alt\Quiver";
        _fs.AddFile(root + @"\Quiver.exe", "MZ");
        _fs.AddFixture(@"Quiver\apps.json", root + @"\apps.json");
        AddBlack(root + @"\Apps\Black-BlackXBERecomp");

        var result = Scan(Learned(root));

        Assert.Equal(root + @"\Apps\Black-BlackXBERecomp\BLACK.exe", Assert.Single(result.Games).LaunchCommand);
        Assert.True(_log.Contains(LogLevel.Information, "Aufbau Legacy"));
    }

    [Fact]
    public void Quiver_search_without_hint_stops_after_its_limit()
    {
        // 40 × 40 Ordner unter C:\, der Quiver-Ordner ganz hinten in Ebene 2: jenseits der Grenze, nie gesucht.
        for (var i = 0; i < 40; i++)
        {
            for (var j = 0; j < 40; j++)
            {
                _fs.AddDirectory($@"C:\Ordner{i:00}\Unter{j:00}");
            }
        }

        AddRoot(@"C:\zz-hinten\Quiver");
        AddBlack(@"C:\zz-hinten\Quiver\Apps\Black-BlackXBERecomp");
        var counting = new CountingFileSystem(_fs);

        var result = Scan(fileSystem: counting);

        // Genau die Grenze, dazu vor der Suche %LOCALAPPDATA%\QuiverLauncher; jeder Ordner nur einmal.
        Assert.False(result.LauncherDetected);
        Assert.Equal(QuiverRootFinder.MaxSearchedFolders + 1, counting.ManifestChecks);
        Assert.Equal(counting.ManifestChecks, counting.ManifestChecksDistinct);

        // Gegenprobe: mit weniger Ordnern davor wird er gefunden.
        var small = new InMemoryFileSystem();
        small.AddDirectory(@"C:\Ordner00\Unter00");
        AddRoot(@"C:\zz-hinten\Quiver", fileSystem: small);
        AddBlack(@"C:\zz-hinten\Quiver\Apps\Black-BlackXBERecomp", small);
        Assert.Equal(BlackKey, Assert.Single(Scan(fileSystem: small).Games).Key);
    }

    [Fact]
    public void Quiver_velopack_log_paths_are_read_newest_first()
    {
        var text = File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Quiver", "velopack_QuiverLauncher.log"));

        var folders = QuiverRootFinder.FoldersFromLog(text);

        Assert.Equal(@"D:\Spielordner\QuiverLauncher-win-Portable\packages\.betaId", folders[0]);
        Assert.Contains(Root, folders);
        Assert.Contains(Root + @"\current", folders);
        Assert.Equal(folders.Count, folders.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.InRange(folders.Count, 1, QuiverRootFinder.MaxLogCandidates);
        Assert.DoesNotContain(folders, f => f.Contains(@"\\", StringComparison.Ordinal) || f.EndsWith('"') || f.EndsWith('\''));
    }

    [Fact]
    public void Quiver_velopack_log_is_read_from_the_end_only()
    {
        AddRoot();
        AddBlack();
        var old = $"[stub:1] [INFO] Starting Velopack Stub (at \"{Root.Replace(@"\", @"\\")}\\\\QuiverLauncher.exe\")\n";
        var filler = string.Concat(Enumerable.Repeat("[lib-csharp:2] [Debug] Retrieving latest release feed.\n", 6000));
        _fs.AddFile(VelopackLog, old + filler);

        Assert.True(Encoding.UTF8.GetByteCount(filler) > QuiverRootFinder.MaxLogBytes);
        Assert.False(Scan().LauncherDetected);

        _fs.AddFile(VelopackLog, filler + old);
        Assert.True(Scan().LauncherDetected);
    }

    [Theory]
    [InlineData("<package><metadata><id>QuiverLauncher</id></metadata></package>", true)]
    [InlineData("<package><metadata><id> quiverlauncher </id></metadata></package>", true)]
    [InlineData("<package><metadata><id>AnderesProgramm</id></metadata></package>", false)]
    [InlineData("<package><id>QuiverLauncher</id></package>", false)]
    [InlineData("<!DOCTYPE package [<!ENTITY x \"QuiverLauncher\">]><package><metadata><id>&x;</id></metadata></package>", false)]
    [InlineData("kein XML", false)]
    public void Quiver_manifest_is_recognised_only_with_its_own_id(string manifest, bool expected)
    {
        var bytes = Encoding.UTF8.GetBytes(manifest);

        Assert.Equal(expected, QuiverRoot.IsQuiverManifest(new SharedRead(bytes, bytes.Length)));
    }

    // ---- Spielordner: AppsPath, installPath ----

    [Fact]
    public void Quiver_apps_path_from_settings_is_used()
    {
        AddRoot();
        _fs.AddFile(Root + @"\settings.json", "{ \"AppsPath\": \"E:\\\\Quiver-Spiele\", \"Platform\": 0 }");
        AddBlack(@"E:\Quiver-Spiele\Black-BlackXBERecomp");
        AddBlack(BlackDir); // liegt auch am Standardort, gilt aber nicht

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal(@"E:\Quiver-Spiele\Black-BlackXBERecomp", game.InstallDir);
    }

    [Theory]
    [InlineData("Spiele", "\"Spiele\" ist kein gültiger absoluter Pfad")]
    [InlineData("\\\\Spiele", "\"\\Spiele\" ist kein gültiger absoluter Pfad")] // ohne Laufwerk
    [InlineData("E:\\\\Spiele|1", "\"E:\\Spiele|1\" ist kein gültiger absoluter Pfad")]
    public void Broken_apps_path_is_reported_and_the_apps_folder_used(string appsPath, string problem)
    {
        AddRoot();
        _fs.AddFile(Root + @"\settings.json", "{ \"AppsPath\": \"" + appsPath + "\" }");
        AddBlack();

        var result = Scan(Learned(Root));

        Assert.Equal(BlackDir, Assert.Single(result.Games).InstallDir);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Quiver-Einstellung AppsPath unbrauchbar, es gilt der Ordner Apps:", error.Message, StringComparison.Ordinal);
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Quiver_install_path_wins_over_the_apps_folder()
    {
        AddRoot(apps: Apps1("Black-BlackXBERecomp", installPath: @"G:\\Eigene Spiele\\Black"));
        AddBlack(@"G:\Eigene Spiele\Black");
        AddBlack(BlackDir);

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal(@"G:\Eigene Spiele\Black", game.InstallDir);
        Assert.Equal(@"G:\Eigene Spiele\Black\BLACK.exe", game.LaunchCommand);
    }

    [Theory]
    [InlineData(@"G:\\", "Laufwerkswurzel")]
    [InlineData(@"relativ\\Black", "kein absoluter Pfad")]
    public void Broken_install_path_is_reported_and_the_game_skipped(string installPath, string problem)
    {
        AddRoot(apps: Apps1("Black-BlackXBERecomp", installPath: installPath));
        AddBlack();

        var result = Scan(Learned(Root));

        Assert.Empty(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Quiver-Eintrag \"Spiel\": installPath unbrauchbar:", error.Message, StringComparison.Ordinal);
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
        Assert.Equal(Root + @"\apps.json", error.Path);
    }

    // ---- Wahl der EXE ----

    [Fact]
    public void Quiver_selected_executable_wins()
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\BLACK_Vulkan.exe", "MZ");
        _fs.AddFile(BlackDir + @"\selected_executable.txt", BlackDir + "\\BLACK_Vulkan.exe\r\n");

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal(BlackDir + @"\BLACK_Vulkan.exe", game.LaunchCommand);
        Assert.Equal(BlackDir, game.LaunchWorkingDirectory);
        Assert.Equal([BlackDir + @"\BLACK_Vulkan.exe"], game.Executables);
    }

    [Theory]
    [InlineData(@"D:\Anderswo\Spiel.exe")]
    [InlineData(@"..\..\..\Anderswo\Spiel.exe")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(BlackDir + @"\fehlt.exe")]
    public void Quiver_selected_executable_outside_the_game_folder_is_ignored(string selected)
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(@"D:\Anderswo\Spiel.exe", "MZ");
        _fs.AddFile(@"C:\Windows\notepad.exe", "MZ");
        _fs.AddFile(BlackDir + @"\selected_executable.txt", selected);

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal(BlackDir + @"\BLACK.exe", game.LaunchCommand);
    }

    [Fact]
    public void Quiver_selected_batch_file_gives_no_launch_command()
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\start.bat", "@echo off");
        _fs.AddFile(BlackDir + @"\selected_executable.txt", BlackDir + @"\start.bat");

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Null(game.LaunchCommand);
        Assert.Equal([BlackDir + @"\BLACK.exe"], game.Executables);
        Assert.True(_log.Contains(LogLevel.Information, "in Quiver gewählt ist keine EXE"));
    }

    [Fact]
    public void Quiver_several_exes_without_choice_give_no_launch_command()
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\BLACK_Vulkan.exe", "MZ");

        var result = Scan(Learned(Root));

        var game = Assert.Single(result.Games);
        Assert.Empty(result.Errors);
        Assert.Null(game.LaunchCommand);
        Assert.Null(game.LaunchWorkingDirectory);
        Assert.Equal([BlackDir + @"\BLACK.exe", BlackDir + @"\BLACK_Vulkan.exe"], game.Executables);
        Assert.Equal(BlackDir, game.InstallDir);
        Assert.True(_log.Contains(LogLevel.Information, "ohne Start-Befehl (2 Kandidaten und keine Wahl in Quiver)"));
    }

    [Fact]
    public void Quiver_helper_programs_are_no_candidates()
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\unins000.exe", "MZ");
        _fs.AddFile(BlackDir + @"\crashpad_handler.exe", "MZ");
        _fs.AddFile(BlackDir + @"\vc_redist.x64.exe", "MZ");

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal(BlackDir + @"\BLACK.exe", game.LaunchCommand);
    }

    [Theory]
    [InlineData(@"bin\Spiel.exe", true)]
    [InlineData(@"bin\x64\Release\Spiel.exe", true)]
    [InlineData(@"bin\x64\Release\tief\Spiel.exe", false)]
    public void Quiver_single_nested_exe_is_found_up_to_three_levels(string relative, bool found)
    {
        AddRoot();
        _fs.AddFile(BlackDir + @"\version.txt", "V1");
        _fs.AddFile(BlackDir + @"\" + relative, "MZ");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games); // installiert wie in Quiver, das ohne Grenze sucht
        if (found)
        {
            Assert.Equal(BlackDir + @"\" + relative, game.LaunchCommand);
            Assert.Equal(Path.GetDirectoryName(BlackDir + @"\" + relative), game.LaunchWorkingDirectory);
        }
        else
        {
            // Zu tief für die Wahl der Start-EXE: kein Start-Befehl, aber das Spiel ist da (und bleibt „installiert“).
            Assert.Null(game.LaunchCommand);
            Assert.Equal([BlackDir + @"\" + relative], game.Executables);
            Assert.True(_log.Contains(LogLevel.Information, "EXE erst tiefer als 3 Ebenen"));
        }
    }

    [Fact]
    public void Quiver_installed_check_reaches_deep_but_stays_bounded()
    {
        // Eine EXE in Ebene 16 zählt für „installiert“, in Ebene 17 nicht mehr (Quiver sucht ohne Grenze, HDRpilot begrenzt).
        AddRoot(apps: """{ "apps": [ { "folderName": "Tief", "name": "Tief" }, { "folderName": "ZuTief", "name": "ZuTief" } ] }""");
        var sixteen = string.Concat(Enumerable.Range(1, QuiverExecutables.MaxInstallDepth).Select(i => $@"\e{i}"));
        _fs.AddFile(Apps + @"\Tief" + sixteen + @"\Spiel.exe", "MZ");
        _fs.AddFile(Apps + @"\ZuTief" + sixteen + @"\e17\Spiel.exe", "MZ");

        var result = Scan(Learned(Root));

        Assert.Equal(["Quiver:Tief"], result.Games.Select(g => g.Key));
        Assert.Null(result.Games[0].LaunchCommand);
    }

    [Fact]
    public void Quiver_search_that_hits_its_folder_limit_never_says_not_installed()
    {
        // Mehr Ordner als die Grenze und keine EXE darin: lieber „installiert ohne Start-Befehl“ als „nicht installiert“
        // (das gäbe „Einstellungen entfernen…“ frei, E90).
        AddRoot();
        _fs.AddDirectory(BlackDir + @"\daten");

        // Spielordner, „daten“ und die Unterordner: eins über der Grenze.
        var result = Scan(Learned(Root), new WideFolder(_fs, BlackDir + @"\daten", QuiverExecutables.MaxFolders - 1));

        var game = Assert.Single(result.Games);
        Assert.Null(game.LaunchCommand);
        Assert.Empty(game.Executables);
        Assert.True(_log.Contains(LogLevel.Information, $"Suche nach {QuiverExecutables.MaxFolders} Ordnern beendet"));

        // Gegenprobe: genau an der Grenze ist die Suche vollständig, also nicht installiert.
        Assert.Empty(Scan(Learned(Root), new WideFolder(_fs, BlackDir + @"\daten", QuiverExecutables.MaxFolders - 2)).Games);
    }

    [Fact]
    public void Quiver_linked_folders_in_the_game_folder_are_not_followed()
    {
        // Eine Junction im Spielordner (etwa auf ein anderes Laufwerk) wird nie betreten; Ordner von Wine/Proton auch nicht.
        AddRoot();
        _fs.AddFile(BlackDir + @"\verknuepft\Fremd.exe", "MZ");
        _fs.AddFile(BlackDir + @"\bin\Game.exe", "MZ");
        _fs.AddFile(BlackDir + @"\pfx\dosdevices\c\Tool.exe", "MZ");
        _fs.AddFile(BlackDir + @"\pfx\drive_c\windows\Setup.exe", "MZ");
        var counting = new ListingCounter(_fs);
        counting.Links.Add(BlackDir + @"\verknuepft");

        var game = Assert.Single(Scan(Learned(Root), counting).Games);

        Assert.Equal(BlackDir + @"\bin\Game.exe", game.LaunchCommand);
        Assert.DoesNotContain(counting.Listed, d => d.Contains("verknuepft", StringComparison.OrdinalIgnoreCase) || d.Contains("dosdevices", StringComparison.OrdinalIgnoreCase) || d.Contains("drive_c", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Quiver_exe_in_a_wine_prefix_does_not_count()
    {
        // Oben im Spielordner liegt keine EXE, die einzige Spiel-EXE eine Ebene tiefer; daneben Programme in den Ordnern von
        // Wine/Proton (legt Quiver unter Linux an). Zählten sie mit, gäbe es mehrere Kandidaten und keinen Start-Befehl.
        AddRoot();
        _fs.AddFile(BlackDir + @"\version.txt", "V1");
        _fs.AddFile(BlackDir + @"\bin\Spiel.exe", "MZ");
        _fs.AddFile(BlackDir + @"\.wine-prefix\Setup.exe", "MZ");
        _fs.AddFile(BlackDir + @"\dosdevices\Tool.exe", "MZ");
        _fs.AddFile(BlackDir + @"\.steam-compat-data\pfx\drive_c\Start.exe", "MZ");

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal(BlackDir + @"\bin\Spiel.exe", game.LaunchCommand);
        Assert.Equal([BlackDir + @"\bin\Spiel.exe"], game.Executables);
    }

    // ---- Schlüssel und Name ----

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\\..\\Windows")]
    [InlineData("Spiel..")]
    [InlineData("Spiel..Teil")] // „..“ auch als Teil des Namens (E91), nicht nur als ganzer Name
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData(@"a\\b")]
    [InlineData("C:Spiel")]
    [InlineData("Spiel|Name")]
    [InlineData("Spiel ")]
    public void Quiver_unsafe_folder_name_is_rejected(string folderName)
    {
        AddRoot(apps: Apps1(folderName));
        _fs.AddFile(@"D:\Spielordner\Windows\notepad.exe", "MZ");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Quiver-Eintrag \"Spiel\" abgelehnt: folderName", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Quiver_name_is_custom_name_then_name_then_project_then_folder()
    {
        AddRoot(apps: """
            { "apps": [
              { "folderName": "Eins", "name": "Name Eins", "project": "Projekt Eins", "customDisplayName": "  Eigener Name  " },
              { "folderName": "Zwei", "name": "Name Zwei", "project": "Projekt Zwei", "customDisplayName": "" },
              { "folderName": "Drei", "project": "Projekt Drei" },
              { "folderName": "Vier" }
            ] }
            """);
        foreach (var name in new[] { "Eins", "Zwei", "Drei", "Vier" })
        {
            _fs.AddFile(Apps + $@"\{name}\{name}.exe", "MZ");
        }

        var names = Scan(Learned(Root)).Games.ToDictionary(g => g.SourceId, g => g.Name);

        Assert.Equal("Eigener Name", names["Eins"]);
        Assert.Equal("Name Zwei", names["Zwei"]);
        Assert.Equal("Projekt Drei", names["Drei"]);
        Assert.Equal("Vier", names["Vier"]);
    }

    [Fact]
    public void Quiver_duplicate_folder_name_across_folders_keeps_the_first()
    {
        const string second = @"D:\Kopie\QuiverLauncher";
        AddRoot();
        AddBlack();
        AddRoot(second);
        AddBlack(second + @"\Apps\Black-BlackXBERecomp");
        _fs.AddFile(VelopackLog, $"[update:1] [INFO] CWD: \"{second.Replace(@"\", @"\\")}\"\n");

        var result = Scan(Learned(Root));

        Assert.Equal(BlackDir, Assert.Single(result.Games).InstallDir);
        Assert.Empty(result.Errors);
        Assert.True(_log.Contains(LogLevel.Information, "kommt auch in " + second + " vor; es gilt der Eintrag aus " + Root));
    }

    [Fact]
    public void Quiver_duplicate_folder_name_in_apps_json_keeps_the_first()
    {
        AddRoot(apps: """
            { "apps": [
              { "folderName": "Black-BlackXBERecomp", "name": "Erster" },
              { "folderName": "black-blackxberecomp", "name": "Zweiter" }
            ] }
            """);
        AddBlack();

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal("Erster", game.Name);
    }

    // ---- Zuletzt gespielt ----

    [Fact]
    public void Quiver_last_played_is_the_later_of_exe_folder_and_game_folder()
    {
        AddRoot();
        _fs.AddFile(BlackDir + @"\bin\Spiel.exe", "MZ");
        _fs.AddFile(BlackDir + @"\LastPlayed.txt", "2026-10-16 21:47:35");
        _fs.AddFile(BlackDir + @"\bin\LastPlayed.txt", "2026-10-19 08:15:00\r\n");

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal(new DateTimeOffset(new DateTime(2026, 10, 19, 8, 15, 0, DateTimeKind.Local)), game.LastPlayed);
    }

    [Fact]
    public void Quiver_unreadable_last_played_is_ignored()
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\LastPlayed.txt", "gestern");

        Assert.Null(Assert.Single(Scan(Learned(Root)).Games).LastPlayed);
    }

    // ---- Cover aus dem Zwischenspeicher ----

    [Fact]
    public void Quiver_cover_cache_name_matches_quiver()
    {
        // Gemessen am Zielrechner: Cache\Images\8C8FC6677FB84678288E4615E4E470A5 ist die 480 px breite Kopie (WebP).
        var card = QuiverArtwork.ResizedUrl(IconUrl, 480, 70);

        Assert.Equal("https://quiverlauncher.com/cdn-cgi/image/width=480,quality=70,format=auto,fit=scale-down/" + IconUrl, card);
        Assert.Equal(BlackCacheName, QuiverArtwork.CacheName(card));
        Assert.Equal(32, QuiverArtwork.CacheName(IconUrl).Length);
        Assert.Equal("https://example.org/cover.png", QuiverArtwork.ResizedUrl("https://example.org/cover.png", 480, 70));
        Assert.Equal("Black-BlackXBERecomp_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(IconUrl)))[..16].ToLowerInvariant() + ".png",
            QuiverArtwork.LegacyFileName("Black-BlackXBERecomp", IconUrl));
    }

    [Theory]
    [InlineData("https://cdn2.steamgriddb.com/grid/df6f08b0faeb3094ca257cafd49dbcd9.png", true)]
    [InlineData("https://cdn2.steamgriddb.com/hero/0A1B2C3D.jpg", true)]
    [InlineData("HTTPS://CDN2.STEAMGRIDDB.COM/GRID/abc.webp", true)]
    [InlineData("https://cdn2.steamgriddb.com/logo/abc.png", false)]
    [InlineData("https://cdn2.steamgriddb.com/grid/xyz.png", false)] // keine Hex-Ziffern
    [InlineData("https://cdn2.steamgriddb.com/grid/abc.png?x=1", false)]
    [InlineData("https://cdn2.steamgriddb.com/grid/sub/abc.png", false)]
    [InlineData("https://cdn2.steamgriddb.com/grid/abc.", false)]
    [InlineData("https://cdn2.steamgriddb.com/grid/.png", false)]
    [InlineData("http://cdn2.steamgriddb.com/grid/abc.png", false)]
    [InlineData("https://cdn2.steamgriddb.com.example.org/grid/abc.png", false)]
    public void Quiver_resized_copies_exist_only_for_steamgriddb_grids_and_heroes(string url, bool resized)
    {
        Assert.Equal(resized, QuiverArtwork.HasResizedCopies(url));
        Assert.Equal(resized, QuiverArtwork.ResizedUrl(url, 480, 70) != url);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 13, 10 }, true)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, true)]
    [InlineData(new byte[] { (byte)'B', (byte)'M', 0, 0 }, true)]
    [InlineData(new byte[] { 0, 0, 1, 0, 1, 0 }, true)]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, true)]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'A', (byte)'V', (byte)'E' }, false)] // Ton, kein Bild
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B' }, false)] // abgeschnitten
    [InlineData(new byte[] { 0, 0, 2, 0 }, false)] // Mauszeiger, kein Symbol
    [InlineData(new byte[] { 0x89, 0x50 }, false)]
    [InlineData(new byte[] { (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l', (byte)'>' }, false)]
    public void Quiver_image_header_is_recognised_from_the_first_bytes(byte[] head, bool image)
    {
        Assert.Equal(image, QuiverArtwork.HasImageHeader(head));
        Assert.Equal(12, QuiverArtwork.HeaderBytes);
    }

    [Fact]
    public void Quiver_cover_candidates_are_custom_then_shared_cache_then_old_cache()
    {
        AddRoot();
        AddBlack();
        var custom = Root + @"\Cache\CustomIcons\Black-BlackXBERecomp_custom.jpg";
        var shared = Root + @"\Cache\Images\" + BlackCacheName;
        var old = Root + @"\Cache\Icons\" + QuiverArtwork.LegacyFileName("Black-BlackXBERecomp", IconUrl);
        _fs.AddFile(custom, [0xFF, 0xD8, 0xFF, 0xE0, 0, 16]);
        _fs.AddFixture(@"Quiver\cover-1x1.webp", shared);
        _fs.AddFile(old, Png);

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal([custom, shared, old], game.CoverCandidates);
    }

    [Fact]
    public void Quiver_cover_without_image_header_is_skipped()
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(Root + @"\Cache\Images\" + BlackCacheName, "<html>Fehler 404</html>");
        _fs.AddFile(Root + @"\Cache\Images\" + QuiverArtwork.CacheName(QuiverArtwork.ResizedUrl(IconUrl, 960, 70)), []);
        _fs.AddFile(Root + @"\Cache\Images\" + QuiverArtwork.CacheName(IconUrl), Png);

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal([Root + @"\Cache\Images\" + QuiverArtwork.CacheName(IconUrl)], game.CoverCandidates);
    }

    [Fact]
    public void Quiver_cover_of_another_host_is_cached_under_its_own_address()
    {
        AddRoot(apps: """{ "apps": [ { "folderName": "Black-BlackXBERecomp", "name": "Black", "appIconUrl": "https://example.org/bilder/black.jpg" } ] }""");
        AddBlack();
        var cached = Root + @"\Cache\Images\" + QuiverArtwork.CacheName("https://example.org/bilder/black.jpg");
        _fs.AddFile(cached, Png);

        Assert.Equal([cached], Assert.Single(Scan(Learned(Root)).Games).CoverCandidates);
    }

    [Fact]
    public void Quiver_black_like_the_target_system()
    {
        // Abbild des Zielrechners mit erfundenem Ordner: Black, Cover im gemeinsamen Zwischenspeicher, zuletzt gespielt.
        AddRoot();
        AddBlack();
        _fs.AddFixture(@"Quiver\velopack_QuiverLauncher.log", VelopackLog);
        _fs.AddFixture(@"Quiver\cover-1x1.webp", Root + @"\Cache\Images\" + BlackCacheName);
        _fs.AddFile(Root + @"\Cache\Images\3E7B1C9A5F2D4B6E8A0C1D3F5B7E9A2C", Png); // Bild eines anderen Katalog-Eintrags (erfundener Name)

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal((BlackKey, "Black", BlackDir, BlackDir + @"\BLACK.exe"), (game.Key, game.Name, game.InstallDir, game.LaunchCommand));
        Assert.Equal([Root + @"\Cache\Images\" + BlackCacheName], game.CoverCandidates);
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 10, 16, 21, 47, 35, DateTimeKind.Local)), game.LastPlayed);
        Assert.Empty(game.IconCandidates);
    }

    // ---- Windows Installer ----

    [Fact]
    public void Quiver_windows_installer_game_starts_its_linked_exe_from_its_own_folder()
    {
        const string program = @"E:\Programme\Beispiel-Launcher";
        AddRoot(apps: Apps1("Beispiel-Launcher"));
        _fs.AddFixture(@"Quiver\windows-installer.json", Apps + @"\Beispiel-Launcher\windows-installer.json");
        _fs.AddFile(Apps + @"\Beispiel-Launcher\installer-20261018.log", "Protokoll");
        _fs.AddFile(Apps + @"\Beispiel-Launcher\LastPlayed.txt", "2026-10-17 20:00:00");
        _fs.AddFile(program + @"\Beispiel-Launcher.exe", "MZ");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal(InstallKinds.WindowsInstaller, game.InstallKind);
        Assert.Equal(program, game.InstallDir);
        Assert.Equal(program + @"\Beispiel-Launcher.exe", game.LaunchCommand);
        Assert.Equal(program, game.LaunchWorkingDirectory);
        Assert.Equal([program + @"\Beispiel-Launcher.exe"], game.Executables);
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 10, 17, 20, 0, 0, DateTimeKind.Local)), game.LastPlayed);
    }

    [Theory]
    [InlineData("""{"Phase":"NeedsExecutable","ReleaseTag":"v2.1.0","ExecutablePath":null}""")]
    [InlineData("""{"Phase":"Linked","ReleaseTag":"v2.1.0","ExecutablePath":"E:\\Programme\\Fehlt\\Fehlt.exe"}""")]
    [InlineData("""{"Phase":"Linked","ReleaseTag":"v2.1.0","ExecutablePath":"Beispiel-Launcher.exe"}""")]
    public void Incomplete_windows_installer_link_is_skipped_without_error(string receipt)
    {
        AddRoot(apps: Apps1("Beispiel-Launcher"));
        _fs.AddFile(Apps + @"\Beispiel-Launcher\windows-installer.json", receipt);
        _fs.AddFile(Apps + @"\Beispiel-Launcher\Beispiel-Launcher.exe", "MZ");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Broken_windows_installer_link_is_reported()
    {
        AddRoot(apps: Apps1("Beispiel-Launcher"));
        _fs.AddFile(Apps + @"\Beispiel-Launcher\windows-installer.json", """{"Phase":"Irgendwas","ExecutablePath":"E:\\x.exe"}""");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Quiver-Eintrag \"Spiel\": Verknüpfung des Windows Installers defekt: Unbekannte Phase \"Irgendwas\".", error.Message);
        Assert.Equal(Apps + @"\Beispiel-Launcher\windows-installer.json", error.Path);
    }

    // ---- Unvollständige Installationen ----

    [Fact]
    public void Incomplete_install_marker_skips_the_game_without_error()
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\install-incomplete.txt", "");

        var result = Scan(Learned(Root));

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
        Assert.True(_log.Contains(LogLevel.Information, "Installation unvollständig"));
    }

    [Fact]
    public void Incomplete_missing_game_folder_is_skipped_without_error()
    {
        AddRoot();

        var result = Scan(Learned(Root));

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_game_folder_without_programs_is_skipped_without_error()
    {
        AddRoot();
        _fs.AddFile(BlackDir + @"\version.txt", "V1");
        _fs.AddFile(BlackDir + @"\d3dcompiler_47.dll", "MZ");
        _fs.AddFile(BlackDir + @"\LastPlayed.txt", "2026-10-16 21:47:35");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_library_without_apps_json_detects_the_launcher_only()
    {
        _fs.AddFixture(@"Quiver\sq.version", Root + @"\current\sq.version");
        _fs.AddFixture(@"Quiver\settings.json", Root + @"\settings.json");

        var result = Scan(Learned(Root));

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    // ---- Defekte Dateien ----

    [Fact]
    public void Broken_apps_json_is_reported()
    {
        AddRoot(apps: "{ \"apps\": [ { \"folderName\": \"Black-BlackXBERecomp\", ");
        AddBlack();

        var result = Scan(Learned(Root));

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Quiver-Bibliothek defekt: Kein gültiges JSON", error.Message, StringComparison.Ordinal);
        Assert.Equal(Root + @"\apps.json", error.Path);
    }

    [Theory]
    [InlineData("{ \"games\": [] }")]
    [InlineData("{ \"apps\": {} }")]
    [InlineData("[ { \"folderName\": \"Black-BlackXBERecomp\" } ]")]
    public void Broken_apps_json_without_app_list_is_reported(string apps)
    {
        AddRoot(apps: apps);
        AddBlack();

        var result = Scan(Learned(Root));

        Assert.Empty(result.Games);
        Assert.StartsWith("Quiver-Bibliothek defekt:", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_entries_are_reported_and_the_others_delivered()
    {
        AddRoot(apps: """
            { "apps": [
              42,
              { "name": "Ohne Ordner" },
              { "folderName": "Kaputt", "name": "Kaputt\uD800" },
              { "folderName": "Black-BlackXBERecomp", "name": "Black" }
            ] }
            """);
        AddBlack();

        var result = Scan(Learned(Root));

        Assert.Equal(BlackKey, Assert.Single(result.Games).Key);
        Assert.Equal(
            [
                "Quiver-Eintrag 1 unbrauchbar: Eintrag ist kein Objekt.",
                "Quiver-Eintrag 3 unbrauchbar: Feld \"name\" enthält eine ungültige Escape-Folge (einzelnes UTF-16-Surrogat).",
                "Quiver-Eintrag \"Ohne Ordner\" abgelehnt: folderName fehlt.",
            ],
            result.Errors.Select(e => e.Message));
    }

    [Fact]
    public void Broken_settings_json_is_reported_and_the_apps_folder_used()
    {
        AddRoot();
        _fs.AddFile(Root + @"\settings.json", Encoding.Latin1.GetBytes("{ \"AppsPath\": \"E:\\\\Spiele Ä\" }"));
        AddBlack();

        var result = Scan(Learned(Root));

        Assert.Equal(BlackDir, Assert.Single(result.Games).InstallDir);
        Assert.StartsWith("Quiver-Einstellungen defekt, es gilt der Ordner Apps: Ungültige Zeichenkodierung", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_unreadable_apps_json_is_reported()
    {
        AddRoot();
        AddBlack();
        _fs.FailReading(Root + @"\apps.json", new UnauthorizedAccessException("Zugriff verweigert"));

        var result = Scan(Learned(Root));

        Assert.Empty(result.Games);
        Assert.Equal("Quiver-Bibliothek nicht lesbar: UnauthorizedAccessException: Zugriff verweigert", Assert.Single(result.Errors).Message);
    }

    // ---- Rahmen: nur lesen, kein Netz ----

    [Fact]
    public void Quiver_scanner_reads_only_shared_and_never_writes()
    {
        // Alles, was der Scanner liest, auf einmal: Protokoll, Kennzeichen, Einstellungen, Bibliothek, Wahl, Verknüpfung,
        // zuletzt gespielt, Cover. Gelesen werden darf nur geteilt (Quiver und Velopack schreiben gleichzeitig), geschrieben nie.
        AddRoot(apps: """
            { "apps": [
              { "folderName": "Black-BlackXBERecomp", "name": "Black", "appIconUrl": "https://cdn2.steamgriddb.com/grid/df6f08b0faeb3094ca257cafd49dbcd9.png" },
              { "folderName": "Beispiel-Launcher", "name": "Beispiel" }
            ] }
            """);
        AddBlack();
        _fs.AddFile(BlackDir + @"\selected_executable.txt", BlackDir + @"\BLACK.exe");
        _fs.AddFixture(@"Quiver\velopack_QuiverLauncher.log", VelopackLog);
        _fs.AddFixture(@"Quiver\cover-1x1.webp", Root + @"\Cache\Images\" + BlackCacheName);
        _fs.AddFixture(@"Quiver\windows-installer.json", Apps + @"\Beispiel-Launcher\windows-installer.json");
        _fs.AddFile(@"E:\Programme\Beispiel-Launcher\Beispiel-Launcher.exe", "MZ");
        var shared = new SharedReadsOnly(_fs);

        var result = Scan(fileSystem: shared);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Games.Count);
        Assert.True(shared.SharedReads > 5);
        Assert.All(_lastRegistry!.Opened, o => Assert.Equal(RegistryHiveKind.CurrentUser, o.Hive));
    }

    [Fact]
    public void Quiver_scanner_sources_use_no_network()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "HdrPilot.Core", "Games", "Scanners", "Quiver"), "*.cs")
            .Append(Path.Combine(RepositoryRoot(), "src", "HdrPilot.Core", "Games", "Scanners", "QuiverScanner.cs"))
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

        Assert.True(sources.Count >= 6, "Quelltexte des Quiver-Scanners gefunden: " + string.Join(", ", sources.Keys));
        Assert.Empty(NetworkUses(sources));

        // Gegenprobe: die Regel findet einen Netzwerkzugriff.
        Assert.NotEmpty(NetworkUses(new Dictionary<string, string> { ["Beispiel.cs"] = "using var client = new System.Net.Http.HttpClient();" }));
        Assert.NotEmpty(NetworkUses(new Dictionary<string, string> { ["Beispiel.cs"] = "var c = new WebClient();" }));
    }

    [Fact]
    public void Quiver_cancellation_is_honored()
    {
        AddRoot();
        AddBlack();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new QuiverScanner(_fs, _registry, _env).Scan(Learned(Root), cts.Token));
    }

    [Fact]
    public void Quiver_scanner_is_found_by_catalog()
    {
        Assert.Contains(typeof(QuiverScanner), ScannerCatalog.ScannerTypes);
        var scanner = Assert.Single(ScannerCatalog.CreateAll(_fs, _registry, _env), s => s.Source == GameSource.Quiver);
        Assert.Equal("Quiver Launcher", scanner.Name);
        Assert.Equal(GameSource.Quiver, ScannerCatalog.CreateAll(_fs, _registry, _env).Last().Source); // neue Quelle am Ende
    }

    // ---- Prüfung 2026-10-20: Grenzen der Suche, Startskripte, Kalender, Doppelte, Protokoll ----

    [Fact]
    public void Quiver_many_asset_files_do_not_hide_the_game_exe()
    {
        // Mehr Dateien als früher je Spiel aufgelistet wurden (5000), alphabetisch vor der EXE: Das Spiel fehlte ganz.
        AddRoot();
        AddManyFiles(BlackDir + @"\assets", 5001);
        _fs.AddFile(BlackDir + @"\bin\Game.exe", "MZ");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal(BlackDir + @"\bin\Game.exe", game.LaunchCommand);
        Assert.Equal(BlackDir + @"\bin", game.LaunchWorkingDirectory);
    }

    [Fact]
    public void Quiver_selected_executable_counts_even_with_many_files()
    {
        AddRoot();
        AddManyFiles(BlackDir + @"\assets", 5001);
        _fs.AddFile(BlackDir + @"\bin\Game.exe", "MZ");
        _fs.AddFile(BlackDir + @"\bin\Editor.exe", "MZ");
        _fs.AddFile(BlackDir + @"\selected_executable.txt", @"bin\Game.exe");

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Equal(BlackDir + @"\bin\Game.exe", game.LaunchCommand);
    }

    [Fact]
    public void Quiver_files_in_wine_folders_do_not_use_up_the_search()
    {
        // Ordner von Wine/Proton betritt die Suche gar nicht erst (früher: aufgelistet, erst danach aussortiert).
        AddRoot();
        AddManyFiles(BlackDir + @"\.wine-prefix\drive_c\windows", 5001);
        _fs.AddFile(BlackDir + @"\bin\Game.exe", "MZ");
        var counting = new ListingCounter(_fs);

        var game = Assert.Single(Scan(Learned(Root), counting).Games);

        Assert.Equal(BlackDir + @"\bin\Game.exe", game.LaunchCommand);
        Assert.DoesNotContain(counting.Listed, d => d.Contains(".wine-prefix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Quiver_launcher_script_at_the_top_gives_no_launch_command()
    {
        // Oben nur ein Startskript, die EXE tiefer: Quiver startet das Skript (mit seinen Argumenten und Umgebung). HDRpilot
        // startet keine Skripte und darf die EXE nicht am Skript vorbei starten.
        AddRoot();
        _fs.AddFile(BlackDir + @"\launch.bat", "@echo off\r\nstart bin\\game.exe -vulkan");
        _fs.AddFile(BlackDir + @"\bin\game.exe", "MZ");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Null(game.LaunchCommand);
        Assert.Equal([BlackDir + @"\bin\game.exe"], game.Executables);
        Assert.True(_log.Contains(LogLevel.Information, "Quiver startet launch.bat"));
    }

    [Fact]
    public void Quiver_exe_and_script_side_by_side_are_two_candidates()
    {
        // Wie in Quiver: oben eine EXE und ein Skript sind zwei Kandidaten; ohne Wahl in Quiver kein Start-Befehl.
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\start-vulkan.cmd", "@echo off");

        var game = Assert.Single(Scan(Learned(Root)).Games);

        Assert.Null(game.LaunchCommand);
        Assert.Equal([BlackDir + @"\BLACK.exe"], game.Executables);
    }

    [Fact]
    public void Quiver_apps_path_on_a_drive_root_is_used()
    {
        // Quiver nimmt AppsPath unverändert, auch eine Laufwerkswurzel: die Spiele liegen dann in G:\<folderName>.
        AddRoot();
        _fs.AddFile(Root + @"\settings.json", "{ \"AppsPath\": \"G:\\\\\" }");
        AddBlack(@"G:\Black-BlackXBERecomp");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal(@"G:\Black-BlackXBERecomp", game.InstallDir);
        Assert.Equal(@"G:\Black-BlackXBERecomp\BLACK.exe", game.LaunchCommand);
    }

    [Theory]
    [InlineData("2569-10-16 21:47:35")] // Thai (Buddhistischer Kalender): Quiver schreibt im Kalender der Kultur
    [InlineData("1448-05-05 21:47:35")] // Umm al-Qura (ar-SA)
    public void Quiver_last_played_from_another_calendar_is_ignored(string text)
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\LastPlayed.txt", text);

        Assert.Null(Assert.Single(Scan(Learned(Root)).Games).LastPlayed);
    }

    [Theory]
    [InlineData("2026-10-16 21:47:35")]
    [InlineData("2026-10-16 21.47.35")] // Kultur mit Punkt als Zeittrenner (etwa fi-FI)
    [InlineData("2026-10-16T21:47:35")]
    public void Quiver_last_played_formats_are_read(string text)
    {
        AddRoot();
        AddBlack();
        _fs.AddFile(BlackDir + @"\LastPlayed.txt", text);

        Assert.Equal(new DateTimeOffset(new DateTime(2026, 10, 16, 21, 47, 35, DateTimeKind.Local)), Assert.Single(Scan(Learned(Root)).Games).LastPlayed);
    }

    [Fact]
    public void Quiver_duplicate_during_a_migration_follows_the_velopack_copy()
    {
        // MIGRATING.md von Quiver: Der alte Ordner (2.4.x) bleibt liegen, bis der neue (3.x) passt. Auch wenn der alte zuerst
        // gelernt wurde, gilt der neue.
        const string old = @"D:\Alt\Quiver";
        _fs.AddFile(old + @"\Quiver.exe", "MZ");
        _fs.AddFixture(@"Quiver\apps.json", old + @"\apps.json");
        AddBlack(old + @"\Apps\Black-BlackXBERecomp");
        AddRoot();
        AddBlack();

        var result = Scan(new ScanOptions { QuiverFolders = [old, Root] });

        Assert.Equal(BlackDir, Assert.Single(result.Games).InstallDir);
        Assert.True(_log.Contains(LogLevel.Information, "Black-BlackXBERecomp kommt auch in " + old + " vor"));
    }

    [Fact]
    public void Quiver_duplicate_in_two_new_folders_follows_the_one_played_last()
    {
        const string copy = @"E:\Kopie\QuiverLauncher";
        AddRoot();
        AddBlack();
        AddRoot(copy);
        AddBlack(copy + @"\Apps\Black-BlackXBERecomp");
        _fs.AddFile(copy + @"\Apps\Black-BlackXBERecomp\LastPlayed.txt", "2026-10-19 20:00:00");

        var game = Assert.Single(Scan(new ScanOptions { QuiverFolders = [Root, copy] }).Games);

        Assert.Equal(copy + @"\Apps\Black-BlackXBERecomp", game.InstallDir);
    }

    [Fact]
    public void Quiver_velopack_log_paths_with_an_apostrophe_are_found()
    {
        const string root = @"C:\Users\O'Brien\Downloads\Quiver";
        var doubled = root.Replace(@"\", @"\\", StringComparison.Ordinal);
        var text = string.Join('\n',
            "[update:1] [INFO] Moving C:\\Alt\\Quiver to D:\\Neu\\Quiver",
            $"[update:1] [INFO]     CWD: \"{doubled}\"",
            $"[update:1] [INFO] Root directory '{root}' writable: true",
            $"[update:1] [INFO] Using root packages directory: {root}\\packages");

        var folders = QuiverRootFinder.FoldersFromLog(text);

        Assert.Equal(root + @"\packages", folders[0]); // die jüngste Zeile mit Pfad zuerst
        Assert.Contains(root, folders);
        Assert.Contains(@"D:\Neu\Quiver", folders);
        Assert.DoesNotContain(folders, f => f.Contains("C:\\Users\\O\\", StringComparison.Ordinal) || f.EndsWith(@"\O", StringComparison.Ordinal));
        Assert.DoesNotContain(folders, f => f.Contains(" D:", StringComparison.Ordinal));
        Assert.DoesNotContain(folders, f => f.Contains("writable", StringComparison.Ordinal) || (f.Contains('\'', StringComparison.Ordinal) && f.EndsWith('\'')));

        // Nur die Zeile in einfachen Anführungszeichen: der ganze Pfad samt ', nichts dahinter.
        Assert.Equal(root, QuiverRootFinder.FoldersFromLog($"[update:1] [INFO] Root directory '{root}' writable: true")[0]);
    }

    [Fact]
    public void Quiver_search_skips_system_hidden_and_recycle_folders()
    {
        foreach (var hidden in new[] { @"C:\ProgramData\Quiver", @"C:\.versteckt\Quiver", @"C:\$Recycle.Bin\Quiver", @"C:\Users\Test\AppData\Quiver" })
        {
            var fs = new InMemoryFileSystem();
            AddRoot(hidden, fileSystem: fs);
            AddBlack(hidden + @"\Apps\Black-BlackXBERecomp", fs);
            Assert.False(Scan(fileSystem: fs).LauncherDetected, hidden);
        }

        // Gegenprobe: ein gewöhnlicher Ordner in derselben Tiefe wird gefunden.
        AddRoot(@"C:\Daten\Quiver");
        AddBlack(@"C:\Daten\Quiver\Apps\Black-BlackXBERecomp");
        Assert.True(Scan().LauncherDetected);
    }

    [Theory]
    [InlineData(QuiverRootFinder.MaxSearchFolderChildren, false)]
    [InlineData(QuiverRootFinder.MaxSearchFolderChildren - 1, true)] // Gegenprobe: dann liegt Quiver innerhalb der Grenze
    public void Quiver_search_folder_children_are_limited(int before, bool found)
    {
        for (var i = 0; i < before; i++)
        {
            _fs.AddDirectory($@"D:\Spielordner\Ordner{i:000}");
        }

        AddRoot(@"D:\Spielordner\zz-Quiver");
        AddBlack(@"D:\Spielordner\zz-Quiver\Apps\Black-BlackXBERecomp");

        Assert.Equal(found, Scan(new ScanOptions { SearchFolders = [@"D:\Spielordner"] }).LauncherDetected);
    }

    [Theory]
    [InlineData("Beispiel-Launcher.exe")] // relativ (läge sonst im Arbeitsordner von HDRpilot)
    [InlineData("E:\\\\Programme\\\\Beispiel-Launcher\\\\start.bat")]
    public void Incomplete_windows_installer_link_must_be_an_absolute_exe(string executable)
    {
        AddRoot(apps: Apps1("Beispiel-Launcher"));
        _fs.AddFile(Apps + @"\Beispiel-Launcher\windows-installer.json", "{\"Phase\":\"Linked\",\"ExecutablePath\":\"" + executable + "\"}");
        _fs.AddFile("Beispiel-Launcher.exe", "MZ"); // relativer Pfad, den die Attrappe kennt
        _fs.AddFile(@"E:\Programme\Beispiel-Launcher\start.bat", "@echo off");

        var result = Scan(Learned(Root));

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
        Assert.True(_log.Contains(LogLevel.Information, "keine vorhandene EXE verknüpft"));
    }

    [Fact]
    public void Quiver_search_without_hint_stays_on_fixed_drives()
    {
        // Downloads auf einer Netzfreigabe (Ordnerumleitung): Die Suche ohne Hinweis fasst keine Netzpfade an.
        _registry.Set(RegistryHiveKind.CurrentUser, QuiverRootFinder.UserShellFoldersKeyPath, QuiverRootFinder.DownloadsValueName, @"\\server\freigabe\Downloads");
        const string root = @"\\server\freigabe\Downloads\Quiver";
        AddRoot(root);
        AddBlack(root + @"\Apps\Black-BlackXBERecomp");
        var counting = new ListingCounter(_fs);

        Assert.False(Scan(fileSystem: counting).LauncherDetected);
        Assert.DoesNotContain(counting.Touched, p => p.StartsWith(@"\\", StringComparison.Ordinal));

        // Gegenprobe: als gelernter Ordner (Quiver lief dort) gilt er.
        Assert.True(Scan(Learned(root)).LauncherDetected);
    }

    [Fact]
    public void Quiver_search_without_hint_does_not_follow_linked_folders()
    {
        // Ein verknüpfter Ordner in Downloads (Junction, etwa auf eine Netzfreigabe) wird nicht betreten.
        const string link = @"C:\Users\Test\Downloads\Verknuepft";
        AddRoot(link + @"\Quiver");
        AddBlack(link + @"\Quiver\Apps\Black-BlackXBERecomp");
        var counting = new ListingCounter(_fs);
        counting.Links.Add(link);

        Assert.False(Scan(fileSystem: counting).LauncherDetected);
        Assert.DoesNotContain(counting.Touched, p => p.StartsWith(link + @"\", StringComparison.OrdinalIgnoreCase));

        // Gegenprobe: derselbe Ordner ohne Verknüpfung wird gefunden.
        Assert.True(Scan(fileSystem: new ListingCounter(_fs)).LauncherDetected);
    }

    // ---- Hilfen ----

    private void AddManyFiles(string folder, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _fs.AddFile($@"{folder}\datei{i:00000}.pak", [1]);
        }
    }

    private ScanResult Scan(ScanOptions? options = null, IFileSystem? fileSystem = null)
    {
        _lastRegistry = new ReadOnlyRegistry(_registry);
        return new QuiverScanner(new ReadOnlyFileSystem(fileSystem ?? _fs), _lastRegistry, _env, _log).Scan(options ?? ScanOptions.Default, Ct);
    }

    private static ScanOptions Learned(string folder) => new() { QuiverFolders = [folder] };

    /// <summary>Quiver-Ordner wie am Zielrechner (Velopack): Starter, current\ mit Kennzeichen, Einstellungen, Bibliothek.</summary>
    private void AddRoot(string root = Root, string? apps = null, InMemoryFileSystem? fileSystem = null)
    {
        var fs = fileSystem ?? _fs;
        fs.AddFixture(@"Quiver\sq.version", root + @"\current\sq.version");
        fs.AddFile(root + @"\current\QuiverLauncher.exe", "MZ");
        fs.AddFile(root + @"\QuiverLauncher.exe", "MZ");
        fs.AddFile(root + @"\Update.exe", "MZ");
        fs.AddFile(root + @"\.portable", "");
        fs.AddFixture(@"Quiver\settings.json", root + @"\settings.json");
        if (apps is null)
        {
            fs.AddFixture(@"Quiver\apps.json", root + @"\apps.json");
        }
        else
        {
            fs.AddFile(root + @"\apps.json", apps);
        }

        fs.AddDirectory(root + @"\Apps");
    }

    /// <summary>Spielordner von „Black“ wie am Zielrechner (gemessen): eine EXE oben, Abbild, Spielstände, Protokolle.</summary>
    private void AddBlack(string dir = BlackDir, InMemoryFileSystem? fileSystem = null)
    {
        var fs = fileSystem ?? _fs;
        fs.AddFile(dir + @"\BLACK.exe", "MZ");
        fs.AddFile(dir + @"\d3dcompiler_47.dll", "MZ");
        fs.AddFile(dir + @"\black.toml", "[config]");
        fs.AddFile(dir + @"\installed-build.txt", "BLACK-XBERecomp.zip");
        fs.AddFile(dir + @"\LastPlayed.txt", "2026-10-16 21:47:35");
        fs.AddFile(dir + @"\quiver-launch.log", "game=Black");
        fs.AddFile(dir + @"\version.txt", "V1");
        fs.AddFile(dir + @"\assets\Black (Europe) (En,Fr,De,Es,It).iso", "Abbild");
        fs.AddFile(dir + @"\hdd\E\UDATA\4541000d\Spielstand.xsv", "x");
        fs.AddFile(dir + @"\logs\black.log", "Protokoll");
    }

    /// <summary>apps.json mit einem Eintrag „Spiel“.</summary>
    private static string Apps1(string folderName, string? installPath = null) =>
        "{ \"apps\": [ { \"name\": \"Spiel\", \"folderName\": \"" + folderName + "\""
        + (installPath is null ? string.Empty : ", \"installPath\": \"" + installPath + "\"") + ", \"appIconUrl\": null } ] }";

    private static List<string> NetworkUses(IReadOnlyDictionary<string, string> sources) =>
        sources.SelectMany(s => Regex.Matches(s.Value, @"System\.Net|\bHttpClient\b|\bWebClient\b|\bWebRequest\b|\bSocket\b|\bDns\.")
            .Select(m => s.Key + ": " + m.Value))
            .ToList();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HdrPilot.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("HdrPilot.slnx nicht gefunden.");
    }

    /// <summary>Lässt nur geteiltes Lesen zu (<see cref="IFileSystem.ReadShared"/>); ReadAllBytes und ReadAllText lassen den Test scheitern.</summary>
    private sealed class SharedReadsOnly(IFileSystem inner) : IFileSystem
    {
        public int SharedReads { get; private set; }

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => throw new InvalidOperationException("Quiver-Dateien nur geteilt lesen: " + path);

        public byte[] ReadAllBytes(string path) => throw new InvalidOperationException("Quiver-Dateien nur geteilt lesen: " + path);

        public SharedRead ReadShared(string path, int maxBytes, bool fromEnd = false)
        {
            SharedReads++;
            return inner.ReadShared(path, maxBytes, fromEnd);
        }

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) => inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) => inner.EnumerateDirectories(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateFilesUpTo(string directory, string searchPattern, int maxDepth) => inner.EnumerateFilesUpTo(directory, searchPattern, maxDepth);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void WriteAllTextAtomic(string path, string contents) => inner.WriteAllTextAtomic(path, contents);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();
    }

    /// <summary>Ein Ordner mit sehr vielen leeren Unterordnern, ohne sie alle im Speicher anzulegen (die Attrappe listet linear).</summary>
    private sealed class WideFolder(IFileSystem inner, string folder, int children) : IFileSystem
    {
        private bool IsChild(string path) => path.StartsWith(folder + @"\leer", StringComparison.OrdinalIgnoreCase);

        public bool FileExists(string path) => !IsChild(path) && inner.FileExists(path);

        public bool DirectoryExists(string path) => IsChild(path) || inner.DirectoryExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public SharedRead ReadShared(string path, int maxBytes, bool fromEnd = false) => inner.ReadShared(path, maxBytes, fromEnd);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) =>
            IsChild(directory) ? [] : inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) =>
            IsChild(directory) ? []
            : string.Equals(directory, folder, StringComparison.OrdinalIgnoreCase) ? Enumerable.Range(0, children).Select(i => $@"{folder}\leer{i:0000}")
            : inner.EnumerateDirectories(directory, searchPattern, recursive);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void WriteAllTextAtomic(string path, string contents) => inner.WriteAllTextAtomic(path, contents);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();
    }

    /// <summary>Merkt sich jeden aufgelisteten Ordner und jeden berührten Pfad (Prüfung der Grenzen der Suche).</summary>
    private sealed class ListingCounter(IFileSystem inner) : IFileSystem
    {
        public List<string> Listed { get; } = [];

        public List<string> Touched { get; } = [];

        public bool FileExists(string path)
        {
            Touched.Add(path);
            return inner.FileExists(path);
        }

        public bool DirectoryExists(string path)
        {
            Touched.Add(path);
            return inner.DirectoryExists(path);
        }

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public SharedRead ReadShared(string path, int maxBytes, bool fromEnd = false)
        {
            Touched.Add(path);
            return inner.ReadShared(path, maxBytes, fromEnd);
        }

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false)
        {
            Listed.Add(directory);
            Touched.Add(directory);
            return inner.EnumerateFiles(directory, searchPattern, recursive);
        }

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false)
        {
            Listed.Add(directory);
            Touched.Add(directory);
            return inner.EnumerateDirectories(directory, searchPattern, recursive);
        }

        public IEnumerable<string> EnumerateDirectoriesWithoutLinks(string directory)
        {
            Listed.Add(directory);
            Touched.Add(directory);
            return inner.EnumerateDirectoriesWithoutLinks(directory).Where(d => !Links.Contains(d, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Ordner, die hier als verknüpft gelten (Junction): Sie fehlen in <see cref="EnumerateDirectoriesWithoutLinks"/>.</summary>
        public List<string> Links { get; } = [];

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void WriteAllTextAtomic(string path, string contents) => inner.WriteAllTextAtomic(path, contents);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();
    }

    /// <summary>Zählt, wie oft nach dem Kennzeichen von Quiver gesehen wird (je geprüftem Ordner einmal; auch für das Lernen).</summary>
    internal sealed class CountingFileSystem(IFileSystem inner) : IFileSystem
    {
        private readonly HashSet<string> _distinct = new(StringComparer.OrdinalIgnoreCase);

        public int ManifestChecks { get; private set; }

        public int ManifestChecksDistinct => _distinct.Count;

        public bool FileExists(string path)
        {
            if (path.EndsWith(@"\current\sq.version", StringComparison.OrdinalIgnoreCase))
            {
                ManifestChecks++;
                _distinct.Add(path);
            }

            return inner.FileExists(path);
        }

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public SharedRead ReadShared(string path, int maxBytes, bool fromEnd = false) => inner.ReadShared(path, maxBytes, fromEnd);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) => inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) => inner.EnumerateDirectories(directory, searchPattern, recursive);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void WriteAllTextAtomic(string path, string contents) => inner.WriteAllTextAtomic(path, contents);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();
    }
}
