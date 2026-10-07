using HdrPilot.Core.Games;

namespace HdrPilot.Tests.Games;

/// <summary>
/// Beispiele stammen fast alle aus der EXE-Liste aller Spielordner am Zielrechner (docs/scanner/hilfsprozesse.md):
/// Positivbeispiele sind dort gefundene Hilfsprogramme, Negativbeispiele echte Spiel-EXEs und Start-Helfer. Für
/// Regeln aus dem Auftrag ohne Fundstelle am Zielrechner (BEService*, CrashHandler*) stehen die bekannten Dateinamen.
/// </summary>
public sealed class HelperExecutablesTests
{
    private static readonly string[] HelperExamples =
    [
        // Absturzmelder
        @"Engine\Binaries\Win64\CrashReportClient.exe",
        "CrashReport.exe",
        @"bin\x64\CrashReporter\CrashReporter.exe",
        @"Plugins\Sentry\Binaries\Win64\Sentry.CrashReporter.exe",
        @"client_pc\root\bin\pc\crash_reporter.exe",
        "UnityCrashHandler64.exe",
        "UnityCrashHandler32.exe",
        "BsUnityCrashHandler.exe",
        "CrashHandler.exe",
        @"x64\crashpad\release\crashpad_handler.exe",
        "BsSndRpt.exe",
        "BsSndRpt64.exe",
        "brigador-crashdumper.exe",
        @"newRoot\UnrailedCrashDumper.exe",
        @"system64\CrashSender.exe",
        @"bin\CrashUploader.Publish\CrashUploader.Publish.exe",
        "FarFarWestCrashMessage.exe",
        @"bin\x64\REDEngineErrorReporter.exe",
        "BugReportClient.exe",
        @"Microsoft.NETCore.App\createdump.exe",
        "moon-minidump-stackwalk.exe",
        "sentry-cli.exe",
        @"Watchdog\Watchdog.exe",

        // Anti-Cheat-Installer und -Dienste
        @"EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe",
        "EasyAntiCheat_Setup.exe",
        @"BattlEye\BEService_x64.exe",
        "BEService.exe",
        @"EAAntiCheat\EAAntiCheat.Installer.exe",
        @"denuvoac\AntiCheatInstaller.exe",
        @"tools\GGSetup.exe",
        @"Elytra\Elytra-Setup.exe",
        @"pb\pbsvc_moh.exe",

        // Redistributables und Installer
        @"_CommonRedist\vcredist\2015\vc_redist.x64.exe",
        "vc_redist.arm64.exe",
        "vc_redist_2022.x64.exe",
        "vcredist_x86.exe",
        "vcredist_x64_2012.exe",
        @"install\Redistributable\vc8redist.exe",
        @"_CommonRedist\DirectX\Jun2010\DXSETUP.exe",
        "dxwebsetup.exe",
        "dotNetFx40_Full_setup.exe",
        "dotnetfx35.exe",
        @"_CommonRedist\DotNet\4.7\NDP472-KB4054530-x86-x64-AllOS-ENU.exe",
        "ndp48-x86-x64-allos-enu.exe",
        @"_CommonRedist\PhysX\8.09.04\PhysX_8.09.04_SystemSoftware.exe",
        "PhysX_9.09.0408_SystemSoftware.exe",
        @"EpicOnlineServices\CertsUpdater.exe",
        @"Engine\Extras\Redist\en-us\UE4PrereqSetup_x64.exe",
        "UE4PrereqSetup_x86.exe",
        @"Engine\Extras\Redist\en-us\UEPrereqSetup_x64.exe",
        @"DDS\Install\UE4PrereqSetup_x64.exe",
        @"Binaries\Redist\UE3Redist.exe",
        @"Binaries\UnSetup.exe",
        @"Binaries\Win32\UserCode\UnSetupNativeWrapper.exe",
        "oalinst.exe",
        @"thirdparty\MicrosoftEdgeWebview2Setup.exe",
        @"Redistributables\VulkanRT-1.1.108.0-Installer.exe",
        @"Tactics\Quicktime\QuickTimeInstall.exe",
        @"Tactics\Quicktime\QuickTimeInstaller.exe",
        @"GameSpy\ArcadeInstallPAINKILLER14c.EXE",
        @"EpicOnlineServices\EpicOnlineServicesInstaller.exe",
        "UbisoftConnectInstaller.exe",
        @"Tools\Uplay\UplayInstaller.exe",
        @"Redistributables\Social-Club-Setup.exe",
        @"Redistributables\Rockstar-Games-Launcher.exe",
        @"Redistributables\Rockstar-Games-Epic.exe",
        "InstallerMessage.exe",
        @"Binaries\RKSetup.exe",
        @"__Installer\Touchup.exe",
        @"__Installer\Cleanup.exe",
        "unins000.exe",
        "unins001.exe",
        @"base\uninstall.exe",
        "Uninstall_Last_Resort.exe",
        @"tools\gguninst.exe",

        // Reine Launcher-Fenster
        "REDprelauncher.exe",
        @"Launcher\LariLauncher.exe",
        @"Launcher\DriverVersionChecker.exe",
        @"Launcher\LayersChecker.exe",
        @"bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Launcher.exe",
        "launcher.exe",
        @"bin\Launcher.exe",

        // Browser-Unterprozesse
        @"Engine\Binaries\Win64\EpicWebHelper.exe",
        "UnrealCEFSubProcess.exe",
        "CefSharp.BrowserSubprocess.exe",
    ];

