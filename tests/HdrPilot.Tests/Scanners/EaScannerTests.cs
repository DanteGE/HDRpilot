using System.Text;
using System.Text.RegularExpressions;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Core.Games.Scanners.Ea;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Scanners;

public sealed class EaScannerTests
{
    private const RegistryHiveKind Hklm = RegistryHiveKind.LocalMachine;
    private const string Fixture = @"EA\installerdata-ew6.xml";
    private const string GameDir = @"C:\Program Files\EA Games\Eisenwall 6";
    private const string UninstallKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{0f1e2d3c-4b5a-4697-8877-665544332211}";
    private const string ExpectedLaunch = "origin2://game/launch/?offerIds=18306642&authCode=&cmdParams=";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string FixtureText => File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, Fixture), Encoding.UTF8);

    [Fact]
    public void Missing_launcher_returns_empty_without_errors()
    {
        var result = Scan(new InMemoryFileSystem(), new FakeRegistry());

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
        Assert.Equal(GameSource.EA, result.Source);
        Assert.Equal("EA app", result.ScannerName);
    }

    [Fact]
    public void Finds_game_from_uninstall_entry_with_all_details()
    {
        var fs = AddEw6(new InMemoryFileSystem());
        var registry = new FakeRegistry()
            .Set(Hklm, UninstallKey, "DisplayName", "Eisenwall™ 6")
            .Set(Hklm, UninstallKey, "Publisher", "Electronic Arts")
            .Set(Hklm, UninstallKey, "InstallLocation", GameDir + @"\")
            .Set(Hklm, UninstallKey, "UninstallString", "\"C:\\Program Files\\Common Files\\EAInstaller\\Eisenwall 6\\Cleanup.exe\" uninstall_game -autologging")
            .Set(Hklm, UninstallKey, "DisplayIcon", "\"" + GameDir + "\\ew6.exe\"")
            .Set(Hklm, @"SOFTWARE\EA Games\Eisenwall 6", "Install Dir", GameDir + @"\")
            .Set(Hklm, @"SOFTWARE\WOW6432Node\EA Games\Eisenwall 6", "Install Dir", GameDir + @"\");
        fs.AddDirectory(@"C:\Program Files (x86)\Unbeteiligt"); // ohne installerdata.xml

        var result = Scan(fs, registry);

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal(GameSource.EA, game.Source);
        Assert.Equal("18306642", game.SourceId);
        Assert.Equal("EA:18306642", game.Key);
        Assert.Equal("Eisenwall™ 6", game.Name);
        Assert.Equal(GameDir, game.InstallDir);
        Assert.Equal(
            [GameDir + @"\EAAntiCheat.GameServiceLauncher.exe", GameDir + @"\SP\ew6.exe", GameDir + @"\ew6.exe"],
            game.Executables);
        Assert.Equal(ExpectedLaunch, game.LaunchCommand);
        Assert.Null(game.LaunchArguments);
        Assert.Null(game.LaunchWorkingDirectory);
    }

    [Fact]
    public void Launch_command_is_origin2_uri_with_escaped_content_id()
    {
        Assert.Equal(ExpectedLaunch, EaScanner.CreateLaunchCommand("18306642"));
        Assert.Equal("origin2://game/launch/?offerIds=Origin.OFR.50.0001%26x&authCode=&cmdParams=", EaScanner.CreateLaunchCommand("Origin.OFR.50.0001&x"));
    }

    [Fact]
    public void Title_prefers_de_DE_over_en_US_and_first_entry()
    {
        var xml = FixtureText.Replace("<gameTitle locale=\"de_DE\">Eisenwall™ 6</gameTitle>", "<gameTitle locale=\"de_DE\">Eisenwall 6 (DE)</gameTitle>", StringComparison.Ordinal);
        var game = Assert.Single(Scan(AddEw6(new InMemoryFileSystem(), xml: xml), new FakeRegistry()).Games);
        Assert.Equal("Eisenwall 6 (DE)", game.Name);
    }

    [Fact]
    public void Title_falls_back_to_en_US_then_to_first_entry()
    {
        var withoutDe = Regex.Replace(FixtureText, "<gameTitle locale=\"de_DE\">.*?</gameTitle>", string.Empty);
        var enUs = withoutDe.Replace("<gameTitle locale=\"en_US\">Eisenwall™ 6</gameTitle>", "<gameTitle locale=\"en-us\">Eisenwall 6 (US)</gameTitle>", StringComparison.Ordinal);
        Assert.Equal("Eisenwall 6 (US)", Assert.Single(Scan(AddEw6(new InMemoryFileSystem(), xml: enUs), new FakeRegistry()).Games).Name);

        var neither = Regex.Replace(withoutDe, "<gameTitle locale=\"(en_US|fr_FR)\">.*?</gameTitle>", string.Empty)
            .Replace("<gameTitle locale=\"es_ES\">Eisenwall™ 6</gameTitle>", "<gameTitle locale=\"es_ES\">Eisenwall 6 (ES)</gameTitle>", StringComparison.Ordinal);
        Assert.Equal("Eisenwall 6 (ES)", Assert.Single(Scan(AddEw6(new InMemoryFileSystem(), xml: neither), new FakeRegistry()).Games).Name);
    }

    [Fact]
    public void Finds_game_in_default_EA_Games_folder_without_registry_and_resolves_macro_relative_to_folder()
    {
        var result = Scan(AddEw6(new InMemoryFileSystem(), withRootExe: false), new FakeRegistry());

        var game = Assert.Single(result.Games);
        Assert.Equal(GameDir, game.InstallDir);
        Assert.Equal([GameDir + @"\EAAntiCheat.GameServiceLauncher.exe", GameDir + @"\SP\ew6.exe"], game.Executables);
        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Finds_games_in_origin_games_folder_and_origin_download_dir()
    {
        var fs = new InMemoryFileSystem()
            .AddFixture(@"EA\origin-local.xml", @"C:\Users\Test\AppData\Roaming\Origin\local.xml");
        AddEw6(fs, @"C:\Program Files (x86)\Origin Games\Eisenwall 6");
        AddEw6(fs, @"D:\Programmordner\EA Games\Eisenwall 6 Kopie", xml: FixtureText.Replace("18306642", "70619", StringComparison.Ordinal));

        var result = Scan(fs, new FakeRegistry());

        Assert.Equal(
            [@"C:\Program Files (x86)\Origin Games\Eisenwall 6", @"D:\Programmordner\EA Games\Eisenwall 6 Kopie"],
            result.Games.Select(g => g.InstallDir).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Finds_game_from_EA_Games_key_in_32_bit_view_only()
    {
        const string dir = @"E:\Spiele\EW6";
        var fs = AddEw6(new InMemoryFileSystem(), dir);
        var registry = new FakeRegistry().Set(Hklm, @"SOFTWARE\EA Games\Eisenwall 6", "Install Dir", dir + @"\", RegistryViewKind.Registry32);

        var game = Assert.Single(Scan(fs, registry).Games);

        Assert.Equal(dir, game.InstallDir);
        Assert.Equal([dir + @"\EAAntiCheat.GameServiceLauncher.exe", dir + @"\SP\ew6.exe"], game.Executables);
    }

    [Fact]
    public void Search_folders_are_searched_up_to_depth_three_only()
    {
        var fs = new InMemoryFileSystem();
        AddEw6(fs, @"D:\Spielordner", xml: WithId("1"));                       // Tiefe 0: der Suchordner selbst
        AddEw6(fs, @"E:\Spiele\EA\Shooter\EW", xml: WithId("3"));         // Tiefe 3
        AddEw6(fs, @"E:\Spiele\EA\Shooter\Alt\EW", xml: WithId("4"));     // Tiefe 4: zu tief
        AddEw6(fs, @"D:\Spielordner\Unterordner\EW", xml: WithId("9"));        // unter einem Spielordner: nicht durchsucht

        var result = Scan(fs, new FakeRegistry(), new ScanOptions { SearchFolders = [@"D:\Spielordner", @"E:\Spiele", @"X:\fehlt"] });

        Assert.Equal(["1", "3"], result.Games.Select(g => g.SourceId).Order(StringComparer.Ordinal));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Search_folder_with_forward_slashes_trailing_separator_and_other_case_is_normalized()
    {
        var fs = AddEw6(new InMemoryFileSystem(), @"D:\Games\EA\Eisenwall 6");

        var result = Scan(fs, new FakeRegistry(), new ScanOptions { SearchFolders = ["d:/GAMES/ea/"] });

        var game = Assert.Single(result.Games);
        Assert.Equal(@"D:\Games\EA\Eisenwall 6", game.InstallDir, ignoreCase: true);
        Assert.DoesNotContain("/", game.InstallDir, StringComparison.Ordinal);
        Assert.All(game.Executables, e => Assert.DoesNotContain("/", e, StringComparison.Ordinal));
        Assert.Contains(game.Executables, e => e.EndsWith(@"\SP\ew6.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Same_folder_from_several_sources_is_reported_once()
    {
        var fs = AddEw6(new InMemoryFileSystem());
        var registry = Ew6Uninstall(new FakeRegistry(), GameDir + @"\")
            .Set(Hklm, @"SOFTWARE\EA Games\Eisenwall 6", "Install Dir", "c:/program files/ea games/eisenwall 6/");

        var result = Scan(fs, registry, new ScanOptions { SearchFolders = [@"C:\Program Files"] });

        Assert.Single(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Same_content_id_in_two_folders_is_reported_once_preferring_registry_source()
    {
        var fs = AddEw6(new InMemoryFileSystem());
        AddEw6(fs, @"D:\Sicherung\Eisenwall 6");
        var registry = Ew6Uninstall(new FakeRegistry(), @"D:\Sicherung\Eisenwall 6\");

        var game = Assert.Single(Scan(fs, registry).Games);

        Assert.Equal(@"D:\Sicherung\Eisenwall 6", game.InstallDir);
    }

    [Fact]
    public void Broken_unlistable_search_folder_is_skipped_without_exception()
    {
        var inner = AddEw6(new InMemoryFileSystem()).AddDirectory(@"D:\Gesperrt\Unterordner");
        var fs = new EaBattleNetTestSupport.FailingEnumerationFileSystem(inner, @"D:\Gesperrt", new UnauthorizedAccessException("Zugriff verweigert"));

        var result = new EaScanner(fs, new FakeRegistry(), new FakeEnvironment())
            .Scan(new ScanOptions { SearchFolders = [@"D:\Gesperrt"] }, Ct);

        Assert.Equal("18306642", Assert.Single(result.Games).SourceId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Steam_library_folders_are_ignored()
    {
        var fs = AddEw6(new InMemoryFileSystem(), @"D:\Steam-Bibliothek\steamapps\common\Eisenwall 6");

        var result = Scan(fs, new FakeRegistry(), new ScanOptions { SearchFolders = [@"D:\Steam-Bibliothek\steamapps\common"] });

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Executables_resolve_via_registry_but_only_inside_install_dir()
    {
        // Registry zeigt auf einen alten Ordner, in dem noch EXEs liegen: die gehören nicht zu diesem Eintrag.
        var fs = AddEw6(new InMemoryFileSystem(), withRootExe: false)
            .AddFile(@"D:\Alt\Eisenwall 6\EAAntiCheat.GameServiceLauncher.exe", "MZ")
            .AddFile(@"D:\Alt\Eisenwall 6\SP\ew6.exe", "MZ");
        var registry = new FakeRegistry().Set(Hklm, @"SOFTWARE\EA Games\Eisenwall 6", "Install Dir", @"D:\Alt\Eisenwall 6\");

        var game = Assert.Single(Scan(fs, registry).Games);

        Assert.Equal([GameDir + @"\EAAntiCheat.GameServiceLauncher.exe", GameDir + @"\SP\ew6.exe"], game.Executables);
    }

    [Fact]
    public void Launcher_paths_without_macro_are_resolved_absolute_or_relative()
    {
        var xml = FixtureText
            .Replace(@"[HKEY_LOCAL_MACHINE\SOFTWARE\EA Games\Eisenwall 6\Install Dir]EAAntiCheat.GameServiceLauncher.exe", GameDir + @"\Game.EXE", StringComparison.Ordinal)
            .Replace(@"[HKEY_LOCAL_MACHINE\SOFTWARE\EA Games\Eisenwall 6\Install Dir]SP/ew6.exe", "/Bin/Tool.exe", StringComparison.Ordinal);
        var fs = AddEw6(new InMemoryFileSystem(), xml: xml, withRootExe: false)
            .AddFile(GameDir + @"\Game.EXE", "MZ")
            .AddFile(GameDir + @"\Bin\Tool.exe", "MZ");

        var game = Assert.Single(Scan(fs, new FakeRegistry()).Games);

        Assert.Equal([GameDir + @"\Game.EXE", GameDir + @"\Bin\Tool.exe"], game.Executables);
    }

    [Fact]
    public void Registry_macro_parser_reads_hive_key_value_and_rest()
    {
        Assert.True(EaRegistryMacro.TryParse(@"[HKEY_LOCAL_MACHINE\SOFTWARE\EA Games\Eisenwall 6\Install Dir]SP/ew6.exe", out var m));
        Assert.Equal(new EaRegistryMacro(Hklm, @"SOFTWARE\EA Games\Eisenwall 6", "Install Dir", "SP/ew6.exe"), m);
        Assert.True(EaRegistryMacro.TryParse(@"[HKCU\Software\X\Path]", out var cu));
        Assert.Equal(RegistryHiveKind.CurrentUser, cu.Hive);
        Assert.Equal(string.Empty, cu.Rest);
        Assert.False(EaRegistryMacro.TryParse(@"[HKEY_CLASSES_ROOT\x\y]a.exe", out _));
        Assert.False(EaRegistryMacro.TryParse(@"[HKEY_LOCAL_MACHINE\NurWert]a.exe", out _));
        Assert.False(EaRegistryMacro.TryParse(@"[HKEY_LOCAL_MACHINE\SOFTWARE\X\Install Dir a.exe", out _));
        Assert.False(EaRegistryMacro.TryParse("ew6.exe", out _));
        Assert.False(EaRegistryMacro.TryParse(null, out _));
    }

    [Fact]
    public void Display_icon_with_quotes_or_index_is_parsed()
    {
        Assert.Equal(@"C:\G\ew6.exe", EaScanner.StripIconIndex("\"C:\\G\\ew6.exe\""));
        Assert.Equal(@"C:\G\ew6.exe", EaScanner.StripIconIndex(@"C:\G\ew6.exe,0"));
        Assert.Equal(@"C:\G\a,b.exe", EaScanner.StripIconIndex(@"C:\G\a,b.exe"));
    }

    [Fact]
    public void Broken_truncated_xml_reports_error_and_keeps_other_games()
    {
        var bytes = Encoding.UTF8.GetBytes(FixtureText);
        AssertBrokenFileReported(bytes[..(bytes.Length / 2)], "defekt");
    }

    [Fact]
    public void Broken_binary_garbage_reports_error_and_keeps_other_games()
    {
        AssertBrokenFileReported([0x00, 0xFF, 0x13, 0x37, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00], "defekt");
    }

    [Fact]
    public void Broken_wrong_encoding_reports_error_and_keeps_other_games()
    {
        // Latin-1-Bytes, obwohl die Deklaration utf-8 verspricht.
        var xml = FixtureText.Replace("<gameTitle locale=\"de_DE\">Eisenwall™ 6</gameTitle>", "<gameTitle locale=\"de_DE\">Eisenwall Ä</gameTitle>", StringComparison.Ordinal)
            .Replace("™", string.Empty, StringComparison.Ordinal);
        xml = Regex.Replace(xml, "<gameTitle locale=\"zh_..\">.*?</gameTitle>", string.Empty);
        AssertBrokenFileReported(Encoding.Latin1.GetBytes(xml), "defekt");
    }

    [Fact]
    public void Broken_wrong_root_element_reports_error_and_keeps_other_games()
    {
        AssertBrokenFileReported(Encoding.UTF8.GetBytes("<?xml version='1.0'?><Settings><contentIDs><contentID>1</contentID></contentIDs></Settings>"), "DiPManifest");
    }

    [Fact]
    public void Broken_missing_content_id_reports_error_and_keeps_other_games()
    {
        var xml = Regex.Replace(FixtureText, "<contentIDs>.*?</contentIDs>", string.Empty, RegexOptions.Singleline);
        AssertBrokenFileReported(Encoding.UTF8.GetBytes(xml), "contentID");
    }

    [Fact]
    public void Broken_unreadable_file_reports_error_and_keeps_other_games()
    {
        var fs = AddEw6(new InMemoryFileSystem());
        AddEw6(fs, @"C:\Program Files\EA Games\Gesperrt", xml: WithId("2"));
        fs.FailReading(@"C:\Program Files\EA Games\Gesperrt\__Installer\installerdata.xml", new UnauthorizedAccessException("Zugriff verweigert"));

        var result = Scan(fs, new FakeRegistry());

        Assert.Equal("18306642", Assert.Single(result.Games).SourceId);
        var error = Assert.Single(result.Errors);
        Assert.Contains("nicht lesbar", error.Message, StringComparison.Ordinal);
        Assert.Equal(@"C:\Program Files\EA Games\Gesperrt\__Installer\installerdata.xml", error.Path);
    }

    [Fact]
    public void Broken_origin_settings_are_logged_but_not_reported_as_scan_error()
    {
        var logger = new ListLogger<EaScanner>();
        var fs = AddEw6(new InMemoryFileSystem()).AddFile(@"C:\Users\Test\AppData\Roaming\Origin\local.xml", "<Settings><Setting key=");

        var result = new EaScanner(fs, new FakeRegistry(), new FakeEnvironment(), logger).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        Assert.Empty(result.Errors);
        Assert.True(logger.Contains(LogLevel.Warning, "Origin-Einstellungen"));
    }

    [Fact]
    public void Incomplete_registry_entries_pointing_to_missing_folders_are_skipped_without_error()
    {
        var registry = Ew6Uninstall(new FakeRegistry(), @"D:\Weg\Eisenwall 6\")
            .Set(Hklm, @"SOFTWARE\EA Games\Eisenwall 6", "Install Dir", @"D:\Weg\Eisenwall 6\")
            .Set(Hklm, @"SOFTWARE\EA Games\Leer", "Install Dir", string.Empty);

        var result = Scan(new InMemoryFileSystem(), registry);

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_installation_without_any_named_executable_is_skipped()
    {
        var fs = new InMemoryFileSystem().AddFixture(Fixture, GameDir + @"\__Installer\installerdata.xml");
        AddEw6(fs, @"C:\Program Files\EA Games\Vollständig", xml: WithId("2"));

        var result = Scan(fs, new FakeRegistry());

        Assert.Equal("2", Assert.Single(result.Games).SourceId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_manifest_without_titles_and_runtime_uses_folder_name_and_no_executables()
    {
        var xml = Regex.Replace(FixtureText, "<(gameTitles|runtime|uninstall)>.*?</\\1>", string.Empty, RegexOptions.Singleline);
        var fs = new InMemoryFileSystem().AddFile(GameDir + @"\__Installer\installerdata.xml", xml);

        var game = Assert.Single(Scan(fs, new FakeRegistry()).Games);

        Assert.Equal("Eisenwall 6", game.Name);
        Assert.Empty(game.Executables);
        Assert.Equal(ExpectedLaunch, game.LaunchCommand);
    }

    [Fact]
    public void Launcher_detected_from_ea_app_traces_without_games()
    {
        var byRegistry = Scan(new InMemoryFileSystem(), new FakeRegistry().AddKey(Hklm, @"SOFTWARE\Electronic Arts\EA Desktop"));
        var byProgramData = Scan(new InMemoryFileSystem().AddDirectory(@"C:\ProgramData\EA Desktop"), new FakeRegistry());

        Assert.True(byRegistry.LauncherDetected);
        Assert.True(byProgramData.LauncherDetected);
        Assert.Empty(byRegistry.Games);
        Assert.Empty(byProgramData.Errors);
    }

    [Fact]
    public void Scan_honors_cancellation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.Cancel();
        var scanner = new EaScanner(AddEw6(new InMemoryFileSystem()), new FakeRegistry(), new FakeEnvironment());

        Assert.ThrowsAny<OperationCanceledException>(() => scanner.Scan(ScanOptions.Default, cts.Token));
    }

    [Fact]
    public void Scan_only_reads()
    {
        var fs = new EaBattleNetTestSupport.ReadOnlyFileSystem(AddEw6(new InMemoryFileSystem()));
        var registry = new EaBattleNetTestSupport.ReadOnlyRegistry(Ew6Uninstall(new FakeRegistry(), GameDir));

        var result = new EaScanner(fs, registry, new FakeEnvironment()).Scan(new ScanOptions { SearchFolders = [@"C:\"] }, Ct);

        Assert.Single(result.Games);
    }

    [Fact]
    public void Scanner_is_found_by_catalog()
    {
        Assert.Contains(typeof(EaScanner), ScannerCatalog.ScannerTypes);
    }

    [Fact]
    public void Dlc_manifests_inside_the_game_installer_folder_are_neither_games_nor_errors()
    {
        // Am Zielrechner gemessen: Eisenwall 6\__Installer\DLC\<DLC>\__Installer\installerdata.xml (9 Stück, leere contentID).
        // Ein Suchordner oder Registry-Eintrag, der in diesen Baum führt, ergab vorher je DLC einen falschen Scanfehler,
        // mit gefüllter contentID sogar ein DLC als eigenes Spiel.
        const string dlcRoot = GameDir + @"\__Installer\DLC";
        var fs = AddEw6(new InMemoryFileSystem())
            .AddFixture(@"EA\installerdata-ew6-dlc.xml", dlcRoot + @"\EW6_ZUS1_DLC\__Installer\installerdata.xml")
            .AddFile(dlcRoot + @"\EW6_MARKER_DLC\__Installer\installerdata.xml", WithId("ew_content002"))
            .AddFile(dlcRoot + @"\EW6_MARKER_DLC\EAAntiCheat.GameServiceLauncher.exe", "MZ")
            .AddFile(dlcRoot + @"\EW6_MARKER_DLC\SP\ew6.exe", "MZ");
        var registry = Ew6Uninstall(new FakeRegistry(), GameDir)
            .Set(Hklm, @"SOFTWARE\EA Games\EW6_MARKER_DLC", "Install Dir", dlcRoot + @"\EW6_MARKER_DLC\");

        var result = Scan(fs, registry, new ScanOptions { SearchFolders = [GameDir + @"\__Installer", dlcRoot, dlcRoot + @"\EW6_ZUS1_DLC"] });

        Assert.Equal("18306642", Assert.Single(result.Games).SourceId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Broken_empty_file_reports_error_and_keeps_other_games()
    {
        AssertBrokenFileReported([], "defekt");
    }

    [Fact]
    public void Broken_xml_with_dtd_reports_error_and_keeps_other_games()
    {
        var xml = FixtureText.Replace("<DiPManifest version=\"4.0\">", "<!DOCTYPE DiPManifest [<!ENTITY x \"y\">]>\n<DiPManifest version=\"4.0\">", StringComparison.Ordinal);
        AssertBrokenFileReported(Encoding.UTF8.GetBytes(xml), "defekt");
    }

    [Theory]
    [InlineData("x-unbekannt-42")]
    [InlineData("windows-1252")]
    public void Broken_unsupported_declared_encoding_reports_error_and_keeps_other_games(string encoding)
    {
        var xml = FixtureText.Replace("encoding='utf-8'", "encoding='" + encoding + "'", StringComparison.Ordinal);
        AssertBrokenFileReported(Encoding.UTF8.GetBytes(xml), "defekt");
    }

    [Fact]
    public void Utf16_file_with_byte_order_mark_is_read()
    {
        var xml = FixtureText.Replace("encoding='utf-8'", "encoding='utf-16'", StringComparison.Ordinal);
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray();
        var fs = AddEw6(new InMemoryFileSystem()).AddFile(GameDir + @"\__Installer\installerdata.xml", bytes);

        var game = Assert.Single(Scan(fs, new FakeRegistry()).Games);

        Assert.Equal("Eisenwall™ 6", game.Name);
        Assert.Equal("18306642", game.SourceId);
    }

    [Fact]
    public void Registry_values_with_wrong_types_are_ignored_without_exception()
    {
        const string other = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
        var fs = AddEw6(new InMemoryFileSystem(), withRootExe: false);
        var registry = new FakeRegistry()
            .Set(Hklm, UninstallKey, "Publisher", "Electronic Arts")
            .Set(Hklm, UninstallKey, "InstallLocation", 42)                                  // DWORD statt Text
            .Set(Hklm, other + "Binaer", "Publisher", "Electronic Arts")
            .Set(Hklm, other + "Binaer", "InstallLocation", new byte[] { 0x43, 0x3A, 0x5C })  // REG_BINARY
            .Set(Hklm, other + "Mehrfach", "Publisher", new[] { "Electronic Arts" })          // REG_MULTI_SZ
            .Set(Hklm, other + "Mehrfach", "InstallLocation", new[] { GameDir })
            .Set(Hklm, @"SOFTWARE\EA Games\Eisenwall 6", "Install Dir", 7L)                 // QWORD statt Text
            .Set(Hklm, UninstallKey.Replace(@"WOW6432Node\", string.Empty, StringComparison.Ordinal), "DisplayIcon", new byte[] { 1, 2 });

        var result = Scan(fs, registry);

        var game = Assert.Single(result.Games); // über den Standardordner gefunden
        Assert.Equal([GameDir + @"\EAAntiCheat.GameServiceLauncher.exe", GameDir + @"\SP\ew6.exe"], game.Executables);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Paths_with_umlauts_special_characters_forward_slashes_and_trailing_separator_are_normalized()
    {
        const string dir = @"D:\Spielordner\Über & Co (2) #1\Eisenwall 6";
        var fs = AddEw6(new InMemoryFileSystem(), dir);
        var registry = Ew6Uninstall(new FakeRegistry(), "d:/spielordner/über & co (2) #1/Eisenwall 6/")
            .Set(Hklm, UninstallKey, "DisplayIcon", "\"" + dir + "\\ew6.exe\",0");

        var game = Assert.Single(Scan(fs, registry).Games);

        Assert.Equal(dir, game.InstallDir, ignoreCase: true);
        Assert.DoesNotContain("/", game.InstallDir, StringComparison.Ordinal);
        Assert.False(game.InstallDir.EndsWith('\\'));
        Assert.Equal(3, game.Executables.Count);
        Assert.All(game.Executables, e => Assert.StartsWith(game.InstallDir + @"\", e, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Broken_game_directly_in_drive_root_is_reported_and_skipped()
    {
        // In Phase 3 gilt jeder Prozess unter InstallDir als Spiel; ein Laufwerk als InstallDir träfe jeden Prozess darauf.
        var fs = AddEw6(new InMemoryFileSystem(), @"D:\", xml: WithId("2"));
        AddEw6(fs);
        var registry = Ew6Uninstall(new FakeRegistry(), "D:/");

        var result = Scan(fs, registry, new ScanOptions { SearchFolders = [@"D:\"] });

        Assert.Equal("18306642", Assert.Single(result.Games).SourceId);
        var error = Assert.Single(result.Errors);
        Assert.Equal(@"D:\__Installer\installerdata.xml", error.Path);
        Assert.Contains("Laufwerk", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Incomplete_registry_entry_on_missing_drive_and_relative_path_is_skipped_without_error()
    {
        var registry = Ew6Uninstall(new FakeRegistry(), @"Z:\Spiele\Eisenwall 6\")
            .Set(Hklm, @"SOFTWARE\EA Games\Relativ", "Install Dir", @"EA Games\Eisenwall 6");

        var result = Scan(new InMemoryFileSystem(), registry, new ScanOptions { SearchFolders = [@"Z:\", "relativ", string.Empty] });

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
        Assert.False(result.LauncherDetected);
    }

    private static ScanResult Scan(IFileSystem fs, IRegistry registry, ScanOptions? options = null) =>
        new EaScanner(fs, registry, new FakeEnvironment()).Scan(options ?? ScanOptions.Default, Ct);

    private static void AssertBrokenFileReported(byte[] brokenFile, string expectedFragment)
    {
        const string brokenPath = @"C:\Program Files\EA Games\Kaputt\__Installer\installerdata.xml";
        var fs = AddEw6(new InMemoryFileSystem()).AddFile(brokenPath, brokenFile);

        var result = Scan(fs, new FakeRegistry());

        Assert.Equal("18306642", Assert.Single(result.Games).SourceId);
        var error = Assert.Single(result.Errors);
        Assert.Equal(brokenPath, error.Path);
        Assert.StartsWith("installerdata.xml", error.Message, StringComparison.Ordinal);
        Assert.Contains(expectedFragment, error.Message, StringComparison.Ordinal);
        Assert.True(result.LauncherDetected);
    }

    private static string WithId(string contentId) =>
        FixtureText.Replace("<contentID>18306642</contentID>", "<contentID>" + contentId + "</contentID>", StringComparison.Ordinal);

    private static InMemoryFileSystem AddEw6(InMemoryFileSystem fs, string dir = GameDir, string? xml = null, bool withRootExe = true)
    {
        fs.AddFile(dir + @"\__Installer\installerdata.xml", xml ?? FixtureText)
            .AddFile(dir + @"\EAAntiCheat.GameServiceLauncher.exe", "MZ")
            .AddFile(dir + @"\SP\ew6.exe", "MZ")
            .AddFile(dir + @"\Data\initfs_Win32", "x");
        if (withRootExe)
        {
            fs.AddFile(dir + @"\ew6.exe", "MZ");
        }

        return fs;
    }

    private static FakeRegistry Ew6Uninstall(FakeRegistry registry, string installLocation) => registry
        .Set(Hklm, UninstallKey, "DisplayName", "Eisenwall™ 6")
        .Set(Hklm, UninstallKey, "Publisher", "Electronic Arts")
        .Set(Hklm, UninstallKey, "InstallLocation", installLocation)
        .Set(Hklm, UninstallKey, "UninstallString", "\"C:\\Program Files\\Common Files\\EAInstaller\\Eisenwall 6\\Cleanup.exe\" uninstall_game -autologging");
}
