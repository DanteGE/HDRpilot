using System.Text;
using HdrPilot.Core.Launchers;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Launchers;

/// <summary>
/// Erkennung angekündigter Spielstarts in Zeilen im echten Format (aus den Protokollen des Zielrechners, anonymisiert:
/// Katalog-IDs ersetzt, Anmeldecode und Benutzerdaten entfernt).
/// </summary>
public sealed class LauncherSignalParserTests
{
    private const string EpicLibraryLine =
        "[2026.09.23-10.55.40:042][824]LogTemp: UProductInfoJSBridge::LaunchWithOptions: Received 0123456789abcdef0123456789abcdef:fedcba9876543210fedcba9876543210:Crow library ";

    private const string EpicUriLine =
        "[2026.09.23-10.51.30:801][567]LogUriHandler: Started processing URI [\"D:\\Programmordner\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe\" "
        + "com.epicgames.launcher://apps/0123456789abcdef0123456789abcdef%3Afedcba9876543210fedcba9876543210%3ACrow?action=launch&silent=true -AllowSoftwareRendering] with AppLaunchUriHandler";

    private const string SteamLine =
        "[2026-09-23 12:44:30] GameAction [AppID 9990130, ActionID 5] : LaunchApp changed task to CheckShaderDepotManifest with \"\"";

    private static LauncherSignal? Parse(LauncherKind launcher, string line)
    {
        // Wie im Watcher: erst die Byte-Vorauswahl, dann das Erkennen.
        return LauncherLogParser.IsCandidate(launcher, Encoding.UTF8.GetBytes(line)) ? LauncherLogParser.Parse(launcher, line) : null;
    }

    [Fact]
    public void Epic_library_launch_yields_the_app_name_key()
    {
        var signal = Parse(LauncherKind.Epic, EpicLibraryLine);

        Assert.Equal(new LauncherSignal(LauncherKind.Epic, "Epic:Crow"), signal);
    }

    [Fact]
    public void Epic_uri_launch_yields_the_app_name_key()
    {
        var signal = Parse(LauncherKind.Epic, EpicUriLine);

        Assert.Equal(new LauncherSignal(LauncherKind.Epic, "Epic:Crow"), signal);
    }

    [Theory]
    [InlineData("com.epicgames.launcher://apps/Crow?action=launch&silent=true", "Epic:Crow")]
    [InlineData("com.epicgames.launcher://apps/ns:item:Sugar?silent=true&action=launch", "Epic:Sugar")]
    [InlineData("com.epicgames.launcher://apps/ns%3aitem%3a9d2d0eb64d5c44529cece33fe2a46482?action=LAUNCH", "Epic:9d2d0eb64d5c44529cece33fe2a46482")]
    public void Epic_uri_variants_are_recognised(string uri, string expectedKey)
    {
        var line = "[2026.09.23-10.51.30:801][567]LogUriHandler: Started processing URI [\"EpicGamesLauncher.exe\" " + uri + "] with AppLaunchUriHandler";

        Assert.Equal(expectedKey, Parse(LauncherKind.Epic, line)?.GameKey);
    }

    [Theory]
    [InlineData("[2026.09.23-09.57.20:976][ 84]WinPlatform: Destroying shortcut. LinkPath: C:\\Users\\User\\Desktop\\Alan Wake 2.url ExpectedTargetPath: com.epicgames.launcher://apps/a%3Ab%3ACoral?action=launch&silent=true")]
    [InlineData("[2026.09.23-10.13.29:701][293]LogUriHandler: Warning: Was unable to parse URI [ActivateLauncher]")]
    [InlineData("[2026.09.23-10.51.30:801][567]LogUriHandler: Started processing URI [\"EpicGamesLauncher.exe\" com.epicgames.launcher://apps/a%3Ab%3ACrow?action=install] with AppInstallUriHandler")]
    [InlineData("[2026.09.23-10.51.30:801][567]LogUriHandler: Started processing URI [\"EpicGamesLauncher.exe\" com.epicgames.launcher://store/p/dead-island-2] with StoreUriHandler")]
    [InlineData("[2026.09.23-10.51.31:352][580]LogCloudSync: Cloud Sync: Sync Started for 0123456789abcdef0123456789abcdef:fedcba9876543210fedcba9876543210:Crow")]
    [InlineData("[2026.09.23-10.55.41:804][929]FCommunityPortalLaunchAppTask: Preparing to launch app 'D:/Anwendungen/Epic Games/DeadIsland2/DeadIsland.exe' with commandline ' -AUTH_PASSWORD=REDACTED -AUTH_TYPE=exchangecode -epicapp=Crow'")]
    [InlineData("[2026.09.23-10.55.44:331][  8]LogPortalPublishTime: Started tracking playtime for 0123:4567:Crow")]
    [InlineData("[2026.09.23-10.55.40:042][824]LogTemp: UProductInfoJSBridge::LaunchWithOptions: Received ns:item:Crow\\..\\x library")]
    [InlineData("[2026.09.23-10.55.40:042][824]LogTemp: UProductInfoJSBridge::LaunchWithOptions: Received ns:item: library")]
    [InlineData("")]
    public void Other_epic_lines_are_no_launch(string line)
    {
        Assert.Null(LauncherLogParser.Parse(LauncherKind.Epic, line));
    }

