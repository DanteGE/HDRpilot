using System.Text;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Core.Games.Scanners.Xbox;
using HdrPilot.Core.Util;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

public sealed class XboxScannerTests
{
    private const string WolkenhofFamily = "Contoso.Wolkenhof_50p2hsbcnaasp";
    private const string WolkenhofContent = @"C:\XboxGames\Wolkenhof\Content";
    private const string WolkenhofExe = WolkenhofContent + @"\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe";
    private const string BeispielFamily = "Contoso.Beispielspiel_t69f6hpx382ka";
    private const string BeispielContent = @"D:\Xbox Bibliothek\Beispielspiel\Content";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Missing_launcher_returns_empty_without_errors()
    {
        var fs = new InMemoryFileSystem().AddDirectory(@"C:\Program Files").AddDirectory(@"C:\XboxGames\Wolkenhof\Content");
        fs.FixedDriveRoots.Add(@"D:\");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
        Assert.Equal(GameSource.Xbox, result.Source);
    }

    [Fact]
    public void Finds_wolkenhof_with_exact_entry_from_real_format()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem());

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal(GameSource.Xbox, game.Source);
        Assert.Equal(WolkenhofFamily, game.SourceId);
        Assert.Equal("Wolkenhof", game.Name);
        Assert.Equal(WolkenhofContent, game.InstallDir);
        Assert.Equal([WolkenhofExe], game.Executables);
        Assert.Equal(@"shell:AppsFolder\Contoso.Wolkenhof_50p2hsbcnaasp!AppHofShipping", game.LaunchCommand);
        Assert.Null(game.LaunchArguments);
        Assert.Null(game.LaunchWorkingDirectory);
        Assert.Equal("Xbox:" + WolkenhofFamily, game.Key);
    }

    [Fact]
    public void Gamelaunchhelper_lies_in_install_dir_but_is_not_a_listed_executable()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem());

        var game = Assert.Single(Scanner(fs).Scan(ScanOptions.Default, Ct).Games);

        var helper = WolkenhofContent + @"\gamelaunchhelper.exe";
        Assert.DoesNotContain(helper, game.Executables, StringComparer.OrdinalIgnoreCase);
        Assert.True(PathUtil.IsUnder(helper, game.InstallDir)); // Phase 3: Treffer über den Installationsordner
        Assert.True(PathUtil.IsUnder(WolkenhofExe, game.InstallDir));
        Assert.False(HelperExecutables.IsHelper(helper));
    }

    [Fact]
    public void Reads_gaming_roots_on_all_fixed_drives_including_folder_names_with_spaces()
    {
        var fs = WithBeispiel(WithWolkenhof(new InMemoryFileSystem()));
        fs.FixedDriveRoots.Add(@"D:\");

        var result = Scanner(new FakeResolver("Beispielspiel")).Scan(fs, Ct);

        Assert.Empty(result.Errors);
        Assert.Equal([WolkenhofFamily, BeispielFamily], result.Games.Select(g => g.SourceId));
        Assert.Equal(BeispielContent, result.Games[1].InstallDir);
    }

    [Fact]
    public void Drive_that_is_not_a_fixed_drive_is_ignored()
    {
        var fs = new InMemoryFileSystem()
            .AddFixture(@"Xbox\GamingRoot_C.bin", @"E:\.GamingRoot")
            .AddFixture(@"Xbox\Wolkenhof\MicrosoftGame.config", @"E:\XboxGames\Wolkenhof\Content\MicrosoftGame.config")
            .AddFile(@"E:\XboxGames\Wolkenhof\Content\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe", "MZ");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
    }

    [Fact]
    public void Gaming_root_with_several_entries_scans_every_folder()
    {
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_zwei_Eintraege.bin", @"C:\.GamingRoot");
        AddGame(fs, @"C:\XboxGames\Wolkenhof", "Wolkenhof");
        AddGame(fs, @"C:\Spiele\Xbox\Beispielspiel", "Beispielspiel");

        var result = Scanner(new FakeResolver("Beispielspiel")).Scan(fs, Ct);

        Assert.Empty(result.Errors);
        Assert.Equal([WolkenhofFamily, BeispielFamily], result.Games.Select(g => g.SourceId));
        Assert.Equal(@"C:\Spiele\Xbox\Beispielspiel\Content", result.Games[1].InstallDir);
    }

    [Fact]
    public void Launch_command_uses_application_id_from_appxmanifest()
    {
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .AddFile(@"C:\XboxGames\Spiel\Content\MicrosoftGame.config", Config("Contoso.Spiel", "Spiel", ("Bin/Spiel.exe", "ConfigId", null)))
            .AddFile(@"C:\XboxGames\Spiel\Content\appxmanifest.xml", Manifest("Contoso.Spiel", "ManifestApp"))
            .AddFile(@"C:\XboxGames\Spiel\Content\Bin\Spiel.exe", "MZ");

        var game = Assert.Single(Scanner(fs).Scan(ScanOptions.Default, Ct).Games);

        Assert.EndsWith("!ManifestApp", game.LaunchCommand, StringComparison.Ordinal);
        Assert.StartsWith(@"shell:AppsFolder\Contoso.Spiel_", game.LaunchCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_command_falls_back_to_first_pc_executable_id_without_appxmanifest()
    {
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .AddFile(@"C:\XboxGames\Spiel\Content\MicrosoftGame.config", Config(
                "Contoso.Spiel", "Spiel", ("Konsole.exe", "KonsoleId", "XboxOne"), ("Bin/Spiel.exe", "PcId", "PC")))
            .AddFile(@"C:\XboxGames\Spiel\Content\Bin\Spiel.exe", "MZ");

        var game = Assert.Single(Scanner(fs).Scan(ScanOptions.Default, Ct).Games);

        Assert.EndsWith("!PcId", game.LaunchCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Executables_are_relative_to_content_with_any_slashes_existing_pc_only_and_in_config_order()
    {
        var fs = WithBeispiel(new InMemoryFileSystem());
        fs.FixedDriveRoots.Add(@"D:\");

        var game = Assert.Single(Scanner(new FakeResolver("Beispielspiel")).Scan(fs, Ct).Games);

        Assert.Equal(
            [
                BeispielContent + @"\Beispiel\Binaries\WinGDK\Beispiel-WinGDK-Shipping.exe",
                BeispielContent + @"\Tools\BeispielBenchmark.exe",
            ],
            game.Executables);
        Assert.EndsWith("!Game", game.LaunchCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Executable_lookup_ignores_case_and_skips_missing_and_escaping_paths()
    {
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .AddFile(@"C:\XboxGames\Spiel\Content\MicrosoftGame.config", Config(
                "Contoso.Spiel", "Spiel",
                ("BIN/SPIEL.EXE", "Game", "pc"),
                ("Bin/Fehlt.exe", "Fehlt", null),
                (@"..\..\Fremd.exe", "Fremd", null),
                (@"C:\Windows\notepad.exe", "Absolut", null)))
            .AddFile(@"C:\XboxGames\Spiel\Content\bin\spiel.exe", "MZ")
            .AddFile(@"C:\XboxGames\Fremd.exe", "MZ")
            .AddFile(@"C:\Windows\notepad.exe", "MZ");

        var game = Assert.Single(Scanner(fs).Scan(ScanOptions.Default, Ct).Games);

        Assert.Equal([@"C:\XboxGames\Spiel\Content\BIN\SPIEL.EXE"], game.Executables);
    }

    [Fact]
    public void Ms_resource_display_name_is_resolved_through_resources_pri()
    {
        var resolver = new FakeResolver("Beispielspiel");
        var fs = WithBeispiel(new InMemoryFileSystem());
        fs.FixedDriveRoots.Add(@"D:\");

        var game = Assert.Single(Scanner(resolver).Scan(fs, Ct).Games);

        Assert.Equal("Beispielspiel", game.Name);
        Assert.Equal(
            @"@{D:\Xbox Bibliothek\Beispielspiel\Content\resources.pri?ms-resource://Contoso.Beispielspiel/Resources/ApplicationDisplayName}",
            Assert.Single(resolver.Requests));
    }

    [Fact]
    public void Plain_manifest_display_name_wins_over_resource_reference()
    {
        var resolver = new FakeResolver("Aus Ressource");
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .AddFile(@"C:\XboxGames\Ordner\Content\MicrosoftGame.config", Config("Contoso.Spiel", "ms-resource:AppDisplayName", ("Spiel.exe", "Game", null)))
            .AddFile(@"C:\XboxGames\Ordner\Content\appxmanifest.xml", Manifest("Contoso.Spiel", "Game", displayName: "  Spiel aus dem Manifest  "))
            .AddFile(@"C:\XboxGames\Ordner\Content\resources.pri", "PRI")
            .AddFile(@"C:\XboxGames\Ordner\Content\Spiel.exe", "MZ");

        var game = Assert.Single(Scanner(resolver).Scan(fs, Ct).Games);

        Assert.Equal("Spiel aus dem Manifest", game.Name);
        Assert.Empty(resolver.Requests);
    }

    [Fact]
    public void Unresolvable_display_name_falls_back_to_folder_name()
    {
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .AddFile(@"C:\XboxGames\Mein Spiel\Content\MicrosoftGame.config", Config("Contoso.Spiel", "ms-resource:AppDisplayName", ("Spiel.exe", "Game", null)))
            .AddFile(@"C:\XboxGames\Mein Spiel\Content\resources.pri", "PRI")
            .AddFile(@"C:\XboxGames\Mein Spiel\Content\Spiel.exe", "MZ");

        var game = Assert.Single(Scanner(new FakeResolver(null)).Scan(fs, Ct).Games);

        Assert.Equal("Mein Spiel", game.Name);
    }

    [Fact]
    public void Folder_without_microsoftgame_config_is_ignored_silently()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddDirectory(@"C:\XboxGames\GameSave\wgs")
            .AddDirectory(@"C:\XboxGames\GameSave\pgs");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Utf16_config_with_bom_is_read()
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, @"Xbox\Wolkenhof\MicrosoftGame.config")))
            .TrimStart('\uFEFF')
            .Replace("encoding=\"utf-8\"", "encoding=\"utf-16\"", StringComparison.Ordinal);
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .AddFile(WolkenhofContent + @"\MicrosoftGame.config", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)])
            .AddFile(WolkenhofExe, "MZ");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Empty(result.Errors);
        Assert.Equal("Wolkenhof", Assert.Single(result.Games).Name);
    }

    [Fact]
    public void Broken_truncated_config_is_reported_and_other_games_are_still_listed()
    {
        var full = File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, @"Xbox\Wolkenhof\MicrosoftGame.config"));
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFile(@"C:\XboxGames\Abgeschnitten\Content\MicrosoftGame.config", full[..(full.Length / 2)])
            .AddFile(@"C:\XboxGames\Abgeschnitten\Content\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe", "MZ");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Equal(WolkenhofFamily, Assert.Single(result.Games).SourceId);
        var error = Assert.Single(result.Errors);
        Assert.Equal(@"C:\XboxGames\Abgeschnitten\Content\MicrosoftGame.config", error.Path);
        Assert.Contains("kein gültiges XML", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_binary_garbage_config_is_reported()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFixture(@"Xbox\Broken\Binaermuell.bin", @"C:\XboxGames\Muell\Content\MicrosoftGame.config");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        Assert.Equal(@"C:\XboxGames\Muell\Content\MicrosoftGame.config", Assert.Single(result.Errors).Path);
    }

    [Fact]
    public void Broken_wrong_encoding_config_is_reported()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFixture(@"Xbox\Broken\MicrosoftGame_Latin1_als_UTF8.config", @"C:\XboxGames\Kodierung\Content\MicrosoftGame.config")
            .AddFile(@"C:\XboxGames\Kodierung\Content\Spiel.exe", "MZ");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Equal(@"C:\XboxGames\Kodierung\Content\MicrosoftGame.config", error.Path);
        Assert.StartsWith("MicrosoftGame.config ist kein gültiges XML (abgeschnitten, falsche Kodierung", error.Message, StringComparison.Ordinal);
        Assert.Contains("encoding", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<Game><ShellVisuals DefaultDisplayName=\"X\" /></Game>", "Identity")]
    [InlineData("<Game><Identity Publisher=\"CN=Contoso\" /></Game>", "Identity/@Name")]
    [InlineData("<Game><Identity Name=\"Contoso.Spiel\" /></Game>", "Identity/@Publisher")]
    [InlineData("<Package><Identity Name=\"Contoso.Spiel\" Publisher=\"CN=Contoso\" /></Package>", "<Game>")]
    [InlineData("", "leer")]
    [InlineData("<Game><Identity Name=\"Ungültig Name!\" Publisher=\"CN=Contoso\" /></Game>", "Paketname")]
    public void Broken_config_with_missing_or_invalid_required_fields_is_reported(string xml, string expectedInMessage)
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFile(@"C:\XboxGames\Kaputt\Content\MicrosoftGame.config", xml)
            .AddFile(@"C:\XboxGames\Kaputt\Content\Spiel.exe", "MZ");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Contains(expectedInMessage, error.Message, StringComparison.Ordinal);
        Assert.Equal(@"C:\XboxGames\Kaputt\Content\MicrosoftGame.config", error.Path);
    }

    [Fact]
    public void Broken_unreadable_config_is_reported()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFile(@"C:\XboxGames\Gesperrt\Content\MicrosoftGame.config", "<Game/>")
            .FailReading(@"C:\XboxGames\Gesperrt\Content\MicrosoftGame.config", new IOException("Die Datei wird von einem anderen Prozess verwendet."));

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Contains("konnte nicht gelesen werden", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_appxmanifest_is_reported_and_config_values_are_used()
    {
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .AddFixture(@"Xbox\Wolkenhof\MicrosoftGame.config", WolkenhofContent + @"\MicrosoftGame.config")
            .AddFile(WolkenhofContent + @"\appxmanifest.xml", "<Package><Applications><Application Id=")
            .AddFile(WolkenhofExe, "MZ");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        var game = Assert.Single(result.Games);
        Assert.Equal(@"shell:AppsFolder\Contoso.Wolkenhof_50p2hsbcnaasp!AppHofShipping", game.LaunchCommand);
        var error = Assert.Single(result.Errors);
        Assert.Equal(WolkenhofContent + @"\appxmanifest.xml", error.Path);
        Assert.Contains("Es gelten die Angaben aus MicrosoftGame.config", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"Xbox\Broken\GamingRoot_falsche_Kennung.bin", "RGBX")]
    [InlineData(@"Xbox\Broken\GamingRoot_abgeschnitten.bin", "abgeschnitten")]
    [InlineData(@"Xbox\Broken\Binaermuell.bin", "RGBX")]
    public void Broken_gaming_root_is_reported_and_other_drives_are_still_scanned(string fixture, string expectedInMessage)
    {
        var fs = new InMemoryFileSystem().AddFixture(fixture, @"C:\.GamingRoot");
        fs.FixedDriveRoots.Add(@"D:\");
        WithBeispiel(fs);

        var result = Scanner(new FakeResolver("Beispielspiel")).Scan(fs, Ct);

        Assert.True(result.LauncherDetected);
        Assert.Equal(BeispielFamily, Assert.Single(result.Games).SourceId);
        var error = Assert.Single(result.Errors);
        Assert.Equal(@"C:\.GamingRoot", error.Path);
        Assert.Contains(expectedInMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_unreadable_gaming_root_is_reported()
    {
        var fs = new InMemoryFileSystem()
            .AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .FailReading(@"C:\.GamingRoot", new UnauthorizedAccessException("Zugriff verweigert."));

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Contains("konnte nicht gelesen werden", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Incomplete_installation_without_any_listed_executable_is_skipped()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFixture(@"Xbox\Beispielspiel\MicrosoftGame.config", @"C:\XboxGames\Beispielspiel\Content\MicrosoftGame.config")
            .AddFile(@"C:\XboxGames\Beispielspiel\Content\gamelaunchhelper.exe", "MZ");

        var result = Scanner(new FakeResolver("Beispielspiel")).Scan(fs, Ct);

        Assert.Equal(WolkenhofFamily, Assert.Single(result.Games).SourceId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_gaming_root_pointing_to_missing_folder_is_skipped_without_error()
    {
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_content_folder_without_config_is_not_a_game()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFile(@"C:\XboxGames\Download\Content\Teil1.bin", "x");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_config_without_application_id_is_reported()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFile(@"C:\XboxGames\OhneId\Content\MicrosoftGame.config", Config("Contoso.OhneId", "Ohne Id", ("Spiel.exe", null, null)))
            .AddFile(@"C:\XboxGames\OhneId\Content\Spiel.exe", "MZ");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Keine App-Id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Incomplete_config_without_executable_list_uses_manifest_and_has_no_executables()
    {
        var fs = new InMemoryFileSystem().AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
            .AddFile(@"C:\XboxGames\Spiel\Content\MicrosoftGame.config", Config("Contoso.Spiel", "Spiel"))
            .AddFile(@"C:\XboxGames\Spiel\Content\appxmanifest.xml", Manifest("Contoso.Spiel", "App"));

        var game = Assert.Single(Scanner(fs).Scan(ScanOptions.Default, Ct).Games);

        Assert.Empty(game.Executables);
        Assert.EndsWith("!App", game.LaunchCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_package_on_two_drives_is_listed_once()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem())
            .AddFixture(@"Xbox\GamingRoot_C.bin", @"D:\.GamingRoot")
            .AddFixture(@"Xbox\Wolkenhof\MicrosoftGame.config", @"D:\XboxGames\Wolkenhof\Content\MicrosoftGame.config")
            .AddFile(@"D:\XboxGames\Wolkenhof\Content\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe", "MZ");
        fs.FixedDriveRoots.Add(@"D:\");

        var result = Scanner(fs).Scan(ScanOptions.Default, Ct);

        Assert.Equal(WolkenhofContent, Assert.Single(result.Games).InstallDir);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Cancellation_stops_the_scan()
    {
        var fs = WithWolkenhof(new InMemoryFileSystem());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Scanner(fs).Scan(ScanOptions.Default, cts.Token));
    }

    [Fact]
    public void Catalog_creates_xbox_scanner_with_contract_constructor()
    {
        Assert.Contains(typeof(XboxScanner), ScannerCatalog.ScannerTypes);
        var scanner = Assert.Single(
            ScannerCatalog.CreateAll(new InMemoryFileSystem(), new FakeRegistry(), new FakeEnvironment()),
            s => s.Source == GameSource.Xbox);
        Assert.Empty(scanner.Scan(ScanOptions.Default, Ct).Games);
    }

    private static XboxScanner Scanner(InMemoryFileSystem fs) =>
        new(fs, new FakeRegistry(), new FakeEnvironment(), new FakeResolver(null));

    private static ResolverScanner Scanner(FakeResolver resolver) => new(resolver);

    private static InMemoryFileSystem WithWolkenhof(InMemoryFileSystem fs) => fs
        .AddFixture(@"Xbox\GamingRoot_C.bin", @"C:\.GamingRoot")
        .AddFixture(@"Xbox\Wolkenhof\MicrosoftGame.config", WolkenhofContent + @"\MicrosoftGame.config")
        .AddFixture(@"Xbox\Wolkenhof\appxmanifest.xml", WolkenhofContent + @"\appxmanifest.xml")
        .AddFile(WolkenhofContent + @"\gamelaunchhelper.exe", "MZ")
        .AddFile(WolkenhofContent + @"\resources.pri", "PRI")
        .AddFile(WolkenhofExe, "MZ")
        .AddDirectory(@"C:\XboxGames\Wolkenhof\Shaders");

    private static InMemoryFileSystem WithBeispiel(InMemoryFileSystem fs) => fs
        .AddFixture(@"Xbox\GamingRoot_D.bin", @"D:\.GamingRoot")
        .AddFixture(@"Xbox\Beispielspiel\MicrosoftGame.config", BeispielContent + @"\MicrosoftGame.config")
        .AddFixture(@"Xbox\Beispielspiel\appxmanifest.xml", BeispielContent + @"\appxmanifest.xml")
        .AddFile(BeispielContent + @"\gamelaunchhelper.exe", "MZ")
        .AddFile(BeispielContent + @"\resources.pri", "PRI")
        .AddFile(BeispielContent + @"\Beispiel\Binaries\WinGDK\Beispiel-WinGDK-Shipping.exe", "MZ")
        .AddFile(BeispielContent + @"\Tools\BeispielBenchmark.exe", "MZ");

    private static void AddGame(InMemoryFileSystem fs, string gameFolder, string fixture)
    {
        var content = gameFolder + @"\Content";
        fs.AddFixture($@"Xbox\{fixture}\MicrosoftGame.config", content + @"\MicrosoftGame.config")
            .AddFixture($@"Xbox\{fixture}\appxmanifest.xml", content + @"\appxmanifest.xml")
            .AddFile(content + @"\resources.pri", "PRI");
        var exe = fixture == "Wolkenhof"
            ? @"\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe"
            : @"\Beispiel\Binaries\WinGDK\Beispiel-WinGDK-Shipping.exe";
        fs.AddFile(content + exe, "MZ");
    }

    private static string Config(string packageName, string displayName, params (string Name, string? Id, string? Device)[] executables)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Game configVersion=\"1\">\n");
        sb.Append($"  <Identity Name=\"{packageName}\" Publisher=\"CN=Contoso Spiele GmbH, O=Contoso Spiele GmbH, L=Berlin, C=DE\" Version=\"1.0.0.0\" />\n");
        sb.Append($"  <ShellVisuals DefaultDisplayName=\"{displayName}\" />\n");
        if (executables.Length > 0)
        {
            sb.Append("  <ExecutableList>\n");
            foreach (var (name, id, device) in executables)
            {
                sb.Append($"    <Executable Name=\"{name}\"");
                if (id is not null)
                {
                    sb.Append($" Id=\"{id}\"");
                }

                if (device is not null)
                {
                    sb.Append($" TargetDeviceFamily=\"{device}\"");
                }

                sb.Append(" />\n");
            }

            sb.Append("  </ExecutableList>\n");
        }

        sb.Append("</Game>\n");
        return sb.ToString();
    }

    private static string Manifest(string packageName, string applicationId, string? displayName = null) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="{packageName}" Publisher="CN=Contoso Spiele GmbH, O=Contoso Spiele GmbH, L=Berlin, C=DE" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties><DisplayName>{displayName ?? "ms-resource:AppDisplayName"}</DisplayName></Properties>
          <Applications><Application Id="{applicationId}" Executable="GameLaunchHelper.exe" EntryPoint="Windows.FullTrustApplication" /></Applications>
        </Package>
        """;

    /// <summary>Attrappe für die ms-resource-Auflösung: liefert immer denselben Text und merkt sich die Anfragen.</summary>
    private sealed class FakeResolver(string? answer) : IIndirectStringResolver
    {
        public List<string> Requests { get; } = [];

        public string? Resolve(string indirectString)
        {
            Requests.Add(indirectString);
            return answer;
        }
    }

    private sealed class ResolverScanner(FakeResolver resolver)
    {
        public ScanResult Scan(InMemoryFileSystem fs, CancellationToken cancellationToken) =>
            new XboxScanner(fs, new FakeRegistry(), new FakeEnvironment(), resolver).Scan(ScanOptions.Default, cancellationToken);
    }
}
