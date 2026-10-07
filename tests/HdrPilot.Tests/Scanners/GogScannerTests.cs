using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

/// <summary>
/// Tests des GOG-Scanners gegen eine Registry-Attrappe mit den am Zielrechner gemessenen Wertnamen (Galaxy: camelCase,
/// ältere Offline-Installer: Großbuchstaben) und gegen goggame-&lt;ID&gt;.info-Beispieldateien (Fixtures\Gog).
/// </summary>
public sealed class GogScannerTests
{
    private const string GamesKey = @"Software\GOG.com\Games";
    private const string GalaxyDir = @"C:\Program Files (x86)\GOG Galaxy";
    private const string CyberpunkDir = @"D:\GOG-Spiele\Cyberpunk 2077";
    private const string EinsDir = GalaxyDir + @"\Games\Beispielspiel Eins";
    private const string Swat3Dir = @"F:\GOG-Spiele\SWAT 3";
    private const string Swat4Dir = @"F:\GOG-Spiele\SWAT 4";
    private const string ShowgunnersDir = @"D:\GOG-Spiele\Showgunners";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeEnvironment _env = new();
    private ReadOnlyRegistry? _lastRegistry;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Fehlender Launcher und Registry-Sicht ----

    [Fact]
    public void Missing_launcher_returns_empty_without_errors()
    {
        var result = Scan();

        Assert.Equal(GameSource.Gog, result.Source);
        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Galaxy_client_without_games_detects_launcher_without_errors()
    {
        AddGalaxyClient();

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Games_key_is_opened_through_32bit_view_without_wow6432node_path()
    {
        AddCyberpunk();

        Assert.Single(Scan().Games);

        var opened = _lastRegistry!.Opened;
        Assert.Contains((RegistryHiveKind.LocalMachine, GamesKey, RegistryViewKind.Registry32), opened);
        Assert.DoesNotContain(opened, o => o.Path.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase));
        Assert.All(opened.Where(o => o.Hive == RegistryHiveKind.LocalMachine), o => Assert.Equal(RegistryViewKind.Registry32, o.View));
    }

    [Fact]
    public void Games_in_64bit_view_only_are_not_read()
    {
        // Gemessen: in der 64-Bit-Sicht gibt es keinen Schlüssel GOG.com. Ein Eintrag nur dort ist kein GOG-Spiel.
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\1423049311", "path", CyberpunkDir, RegistryViewKind.Registry64);
        _fs.AddFile(CyberpunkDir + @"\REDprelauncher.exe", "MZ");

        var result = Scan();

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
    }

    // ---- Positive Fälle ----

    [Fact]
    public void Galaxy_game_starts_exe_from_registry_with_working_dir()
    {
        AddCyberpunk();

        var game = Assert.Single(Scan().Games);

        Assert.Equal(GameSource.Gog, game.Source);
        Assert.Equal("1423049311", game.SourceId);
        Assert.Equal("Gog:1423049311", game.Key);
        Assert.Equal("Cyberpunk 2077", game.Name);
        Assert.Equal(CyberpunkDir, game.InstallDir);
        Assert.Equal(CyberpunkDir + @"\REDprelauncher.exe", game.LaunchCommand);
        Assert.Null(game.LaunchArguments);
        Assert.Equal(CyberpunkDir, game.LaunchWorkingDirectory);
    }

    [Fact]
    public void Executables_include_game_play_tasks_from_info_file()
    {
        AddCyberpunk();

        var game = Assert.Single(Scan().Games);

        Assert.Equal([CyberpunkDir + @"\REDprelauncher.exe", CyberpunkDir + @"\bin\x64\Cyberpunk2077.exe"], game.Executables);
    }

    [Fact]
    public void Legacy_uppercase_value_names_are_read()
    {
        AddSwat3();

        var game = Assert.Single(Scan().Games);

        Assert.Equal("SWAT 3 - Tactical Game of The Year Edition", game.Name);
        Assert.Equal(Swat3Dir, game.InstallDir);
        Assert.Equal(Swat3Dir + @"\SWAT.EXE", game.LaunchCommand);
        Assert.Equal(Swat3Dir, game.LaunchWorkingDirectory); // "F:\GOG-Spiele\SWAT 3\" ohne abschließenden Trenner
        Assert.Null(game.LaunchArguments);
    }

    [Fact]
    public void Tool_play_tasks_without_game_category_are_not_executables()
    {
        AddSwat3();
        _fs.AddFile(Swat3Dir + @"\Tools\Worldcraft\wc.exe", "MZ");
        _fs.AddFile(Swat3Dir + @"\ScenarioEd.exe", "MZ");
        _fs.AddFile(Swat3Dir + @"\RunTactics.exe", "MZ");

        var game = Assert.Single(Scan().Games);

        // Die Datei heißt auf der Platte "swat.exe", die Registry schreibt "SWAT.EXE": Groß-/Kleinschreibung egal.
        Assert.Equal([Swat3Dir + @"\SWAT.EXE"], game.Executables);
    }

    [Fact]
    public void Launch_parameters_from_registry_are_passed()
    {
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir, launchParam: " -dx12 ");
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");

        var game = Assert.Single(Scan().Games);

        Assert.Equal("-dx12", game.LaunchArguments);
        Assert.Equal(ShowgunnersDir + @"\Showgunners.exe", game.LaunchCommand);
    }

