using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Core.Games.Scanners.BattleNet;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

public sealed class BattleNetScannerTests
{
    private const RegistryHiveKind Hklm = RegistryHiveKind.LocalMachine;
    private const string ProductDb = @"C:\ProgramData\Battle.net\Agent\product.db";
    private const string Uninstall32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const string OverwatchDir = @"C:\Program Files (x86)\Overwatch";
    private const string WowDir = @"K:\Games\World of Warcraft";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Missing_launcher_returns_empty_without_errors()
    {
        var result = Scan(new InMemoryFileSystem(), new FakeRegistry());

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
        Assert.Equal(GameSource.BattleNet, result.Source);
        Assert.Equal("Battle.net", result.ScannerName);
    }

    [Fact]
    public void Launcher_only_product_db_from_this_machine_yields_no_games()
    {
        // Format wie am Zielrechner: nur Agent (uid agent) und Battle.net-App (uid battle.net, Code bna), beide Ordner vorhanden.
        var fs = new InMemoryFileSystem()
            .AddFixture(@"BattleNet\product-launcher-only.db", ProductDb)
            .AddFile(@"C:\Program Files (x86)\Battle.net\Battle.net.exe", "MZ");
        var registry = new FakeRegistry()
            .Set(Hklm, Uninstall32 + "Battle.net", "UninstallString", "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=deDE --uid=battle.net --displayname=\"Battle.net\"")
            .Set(Hklm, Uninstall32 + "Battle.net", "DisplayName", "Battle.net")
            .Set(Hklm, Uninstall32 + "Battle.net", "DisplayIcon", @"C:\Program Files (x86)\Battle.net\Battle.net.exe");

        var result = Scan(fs, registry);

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Product_db_reader_reads_verified_fields_of_this_machine()
    {
        var content = ProductDbReader.Read(File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, @"BattleNet\product-launcher-only.db")));

