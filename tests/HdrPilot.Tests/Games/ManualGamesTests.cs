using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Games;

public sealed class ManualGamesTests
{
    [Fact]
    public void Exe_path_creates_entry_with_exe_launch_and_working_directory()
    {
        var fs = new InMemoryFileSystem().AddFile(@"D:\Spielordner\Doom\gzdoom.exe", "MZ");

        var game = ManualGames.Create(fs, @"D:\Spielordner\Doom\gzdoom.exe", id: "abc");

        Assert.Equal(GameSource.Manual, game.Source);
        Assert.Equal("abc", game.SourceId);
        Assert.Equal("gzdoom", game.Name);
        Assert.Equal(@"D:\Spielordner\Doom", game.InstallDir);
        Assert.Equal([@"D:\Spielordner\Doom\gzdoom.exe"], game.Executables);
        Assert.Equal(@"D:\Spielordner\Doom\gzdoom.exe", game.LaunchCommand);
        Assert.Equal(@"D:\Spielordner\Doom", game.LaunchWorkingDirectory);
        Assert.Null(game.LaunchArguments);
        Assert.Equal("Manual:abc", game.Key);
    }

    [Fact]
    public void Exe_path_with_forward_slashes_and_upper_case_extension_is_normalized()
    {
        var fs = new InMemoryFileSystem().AddFile(@"D:\Spielordner\Alt\GAME.EXE", "MZ");

        var game = ManualGames.Create(fs, "d:/spielordner/alt/game.exe", "Altes Spiel");

        Assert.Equal(@"d:\spielordner\alt", game.InstallDir);
        Assert.Equal([@"d:\spielordner\alt\game.exe"], game.Executables);
        Assert.Equal("Altes Spiel", game.Name);
    }

    [Fact]
    public void Folder_with_single_exe_gets_that_exe_as_launch_command()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\Indie\Indie.exe", "MZ")
            .AddFile(@"D:\Spielordner\Indie\UnityCrashHandler64.exe", "MZ")
            .AddFile(@"D:\Spielordner\Indie\Indie_Data\readme.txt", "x");

        var game = ManualGames.Create(fs, @"D:\Spielordner\Indie\");

        Assert.Equal("Indie", game.Name);
        Assert.Equal(@"D:\Spielordner\Indie", game.InstallDir);
        Assert.Equal([@"D:\Spielordner\Indie\Indie.exe"], game.Executables);
        Assert.Equal(@"D:\Spielordner\Indie\Indie.exe", game.LaunchCommand);
        Assert.Equal(@"D:\Spielordner\Indie", game.LaunchWorkingDirectory);
    }

    [Fact]
    public void Folder_with_several_exes_lists_them_sorted_without_launch_command()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\SWAT\swat_original.exe", "MZ")
            .AddFile(@"D:\Spielordner\SWAT\SWAT.exe", "MZ")
            .AddFile(@"D:\Spielordner\SWAT\unins000.exe", "MZ")
            .AddFile(@"D:\Spielordner\SWAT\Tools\Editor.exe", "MZ");

        var game = ManualGames.Create(fs, @"D:\Spielordner\SWAT");

        Assert.Equal([@"D:\Spielordner\SWAT\SWAT.exe", @"D:\Spielordner\SWAT\swat_original.exe"], game.Executables);
        Assert.Null(game.LaunchCommand);
        Assert.Null(game.LaunchWorkingDirectory);
    }

    [Fact]
    public void Name_is_free_text_and_blank_name_falls_back_to_folder_name()
    {
        var fs = new InMemoryFileSystem().AddFile(@"D:\Spielordner\Mein Spiel\x.exe", "MZ");

        Assert.Equal("Mein Lieblingsspiel™", ManualGames.Create(fs, @"D:\Spielordner\Mein Spiel", "  Mein Lieblingsspiel™ ").Name);
        Assert.Equal("Mein Spiel", ManualGames.Create(fs, @"D:\Spielordner\Mein Spiel", "   ").Name);
    }

    [Fact]
    public void Missing_id_creates_new_guid_in_n_format()
    {
        var fs = new InMemoryFileSystem().AddFile(@"D:\Spielordner\A\a.exe", "MZ");

        var first = ManualGames.Create(fs, @"D:\Spielordner\A");
        var second = ManualGames.Create(fs, @"D:\Spielordner\A", id: " ");

        Assert.Matches("^[0-9a-f]{32}$", first.SourceId);
        Assert.Matches("^[0-9a-f]{32}$", second.SourceId);
        Assert.NotEqual(first.SourceId, second.SourceId);
    }

    [Fact]
    public void Missing_path_throws_argument_exception_with_german_message()
    {
        var fs = new InMemoryFileSystem();

        var ex = Assert.Throws<ArgumentException>(() => ManualGames.Create(fs, @"D:\Gibt\Es\Nicht.exe"));

        Assert.Contains("existiert nicht", ex.Message, StringComparison.Ordinal);
        Assert.Equal("path", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Broken_empty_path_throws(string path)
    {
        var ex = Assert.Throws<ArgumentException>(() => ManualGames.Create(new InMemoryFileSystem(), path));
        Assert.Contains("kein Pfad", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_relative_path_throws()
    {
        var fs = new InMemoryFileSystem().AddFile(@"C:\Spiele\a.exe", "MZ");

        var ex = Assert.Throws<ArgumentException>(() => ManualGames.Create(fs, @"Spiele\a.exe"));

        Assert.Contains("vollständig", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_non_exe_file_throws()
    {
        var fs = new InMemoryFileSystem().AddFile(@"D:\Spielordner\A\Spiel.lnk", "x");

        var ex = Assert.Throws<ArgumentException>(() => ManualGames.Create(fs, @"D:\Spielordner\A\Spiel.lnk"));

        Assert.Contains("Nur EXE-Dateien oder Ordner", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"D:\")]
    [InlineData(@"D:\spiel.exe")]
    public void Broken_drive_root_is_rejected(string path)
    {
        var fs = new InMemoryFileSystem().AddFile(@"D:\spiel.exe", "MZ");

        var ex = Assert.Throws<ArgumentException>(() => ManualGames.Create(fs, path));

        Assert.Contains("ganzes Laufwerk", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Incomplete_folder_without_exe_has_no_executables_and_no_launch_command()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\Leer\daten.pak", "x")
            .AddFile(@"D:\Spielordner\Leer\vc_redist.x64.exe", "MZ")
            .AddFile(@"D:\Spielordner\Leer\bin\spiel.exe", "MZ");

        var game = ManualGames.Create(fs, @"D:\Spielordner\Leer");

        Assert.Empty(game.Executables);
        Assert.Null(game.LaunchCommand);
        Assert.Equal(@"D:\Spielordner\Leer", game.InstallDir);
    }
}