    private static readonly string[] GameExamples =
    [
        "Cyberpunk2077.exe",
        @"D:\GOG-Spiele\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe",
        "ForzaHorizon5.exe",
        "game.exe",
        "Game.exe",
        @"C:\XboxGames\Wolkenhof\Content\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe",
        "bg3.exe",
        "bg3_dx11.exe",
        "RDR2.exe",
        "TheDivision2.exe",
        "bf6.exe",
        "Bannerlord.exe",
        "Bannerlord.Native.exe",
        "Crashlands.exe",              // Spiel mit "Crash" im Namen
        "Sentry.exe",                  // Spiel "SENTRY", nicht der Absturzmelder sentry-cli
        "KILLBUG.exe",
        "Buggos.exe",
        "Benchmark.exe",
        "ContrabandPolice.exe",
        "BeachInvasion1944.exe",       // enthält "eac"
        "Coven - EA.exe",
        "WatchDogs2.exe",
        "watch_dogs.exe",
        "Installation01.exe",
        "Setup Simulator.exe",
        "Praest-Win64-Shipping.exe",
        "riftbreaker_win_release.exe",
        "shooter_win64_release.exe",
        "SWAT.exe",
        "swat_original.exe",
        "DOOM2.exe",
        "javaw.exe",

        // Start-Helfer bleiben Treffer: früher Start-Hinweis für HDR
        @"C:\XboxGames\Wolkenhof\Content\gamelaunchhelper.exe",
        "GameLaunchHelper.exe",
        "start_protected_game.exe",
        "EACLaunch.exe",
        "InsurgencyEAC.exe",
        "BattleBitEAC.exe",
        "EasyAntiCheat.exe",
        "EAAntiCheat.GameServiceLauncher.exe",
        "PlayRDR2.exe",
        "Launch_HLL.exe",
        "TheDivision2Launcher.exe",
    ];

    public static TheoryData<string> Helpers => new(HelperExamples);

    public static TheoryData<string> Games => new(GameExamples);

    [Theory]
    [MemberData(nameof(Helpers))]
    public void Known_helpers_are_recognized(string exe) => Assert.True(HelperExecutables.IsHelper(exe), exe);

    [Theory]
    [MemberData(nameof(Games))]
    public void Game_executables_and_start_helpers_are_not_helpers(string exe) => Assert.False(HelperExecutables.IsHelper(exe), exe);

