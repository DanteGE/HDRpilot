using HdrPilot.Core.Launch;

namespace HdrPilot.Tests.Launch;

/// <summary>Rohe Befehlszeile der Steam-Startoption: Erkennen, Spielbefehl abtrennen, EXE und Argumente, Arbeitsordner.</summary>
public sealed class WrapCommandLineTests
{
    private const string Self = @"""C:\Program Files\HdrPilot\HdrPilot.exe""";
    private const string Cyberpunk = @"D:\Steam Library\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";

    // ---------- Erkennen und Abtrennen ----------

    [Fact]
    public void Quoted_game_exe_with_spaces_keeps_arguments_character_for_character()
    {
        var raw = Self + @" --wrap -- """ + Cyberpunk + @""" -skipStartScreen -name=""foo bar""   --launcher-skip ";

        var request = WrapCommandLine.TryParse(raw)!;
        var game = WrapCommandLine.SplitGameCommand(request.GameCommand)!;

        Assert.True(request.SeparatorFound);
        Assert.Null(request.ConfigDir);
        Assert.Equal(raw, request.RawCommandLine);
        Assert.Equal(@"""" + Cyberpunk + @""" -skipStartScreen -name=""foo bar""   --launcher-skip ", request.GameCommand);
        Assert.Equal(Cyberpunk, game.ExePath);
        Assert.Equal(@"-skipStartScreen -name=""foo bar""   --launcher-skip ", game.Arguments);
    }

    [Fact]
    public void Unquoted_game_exe_with_spaces_in_path_is_recognised_by_its_extension()
    {
        var request = WrapCommandLine.TryParse(Self + " --wrap -- " + Cyberpunk + " -windowed  -x")!;
        var game = WrapCommandLine.SplitGameCommand(request.GameCommand)!;

        Assert.Equal(Cyberpunk, game.ExePath);
        Assert.Equal("-windowed  -x", game.Arguments);
    }

    [Fact]
    public void Unquoted_game_exe_without_arguments()
    {
        var game = WrapCommandLine.SplitGameCommand(Cyberpunk)!;

        Assert.Equal(Cyberpunk, game.ExePath);
        Assert.Equal(string.Empty, game.Arguments);
    }

    [Theory]
    [InlineData(@"D:\Games\Start Game.bat -x", @"D:\Games\Start Game.bat", "-x")]
    [InlineData(@"D:\Games\Start Game.BAT", @"D:\Games\Start Game.BAT", "")]
    [InlineData(@"D:\Games\Mein Spiel\run.CMD /fast", @"D:\Games\Mein Spiel\run.CMD", "/fast")]
    [InlineData("D:\\Games\\Spiel\\Game.Exe\t-tab", @"D:\Games\Spiel\Game.Exe", "-tab")]
    [InlineData(@"D:\Games\Spiel\game.exe.config\game.exe -a", @"D:\Games\Spiel\game.exe.config\game.exe", "-a")]
    public void Unquoted_bat_cmd_and_exe_in_any_case(string command, string exe, string arguments)
    {
        var game = WrapCommandLine.SplitGameCommand(command)!;

        Assert.Equal(exe, game.ExePath);
        Assert.Equal(arguments, game.Arguments);
    }

    [Fact]
    public void Unquoted_path_with_exe_inside_a_folder_name_prefers_the_existing_file()
    {
        const string Command = @"D:\Games\my.exe tools\game.exe -a other.exe";

        var guessed = WrapCommandLine.SplitGameCommand(Command)!;
        var checkedGame = WrapCommandLine.SplitGameCommand(Command, p => p == @"D:\Games\my.exe tools\game.exe")!;

        Assert.Equal(@"D:\Games\my.exe", guessed.ExePath); // ohne Datei: erste Stelle, wie CreateProcess
        Assert.Equal(@"D:\Games\my.exe tools\game.exe", checkedGame.ExePath);
        Assert.Equal("-a other.exe", checkedGame.Arguments);
    }

    [Fact]
    public void Unquoted_without_known_extension_takes_first_word()
    {
        var game = WrapCommandLine.SplitGameCommand(@"D:\Games\launcher --fast")!;

        Assert.Equal(@"D:\Games\launcher", game.ExePath);
        Assert.Equal("--fast", game.Arguments);
    }