    [Fact]
    public void Install_path_with_forward_slashes_and_trailing_separator_is_normalized()
    {
        AddGalaxyGame("1511366207", "Showgunners", "D:/GOG-Spiele/Showgunners/", "D:/GOG-Spiele/Showgunners/Showgunners.exe", "D:/GOG-Spiele/Showgunners/");
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");

        var game = Assert.Single(Scan().Games);

        Assert.Equal(ShowgunnersDir, game.InstallDir);
        Assert.Equal(ShowgunnersDir + @"\Showgunners.exe", game.LaunchCommand);
        Assert.Equal(ShowgunnersDir, game.LaunchWorkingDirectory);
    }

    [Fact]
    public void Missing_registry_exe_falls_back_to_primary_play_task()
    {
        AddGalaxyGame("1999999901", "Beispielspiel Eins", EinsDir, exe: null, workingDir: null);
        AddEinsFiles();

        var game = Assert.Single(Scan().Games);

        Assert.Equal(EinsDir + @"\Launcher\Starter.exe", game.LaunchCommand);
        Assert.Equal(EinsDir + @"\Launcher", game.LaunchWorkingDirectory);
        Assert.Equal(
            [EinsDir + @"\Launcher\Starter.exe", EinsDir + @"\bin\eins.exe", EinsDir + @"\bin\eins_dx11.exe"],
            game.Executables);
    }

    [Fact]
    public void Registry_exe_missing_on_disk_falls_back_to_primary_play_task()
    {
        AddGalaxyGame("1999999901", "Beispielspiel Eins", EinsDir, EinsDir + @"\Launcher\Alt.exe", EinsDir + @"\Launcher");
        AddEinsFiles();

        var game = Assert.Single(Scan().Games);

        Assert.Equal(EinsDir + @"\Launcher\Starter.exe", game.LaunchCommand);
    }