    [Fact]
    public void Every_default_pattern_matches_at_least_one_real_example()
    {
        foreach (var pattern in HelperExecutables.DefaultPatterns)
        {
            Assert.True(HelperExamples.Any(e => HelperExecutables.IsHelper(e, [pattern])), "Muster ohne Beispiel: " + pattern);
        }
    }

    [Fact]
    public void Default_list_contains_every_rule_from_the_assignment()
    {
        string[] required =
        [
            "CrashReportClient", "UnityCrashHandler*", "crashpad_handler", "BsSndRpt*", "CrashHandler*", "*CrashReporter*",
            "EasyAntiCheat*Setup*", "EasyAntiCheat_EOS_Setup", "BEService*", "*AntiCheat*Installer*",
            "vc_redist*", "vcredist*", "dxsetup", "dotnetfx*", "*prereq*", "UE4PrereqSetup*", "UEPrereqSetup*", "unins*", "*uninstall*",
            "REDprelauncher",
        ];
        Assert.All(required, r => Assert.Contains(r, HelperExecutables.DefaultPatterns));
        Assert.DoesNotContain(HelperExecutables.DefaultPatterns, p => HelperExecutables.GlobMatch("gamelaunchhelper", p.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("UNITYCRASHHANDLER64.EXE")]
    [InlineData("unitycrashhandler64.exe")]
    [InlineData("C:/Spiele/Indie/UnityCrashHandler64.exe")]
    [InlineData("\"C:\\Program Files\\Spiel\\UnityCrashHandler64.exe\"")]
    [InlineData("UnityCrashHandler64")]
    public void Matching_ignores_case_slashes_quotes_and_missing_extension(string exe) => Assert.True(HelperExecutables.IsHelper(exe));

    [Theory]
    [InlineData("game1.exe", "game?", true)]
    [InlineData("game12.exe", "game?", false)]
    [InlineData("game.exe", "game?", false)]
    [InlineData("Tool.exe", "tool.EXE", true)]
    [InlineData("Tool.exe", "*", true)]
    [InlineData("Tool.exe", "*.exe", true)]
    [InlineData("MyTool.exe", "*tool", true)]
    [InlineData("MyTool2.exe", "*tool", false)]
    [InlineData("a.b.exe", "a?b", true)]
    [InlineData("setup.exe", "setup", true)]
    [InlineData("gamesetup.exe", "setup", false)]
    public void Custom_patterns_support_star_and_question_mark(string exe, string pattern, bool expected) =>
        Assert.Equal(expected, HelperExecutables.IsHelper(exe, [pattern]));

    [Fact]
    public void Missing_patterns_fall_back_to_defaults_and_empty_list_matches_nothing()
    {
        Assert.True(HelperExecutables.IsHelper("vc_redist.x64.exe", null));
        Assert.False(HelperExecutables.IsHelper("vc_redist.x64.exe", []));
    }

    [Fact]
    public void Broken_pattern_entries_and_empty_names_do_not_throw()
    {
        string[] patterns = ["", "   ", @"C:\Spiele\*.exe", "Ordner/*", " crashpad_handler "];

        Assert.True(HelperExecutables.IsHelper("crashpad_handler.exe", patterns));
        Assert.False(HelperExecutables.IsHelper("spiel.exe", patterns));
        Assert.False(HelperExecutables.IsHelper(string.Empty));
        Assert.False(HelperExecutables.IsHelper("   "));
        Assert.False(HelperExecutables.IsHelper(@"C:\Spiele\"));
        Assert.False(HelperExecutables.IsHelper(".exe"));
    }

    [Fact]
    public void Incomplete_file_name_without_extension_or_with_other_extension()
    {
        Assert.True(HelperExecutables.IsHelper("dxsetup"));
        Assert.False(HelperExecutables.IsHelper("dxsetup.dll"));
        Assert.True(HelperExecutables.IsHelper("dxsetup.dll", ["dxsetup.*"]));
    }
}
