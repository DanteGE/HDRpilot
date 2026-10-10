using HdrPilot.Core.Games;

namespace HdrPilot.Tests.Games;

/// <summary>
/// Ausschlussliste nur für Symbole (E86): je Anti-Cheat-Familie ein Test (Wunsch des Nutzers nach Gears of War: E-Day
/// und The Isle), dazu Fremdbestandteile, Werkzeuge und Gegenproben mit echten Spiel-EXEs.
/// </summary>
public sealed class IconExcludedExecutablesTests
{
    private const string Game = @"D:\Steam-Bibliothek\steamapps\common\Spiel";

    [Theory]
    [InlineData("start_protected_game.exe")]
    [InlineData("EasyAntiCheat.exe")]
    [InlineData("EasyAntiCheat_EOS_Setup.exe")]
    [InlineData("EasyAntiCheat_Setup.exe")]
    [InlineData("GZWClientEAC.exe")]
    [InlineData("InsurgencyEAC.exe")]
    [InlineData("BattleBitEAC.exe")]
    [InlineData("EACLaunch.exe")]
    [InlineData("eac_launcher.exe")]
    public void Icon_exclusion_covers_EasyAntiCheat(string name) => Assert.True(IconExcludedExecutables.IsExcluded(Game + @"\" + name, Game));

    [Theory]
    [InlineData(@"DayZ_BE.exe")]
    [InlineData(@"Arma3_BE.exe")]
    [InlineData(@"BEService_x64.exe")]
    [InlineData(@"BELauncher.exe")]
    [InlineData(@"arma3battleye.exe")]
    [InlineData(@"BattlEye\Install_BattlEye.exe")]
    [InlineData(@"BattlEye\BEClient.exe")]
    public void Icon_exclusion_covers_BattlEye(string name) => Assert.True(IconExcludedExecutables.IsExcluded(Game + @"\" + name, Game));

    [Theory]
    [InlineData("EAAntiCheat.GameServiceLauncher.exe")]
    [InlineData("EAAntiCheat.Installer.exe")]
    [InlineData("AntiCheatExpert.exe")]
    public void Icon_exclusion_covers_EA_AntiCheat(string name) => Assert.True(IconExcludedExecutables.IsExcluded(Game + @"\" + name, Game));

    [Theory]
    [InlineData(@"GameMon.exe")]
    [InlineData(@"GameMon64.exe")]
    [InlineData(@"GameGuard.exe")]
    [InlineData(@"GameGuard\GameGuardUpdater.exe")]
    [InlineData(@"nProtectSetup.exe")]
    public void Icon_exclusion_covers_GameGuard(string name) => Assert.True(IconExcludedExecutables.IsExcluded(Game + @"\" + name, Game));

    [Theory]
    [InlineData("EOSBootstrapper.exe")]
    [InlineData("EpicOnlineServicesInstaller.exe")]
    [InlineData("EpicOnlineServicesHelper.exe")]
    [InlineData("EOSOverlayRenderer-Win64-Shipping.exe")]
    public void Icon_exclusion_covers_Epic_Online_Services(string name) => Assert.True(IconExcludedExecutables.IsExcluded(Game + @"\" + name, Game));

    [Theory]
    [InlineData(@"Engine\Binaries\Win64\CrashReportClient.exe")]
    [InlineData(@"UnityCrashHandler64.exe")]
    [InlineData(@"Engine\Extras\Redist\en-us\UE4PrereqSetup_x64.exe")]
    [InlineData(@"UEPrereqSetup_x64.exe")]
    [InlineData(@"vc_redist.x64.exe")]
    [InlineData(@"vcredist_x86.exe")]
    [InlineData(@"DXSETUP.exe")]
    [InlineData(@"unins000.exe")]
    [InlineData(@"_CommonRedist\DirectX\Jun2010\DXSETUP.exe")]
    [InlineData(@"_CommonRedist\vcredist\2019\Spielhilfe.exe")]
    public void Icon_exclusion_covers_crash_reporters_redistributables_and_installers(string name) =>
        Assert.True(IconExcludedExecutables.IsExcluded(Game + @"\" + name, Game));

    [Theory]
    [InlineData("LaunchGame.exe")]
    [InlineData("GodotWorkshopUtility.exe")]
    [InlineData("P4PopulateDepot.exe")]
    [InlineData(@"bin\elementviewer.exe")]
    [InlineData("deadbolt_map_editor.exe")]
    [InlineData("EDITOR.exe")]
    [InlineData(@"jre\bin\java.exe")]
    [InlineData(@"jre\bin\javaw.exe")]
    [InlineData(@"game\bin\win64\vconsole2.exe")]
    [InlineData(@"bin\qc_eyes.exe")]
    [InlineData(@"Binaries\UnrealFrontend.exe")]
    [InlineData(@"Binaries\ShaderKeyTool.exe")]
    [InlineData(@"Binaries\StatsViewer.exe")]
    [InlineData(@"Binaries\Win64\UE3ShaderCompileWorker.exe")]
    public void Icon_exclusion_covers_tools_found_in_the_measurement(string name) =>
        Assert.True(IconExcludedExecutables.IsExcluded(Game + @"\" + name, Game));

    [Theory]
    [InlineData(@"TheIsle\Binaries\Win64\TheIsleClient-Win64-Shipping.exe")]
    [InlineData(@"FairlightConcept\Binaries\Win64\GoWEDay-Steam.exe")]
    [InlineData(@"BeachInvasion1944.exe")]
    [InlineData(@"Binaries\Win64\BEGame.exe")]
    [InlineData(@"Unreal.exe")]
    [InlineData(@"UnrealTournament.exe")]
    [InlineData(@"PEACE.exe")]
    [InlineData(@"EACH.exe")]
    [InlineData(@"Peacekeeper.exe")]
    [InlineData(@"Agent 64 Spies Never Die.exe")]
    [InlineData(@"bin64\CrimsonDesert.exe")]
    public void Icon_exclusion_keeps_game_executables(string name) => Assert.False(IconExcludedExecutables.IsExcluded(Game + @"\" + name, Game));

    [Fact]
    public void Icon_exclusion_folder_rule_counts_only_folders_below_the_install_dir()
    {
        // Liegt der Spielordner selbst unter „Redist“, ist das kein Grund, die Spiel-EXE auszuschließen.
        Assert.False(IconExcludedExecutables.IsExcluded(@"E:\Redist\Spiele\Foo\Foo.exe", @"E:\Redist\Spiele\Foo"));
        Assert.True(IconExcludedExecutables.IsExcluded(@"E:\Spiele\Foo\Redist\Foo.exe", @"E:\Spiele\Foo"));

        // Ohne Spielordner zählt nur der direkte Ordner.
        Assert.True(IconExcludedExecutables.IsExcluded(@"E:\Spiele\Foo\BattlEye\Foo.exe"));
        Assert.False(IconExcludedExecutables.IsExcluded(@"E:\BattlEye\Spiele\Foo.exe"));
        Assert.False(IconExcludedExecutables.IsExcluded(null));
        Assert.False(IconExcludedExecutables.IsExcluded("   "));
    }

    [Fact]
    public void Icon_exclusion_is_not_part_of_the_helper_list()
    {
        // start_protected_game.exe und *EAC.exe bleiben ein früher Start-Hinweis für HDR (HelperExecutables, E86).
        foreach (var name in new[] { "start_protected_game.exe", "GZWClientEAC.exe", "LaunchGame.exe", "EOSBootstrapper.exe", "java.exe" })
        {
            Assert.True(IconExcludedExecutables.IsExcluded(name));
            Assert.False(HelperExecutables.IsHelper(name), name + " darf nicht in HelperExecutables stehen");
        }
    }

    [Theory]
    [InlineData("GZWClientEAC", "GZW|Client|EAC")]
    [InlineData("EACLaunch", "EAC|Launch")]
    [InlineData("BeachInvasion1944", "Beach|Invasion")]
    [InlineData("eac_launcher", "eac|launcher")]
    [InlineData("PEACE", "PEACE")]
    public void Icon_exclusion_splits_names_into_words(string stem, string words) =>
        Assert.Equal(words.Split('|'), IconExcludedExecutables.Words(stem));
}