    [Fact]
    public void Game_installed_below_galaxy_folder_is_a_game_but_galaxy_folder_itself_is_not()
    {
        AddGalaxyClient();
        AddGalaxyGame("1999999901", "Beispielspiel Eins", EinsDir, EinsDir + @"\Launcher\Starter.exe", EinsDir + @"\Launcher");
        AddEinsFiles();
        AddGalaxyGame("1000000001", "Falscher Eintrag", GalaxyDir, GalaxyDir + @"\GalaxyClient.exe", GalaxyDir);
        _fs.AddFile(GalaxyDir + @"\GalaxyClient.exe", "MZ");

        var result = Scan();

        Assert.Equal(["1999999901"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Contains("enthält GOG Galaxy selbst", error.Message, StringComparison.Ordinal);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\" + GamesKey + @"\1000000001", error.Path);
    }

    [Fact]
    public void Registry_like_target_system_yields_five_games_without_dlc()
    {
        // Abbild der 6 Einträge am Zielrechner: 5 Spiele und Phantom Liberty (DLC, dependsOn = 1423049311).
        AddGalaxyClient();
        AddCyberpunk();
        AddPhantomLiberty(withDependsOn: true);
        AddSwat3();
        AddLegacyGame("1409964317", "SWAT 4 Gold Edition", Swat4Dir, Swat4Dir + @"\Content\System\swat4.exe", Swat4Dir + @"\Content\System");
        _fs.AddFile(Swat4Dir + @"\Content\System\Swat4.exe", "MZ");
        AddGalaxyGame("1999999901", "Beispielspiel Eins", EinsDir, EinsDir + @"\Launcher\Starter.exe", EinsDir + @"\Launcher");
        AddEinsFiles();
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        Assert.Equal(
            ["1207658946", "1409964317", "1423049311", "1511366207", "1999999901"],
            result.Games.Select(g => g.SourceId).Order(StringComparer.Ordinal));
    }

    // ---- Filterregeln: Zusatzinhalte ----

    [Fact]
    public void Dlc_with_dependsOn_is_skipped()
    {
        // Ohne Spielinfo greift nur dependsOn; sonst käme "keine vorhandene Start-EXE" als Fehler.
        AddCyberpunk();
        AddPhantomLiberty(withDependsOn: true, withInfo: false);

        var result = Scan();

        Assert.Equal(["1423049311"], result.Games.Select(g => g.SourceId));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Dlc_detected_by_root_game_id_of_info_file_is_skipped()
    {
        AddCyberpunk();
        AddPhantomLiberty(withDependsOn: false);

        var result = Scan();

        Assert.Equal(["1423049311"], result.Games.Select(g => g.SourceId));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Duplicate_game_id_yields_one_entry()
    {
        AddCyberpunk();
        AddGalaxyGame("Kopie", "Cyberpunk 2077", CyberpunkDir, CyberpunkDir + @"\REDprelauncher.exe", CyberpunkDir);
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\Kopie", "gameID", "1423049311", RegistryViewKind.Registry32);

        var result = Scan();

        Assert.Single(result.Games);
        Assert.Empty(result.Errors);
    }

    // ---- Unvollständige Einträge ----

    [Fact]
    public void Incomplete_missing_game_folder_is_skipped_without_error()
    {
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        AddCyberpunk();

        var result = Scan();

        Assert.Equal(["1423049311"], result.Games.Select(g => g.SourceId));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_entry_without_exe_and_info_is_reported_and_others_delivered()
    {
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, exe: null, workingDir: null);
        _fs.AddFile(ShowgunnersDir + @"\unins000.exe", "MZ");
        AddCyberpunk();

        var result = Scan();

        Assert.Equal(["1423049311"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("GOG-Spiel \"Showgunners\": keine vorhandene Start-EXE", error.Message, StringComparison.Ordinal);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\" + GamesKey + @"\1511366207", error.Path);
    }

    [Fact]
    public void Incomplete_entry_without_name_uses_name_from_info_file()
    {
        AddCyberpunk();
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\1423049311", "gameName", "", RegistryViewKind.Registry32);

        Assert.Equal("Cyberpunk 2077", Assert.Single(Scan().Games).Name);
    }

    [Fact]
    public void Incomplete_entry_without_name_and_info_is_reported()
    {
        AddGalaxyGame("1511366207", "", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Equal("GOG-Registry-Eintrag unvollständig: Wert gameName fehlt.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void Incomplete_working_dir_that_does_not_exist_is_left_empty()
    {
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir + @"\Fehlt");
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");

        Assert.Null(Assert.Single(Scan().Games).LaunchWorkingDirectory);
    }

    // ---- Defekte Einträge und Dateien ----

    [Fact]
    public void Broken_info_file_is_reported_but_game_delivered()
    {
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");
        _fs.AddFixture(@"Gog\goggame-truncated.info", ShowgunnersDir + @"\goggame-1511366207.info");

        var result = Scan();

        var game = Assert.Single(result.Games);
        Assert.Equal([ShowgunnersDir + @"\Showgunners.exe"], game.Executables);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("GOG-Spielinfo defekt: Kein gültiges JSON", error.Message, StringComparison.Ordinal);
        Assert.Equal(ShowgunnersDir + @"\goggame-1511366207.info", error.Path);
    }

    [Fact]
    public void Broken_info_file_with_binary_garbage_or_unreadable_is_reported()
    {
        var garbage = new byte[512];
        new Random(815).NextBytes(garbage);
        AddCyberpunk();
        _fs.AddFile(CyberpunkDir + @"\goggame-1423049311.info", garbage);
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");
        _fs.AddFile(ShowgunnersDir + @"\goggame-1511366207.info", "{}");
        _fs.FailReading(ShowgunnersDir + @"\goggame-1511366207.info", new UnauthorizedAccessException("Zugriff verweigert"));

        var result = Scan();

        Assert.Equal(2, result.Games.Count);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Message.StartsWith("GOG-Spielinfo defekt:", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Message.StartsWith("GOG-Spielinfo nicht lesbar: UnauthorizedAccessException", StringComparison.Ordinal));
    }

    [Fact]
    public void Broken_registry_entry_without_path_is_reported_and_others_delivered()
    {
        AddGalaxyGame("1511366207", "Showgunners", "", ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        AddCyberpunk();

        var result = Scan();

        Assert.Equal(["1423049311"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Equal("GOG-Registry-Eintrag unvollständig: Wert path fehlt.", error.Message);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\" + GamesKey + @"\1511366207", error.Path);
    }

    [Fact]
    public void Broken_unreadable_registry_key_is_reported_and_others_delivered()
    {
        _registry.AddKey(RegistryHiveKind.LocalMachine, GamesKey + @"\1511366207", RegistryViewKind.Registry32);
        _registry.DenyAccess(RegistryHiveKind.LocalMachine, GamesKey + @"\1511366207", RegistryViewKind.Registry32);
        AddCyberpunk();

        var result = Scan();

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Equal("GOG-Registry-Eintrag nicht lesbar.", error.Message);
        Assert.EndsWith(@"\1511366207", error.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_info_file_with_lone_surrogate_escape_is_reported_but_game_delivered()
    {
        // "\uD800" ist syntaktisch gültiges JSON; System.Text.Json wirft erst beim Dekodieren (InvalidOperationException).
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");
        _fs.AddFile(ShowgunnersDir + @"\goggame-1511366207.info",
            "{\"gameId\": \"1511366207\", \"rootGameId\": \"1511366207\", \"name\": \"Show\\uD800gunners\", \"playTasks\": []}");
        AddCyberpunk();

        var result = Scan();

        Assert.Equal(["1423049311", "1511366207"], result.Games.Select(g => g.SourceId).Order(StringComparer.Ordinal));
        var error = Assert.Single(result.Errors);
        Assert.Equal("GOG-Spielinfo defekt: Feld \"name\" enthält eine ungültige Escape-Folge (einzelnes UTF-16-Surrogat).", error.Message);
        Assert.Equal(ShowgunnersDir + @"\goggame-1511366207.info", error.Path);
    }

    [Fact]
    public void Broken_info_file_in_wrong_encoding_is_reported_but_game_delivered()
    {
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");
        _fs.AddFile(ShowgunnersDir + @"\goggame-1511366207.info",
            Encoding.Latin1.GetBytes("{\"gameId\": \"1511366207\", \"rootGameId\": \"1511366207\", \"name\": \"Showgunners Ä\", \"playTasks\": []}"));

        var result = Scan();

        Assert.Equal("Showgunners", Assert.Single(result.Games).Name);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("GOG-Spielinfo defekt: Ungültige Zeichenkodierung", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_dependsOn_written_as_dword_zero_is_not_dlc()
    {
        // DWORD 0 statt REG_SZ "" liest sich als "0"; das ist keine GOG-ID und darf das Spiel nicht als Zusatzinhalt verstecken.
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\1511366207", "dependsOn", 0, RegistryViewKind.Registry32);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");

        var result = Scan();

        Assert.Equal("1511366207", Assert.Single(result.Games).SourceId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_info_with_root_game_id_zero_empty_or_own_id_is_not_dlc()
    {
        AddGalaxyGame("1511366207", "Showgunners", ShowgunnersDir, ShowgunnersDir + @"\Showgunners.exe", ShowgunnersDir);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");
        _fs.AddFile(ShowgunnersDir + @"\goggame-1511366207.info", "{\"gameId\": 1511366207, \"rootGameId\": 0, \"playTasks\": []}");
        AddLegacyGame("1409964317", "SWAT 4 Gold Edition", Swat4Dir, Swat4Dir + @"\Content\System\swat4.exe", Swat4Dir + @"\Content\System");
        _fs.AddFile(Swat4Dir + @"\Content\System\swat4.exe", "MZ");
        _fs.AddFile(Swat4Dir + @"\goggame-1409964317.info", "{\"gameId\": \"1409964317\", \"rootGameId\": \"\", \"playTasks\": []}");
        AddGalaxyGame("1207658946", "SWAT 3 - Tactical Game of The Year Edition", Swat3Dir, Swat3Dir + @"\SWAT.EXE", Swat3Dir);
        _fs.AddFile(Swat3Dir + @"\swat.exe", "MZ");
        _fs.AddFile(Swat3Dir + @"\goggame-1207658946.info", "{\"rootGameId\": 1207658946}");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["1207658946", "1409964317", "1511366207"], result.Games.Select(g => g.SourceId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Broken_registry_values_with_dword_qword_or_multi_string_types_do_not_throw()
    {
        // gameID als DWORD (Zahl wird zu Text), workingDir als REG_BINARY (ignoriert).
        Set(GamesKey + @"\1511366207", "gameName", "Showgunners");
        Set(GamesKey + @"\1511366207", "path", ShowgunnersDir);
        Set(GamesKey + @"\1511366207", "exe", ShowgunnersDir + @"\Showgunners.exe");
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\1511366207", "gameID", 1511366207, RegistryViewKind.Registry32);
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\1511366207", "workingDir", new byte[] { 0x44 }, RegistryViewKind.Registry32);
        _fs.AddFile(ShowgunnersDir + @"\Showgunners.exe", "MZ");

        // Pfad als QWORD (kein absoluter Pfad), Name als REG_MULTI_SZ.
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\2", "gameName", new[] { "Zeile 1", "Zeile 2" }, RegistryViewKind.Registry32);
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\2", "path", 42L, RegistryViewKind.Registry32);

        // Pfad als REG_MULTI_SZ (kein Text).
        Set(GamesKey + @"\3", "gameName", "Mehrzeilig");
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\3", "path", new[] { @"D:\GOG-Spiele\A", @"D:\GOG-Spiele\B" }, RegistryViewKind.Registry32);

        var result = Scan();

        var game = Assert.Single(result.Games);
        Assert.Equal("1511366207", game.SourceId);
        Assert.Null(game.LaunchWorkingDirectory);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Message.Contains("\"42\" ist kein absoluter Pfad", StringComparison.Ordinal) && e.Path!.EndsWith(@"\2", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Message == "GOG-Registry-Eintrag unvollständig: Wert path fehlt." && e.Path!.EndsWith(@"\3", StringComparison.Ordinal));
    }

    [Fact]
    public void Paths_with_umlauts_special_characters_forward_slashes_and_missing_drive_are_handled()
    {
        const string dir = @"E:\Spiele\Größe & Co. (Ü-Edition) #1";
        AddGalaxyGame("1000000002", "Größe & Co.™", "e:/spiele/GRÖßE & co. (ü-edition) #1/", dir.Replace('\\', '/') + "/bin/Spiel.exe", dir + @"\bin\");
        _fs.AddFile(dir + @"\bin\spiel.EXE", "MZ");
        AddGalaxyGame("1000000003", "Fehlendes Laufwerk", @"Q:\GOG\Weg", @"Q:\GOG\Weg\Weg.exe", @"Q:\GOG\Weg");

        var result = Scan();

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal("Größe & Co.™", game.Name);
        Assert.Equal(@"e:\spiele\GRÖßE & co. (ü-edition) #1", game.InstallDir);
        Assert.Equal(dir + @"\bin\Spiel.exe", game.LaunchCommand);
        Assert.Equal(dir + @"\bin", game.LaunchWorkingDirectory);
        Assert.Equal([dir + @"\bin\Spiel.exe"], game.Executables);
    }

    [Fact]
    public void Broken_value_types_and_drive_root_paths_are_reported()
    {
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\1", "gameName", "Binärpfad", RegistryViewKind.Registry32);
        _registry.Set(RegistryHiveKind.LocalMachine, GamesKey + @"\1", "path", new byte[] { 0x44, 0x3A }, RegistryViewKind.Registry32);
        AddGalaxyGame("2", "Wurzel", @"D:\", @"D:\Spiel.exe", @"D:\");
        _fs.AddFile(@"D:\Spiel.exe", "MZ");

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Message == "GOG-Registry-Eintrag unvollständig: Wert path fehlt.");
        Assert.Contains(result.Errors, e => e.Message.Contains("Laufwerkswurzel", StringComparison.Ordinal));
    }

    // ---- Rahmen ----

    [Fact]
    public void Cancellation_is_honored()
    {
        AddCyberpunk();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new GogScanner(_fs, _registry, _env).Scan(ScanOptions.Default, cts.Token));
    }

    [Fact]
    public void Scanner_is_found_by_catalog()
    {
        Assert.Contains(typeof(GogScanner), ScannerCatalog.ScannerTypes);
        var scanner = Assert.Single(ScannerCatalog.CreateAll(_fs, _registry, _env), s => s.Source == GameSource.Gog);
        Assert.Equal("GOG Galaxy", scanner.Name);
    }

    // ---- Hilfen ----

    private ScanResult Scan()
    {
        _lastRegistry = new ReadOnlyRegistry(_registry);
        return new GogScanner(new ReadOnlyFileSystem(_fs), _lastRegistry, _env).Scan(ScanOptions.Default, Ct);
    }

    private void AddGalaxyClient()
    {
        _registry.Set(RegistryHiveKind.LocalMachine, @"Software\GOG.com\GalaxyClient", "clientExecutable", "GalaxyClient.exe", RegistryViewKind.Registry32);
        _registry.Set(RegistryHiveKind.LocalMachine, @"Software\GOG.com\GalaxyClient\paths", "client", GalaxyDir, RegistryViewKind.Registry32);
    }

    /// <summary>Eintrag, wie GOG Galaxy ihn schreibt (Wertnamen in camelCase, gemessen).</summary>
    private void AddGalaxyGame(string id, string name, string path, string? exe, string? workingDir, string launchParam = "", string dependsOn = "")
    {
        var key = GamesKey + @"\" + id;
        Set(key, "gameID", id);
        Set(key, "productID", id);
        Set(key, "gameName", name);
        Set(key, "path", path);
        Set(key, "uninstallCommand", path.TrimEnd('\\', '/') + @"\unins000.exe");
        Set(key, "dependsOn", dependsOn);
        Set(key, "DLC", "");
        Set(key, "INSTALLDATE", "2024-10-19 20:29:38");
        if (exe is not null)
        {
            Set(key, "exe", exe);
            Set(key, "exeFile", Path.GetFileName(exe));
            Set(key, "launchCommand", exe + (launchParam.Length > 0 ? " " + launchParam : string.Empty));
            Set(key, "launchParam", launchParam);
        }

        if (workingDir is not null)
        {
            Set(key, "workingDir", workingDir);
        }
    }

    /// <summary>Eintrag eines älteren Offline-Installers (Wertnamen in Großbuchstaben, gemessen).</summary>
    private void AddLegacyGame(string id, string name, string path, string exe, string workingDir)
    {
        var key = GamesKey + @"\" + id;
        Set(key, "PATH", path);
        Set(key, "GUID", "{00000000-0000-0000-0000-000000000000}");
        Set(key, "gameID", id);
        Set(key, "GAMENAME", name);
        Set(key, "DEPENDSON", "");
        Set(key, "DLC", "");
        Set(key, "LAUNCHPARAM", "");
        Set(key, "LAUNCHCOMMAND", exe.ToLowerInvariant());
        Set(key, "EXE", exe);
        Set(key, "EXEFILE", Path.GetFileName(exe));
        Set(key, "WORKINGDIR", workingDir);
    }

    private void Set(string key, string name, string value) =>
        _registry.Set(RegistryHiveKind.LocalMachine, key, name, value, RegistryViewKind.Registry32);

    private void AddCyberpunk()
    {
        AddGalaxyGame("1423049311", "Cyberpunk 2077", CyberpunkDir, CyberpunkDir + @"\REDprelauncher.exe", CyberpunkDir);
        Set(GamesKey + @"\1423049311", "DLC", "1256837418");
        _fs.AddFile(CyberpunkDir + @"\REDprelauncher.exe", "MZ");
        _fs.AddFile(CyberpunkDir + @"\bin\x64\Cyberpunk2077.exe", "MZ");
        _fs.AddFixture(@"Gog\goggame-1423049311.info", CyberpunkDir + @"\goggame-1423049311.info");
    }

    private void AddPhantomLiberty(bool withDependsOn, bool withInfo = true)
    {
        // Gemessen: kein exe/workingDir, gleicher Ordner wie das Hauptspiel, eigene Spielinfo mit rootGameId des Hauptspiels.
        AddGalaxyGame("1256837418", "Cyberpunk 2077: Phantom Liberty", CyberpunkDir, exe: null, workingDir: null,
            dependsOn: withDependsOn ? "1423049311" : "");
        if (withInfo)
        {
            _fs.AddFixture(@"Gog\goggame-1256837418.info", CyberpunkDir + @"\goggame-1256837418.info");
        }
    }

    private void AddSwat3()
    {
        AddLegacyGame("1207658946", "SWAT 3 - Tactical Game of The Year Edition", Swat3Dir, Swat3Dir + @"\SWAT.EXE", Swat3Dir + @"\");
        _fs.AddFile(Swat3Dir + @"\swat.exe", "MZ");
        _fs.AddFixture(@"Gog\goggame-1207658946.info", Swat3Dir + @"\goggame-1207658946.info");
    }

    private void AddEinsFiles()
    {
        _fs.AddFile(EinsDir + @"\Launcher\Starter.exe", "MZ");
        _fs.AddFile(EinsDir + @"\bin\eins.exe", "MZ");
        _fs.AddFile(EinsDir + @"\bin\eins_dx11.exe", "MZ");
        _fs.AddFixture(@"Gog\goggame-1999999901.info", EinsDir + @"\goggame-1999999901.info");
    }
}
