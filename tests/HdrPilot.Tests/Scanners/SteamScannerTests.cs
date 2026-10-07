using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Scanners;

public sealed class SteamScannerTests
{
    private const string SteamDir = @"C:\Program Files (x86)\Steam";
    private const string DefaultLibrary = SteamDir;
    private const string LibraryD = @"D:\Steam-Bibliothek";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeEnvironment _environment = new();
    private readonly ListLogger<SteamScanner> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Fehlender Launcher ----------

    [Fact]
    public void Missing_launcher_returns_empty_without_errors()
    {
        var result = Scan();

        Assert.Equal(GameSource.Steam, result.Source);
        Assert.Equal("Steam", result.ScannerName);
        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Missing_launcher_when_steam_path_points_to_missing_folder_returns_empty_without_errors()
    {
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", "e:/deinstalliert/steam");

        var result = Scan();

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(1234)]
    [InlineData(new byte[] { 0x63, 0x3A })]
    public void Missing_launcher_when_steam_path_has_wrong_registry_type_returns_empty_without_errors(object value)
    {
        // Falscher Werttyp (DWORD bzw. REG_BINARY statt REG_SZ) darf weder werfen noch als Pfad gelten.
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", value);
        _fs.AddDirectory(@"C:\1234");

        var result = Scan();

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    // ---------- Positive Fälle ----------

    [Fact]
    public void Library_path_with_umlauts_slashes_and_trailing_backslash_is_normalized()
    {
        WithSteam();
        WriteLibraryFolders("\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"E:/Spiele Ä/Steam Bibliothek\\\\\"\n\t}\n\t\"2\"\n\t{\n\t\t\"path\"\t\t\"Z:\\\\Gibt es nicht\"\n\t}\n}\n");
        AddManifest(@"E:\Spiele Ä\Steam Bibliothek", 9400, "Größenwahn™", "Größenwahn", 4);

        var result = Scan();

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal("Größenwahn™", game.Name);
        Assert.Equal(@"E:\Spiele Ä\Steam Bibliothek\steamapps\common\Größenwahn", game.InstallDir);
        Assert.Equal("steam://rungameid/9400", game.LaunchCommand);
        Assert.True(_logger.Contains(LogLevel.Information, @"Z:\Gibt es nicht"));
    }

    [Fact]
    public void Finds_games_in_default_and_additional_library_with_exact_launch_command()
    {
        WithSteam();
        AddLibraryFolders();
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        Assert.Equal(["9990110", "9990120"], result.Games.Select(g => g.SourceId));

        var sternenwacht = result.Games[0];
        Assert.Equal(GameSource.Steam, sternenwacht.Source);
        Assert.Equal("STERNENWACHT™ 2", sternenwacht.Name);
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\Sternenwacht 2", sternenwacht.InstallDir);
        Assert.Equal("steam://rungameid/9990110", sternenwacht.LaunchCommand);
        Assert.Empty(sternenwacht.Executables);
        Assert.Null(sternenwacht.LaunchArguments);
        Assert.Null(sternenwacht.LaunchWorkingDirectory);
        Assert.Equal("Steam:9990110", sternenwacht.Key);

        var turm = result.Games[1];
        Assert.Equal("Turmfall", turm.Name);
        Assert.Equal(@"D:\Steam-Bibliothek\steamapps\common\Turmfall", turm.InstallDir);
        Assert.Equal("steam://rungameid/9990120", turm.LaunchCommand);
    }

    [Fact]
    public void Install_dir_uses_library_spelling_from_libraryfolders_not_lowercase_registry_path()
    {
        WithSteam();
        AddLibraryFolders();
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var game = Assert.Single(Scan().Games);

        // SteamPath steht gemessen als "c:/program files (x86)/steam" in der Registry.
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\Sternenwacht 2", game.InstallDir);
    }

    [Fact]
    public void Default_library_is_scanned_when_libraryfolders_is_missing()
    {
        WithSteam();
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal(@"c:\program files (x86)\steam\steamapps\common\Sternenwacht 2", game.InstallDir);
    }

    [Fact]
    public void Default_library_is_scanned_when_libraryfolders_does_not_list_it()
    {
        WithSteam();
        WriteLibraryFolders("\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Steam-Bibliothek\"\n\t}\n}\n");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["9990110", "9990120"], result.Games.Select(g => g.SourceId));
    }

    [Fact]
    public void Libraries_are_deduplicated_ignoring_case_and_slashes()
    {
        WithSteam();
        WriteLibraryFolders("""
            "libraryfolders"
            {
            	"0" { "path" "C:\\Program Files (x86)\\Steam" }
            	"1" { "path" "c:/PROGRAM FILES (X86)/steam/" }
            	"2" { "path" "D:\\Steam-Bibliothek" }
            	"3" { "path" "d:/steam-bibliothek/" }
            	"4" { "path" "D:\\Steam-Bibliothek\\\\" }
            }
            """);
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["9990110", "9990120"], result.Games.Select(g => g.SourceId));
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\Sternenwacht 2", result.Games[0].InstallDir);
    }

