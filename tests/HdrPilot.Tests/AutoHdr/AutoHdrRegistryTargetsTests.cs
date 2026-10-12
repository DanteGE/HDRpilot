using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Welche EXEs eines Spiels den Auto-HDR-Wert bekommen (E88): bekannte Spiel-EXEs mit vollem Pfad, ohne vom Nutzer
/// entfernte, Hilfsprogramme, Anti-Cheat-Starter, Absturzmelder und Installer.
/// </summary>
public sealed class AutoHdrRegistryTargetsTests
{
    private const string Dir = @"D:\Games\Spiel";

    private static GameConfig Game(params string[] known) => new()
    {
        Source = GameSource.Steam,
        SourceId = "1",
        Name = "Spiel",
        InstallDir = Dir,
        KnownExecutables = [.. known],
        AutoHdr = true,
    };

    [Fact]
    public void Detected_then_known_then_extra_without_duplicates_bare_names_or_non_exe()
    {
        var game = Game(Dir + @"\Known.exe", Dir + @"/bin/../Detected.EXE");
        game.DetectedExecutables = [Dir + @"\Detected.exe", Dir + @"\\Second.exe"];
        game.ExtraExecutables = ["Bare.exe", @"E:\Other\Extra.exe", "relative\\Rel.exe", Dir + @"\readme.txt", " "];

        var targets = AutoHdrTargets.For(game, null);

        Assert.Equal([Dir + @"\Detected.exe", Dir + @"\Second.exe", Dir + @"\Known.exe", @"E:\Other\Extra.exe"], targets);
    }

    [Fact]
    public void Excluded_by_the_user_by_path_or_by_name()
    {
        var game = Game(Dir + @"\Game.exe", Dir + @"\Tool.exe", Dir + @"\sub\Game.exe", Dir + @"\Keep.exe");
        game.ExcludedExecutables = ["tool.exe", Dir + @"/sub/Game.exe"];

        Assert.Equal([Dir + @"\Game.exe", Dir + @"\Keep.exe"], AutoHdrTargets.For(game, null));

        // Gegenprobe: ohne Ausschluss alle vier.
        game.ExcludedExecutables = [];
        Assert.Equal(4, AutoHdrTargets.For(game, null).Count);
    }

    [Theory]
    [InlineData(@"\UnityCrashHandler64.exe")]
    [InlineData(@"\CrashReportClient.exe")]
    [InlineData(@"\start_protected_game.exe")]
    [InlineData(@"\Game_BE.exe")]
    [InlineData(@"\GameClientEAC.exe")]
    [InlineData(@"\EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe")]
    [InlineData(@"\BattlEye\Launcher.exe")]
    [InlineData(@"\EOSBootstrapper.exe")]
    [InlineData(@"\_CommonRedist\vcredist_x64.exe")]
    [InlineData(@"\unins000.exe")]
    [InlineData(@"\Launcher.exe")]
    [InlineData(@"\Engine\Binaries\Win64\UnrealEditor.exe")]
    public void Helpers_anti_cheat_starters_and_installers_never_get_the_value(string relative)
    {
        var game = Game(Dir + @"\Game.exe", Dir + relative);
        Assert.Equal([Dir + @"\Game.exe"], AutoHdrTargets.For(game, null));
    }

    [Fact]
    public void Configured_helper_list_replaces_the_default_but_icon_exclusions_stay()
    {
        var game = Game(Dir + @"\Game.exe", Dir + @"\Launcher.exe", Dir + @"\Companion.exe", Dir + @"\start_protected_game.exe");

        // Eigene Liste: „Companion*“ fällt weg, „Launcher“ (nur Standardliste) bleibt; Anti-Cheat-Starter bleiben ausgeschlossen.
        Assert.Equal([Dir + @"\Game.exe", Dir + @"\Launcher.exe"], AutoHdrTargets.For(game, ["Companion*"]));
        Assert.Equal([Dir + @"\Game.exe", Dir + @"\Companion.exe"], AutoHdrTargets.For(game, null));
    }

    [Fact]
    public void Shared_exe_belongs_to_both_games_and_games_without_the_option_contribute_nothing()
    {
        var config = new AppConfig();
        var a = Game(Dir + @"\Shared.exe", Dir + @"\A.exe");
        var b = Game(Dir + @"\shared.EXE");
        b.SourceId = "2";
        var off = Game(Dir + @"\Off.exe");
        off.SourceId = "3";
        off.AutoHdr = false;
        off.HdrAuto = true;
        config.Games.AddRange([a, b, off]);

        var desired = AutoHdrTargets.Desired(config);

        Assert.Equal([Dir + @"\Shared.exe", Dir + @"\A.exe"], desired.TargetOrder);
        Assert.Equal(["Steam:1", "Steam:2"], desired.Targets[Dir + @"\SHARED.exe"]);
        Assert.Equal(["Steam:1", "Steam:2"], desired.ByGame.Keys.Order(StringComparer.Ordinal));
        Assert.False(desired.Targets.ContainsKey(Dir + @"\Off.exe"));
    }

    [Fact]
    public void Signature_follows_targets_and_their_games_only()
    {
        var config = new AppConfig();
        config.Games.Add(Game(Dir + @"\A.exe"));
        var first = AutoHdrTargets.Desired(config).Signature;

        config.Games[0].LastPlayed = DateTimeOffset.UnixEpoch;
        config.Games[0].HdrAuto = true;
        config.Settings.LibrarySortOrder = "LastPlayed";
        Assert.Equal(first, AutoHdrTargets.Desired(config).Signature);

        config.Games[0].DetectedExecutables = [Dir + @"\B.exe"];
        Assert.NotEqual(first, AutoHdrTargets.Desired(config).Signature);

        config.Games[0].DetectedExecutables = [];
        config.Games[0].AutoHdr = false;
        Assert.Equal(string.Empty, AutoHdrTargets.Desired(config).Signature);
    }

    [Fact]
    public void No_file_system_access_missing_exes_are_still_targets()
    {
        var game = Game(@"Z:\Nirgends\Fehlt.exe");
        Assert.Equal([@"Z:\Nirgends\Fehlt.exe"], AutoHdrTargets.For(game, null));
    }
}