    [Fact]
    public void Steam_launch_yields_app_id_key_and_action_id()
    {
        var signal = Parse(LauncherKind.Steam, SteamLine);

        Assert.Equal(new LauncherSignal(LauncherKind.Steam, "Steam:9990130", 5), signal);
    }

    [Theory]
    [InlineData("[2026-09-23 12:44:55] Game process added : AppID 9990130 \"\"C:\\Steam\\steamapps\\common\\X\\Game.exe\"\", ProcID 41736, IP 0.0.0.0:0")]
    [InlineData("[2026-09-23 12:44:30] ExecuteSteamURL: \"steam://rungameid/9990130\"")]
    [InlineData("[2026-09-23 12:44:30] GameAction [AppID 9990130, ActionID 5] : InstallApp changed task to Validating")]
    [InlineData("[2026-09-23 12:44:30] GameAction [AppID 0, ActionID 5] : LaunchApp changed task to CreatingProcess with \"\"")]
    [InlineData("[2026-09-23 12:44:30] GameAction [AppID 99999999999, ActionID 5] : LaunchApp changed task to CreatingProcess with \"\"")]
    [InlineData("[2026-09-23 12:44:32] Loaded Config for Local Override Path for App ID 9990130, Controller 4: C:\\Steam/controller_base/empty.vdf")]
    public void Other_steam_lines_are_no_launch(string line)
    {
        Assert.Null(LauncherLogParser.Parse(LauncherKind.Steam, line));
    }

    [Fact]
    public void Lines_of_one_launcher_are_ignored_for_the_other()
    {
        Assert.Null(Parse(LauncherKind.Steam, EpicLibraryLine));
        Assert.Null(Parse(LauncherKind.Epic, SteamLine));
    }

    [Fact]
    public void Byte_prefilter_passes_exactly_the_marker_lines()
    {
        Assert.True(LauncherLogParser.IsCandidate(LauncherKind.Epic, Encoding.UTF8.GetBytes(EpicLibraryLine)));
        Assert.True(LauncherLogParser.IsCandidate(LauncherKind.Epic, Encoding.UTF8.GetBytes(EpicUriLine)));
        Assert.True(LauncherLogParser.IsCandidate(LauncherKind.Steam, Encoding.UTF8.GetBytes(SteamLine)));
        Assert.False(LauncherLogParser.IsCandidate(LauncherKind.Epic, "[2026.09.23-10.51.31:352][580]LogCloudSync: Cloud Sync: Sync Started for a:b:Crow"u8));
        Assert.False(LauncherLogParser.IsCandidate(LauncherKind.Steam, "[2026-09-23 12:44:30] ExecuteSteamURL: \"steam://rungameid/9990130\""u8));
    }

    [Fact]
    public void Epic_fixture_contains_two_launch_lines_and_nothing_else()
    {
        var lines = File.ReadAllLines(Path.Combine(InMemoryFileSystem.FixturesRoot, "Launchers", "EpicGamesLauncher.log"));

        var signals = lines.Select(l => Parse(LauncherKind.Epic, l)).OfType<LauncherSignal>().ToList();

        Assert.Equal(["Epic:Crow", "Epic:Crow"], signals.Select(s => s.GameKey));
    }

    [Fact]
    public void Steam_fixture_yields_one_action_per_launch_line()
    {
        var lines = File.ReadAllLines(Path.Combine(InMemoryFileSystem.FixturesRoot, "Launchers", "console_log.txt"));

        var signals = lines.Select(l => Parse(LauncherKind.Steam, l)).OfType<LauncherSignal>().ToList();

        Assert.All(signals, s => Assert.Equal("Steam:9990130", s.GameKey));
        Assert.Equal([5L, 6L], signals.Select(s => s.ActionId!.Value).Distinct());
        Assert.Equal(22, signals.Count); // jede GameAction-LaunchApp-Zeile, Doppelte filtert erst der Deduplizierer
    }
}