    [Fact]
    public void Missing_library_is_skipped_without_error()
    {
        WithSteam();
        AddLibraryFolders(); // nennt auch E:\SteamLibrary, das hier fehlt
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Single(result.Games);
        Assert.True(_logger.Contains(LogLevel.Information, @"E:\SteamLibrary"));
    }

    [Fact]
    public void Old_libraryfolders_format_is_read()
    {
        WithSteam();
        AddLibraryFolders("libraryfolders.old-format.vdf");
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(@"D:\Steam-Bibliothek\steamapps\common\Turmfall", Assert.Single(result.Games).InstallDir);
    }

    [Fact]
    public void Steam_path_falls_back_to_machine_install_path_in_32bit_view()
    {
        _registry.Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", @"C:\Program Files (x86)\Steam", RegistryViewKind.Registry32);
        _fs.AddDirectory(SteamDir);
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Single(result.Games);
    }

    [Fact]
    public void Steam_path_falls_back_to_default_folder_from_environment()
    {
        _environment.Folders[Environment.SpecialFolder.ProgramFilesX86] = @"E:\Programme (x86)";
        _fs.AddFile(@"E:\Programme (x86)\Steam\steam.exe", "MZ");
        AddFixtureGame(@"E:\Programme (x86)\Steam", "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Equal(@"E:\Programme (x86)\Steam\steamapps\common\Sternenwacht 2", Assert.Single(result.Games).InstallDir);
    }

    [Fact]
    public void Update_pending_game_is_included()
    {
        WithSteam();
        AddLibraryFolders();
        AddFixtureGame(LibraryD, "appmanifest_3762660.acf", "Trials Survivors"); // StateFlags 6 = 4 + 2

        var game = Assert.Single(Scan().Games);

        Assert.Equal("Trials Survivors", game.Name);
    }

    [Fact]
    public void Manifest_without_state_flags_is_treated_as_installed()
    {
        WithSteam();
        AddManifest(DefaultLibrary, 4000, "Ohne Status", "OhneStatus", stateFlags: null);

        Assert.Equal("Ohne Status", Assert.Single(Scan().Games).Name);
    }

    [Fact]
    public void Duplicate_app_in_two_libraries_is_returned_once()
    {
        WithSteam();
        AddLibraryFolders();
        AddFixtureGame(DefaultLibrary, "appmanifest_9990120.acf", "Turmfall");
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");

        var result = Scan();

        var game = Assert.Single(result.Games);
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\Turmfall", game.InstallDir);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Install_dir_with_forward_slash_is_normalized()
    {
        WithSteam();
        AddManifest(DefaultLibrary, 4001, "Unterordner", "Studio/Spiel", 4);

        // Ohne libraryfolders.vdf bleibt die Schreibweise aus der Registry (klein); Vergleiche sind ohnehin ohne Groß-/Kleinschreibung.
        Assert.Equal(@"c:\program files (x86)\steam\steamapps\common\Studio\Spiel", Assert.Single(Scan().Games).InstallDir);
    }

    // ---------- Filter für Nicht-Spiele ----------

    [Fact]
    public void Steamworks_redistributables_and_SteamVR_are_filtered_without_appinfo()
    {
        WithSteam();
        AddLibraryFolders();
        AddFixtureGame(DefaultLibrary, "appmanifest_228980.acf", "Steamworks Shared");
        AddFixtureGame(DefaultLibrary, "appmanifest_250820.acf", "SteamVR");
        AddFixtureGame(DefaultLibrary, "appmanifest_431960.acf", "wallpaper_engine");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        Assert.True(_logger.Contains(LogLevel.Information, "Steamworks Common Redistributables"));
        Assert.True(_logger.Contains(LogLevel.Information, "Wallpaper Engine"));
    }

    [Fact]
    public void Non_game_types_from_appinfo_are_filtered_and_demos_and_playtests_kept()
    {
        WithSteam();
        AddManifest(DefaultLibrary, 5001, "Ein Spiel", "Spiel", 4);
        AddManifest(DefaultLibrary, 5002, "Ein Werkzeug", "Werkzeug", 4);
        AddManifest(DefaultLibrary, 5003, "Eine Anwendung", "Anwendung", 4);
        AddManifest(DefaultLibrary, 5004, "Eine Demo", "Demo", 4);
        AddManifest(DefaultLibrary, 5005, "Ein Playtest", "Playtest", 4);
        AddManifest(DefaultLibrary, 5006, "Playtest ohne Daten", "PlaytestOhneDaten", 4);
        AddManifest(DefaultLibrary, 5007, "Konfiguration", "Config", 4);
        AddAppInfo(new SteamAppInfoBuilder()
            .Add(5001, "Game", "Ein Spiel")
            .Add(5002, "Tool", "Ein Werkzeug")
            .Add(5003, "Application", "Eine Anwendung")
            .Add(5004, "Demo", "Eine Demo")
            .Add(5005, "Beta", "Ein Playtest")
            .Add(5006, null, null)
            .Add(5007, "config", "Konfiguration"));

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["5001", "5004", "5005", "5006"], result.Games.Select(g => g.SourceId));
    }

    [Fact]
    public void Name_rules_apply_only_when_appinfo_has_no_type()
    {
        WithSteam();
        AddManifest(DefaultLibrary, 896660, "Valheim Dedicated Server", "Valheim dedicated server", 4);
        AddManifest(DefaultLibrary, 2805730, "Proton 9.0", "Proton 9.0", 4);
        AddManifest(DefaultLibrary, 1070560, "Steam Linux Runtime 1.0 (scout)", "SteamLinuxRuntime", 4);
        AddManifest(DefaultLibrary, 1007, "Steamworks SDK Redist", "Steamworks SDK Redist", 4);
        AddManifest(DefaultLibrary, 6001, "Proton Pack Arena", "ProtonPack", 4);

        Assert.Equal(["6001"], Scan().Games.Select(g => g.SourceId));

        // Kennt appinfo.vdf den Typ "Game", entscheidet der Typ, nicht der Name.
        AddAppInfo(new SteamAppInfoBuilder().Add(896660, "Game", "Valheim Dedicated Server"));
        Assert.Equal(["6001", "896660"], Scan().Games.Select(g => g.SourceId)); // Reihenfolge der Manifest-Dateinamen
    }

    [Fact]
    public void Placeholder_name_is_replaced_by_name_from_appinfo()
    {
        WithSteam();
        AddManifest(DefaultLibrary, 7001, "appid_7001", "platzhalter_ordner", 4);

        Assert.Equal("platzhalter_ordner", Assert.Single(Scan().Games).Name);

        AddAppInfo(new SteamAppInfoBuilder().Add(7001, "Game", "Echter Name"));
        Assert.Equal("Echter Name", Assert.Single(Scan().Games).Name);
    }

    [Fact]
    public void Unknown_appinfo_version_is_ignored_without_error()
    {
        WithSteam();
        AddManifest(DefaultLibrary, 5002, "Ein Werkzeug", "Werkzeug", 4);
        var data = new SteamAppInfoBuilder().Add(5002, "Tool", "Ein Werkzeug").Build();
        data[0] = 0x30; // Kennung 0x07564430: künftige Version
        _fs.AddFile(SteamDir + @"\appcache\appinfo.vdf", data);

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Single(result.Games);
        Assert.True(_logger.Contains(LogLevel.Warning, "unbekannte Formatversion"));
    }

    // ---------- Unvollständige Installationen ----------

    [Fact]
    public void Incomplete_installation_without_fully_installed_flag_is_skipped_without_error()
    {
        WithSteam();
        AddLibraryFolders();
        _fs.AddFixture(@"Steam\appmanifest_2868840.installing.acf", DefaultLibrary + @"\steamapps\appmanifest_2868840.acf");
        _fs.AddDirectory(DefaultLibrary + @"\steamapps\common\Slay the Spire 2"); // Ordner existiert schon während des Downloads
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        Assert.True(_logger.Contains(LogLevel.Information, "StateFlags 1026"));
    }

    [Fact]
    public void Incomplete_uninstalling_app_is_skipped_without_error()
    {
        WithSteam();
        AddManifest(DefaultLibrary, 8001, "Wird entfernt", "Entfernt", 2052); // 2048 + 4

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Empty(result.Games);
    }

    [Fact]
    public void Incomplete_game_folder_missing_is_skipped_without_error()
    {
        WithSteam();
        AddLibraryFolders();
        _fs.AddFixture(@"Steam\appmanifest_9990120.acf", LibraryD + @"\steamapps\appmanifest_9990120.acf");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        Assert.True(_logger.Contains(LogLevel.Information, "Turmfall"));
    }

    [Fact]
    public void Incomplete_manifest_without_name_uses_install_folder()
    {
        WithSteam();
        _fs.AddFile(DefaultLibrary + @"\steamapps\appmanifest_9001.acf", "\"AppState\"\n{\n\t\"appid\"\t\t\"9001\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Namenlos\"\n}\n");
        _fs.AddDirectory(DefaultLibrary + @"\steamapps\common\Namenlos");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal("Namenlos", Assert.Single(result.Games).Name);
    }

    // ---------- Defekte Dateien ----------

    [Fact]
    public void Broken_truncated_manifest_is_reported_and_other_games_are_returned()
    {
        WithSteam();
        AddLibraryFolders();
        _fs.AddFixture(@"Steam\appmanifest_truncated.acf", DefaultLibrary + @"\steamapps\appmanifest_9990120.acf");
        AddFixtureGame(LibraryD, "appmanifest_3762660.acf", "Trials Survivors");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Equal(["9990110", "3762660"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Equal(DefaultLibrary + @"\steamapps\appmanifest_9990120.acf", error.Path);
        Assert.Contains("defekt", error.Message, StringComparison.Ordinal);
        Assert.Contains("abgeschnitten", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_binary_garbage_manifest_is_reported()
    {
        WithSteam();
        var garbage = new byte[512];
        new Random(4711).NextBytes(garbage);
        _fs.AddFile(DefaultLibrary + @"\steamapps\appmanifest_666.acf", garbage);
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.EndsWith("appmanifest_666.acf", error.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"AppState\"\n{\n\t\"name\"\t\t\"Ohne ID\"\n\t\"installdir\"\t\t\"OhneId\"\n}\n", "appid")]
    [InlineData("\"AppState\"\n{\n\t\"appid\"\t\t\"abc\"\n\t\"installdir\"\t\t\"OhneId\"\n}\n", "appid")]
    [InlineData("\"AppState\"\n{\n\t\"appid\"\t\t\"9100\"\n\t\"name\"\t\t\"Ohne Ordner\"\n}\n", "installdir")]
    [InlineData("\"AppState\"\n{\n\t\"appid\"\t\t\"9100\"\n\t\"installdir\"\t\t\"\"\n}\n", "installdir")]
    [InlineData("\"AnderesFormat\"\n{\n\t\"appid\"\t\t\"9100\"\n}\n", "AppState")]
    [InlineData("\"AppState\"\t\t\"kein Block\"\n", "AppState")]
    public void Broken_manifest_without_required_fields_is_reported(string content, string expectedField)
    {
        WithSteam();
        _fs.AddFile(DefaultLibrary + @"\steamapps\appmanifest_9100.acf", content);
        _fs.AddDirectory(DefaultLibrary + @"\steamapps\common\OhneId");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Contains(expectedField, error.Message, StringComparison.Ordinal);
        Assert.EndsWith("appmanifest_9100.acf", error.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"..\..\..\Windows")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"..")]
    [InlineData(@"Spiel\..\..")]
    [InlineData(@"D:Spiel")]
    public void Broken_install_dir_leaving_the_library_is_rejected(string installDir)
    {
        WithSteam();
        AddManifest(DefaultLibrary, 9200, "Böse", installDir, 4);
        _fs.AddDirectory(@"C:\Windows");

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Contains("installdir", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_wrong_encoding_utf16_without_bom_is_reported()
    {
        WithSteam();
        var text = "\"AppState\"\n{\n\t\"appid\"\t\t\"9300\"\n\t\"name\"\t\t\"UTF-16\"\n\t\"installdir\"\t\t\"Utf16\"\n}\n";
        _fs.AddFile(DefaultLibrary + @"\steamapps\appmanifest_9300.acf", Encoding.Unicode.GetBytes(text));
        _fs.AddDirectory(DefaultLibrary + @"\steamapps\common\Utf16");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Contains("falsche Kodierung", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Latin1_manifest_is_read_with_correct_umlauts()
    {
        WithSteam();
        var text = "\"AppState\"\n{\n\t\"appid\"\t\t\"9301\"\n\t\"name\"\t\t\"Große Äpfel\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Aepfel\"\n}\n";
        _fs.AddFile(DefaultLibrary + @"\steamapps\appmanifest_9301.acf", Encoding.Latin1.GetBytes(text));
        _fs.AddDirectory(DefaultLibrary + @"\steamapps\common\Aepfel");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal("Große Äpfel", Assert.Single(result.Games).Name);
        Assert.True(_logger.Contains(LogLevel.Warning, "Latin-1"));
    }

    [Fact]
    public void Utf8_manifest_with_bom_is_read()
    {
        WithSteam();
        var text = "\"AppState\"\n{\n\t\"appid\"\t\t\"9302\"\n\t\"name\"\t\t\"Spiel™ mit BOM\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Bom\"\n}\n";
        _fs.AddFile(DefaultLibrary + @"\steamapps\appmanifest_9302.acf", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]);
        _fs.AddDirectory(DefaultLibrary + @"\steamapps\common\Bom");

        Assert.Equal("Spiel™ mit BOM", Assert.Single(Scan().Games).Name);
    }

    [Fact]
    public void Broken_unreadable_manifest_is_reported()
    {
        WithSteam();
        AddLibraryFolders();
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");
        _fs.FailReading(LibraryD + @"\steamapps\appmanifest_9990120.acf", new IOException("Der Prozess kann nicht auf die Datei zugreifen."));

        var result = Scan();

        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Contains("nicht lesbar", error.Message, StringComparison.Ordinal);
        Assert.Equal(LibraryD + @"\steamapps\appmanifest_9990120.acf", error.Path);
    }

    [Fact]
    public void Broken_libraryfolders_is_reported_and_default_library_still_scanned()
    {
        WithSteam();
        WriteLibraryFolders("\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Steam");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Equal(SteamDir + @"\steamapps\libraryfolders.vdf", error.Path, ignoreCase: true);
        Assert.Contains("Standardbibliothek", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_empty_manifest_and_empty_libraryfolders_are_reported_and_default_library_scanned()
    {
        WithSteam();
        WriteLibraryFolders(string.Empty);
        _fs.AddFile(DefaultLibrary + @"\steamapps\appmanifest_9500.acf", Array.Empty<byte>());
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Path!.EndsWith("libraryfolders.vdf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Path!.EndsWith("appmanifest_9500.acf", StringComparison.OrdinalIgnoreCase) && e.Message.Contains("AppState", StringComparison.Ordinal));
    }

    [Fact]
    public void Broken_libraryfolders_with_unexpected_root_is_reported()
    {
        WithSteam();
        WriteLibraryFolders("\"etwasanderes\"\n{\n}\n");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");

        var result = Scan();

        Assert.Single(result.Games);
        Assert.Contains("libraryfolders", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_library_entry_without_path_is_reported_and_others_kept()
    {
        WithSteam();
        WriteLibraryFolders("\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"label\"\t\t\"\"\n\t}\n\t\"2\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Steam-Bibliothek\"\n\t}\n\t\"3\"\n\t{\n\t\t\"path\"\t\t\"relativ\\\\ordner\"\n\t}\n}\n");
        AddFixtureGame(LibraryD, "appmanifest_9990120.acf", "Turmfall");

        var result = Scan();

        Assert.Single(result.Games);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Equal(SteamDir + @"\steamapps\libraryfolders.vdf", e.Path, ignoreCase: true));
    }

    [Fact]
    public void Broken_appinfo_is_reported_and_fixed_list_still_filters()
    {
        WithSteam();
        AddLibraryFolders();
        AddFixtureGame(DefaultLibrary, "appmanifest_228980.acf", "Steamworks Shared");
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        var data = new SteamAppInfoBuilder().Add(228980, "Tool", "Steamworks Common Redistributables").Add(9990110, "Game", "STERNENWACHT™ 2").Build();
        _fs.AddFile(SteamDir + @"\appcache\appinfo.vdf", data[..60]);

        var result = Scan();

        Assert.Equal(["9990110"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Equal(SteamDir + @"\appcache\appinfo.vdf", error.Path, ignoreCase: true);
        Assert.Contains("App-Cache", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_unreadable_appinfo_is_reported_and_games_still_returned()
    {
        WithSteam();
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        _fs.AddFile(SteamDir + @"\appcache\appinfo.vdf", [1, 2, 3]);
        _fs.FailReading(SteamDir + @"\appcache\appinfo.vdf", new UnauthorizedAccessException("Zugriff verweigert"));

        var result = Scan();

        Assert.Single(result.Games);
        Assert.Contains("nicht lesbar", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    // ---------- Allgemeines ----------

    [Fact]
    public void Scan_honors_cancellation()
    {
        WithSteam();
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => CreateScanner().Scan(ScanOptions.Default, cts.Token));
    }

    [Fact]
    public void Scanner_is_found_by_catalog()
    {
        Assert.Contains(typeof(SteamScanner), ScannerCatalog.ScannerTypes);
        Assert.Contains(
            ScannerCatalog.CreateAll(new InMemoryFileSystem(), new FakeRegistry(), new FakeEnvironment()),
            s => s is SteamScanner && s.Source == GameSource.Steam);
    }

    [Fact]
    public void Scanner_does_not_write_registry_or_files()
    {
        WithSteam();
        AddLibraryFolders();
        AddFixtureGame(DefaultLibrary, "appmanifest_9990110.acf", "Sternenwacht 2");
        var spy = new SteamUbisoftGuards.WriteSpyFileSystem(_fs);

        var result = new SteamScanner(spy, new SteamUbisoftGuards.ReadOnlyRegistry(_registry), _environment).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games); // der Scan lief wirklich durch
        Assert.Equal(0, spy.Writes);
    }

    // ---------- Hilfen ----------

    private ScanResult Scan() => CreateScanner().Scan(ScanOptions.Default, Ct);

    private SteamScanner CreateScanner() => new(_fs, _registry, _environment, _logger);

    /// <summary>Steam wie am Zielrechner: SteamPath klein und mit Schrägstrichen.</summary>
    private void WithSteam()
    {
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", "c:/program files (x86)/steam");
        _fs.AddFile(SteamDir + @"\steam.exe", "MZ");
        _fs.AddDirectory(LibraryD);
    }

    private void AddLibraryFolders(string fixture = "libraryfolders.vdf") =>
        _fs.AddFixture(@"Steam\" + fixture, SteamDir + @"\steamapps\libraryfolders.vdf");

    private void WriteLibraryFolders(string content) => _fs.AddFile(SteamDir + @"\steamapps\libraryfolders.vdf", content);

    private void AddFixtureGame(string library, string fixture, string installDir)
    {
        _fs.AddFixture(@"Steam\" + fixture, library + @"\steamapps\" + fixture);
        _fs.AddDirectory(library + @"\steamapps\common\" + installDir);
    }

    private void AddAppInfo(SteamAppInfoBuilder builder) => _fs.AddFile(SteamDir + @"\appcache\appinfo.vdf", builder.Build());

    /// <summary>Manifest im Aufbau von Steam (Tabulatoren, Anführungszeichen, Escapes für Backslashes).</summary>
    private void AddManifest(string library, uint appId, string name, string installDir, uint? stateFlags)
    {
        var sb = new StringBuilder();
        sb.Append("\"AppState\"\n{\n");
        sb.Append($"\t\"appid\"\t\t\"{appId}\"\n");
        sb.Append("\t\"universe\"\t\t\"1\"\n");
        sb.Append($"\t\"name\"\t\t\"{name}\"\n");
        if (stateFlags is not null)
        {
            sb.Append($"\t\"StateFlags\"\t\t\"{stateFlags}\"\n");
        }

        sb.Append($"\t\"installdir\"\t\t\"{installDir.Replace(@"\", @"\\", StringComparison.Ordinal)}\"\n");
        sb.Append("\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\t\"german\"\n\t}\n}\n");
        _fs.AddFile($@"{library}\steamapps\appmanifest_{appId}.acf", sb.ToString());
        if (!installDir.Contains("..", StringComparison.Ordinal) && !installDir.Contains(':', StringComparison.Ordinal))
        {
            _fs.AddDirectory(library + @"\steamapps\common\" + installDir.Replace('/', '\\'));
        }
    }
}
