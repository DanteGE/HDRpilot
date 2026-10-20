using System.Text;
using System.Text.Json.Nodes;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

/// <summary>
/// Tests des Epic-Scanners gegen Beispielmanifeste im Format des Zielrechners (Fixtures\Epic, anonymisiert)
/// und gegen eine Registry-Attrappe. Alle Zugriffe laufen über Lese-Hüllen, die jeden Schreibversuch melden.
/// </summary>
public sealed class EpicScannerTests
{
    private const string Manifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
    private const string EpicGames = @"D:\Anwendungen\Epic Games";
    private const string Rdr2Dir = EpicGames + @"\RedDeadRedemption2";
    private const string UnrailedDir = EpicGames + @"\Unrailed";
    private const string WitchfireDir = EpicGames + @"\WitchfireRjTMw";
    private const string LegoDir = EpicGames + @"\LEGOBuildersJourney";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeEnvironment _env = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Fehlender Launcher und Fundort ----

    [Fact]
    public void Missing_launcher_returns_empty_without_errors()
    {
        var result = Scan();

        Assert.Equal(GameSource.Epic, result.Source);
        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_manifest_folder_detects_launcher_without_games_or_errors()
    {
        _fs.AddDirectory(Manifests);

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Missing_manifest_folder_falls_back_to_ModSdkMetadataDir_with_forward_slashes()
    {
        // Gemessen: der Wert steht mit Schrägstrichen in der Registry.
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Epic Games\EOS", "ModSdkMetadataDir", "E:/EpicDaten/Data/Manifests");
        _fs.AddFixture(@"Epic\rdr2.item", @"E:\EpicDaten\Data\Manifests\11111111111111111111111111111111.item");
        AddRdr2Files();

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        Assert.Equal("Heather", Assert.Single(result.Games).SourceId);
    }

    [Fact]
    public void Default_manifest_folder_takes_precedence_over_registry()
    {
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Epic Games\EOS", "ModSdkMetadataDir", "E:/Anderswo/Manifests");
        _fs.AddFixture(@"Epic\unrailed.item", @"E:\Anderswo\Manifests\33333333333333333333333333333333.item");
        AddUnrailedFiles();
        AddManifest("rdr2.item");
        AddRdr2Files();

        var result = Scan();

        Assert.Equal(["Heather"], result.Games.Select(g => g.SourceId));
    }

    [Fact]
    public void Registry_pointing_to_missing_folder_detects_launcher_without_errors()
    {
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Epic Games\EOS", "ModSdkMetadataDir", "C:/ProgramData/Epic/EpicGamesLauncher/Data/Manifests");

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ProgramData_folder_comes_from_environment()
    {
        _env.Folders[Environment.SpecialFolder.CommonApplicationData] = @"E:\PD";
        _fs.AddFixture(@"Epic\rdr2.item", @"E:\PD\Epic\EpicGamesLauncher\Data\Manifests\a.item");
        AddRdr2Files();

        Assert.Equal("Heather", Assert.Single(Scan().Games).SourceId);
    }

    [Fact]
    public void Only_top_level_item_files_are_read()
    {
        _fs.AddFixture(@"Epic\rdr2.item", Manifests + @"\Pending\11111111111111111111111111111111.item");
        _fs.AddFixture(@"Epic\rdr2.item", Manifests + @"\11111111111111111111111111111111.item.bak");
        _fs.AddFixture(@"Epic\rdr2.item", Manifests + @"\11111111111111111111111111111111.json");
        AddRdr2Files();

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    // ---- Positive Fälle mit Beispieldateien im echten Format ----

    [Fact]
    public void Real_format_manifests_yield_all_games()
    {
        AddManifest("rdr2.item");
        AddManifest("unrailed.item");
        AddManifest("witchfire.item");
        AddManifest("lego.item");
        AddRdr2Files();
        AddUnrailedFiles();
        AddWitchfireFiles();
        _fs.AddFile(LegoDir + @"\Builder's Journey.exe", "MZ");

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        Assert.Equal(
            ["LEGO® Builder's Journey", "Red Dead Redemption 2", "Unrailed", "Witchfire"],
            result.Games.Select(g => g.Name).Order(StringComparer.Ordinal));
        Assert.All(result.Games, g => Assert.Equal(GameSource.Epic, g.Source));
    }

    [Fact]
    public void Launch_command_is_exact_launcher_uri()
    {
        AddManifest("rdr2.item");
        AddRdr2Files();

        var game = Assert.Single(Scan().Games);

        Assert.Equal(
            "com.epicgames.launcher://apps/b30b6d1b4dfd4dcc93b5490be5e094e5%3A22a7b503221442daa2fb16ad37b6ccbf%3AHeather?action=launch&silent=true",
            game.LaunchCommand);
        Assert.Null(game.LaunchArguments);
        Assert.Null(game.LaunchWorkingDirectory);
        Assert.Equal("Heather", game.SourceId);
        Assert.Equal("Epic:Heather", game.Key);
        Assert.Equal("Red Dead Redemption 2", game.Name);
        Assert.Equal(Rdr2Dir, game.InstallDir);
    }

    [Fact]
    public void Install_location_with_forward_slash_is_normalized()
    {
        // Gemessen: "D:\\Anwendungen\\Epic Games/WitchfireRjTMw" (gemischte Trenner).
        AddManifest("witchfire.item");
        AddWitchfireFiles();

        var game = Assert.Single(Scan().Games);

        Assert.Equal(WitchfireDir, game.InstallDir);
        Assert.Equal([WitchfireDir + @"\Witchfire.exe"], game.Executables);
        Assert.Null(game.LaunchArguments); // Das Manifest-Feld LaunchCommand (Spielargumente) wird bei URI-Start nicht genutzt.
    }

    [Fact]
    public void Launch_executable_in_subfolder_with_forward_slash_is_resolved()
    {
        AddManifest("unrailed.item");
        AddUnrailedFiles();

        var game = Assert.Single(Scan().Games);

        Assert.Equal(UnrailedDir, game.InstallDir);
        Assert.Equal([UnrailedDir + @"\newRoot\UnrailedGameEpic.exe"], game.Executables);
    }

    [Fact]
    public void Main_window_process_is_added_to_executables()
    {
        AddManifest("rdr2.item");
        AddRdr2Files();

        var game = Assert.Single(Scan().Games);

        Assert.Equal([Rdr2Dir + @"\PlayRDR2.exe", Rdr2Dir + @"\RDR2.exe"], game.Executables);
    }

    [Fact]
    public void Process_names_are_added_to_executables_when_they_exist()
    {
        AddManifestJson("x.item", Mutate("unrailed.item", m => m["ProcessNames"] = new JsonArray("newRoot/Helper.exe", "Fehlt.exe")));
        AddUnrailedFiles();
        _fs.AddFile(UnrailedDir + @"\newRoot\Helper.exe", "MZ");

        var game = Assert.Single(Scan().Games);

        Assert.Equal([UnrailedDir + @"\newRoot\UnrailedGameEpic.exe", UnrailedDir + @"\newRoot\Helper.exe"], game.Executables);
    }

    [Fact]
    public void Executables_only_list_existing_files()
    {
        AddManifest("rdr2.item");
        _fs.AddFile(Rdr2Dir + @"\PlayRDR2.exe", "MZ"); // RDR2.exe fehlt

        var game = Assert.Single(Scan().Games);

        Assert.Equal([Rdr2Dir + @"\PlayRDR2.exe"], game.Executables);
    }

    [Fact]
    public void Executables_outside_install_dir_or_without_exe_extension_are_ignored()
    {
        AddManifestJson("x.item", Mutate("rdr2.item", m =>
        {
            m["LaunchExecutable"] = "../Anderes/Fremd.exe";
            m["MainWindowProcessName"] = "readme.txt";
        }));
        _fs.AddFile(EpicGames + @"\Anderes\Fremd.exe", "MZ");
        _fs.AddFile(Rdr2Dir + @"\readme.txt", "x");

        var game = Assert.Single(Scan().Games);

        Assert.Empty(game.Executables);
    }

    [Fact]
    public void Path_case_and_trailing_separator_differences_are_tolerated()
    {
        AddManifestJson("x.item", Mutate("rdr2.item", m => m["InstallLocation"] = @"d:\anwendungen\epic games\REDDEADREDEMPTION2\"));
        AddRdr2Files();

        var game = Assert.Single(Scan().Games);

        Assert.Equal(@"d:\anwendungen\epic games\REDDEADREDEMPTION2", game.InstallDir);
        Assert.Equal(2, game.Executables.Count);
    }

    [Fact]
    public void Manifest_with_utf8_bom_is_read()
    {
        AddManifestBytes("x.item", [0xEF, 0xBB, 0xBF, .. FixtureBytes("lego.item")]);
        _fs.AddFile(LegoDir + @"\Builder's Journey.exe", "MZ");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal("LEGO® Builder's Journey", Assert.Single(result.Games).Name);
    }

    [Fact]
    public void Manifest_in_utf16_with_bom_is_read()
    {
        AddManifestBytes("x.item", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(FixtureText("lego.item"))]);
        _fs.AddFile(LegoDir + @"\Builder's Journey.exe", "MZ");

        Assert.Equal("LEGO® Builder's Journey", Assert.Single(Scan().Games).Name);
    }

    [Fact]
    public void Duplicate_app_name_yields_one_entry()
    {
        AddManifest("rdr2.item", "A.item");
        AddManifest("rdr2.item", "B.item");
        AddRdr2Files();

        var result = Scan();

        Assert.Single(result.Games);
        Assert.Empty(result.Errors);
    }

    // ---- Filterregeln ----

    [Fact]
    public void Dlc_manifest_with_different_MainGameAppName_is_skipped()
    {
        AddManifest("witchfire.item");
        AddManifest("dlc.item");
        AddWitchfireFiles();

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["8764f82381f5436f99e97172df06af35"], result.Games.Select(g => g.SourceId));
    }

    [Fact]
    public void MainGameAppName_differing_only_in_case_is_not_dlc()
    {
        AddManifestJson("x.item", Mutate("rdr2.item", m => m["MainGameAppName"] = "HEATHER"));
        AddRdr2Files();

        Assert.Equal("Heather", Assert.Single(Scan().Games).SourceId);
    }

    [Fact]
    public void Manifest_without_MainGameAppName_is_treated_as_main_game()
    {
        AddManifestJson("x.item", Mutate("rdr2.item", m => m.Remove("MainGameAppName")));
        AddRdr2Files();

        Assert.Single(Scan().Games);
    }

    [Fact]
    public void Engine_manifest_is_skipped()
    {
        AddManifest("unreal-engine.item");
        _fs.AddFile(@"C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\Win64\UnrealEditor.exe", "MZ");

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Plugin_and_non_executable_manifests_are_skipped()
    {
        // "games" steht mit drin, damit wirklich die Plugin-Regel greift und nicht nur die fehlende Spielkategorie.
        AddManifestJson("plugin.item", Mutate("unrailed.item", m => m["AppCategories"] = new JsonArray("plugins", "games")));
        AddManifestJson("nicht-ausfuehrbar.item", Mutate("rdr2.item", m => m["bIsExecutable"] = false));
        AddUnrailedFiles();
        AddRdr2Files();

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Manifest_without_games_category_is_skipped_but_without_categories_is_kept()
    {
        AddManifestJson("app.item", Mutate("unrailed.item", m => m["AppCategories"] = new JsonArray("applications")));
        AddManifestJson("alt.item", Mutate("rdr2.item", m => m.Remove("AppCategories")));
        AddUnrailedFiles();
        AddRdr2Files();

        var result = Scan();

        Assert.Equal(["Heather"], result.Games.Select(g => g.SourceId));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Category_names_are_compared_without_case()
    {
        AddManifestJson("x.item", Mutate("rdr2.item", m => m["AppCategories"] = new JsonArray("Public", "GAMES", "Applications")));
        AddManifestJson("y.item", Mutate("unrailed.item", m => m["AppCategories"] = new JsonArray("Engines", "games")));
        AddRdr2Files();
        AddUnrailedFiles();

        Assert.Equal(["Heather"], Scan().Games.Select(g => g.SourceId));
    }

    // ---- Unvollständige Installationen ----

    [Fact]
    public void Incomplete_install_is_skipped_without_error()
    {
        AddManifest("incomplete.item");
        AddManifest("rdr2.item");
        _fs.AddFile(EpicGames + @"\RingofPain\Ring Of Pain.exe", "MZ");
        AddRdr2Files();

        var result = Scan();

        Assert.Equal(["Heather"], result.Games.Select(g => g.SourceId));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_missing_game_folder_is_skipped_without_error()
    {
        AddManifest("rdr2.item");
        AddManifest("unrailed.item");
        AddUnrailedFiles(); // Ordner von RDR2 fehlt (etwa Laufwerk abgezogen)

        var result = Scan();

        Assert.Equal(["Unrailed"], result.Games.Select(g => g.Name));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_manifest_without_launch_executable_has_no_executables()
    {
        AddManifestJson("x.item", Mutate("unrailed.item", m => m.Remove("LaunchExecutable")));
        AddUnrailedFiles();

        var game = Assert.Single(Scan().Games);

        Assert.Empty(game.Executables);
        Assert.StartsWith("com.epicgames.launcher://apps/", game.LaunchCommand, StringComparison.Ordinal);
    }

    // ---- Defekte Dateien ----

    [Fact]
    public void Broken_truncated_manifest_is_reported_and_others_delivered()
    {
        AddManifest("truncated.item", "AAAA.item");
        AddManifest("rdr2.item");
        AddRdr2Files();

        var result = Scan();

        Assert.Equal(["Heather"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Equal(Manifests + @"\AAAA.item", error.Path);
        Assert.StartsWith("Epic-Manifest defekt: Kein gültiges JSON", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_binary_garbage_is_reported_and_others_delivered()
    {
        var garbage = new byte[2048];
        new Random(4711).NextBytes(garbage);
        AddManifestBytes("Muell.item", garbage);
        AddManifest("rdr2.item");
        AddRdr2Files();

        var result = Scan();

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Equal(Manifests + @"\Muell.item", error.Path);
        Assert.StartsWith("Epic-Manifest defekt:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_wrong_encoding_is_reported_and_others_delivered()
    {
        // Latin-1 statt UTF-8: das ® wird zum einzelnen Byte 0xAE, das in UTF-8 ungültig ist.
        AddManifestBytes("Latin1.item", Encoding.Latin1.GetBytes(FixtureText("lego.item")));
        AddManifest("rdr2.item");
        AddRdr2Files();
        _fs.AddFile(LegoDir + @"\Builder's Journey.exe", "MZ");

        var result = Scan();

        Assert.Equal(["Heather"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Contains("Zeichenkodierung", error.Message, StringComparison.Ordinal);
        Assert.Equal(Manifests + @"\Latin1.item", error.Path);
    }

    [Fact]
    public void Broken_missing_required_fields_are_reported_and_others_delivered()
    {
        AddManifestJson("Felder.item", Mutate("unrailed.item", m =>
        {
            m.Remove("CatalogItemId");
            m.Remove("InstallLocation");
        }));
        AddManifest("rdr2.item");
        AddUnrailedFiles();
        AddRdr2Files();

        var result = Scan();

        Assert.Equal(["Heather"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Equal("Epic-Manifest unvollständig: Pflichtfeld(er) InstallLocation, CatalogItemId fehlen.", error.Message);
        Assert.Equal(Manifests + @"\Felder.item", error.Path);
    }

    [Fact]
    public void Broken_missing_app_name_or_wrong_field_type_is_reported()
    {
        AddManifestJson("OhneAppName.item", Mutate("rdr2.item", m => m.Remove("AppName")));
        AddManifestJson("Zahl.item", Mutate("unrailed.item", m => m["DisplayName"] = 42));
        AddRdr2Files();
        AddUnrailedFiles();

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Message.Contains("AppName", StringComparison.Ordinal) && e.Path!.EndsWith("OhneAppName.item", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Message.Contains("DisplayName", StringComparison.Ordinal) && e.Path!.EndsWith("Zahl.item", StringComparison.Ordinal));
    }

    [Fact]
    public void Broken_unreadable_manifest_is_reported_and_others_delivered()
    {
        AddManifest("unrailed.item", "Gesperrt.item");
        _fs.FailReading(Manifests + @"\Gesperrt.item", new IOException("Die Datei wird von einem anderen Prozess verwendet."));
        AddManifest("rdr2.item");
        AddRdr2Files();

        var result = Scan();

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Epic-Manifest nicht lesbar: IOException", error.Message, StringComparison.Ordinal);
        Assert.Equal(Manifests + @"\Gesperrt.item", error.Path);
    }

    [Fact]
    public void Broken_empty_file_and_non_object_root_are_reported()
    {
        AddManifestBytes("Leer.item", []);
        AddManifestJson("Array.item", "[{\"AppName\": \"Heather\"}]");

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Message.Contains("JSON-Wurzel ist kein Objekt", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Message.Contains("Kein gültiges JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void Broken_lone_surrogate_escape_in_value_is_reported_and_others_delivered()
    {
        // "\uD800" ist syntaktisch gültiges JSON; System.Text.Json wirft erst beim Dekodieren des Werts
        // (InvalidOperationException). Das darf den Scan nicht abbrechen.
        AddManifestJson("Surrogat.item", FixtureText("unrailed.item").Replace("\"Unrailed\"", "\"Unrailed\\uD800\"", StringComparison.Ordinal));
        AddManifest("rdr2.item");
        AddUnrailedFiles();
        AddRdr2Files();

        var result = Scan();

        Assert.Equal(["Heather"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Equal(Manifests + @"\Surrogat.item", error.Path);
        Assert.Equal("Epic-Manifest defekt: Feld \"DisplayName\" enthält eine ungültige Escape-Folge (einzelnes UTF-16-Surrogat).", error.Message);
    }

    [Fact]
    public void Broken_lone_surrogate_escape_in_unused_property_name_is_tolerated()
    {
        // Fehlt ein Feld (hier ProcessNames), sucht der Leser alle Namen ohne Groß-/Kleinschreibung ab und stieß dabei
        // auf den nicht dekodierbaren Namen. Ein solcher Name kann keines der gesuchten Felder sein.
        var json = FixtureText("unrailed.item")
            .Replace("\"FormatVersion\"", "\"Format\\uDC00Version\"", StringComparison.Ordinal)
            .Replace("\"ProcessNames\": [],", string.Empty, StringComparison.Ordinal);
        AddManifestJson("x.item", json);
        AddUnrailedFiles();

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal("76233c0af0a441c7bd541e7d7ab3cde1", Assert.Single(result.Games).SourceId);
    }

    [Fact]
    public void Broken_registry_value_with_wrong_type_does_not_throw()
    {
        // DWORD statt REG_SZ ergibt "5" (kein absoluter Pfad), REG_BINARY ergibt keinen Text.
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Epic Games\EOS", "ModSdkMetadataDir", 5);
        var dword = Scan();
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Epic Games\EOS", "ModSdkMetadataDir", new byte[] { 0x43, 0x3A });
        var binary = Scan();

        Assert.Empty(dword.Games);
        Assert.Empty(dword.Errors);
        Assert.Empty(binary.Games);
        Assert.Empty(binary.Errors);
        Assert.False(binary.LauncherDetected);
    }

    [Fact]
    public void Paths_with_umlauts_special_characters_and_missing_drive_are_handled()
    {
        AddManifestJson("x.item", Mutate("unrailed.item", m =>
        {
            m["InstallLocation"] = "E:/Spiele/Größe & Co. (Ü-Edition) #1/";
            m["LaunchExecutable"] = "bin/Spiel ä.exe";
            m["DisplayName"] = "Größe & Co.™";
        }));
        AddManifestJson("y.item", Mutate("rdr2.item", m => m["InstallLocation"] = @"Q:\Epic\RedDeadRedemption2")); // Laufwerk Q: fehlt
        _fs.AddFile(@"E:\Spiele\größe & co. (ü-edition) #1\BIN\spiel Ä.EXE", "MZ");

        var result = Scan();

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal("Größe & Co.™", game.Name);
        Assert.Equal(@"E:\Spiele\Größe & Co. (Ü-Edition) #1", game.InstallDir);
        Assert.Equal([@"E:\Spiele\Größe & Co. (Ü-Edition) #1\bin\Spiel ä.exe"], game.Executables);
    }

    [Fact]
    public void Broken_install_location_drive_root_or_relative_is_reported()
    {
        AddManifestJson("Wurzel.item", Mutate("rdr2.item", m => m["InstallLocation"] = "D:/"));
        AddManifestJson("Relativ.item", Mutate("unrailed.item", m => m["InstallLocation"] = @"Epic Games\Unrailed"));
        _fs.AddFile(@"D:\PlayRDR2.exe", "MZ");

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Contains(result.Errors, e => e.Message.Contains("Laufwerkswurzel", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Message.Contains("kein absoluter Pfad", StringComparison.Ordinal));
    }

    // ---- Rahmen ----

    [Fact]
    public void Cancellation_is_honored()
    {
        AddManifest("rdr2.item");
        AddRdr2Files();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new EpicScanner(_fs, _registry, _env).Scan(ScanOptions.Default, cts.Token));
    }

    [Fact]
    public void Scanner_is_found_by_catalog()
    {
        Assert.Contains(typeof(EpicScanner), ScannerCatalog.ScannerTypes);
        var scanner = Assert.Single(ScannerCatalog.CreateAll(_fs, _registry, _env), s => s.Source == GameSource.Epic);
        Assert.Equal("Epic Games", scanner.Name);
    }

    [Fact]
    public void Launch_uri_escapes_unusual_characters()
    {
        Assert.Equal(
            "com.epicgames.launcher://apps/ns%3Aitem%3AApp%20Name%3F?action=launch&silent=true",
            EpicScanner.BuildLaunchUri("ns", "item", "App Name?"));
    }

    // ---- Hilfen ----

    private ScanResult Scan() =>
        new EpicScanner(new ReadOnlyFileSystem(_fs), new ReadOnlyRegistry(_registry), _env).Scan(ScanOptions.Default, Ct);

    private void AddManifest(string fixture, string? fileName = null) =>
        _fs.AddFixture(@"Epic\" + fixture, Manifests + @"\" + (fileName ?? Path.GetFileNameWithoutExtension(fixture) + ".item"));

    private void AddManifestJson(string fileName, string json) => _fs.AddFile(Manifests + @"\" + fileName, json);

    private void AddManifestBytes(string fileName, byte[] bytes) => _fs.AddFile(Manifests + @"\" + fileName, bytes);

    private void AddRdr2Files()
    {
        _fs.AddFile(Rdr2Dir + @"\PlayRDR2.exe", "MZ");
        _fs.AddFile(Rdr2Dir + @"\RDR2.exe", "MZ");
    }

    private void AddUnrailedFiles() => _fs.AddFile(UnrailedDir + @"\newRoot\UnrailedGameEpic.exe", "MZ");

    private void AddWitchfireFiles() => _fs.AddFile(WitchfireDir + @"\Witchfire.exe", "MZ");

    private static byte[] FixtureBytes(string fixture) => File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, "Epic", fixture));

    private static string FixtureText(string fixture) => File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Epic", fixture));

    private static string Mutate(string fixture, Action<JsonObject> change)
    {
        var json = JsonNode.Parse(FixtureText(fixture))!.AsObject();
        change(json);
        return json.ToJsonString();
    }
}