    [Fact]
    public void Quoted_exe_without_closing_quote_takes_the_rest()
    {
        var game = WrapCommandLine.SplitGameCommand(@"""D:\Games\Spiel\game.exe")!;

        Assert.Equal(@"D:\Games\Spiel\game.exe", game.ExePath);
        Assert.Equal(string.Empty, game.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"""""")]
    public void Empty_game_command_gives_nothing_to_start(string command)
    {
        Assert.Null(WrapCommandLine.SplitGameCommand(command));
    }

    [Fact]
    public void Game_arguments_containing_double_dash_and_hdrpilot_switches_stay_with_the_game()
    {
        var request = WrapCommandLine.TryParse(Self + @" --wrap -- ""D:\g.exe"" -- --config-dir X --exit")!;
        var game = WrapCommandLine.SplitGameCommand(request.GameCommand)!;

        Assert.Null(request.ConfigDir);
        Assert.Equal("-- --config-dir X --exit", game.Arguments);
    }

    [Fact]
    public void Missing_separator_falls_back_to_everything_after_wrap()
    {
        var request = WrapCommandLine.TryParse(Self + @" --wrap ""D:\Spielordner\g.exe"" -x")!;

        Assert.False(request.SeparatorFound);
        Assert.Equal(@"""D:\Spielordner\g.exe"" -x", request.GameCommand);
    }

    [Theory]
    [InlineData(@"""C:\Program Files\HdrPilot\HdrPilot.exe""")]
    [InlineData(@"""C:\Program Files\HdrPilot\HdrPilot.exe"" --run Steam:1")]
    [InlineData(@"""C:\Program Files\HdrPilot\HdrPilot.exe"" --minimized -- --wrap")]
    [InlineData(@"""C:\Program Files\HdrPilot\HdrPilot.exe"" --wrapper -- game.exe")]
    [InlineData(@"""C:\Program Files\HdrPilot\HdrPilot.exe"" ""--wrap -- game.exe""")]
    [InlineData("")]
    public void Not_a_wrap_call(string raw)
    {
        Assert.Null(WrapCommandLine.TryParse(raw));
    }

    [Fact]
    public void Wrap_switch_ignores_case()
    {
        Assert.NotNull(WrapCommandLine.TryParse(Self + " --WRAP -- game.exe"));
    }

    [Fact]
    public void Double_dash_inside_quoted_hdrpilot_path_is_not_the_separator()
    {
        var request = WrapCommandLine.TryParse(@"""C:\Tools -- Neu\HdrPilot.exe"" --wrap -- game.exe -x")!;

        Assert.Equal("game.exe -x", request.GameCommand);
    }

    [Fact]
    public void Unquoted_hdrpilot_path_with_spaces_and_tabs()
    {
        var request = WrapCommandLine.TryParse("C:\\Program Files\\HdrPilot\\HdrPilot.exe\t--wrap\t--\t\"D:\\g.exe\" -a")!;

        Assert.Equal("\"D:\\g.exe\" -a", request.GameCommand);
    }

    [Fact]
    public void Config_dir_before_wrap_is_read_with_quotes()
    {
        var request = WrapCommandLine.TryParse(Self + @" --config-dir ""C:\Temp\scratch dir\x"" --wrap -- ""D:\g.exe""")!;

        Assert.Equal(@"C:\Temp\scratch dir\x", request.ConfigDir);
        Assert.Equal(@"""D:\g.exe""", request.GameCommand);
    }

    [Fact]
    public void Config_dir_between_wrap_and_separator_is_ignored()
    {
        var request = WrapCommandLine.TryParse(Self + @" --wrap --config-dir C:\x -- ""D:\g.exe""")!;

        Assert.Null(request.ConfigDir);
        Assert.Equal(@"""D:\g.exe""", request.GameCommand);
    }

    // ---------- Arbeitsordner ----------

    [Theory]
    [InlineData(@"D:\Steam Library\steamapps\common\Cyberpunk 2077", @"D:\Steam Library\steamapps\common\Cyberpunk 2077")]
    [InlineData(@"E:\Woanders", @"E:\Woanders")]
    [InlineData("", @"D:\Steam Library\steamapps\common\Cyberpunk 2077\bin\x64")]
    [InlineData(null, @"D:\Steam Library\steamapps\common\Cyberpunk 2077\bin\x64")]
    [InlineData(@"C:\WINDOWS\System32", @"D:\Steam Library\steamapps\common\Cyberpunk 2077\bin\x64")]
    [InlineData(@"C:\Windows\system32\de-DE", @"D:\Steam Library\steamapps\common\Cyberpunk 2077\bin\x64")]
    [InlineData(@"C:\Program Files\HdrPilot\", @"D:\Steam Library\steamapps\common\Cyberpunk 2077\bin\x64")]
    [InlineData(@"c:\program files\hdrpilot", @"D:\Steam Library\steamapps\common\Cyberpunk 2077\bin\x64")]
    public void Working_directory_is_inherited_unless_unusable(string? current, string expected)
    {
        Assert.Equal(expected, WrapCommandLine.ChooseWorkingDirectory(current, @"C:\Program Files\HdrPilot", Cyberpunk));
    }

    [Fact]
    public void Working_directory_without_game_folder_stays_inherited()
    {
        Assert.Equal(@"C:\Windows\System32", WrapCommandLine.ChooseWorkingDirectory(@"C:\Windows\System32", @"C:\HdrPilot", "game.exe"));
    }

    // ---------- Spielschlüssel aus der Umgebung ----------

    [Theory]
    [InlineData("1091500", null, "Steam:1091500", "1091500")]
    [InlineData(" 42 ", null, "Steam:42", "42")]
    [InlineData(null, "480", "Steam:480", "480")]
    [InlineData("", "480", "Steam:480", "480")]
    [InlineData("1091500", "480", "Steam:1091500", "1091500")]
    [InlineData("abc", "480", null, "abc")]
    [InlineData("0", null, null, "0")]
    [InlineData(null, "12345678901234567890", null, "12345678901234567890")]
    [InlineData(null, null, null, null)]
    public void Steam_game_key_from_SteamAppId_with_SteamGameId_fallback(string? appId, string? gameId, string? expectedKey, string? expectedRaw)
    {
        var env = new Dictionary<string, string?> { ["SteamAppId"] = appId, ["SteamGameId"] = gameId };

        var key = WrapCommandLine.SteamGameKey(name => env.GetValueOrDefault(name), out var raw);

        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedRaw, raw);
    }
}
