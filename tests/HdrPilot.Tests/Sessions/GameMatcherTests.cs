using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Sessions;

namespace HdrPilot.Tests.Sessions;

public sealed class GameMatcherTests
{
    private static GameConfig Game(string id, string dir) => new() { Source = GameSource.Steam, SourceId = id, Name = id, InstallDir = dir };

    private static GameMatcher Matcher(params GameConfig[] games) =>
        new(games, name => name.Contains("Crash", StringComparison.OrdinalIgnoreCase), [@"C:\Windows", @"C:\Program Files"]);

    [Fact]
    public void Process_under_install_dir_matches_case_insensitive()
    {
        var m = Matcher(Game("a", @"D:\Games\Alpha"));
        Assert.Equal("a", m.Match(new ProcessInfo(1, "alpha.exe", @"d:\games\ALPHA\bin\alpha.exe"))?.SourceId);
    }

    [Fact]
    public void Sibling_folder_with_common_prefix_does_not_match()
    {
        var m = Matcher(Game("a", @"D:\Games\Alpha"));
        Assert.Null(m.Match(new ProcessInfo(1, "x.exe", @"D:\Games\AlphaBeta\x.exe")));
    }

    [Fact]
    public void Most_specific_install_dir_wins()
    {
        var m = Matcher(Game("outer", @"D:\Games"), Game("inner", @"D:\Games\Inner"));
        Assert.Equal("inner", m.Match(new ProcessInfo(1, "i.exe", @"D:\Games\Inner\i.exe"))?.SourceId);
        Assert.Equal("outer", m.Match(new ProcessInfo(2, "o.exe", @"D:\Games\Other\o.exe"))?.SourceId);
    }

    [Fact]
    public void Helper_processes_never_match()
    {
        var m = Matcher(Game("a", @"D:\Games\Alpha"));
        Assert.Null(m.Match(new ProcessInfo(1, "UnityCrashHandler64.exe", @"D:\Games\Alpha\UnityCrashHandler64.exe")));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Program Files\")]
    [InlineData("")]
    [InlineData("relative\\path")]
    public void Dangerous_install_dirs_are_ignored(string dir)
    {
        var m = Matcher(Game("a", dir));
        Assert.Null(m.Match(new ProcessInfo(1, "explorer.exe", @"C:\Windows\explorer.exe")));
        Assert.Null(m.Match(new ProcessInfo(2, "x.exe", @"D:\x.exe")));
    }

    [Fact]
    public void Excluded_by_name_or_path()
    {
        var g = Game("a", @"D:\Games\Alpha");
        g.ExcludedExecutables = ["Launcher.exe", @"D:\Games\Alpha\tools\editor.exe"];
        var m = Matcher(g);
        Assert.Null(m.Match(new ProcessInfo(1, "launcher.exe", @"D:\Games\Alpha\launcher.exe")));
        Assert.Null(m.Match(new ProcessInfo(2, "editor.exe", @"D:\Games\Alpha\tools\editor.exe")));
        Assert.NotNull(m.Match(new ProcessInfo(3, "editor.exe", @"D:\Games\Alpha\editor.exe")));
    }

    [Fact]
    public void Extra_executable_outside_install_dir_matches()
    {
        var g = Game("a", @"D:\Games\Alpha");
        g.ExtraExecutables = [@"C:\Users\Test\AppData\Local\Alpha\AlphaGame.exe", "OnlyName.exe"];
        var m = Matcher(g);
        Assert.NotNull(m.Match(new ProcessInfo(1, "AlphaGame.exe", @"C:\Users\Test\AppData\Local\Alpha\AlphaGame.exe")));
        Assert.NotNull(m.Match(new ProcessInfo(2, "onlyname.exe", @"E:\Somewhere\OnlyName.exe")));
        Assert.Null(m.Match(new ProcessInfo(3, "AlphaGame.exe", @"C:\Other\AlphaGame.exe")));
    }

    [Fact]
    public void Unreadable_path_matches_only_unique_known_name()
    {
        var a = Game("a", @"D:\Games\Alpha");
        a.DetectedExecutables = [@"D:\Games\Alpha\game.exe"];
        var b = Game("b", @"D:\Games\Beta");
        b.KnownExecutables = [@"D:\Games\Beta\game.exe", @"D:\Games\Beta\beta.exe"];
        var m = Matcher(a, b);

        Assert.Null(m.Match(new ProcessInfo(1, "game.exe", null))); // mehrdeutig
        Assert.Equal("b", m.Match(new ProcessInfo(2, "BETA.exe", null))?.SourceId);
        Assert.Null(m.Match(new ProcessInfo(3, "unknown.exe", null)));
    }
}