        Assert.Empty(content.Errors);
        Assert.Equal(
            [
                new BattleNetProductInstall("agent", "agent", "C:/ProgramData/Battle.net/Agent"),
                new BattleNetProductInstall("battle.net", "bna", "C:/Program Files (x86)/Battle.net"),
            ],
            content.Installs);
    }

    [Fact]
    public void Finds_games_with_uid_name_from_uninstall_entry_icon_and_launch_uri()
    {
        var fs = WithGamesFixture()
            .AddFile(OverwatchDir + @"\Overwatch Launcher.exe", "MZ")
            .AddFile(OverwatchDir + @"\_retail_\Overwatch.exe", "MZ")
            .AddDirectory(WowDir);
        var registry = OverwatchUninstall(new FakeRegistry(), "\"" + OverwatchDir + "\\Overwatch Launcher.exe\"");

        var result = Scan(fs, registry);

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Games.Count); // Diablo IV: Ordner J:\Blizzard\Diablo IV fehlt
        var overwatch = result.Games.Single(g => g.SourceId == "prometheus");
        Assert.Equal(GameSource.BattleNet, overwatch.Source);
        Assert.Equal("BattleNet:prometheus", overwatch.Key);
        Assert.Equal("Overwatch", overwatch.Name);
        Assert.Equal(OverwatchDir, overwatch.InstallDir);
        Assert.Equal([OverwatchDir + @"\Overwatch Launcher.exe"], overwatch.Executables);
        Assert.Equal("battlenet://pro", overwatch.LaunchCommand);
        Assert.Null(overwatch.LaunchArguments);

        var wow = result.Games.Single(g => g.SourceId == "wow");
        Assert.Equal("World of Warcraft", wow.Name); // ohne Uninstall-Eintrag: Ordnername
        Assert.Equal(WowDir, wow.InstallDir);       // "K:/Games/World of Warcraft" mit Backslashes
        Assert.Empty(wow.Executables);              // product.db nennt keine EXE
        Assert.Equal("battlenet://wow", wow.LaunchCommand);
    }

    [Fact]
    public void Launch_command_is_battlenet_uri_with_escaped_product_code()
    {
        Assert.Equal("battlenet://pro", BattleNetScanner.CreateLaunchCommand("pro"));
        Assert.Equal("battlenet://a%20b", BattleNetScanner.CreateLaunchCommand("a b"));
    }

    [Fact]
    public void Agent_and_battle_net_app_entries_are_not_games()
    {
        var db = Concat(
            Install("agent", "agent", "C:/ProgramData/Battle.net/Agent"),
            Install("battle.net", "bna", "C:/Program Files (x86)/Battle.net"),
            Install("BATTLE.NET", "x", "C:/Program Files (x86)/Battle.net"),
            Install("x", "BNA", "C:/Program Files (x86)/Battle.net"));
        var fs = new InMemoryFileSystem().AddFile(ProductDb, db)
            .AddDirectory(@"C:\Program Files (x86)\Battle.net");

        var result = Scan(fs, new FakeRegistry());

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_product_db_yields_no_games_without_errors()
    {
        var result = Scan(new InMemoryFileSystem().AddFile(ProductDb, []), new FakeRegistry());

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Broken_truncated_product_db_reports_error_and_keeps_entries_before_the_cut()
    {
        var bytes = FixtureBytes(@"BattleNet\product-with-games.db");
        var fs = new InMemoryFileSystem().AddFile(ProductDb, bytes[..^10]).AddDirectory(OverwatchDir).AddDirectory(WowDir);

        var result = Scan(fs, new FakeRegistry());

        Assert.Equal(["prometheus", "wow"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Equal(ProductDb, error.Path);
        Assert.Contains("abgeschnitten", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { 0x0A, 0x7F, 0x01 })]
    public void Broken_binary_garbage_reports_error_without_exception(byte[] garbage)
    {
        var result = Scan(new InMemoryFileSystem().AddFile(ProductDb, garbage), new FakeRegistry());

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ProductDb, error.Path);
        Assert.StartsWith("product.db ist defekt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_invalid_utf8_in_one_entry_reports_error_and_keeps_other_games()
    {
        var badUid = Msg(1, Field(1, [0x77, 0xC3, 0x28]), Str(2, "wow"), Msg(3, Str(1, "D:/Games/Kaputt")));
        var fs = new InMemoryFileSystem()
            .AddFile(ProductDb, Concat(badUid, Install("prometheus", "pro", "C:/Program Files (x86)/Overwatch")))
            .AddDirectory(OverwatchDir);

        var result = Scan(fs, new FakeRegistry());

        Assert.Equal("prometheus", Assert.Single(result.Games).SourceId);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Eintrag 1", error.Message, StringComparison.Ordinal);
        Assert.Contains("UTF-8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_entries_without_uid_or_product_code_report_errors_and_keep_other_games()
    {
        var noUid = Msg(1, Str(2, "wow"), Msg(3, Str(1, "K:/Games/World of Warcraft")));
        var noCode = Msg(1, Str(1, "fenris"), Msg(3, Str(1, "K:/Games/World of Warcraft")));
        var fs = new InMemoryFileSystem()
            .AddFile(ProductDb, Concat(noUid, Install("prometheus", "pro", "C:/Program Files (x86)/Overwatch"), noCode))
            .AddDirectory(OverwatchDir)
            .AddDirectory(WowDir);

        var result = Scan(fs, new FakeRegistry());

        Assert.Equal("prometheus", Assert.Single(result.Games).SourceId);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("ohne uid", result.Errors[0].Message, StringComparison.Ordinal);
        Assert.Contains("ohne Produktcode", result.Errors[1].Message, StringComparison.Ordinal);
        Assert.Contains("Eintrag 3", result.Errors[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_group_wire_type_reports_error_and_keeps_games_before_it()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(ProductDb, Concat(Install("prometheus", "pro", "C:/Program Files (x86)/Overwatch"), [0x13, 0x08, 0x01, 0x14]))
            .AddDirectory(OverwatchDir);

        var result = Scan(fs, new FakeRegistry());

        Assert.Single(result.Games);
        Assert.Contains("Gruppe", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_unreadable_product_db_reports_error()
    {
        var fs = new InMemoryFileSystem().AddFile(ProductDb, [0x0A, 0x00]).FailReading(ProductDb, new IOException("Die Datei wird von einem anderen Prozess verwendet."));

        var result = Scan(fs, new FakeRegistry());

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Contains("nicht lesbar", error.Message, StringComparison.Ordinal);
        Assert.Equal(ProductDb, error.Path);
    }

    [Fact]
    public void Incomplete_entry_without_install_path_is_skipped_without_error()
    {
        var noSettings = Msg(1, Str(1, "wow"), Str(2, "wow"));
        var emptyPath = Msg(1, Str(1, "fenris"), Str(2, "fenris"), Msg(3, Str(2, "us")));
        var relative = Install("hs_beta", "hsb", "Hearthstone");
        var fs = new InMemoryFileSystem().AddFile(ProductDb, Concat(noSettings, emptyPath, relative)).AddDirectory(@"C:\Hearthstone");

        var result = Scan(fs, new FakeRegistry());

        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Incomplete_install_folder_missing_is_skipped_without_error()
    {
        var fs = new InMemoryFileSystem().AddFile(ProductDb, FixtureBytes(@"BattleNet\product-with-games.db")).AddDirectory(WowDir);

        var result = Scan(fs, new FakeRegistry());

        Assert.Equal("wow", Assert.Single(result.Games).SourceId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Unknown_fields_wire_types_and_field_order_are_handled()
    {
        var entry = Msg(
            1,
            [0x45, 0x01, 0x02, 0x03, 0x04],                    // Feld 8, fixed32
            [0x49, 1, 2, 3, 4, 5, 6, 7, 8],                    // Feld 9, fixed64
            Msg(3, [0x10, 0x96, 0x01], Str(1, "C:/Program Files (x86)/Overwatch"), Str(6, "deDE")),
            [0x28, 0xAC, 0x02],                                // Feld 5, varint
            Str(2, "pro"),
            Msg(4, Msg(1, [0x08, 0x01])),
            Str(1, "prometheus"));
        var db = Concat([0x30, 0x86, 0xF3, 0xE3, 0x01], entry, Msg(5, [0x08, 0x00])); // fremde Felder auf oberster Ebene
        var fs = new InMemoryFileSystem().AddFile(ProductDb, db).AddDirectory(OverwatchDir);

        var result = Scan(fs, new FakeRegistry());

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal("prometheus", game.SourceId);
        Assert.Equal(OverwatchDir, game.InstallDir);
    }

    [Fact]
    public void Duplicate_uid_is_reported_once()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(ProductDb, Concat(Install("wow", "wow", "K:/Games/World of Warcraft"), Install("WOW", "wow", @"K:\Games\World of Warcraft\")))
            .AddDirectory(WowDir);

        Assert.Single(Scan(fs, new FakeRegistry()).Games);
    }

    [Fact]
    public void Display_icon_outside_install_dir_missing_or_with_index_is_handled()
    {
        var fs = WithGamesFixture().AddFile(OverwatchDir + @"\Overwatch Launcher.exe", "MZ").AddFile(@"C:\Anderswo\Overwatch.exe", "MZ");

        var outside = Scan(fs, OverwatchUninstall(new FakeRegistry(), @"C:\Anderswo\Overwatch.exe")).Games.Single(g => g.SourceId == "prometheus");
        var missing = Scan(fs, OverwatchUninstall(new FakeRegistry(), OverwatchDir + @"\Fehlt.exe")).Games.Single(g => g.SourceId == "prometheus");
        var withIndex = Scan(fs, OverwatchUninstall(new FakeRegistry(), OverwatchDir + @"\Overwatch Launcher.exe,0")).Games.Single(g => g.SourceId == "prometheus");

        Assert.Empty(outside.Executables);
        Assert.Empty(missing.Executables);
        Assert.Equal([OverwatchDir + @"\Overwatch Launcher.exe"], withIndex.Executables);
        Assert.Equal("Overwatch", outside.Name);
    }

    [Theory]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=deDE --uid=battle.net --displayname=\"Battle.net\"", "battle.net")]
    [InlineData("\"C:\\x\\Blizzard Uninstaller.exe\" --uid=\"wow_classic\" --lang=enUS", "wow_classic")]
    [InlineData("\"C:\\x\\Blizzard Uninstaller.exe\" --UID=prometheus", "prometheus")]
    [InlineData("\"C:\\x\\Blizzard Uninstaller.exe\" --lang=deDE", null)]
    [InlineData("\"C:\\x\\Blizzard Uninstaller.exe\" --uid= --lang=deDE", null)]
    public void Uninstall_uid_is_parsed(string uninstallString, string? expected)
    {
        Assert.Equal(expected, BlizzardUninstallEntries.ParseUid(uninstallString));
    }

    [Fact]
    public void Launcher_detected_without_product_db_from_agent_folder_or_registry()
    {
        var byFolder = Scan(new InMemoryFileSystem().AddDirectory(@"C:\ProgramData\Battle.net\Agent"), new FakeRegistry());
        var byRegistry = Scan(new InMemoryFileSystem(), new FakeRegistry().AddKey(Hklm, @"SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net"));

        Assert.True(byFolder.LauncherDetected);
        Assert.True(byRegistry.LauncherDetected);
        Assert.Empty(byFolder.Games);
        Assert.Empty(byRegistry.Errors);
    }

    [Fact]
    public void Scan_honors_cancellation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.Cancel();
        var scanner = new BattleNetScanner(WithGamesFixture(), new FakeRegistry(), new FakeEnvironment());

        Assert.ThrowsAny<OperationCanceledException>(() => scanner.Scan(ScanOptions.Default, cts.Token));
    }

    [Fact]
    public void Scan_only_reads()
    {
        var fs = new EaBattleNetTestSupport.ReadOnlyFileSystem(WithGamesFixture().AddDirectory(WowDir));
        var registry = new EaBattleNetTestSupport.ReadOnlyRegistry(OverwatchUninstall(new FakeRegistry(), "x"));

        Assert.Equal(2, new BattleNetScanner(fs, registry, new FakeEnvironment()).Scan(ScanOptions.Default, Ct).Games.Count);
    }

    [Fact]
    public void Scanner_is_found_by_catalog()
    {
        Assert.Contains(typeof(BattleNetScanner), ScannerCatalog.ScannerTypes);
    }

    private static ScanResult Scan(IFileSystem fs, IRegistry registry) =>
        new BattleNetScanner(fs, registry, new FakeEnvironment()).Scan(ScanOptions.Default, Ct);

    private static byte[] FixtureBytes(string relativePath) => File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, relativePath));

    /// <summary>product.db wie am Zielrechner plus drei Spiele: Overwatch (C:), WoW (D:), Diablo IV (E:, Ordner fehlt hier).</summary>
    private static InMemoryFileSystem WithGamesFixture() => new InMemoryFileSystem()
        .AddFixture(@"BattleNet\product-with-games.db", ProductDb)
        .AddDirectory(OverwatchDir);

    private static FakeRegistry OverwatchUninstall(FakeRegistry registry, string displayIcon) => registry
        .Set(Hklm, Uninstall32 + "Overwatch", "UninstallString", "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=deDE --uid=prometheus --displayname=\"Overwatch\"")
        .Set(Hklm, Uninstall32 + "Overwatch", "DisplayName", "Overwatch")
        .Set(Hklm, Uninstall32 + "Overwatch", "InstallLocation", OverwatchDir)
        .Set(Hklm, Uninstall32 + "Overwatch", "DisplayIcon", displayIcon);

    // Kleiner Protobuf-Schreiber nur für Testdaten.
    private static byte[] Install(string uid, string productCode, string installPath) =>
        Msg(1, Str(1, uid), Str(2, productCode), Msg(3, Str(1, installPath), Str(2, "us")));

    private static byte[] Msg(int field, params byte[][] parts) => Field(field, Concat(parts));

    private static byte[] Str(int field, string value) => Field(field, Encoding.UTF8.GetBytes(value));

    private static byte[] Field(int field, byte[] payload) => Concat(Varint((ulong)((field << 3) | 2)), Varint((ulong)payload.Length), payload);

    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value != 0 ? (byte)(b | 0x80) : b);
        }
        while (value != 0);
        return [.. bytes];
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
