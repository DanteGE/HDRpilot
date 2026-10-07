using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Tests.TestSupport;

/// <summary>Die Attrappen selbst müssen sich wie Windows verhalten, sonst beweisen Scanner-Tests nichts.</summary>
public sealed class TestSupportTests
{
    [Fact]
    public void FileSystem_is_case_insensitive_and_creates_parent_directories()
    {
        var fs = new InMemoryFileSystem().AddFile(@"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf", "x");

        Assert.True(fs.FileExists(@"c:/program files (x86)/steam/STEAMAPPS/libraryfolders.vdf"));
        Assert.True(fs.DirectoryExists(@"C:\Program Files (x86)\Steam"));
        Assert.True(fs.DirectoryExists(@"C:\"));
        Assert.False(fs.DirectoryExists(@"C:\Program Files"));
        Assert.Equal("x", fs.ReadAllText(@"C:\PROGRAM FILES (X86)\Steam\steamapps\libraryfolders.vdf"));
    }

    [Fact]
    public void FileSystem_enumerates_with_wildcards_and_recursion()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Lib\steamapps\appmanifest_1.acf", "a")
            .AddFile(@"D:\Lib\steamapps\appmanifest_22.acf", "b")
            .AddFile(@"D:\Lib\steamapps\libraryfolders.vdf", "c")
            .AddFile(@"D:\Lib\steamapps\common\Game\bin\game.exe", "d");

        Assert.Equal(2, fs.EnumerateFiles(@"D:\Lib\steamapps", "appmanifest_*.acf").Count());
        Assert.Empty(fs.EnumerateFiles(@"D:\Lib\steamapps", "*.exe"));
        Assert.Single(fs.EnumerateFiles(@"D:\Lib\steamapps", "*.exe", recursive: true));
        Assert.Equal([@"D:\Lib\steamapps\common"], fs.EnumerateDirectories(@"D:\Lib\steamapps"));
        Assert.Empty(fs.EnumerateFiles(@"Z:\fehlt"));
    }

    [Fact]
    public void FileSystem_read_failures_and_bom()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"C:\a.json", [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}'])
            .AddFile(@"C:\locked.item", "{}")
            .FailReading(@"C:\locked.item", new IOException("gesperrt"));

        Assert.Equal("{}", fs.ReadAllText(@"C:\a.json"));
        Assert.Throws<IOException>(() => fs.ReadAllText(@"C:\locked.item"));
        Assert.Throws<FileNotFoundException>(() => fs.ReadAllText(@"C:\fehlt.txt"));
    }

    [Fact]
    public void Registry_separates_views_and_maps_wow6432node()
    {
        var reg = new FakeRegistry()
            .Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\GOG.com\Games\1207658946", "gameName", "Test", RegistryViewKind.Registry32)
            .Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Native", "v", 1);

        using (var viaView = reg.OpenKey(RegistryHiveKind.LocalMachine, @"Software\GOG.com\Games", RegistryViewKind.Registry32))
        {
            Assert.NotNull(viaView);
            Assert.Equal(["1207658946"], viaView!.GetSubKeyNames());
            using var game = viaView.OpenSubKey("1207658946");
            Assert.Equal("Test", game!.GetString("gameName"));
        }

        using (var viaWow = reg.OpenKey(RegistryHiveKind.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\Games\1207658946"))
        {
            Assert.Equal("Test", viaWow!.GetString("gameName"));
        }

        Assert.Null(reg.OpenKey(RegistryHiveKind.LocalMachine, @"SOFTWARE\GOG.com\Games")); // nicht in der 64-Bit-Sicht
        Assert.Null(reg.OpenKey(RegistryHiveKind.LocalMachine, @"SOFTWARE\Native", RegistryViewKind.Registry32));
        Assert.Equal(1, reg.Get(RegistryHiveKind.LocalMachine, @"SOFTWARE\Native", "v", RegistryViewKind.Registry64));
    }

    [Fact]
    public void Registry_denied_key_returns_null_and_write_works()
    {
        var reg = new FakeRegistry()
            .Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", "c:/program files (x86)/steam")
            .DenyAccess(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam");

        Assert.Null(reg.OpenKey(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam"));
        Assert.Null(reg.OpenKey(RegistryHiveKind.CurrentUser, @"Software\Valve")!.OpenSubKey("Steam"));

        using var run = reg.CreateKey(RegistryHiveKind.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");
        run.SetValue("HdrPilot", "x");
        Assert.Equal("x", reg.Get(RegistryHiveKind.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HdrPilot"));
        run.DeleteValue("HdrPilot");
        Assert.Null(reg.Get(RegistryHiveKind.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HdrPilot"));
    }

    [Theory]
    [InlineData(@"c:/program files (x86)/steam", @"c:\program files (x86)\steam")]
    [InlineData(@"C:\Games\", @"C:\Games")]
    [InlineData(@"C:\Games\\Sub\\", @"C:\Games\Sub")]
    [InlineData(@"D:", @"D:\")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData(@"""C:\Quoted Path\""", @"C:\Quoted Path")]
    [InlineData("", "")]
    public void PathUtil_normalizes(string input, string expected) => Assert.Equal(expected, PathUtil.Normalize(input));

    [Theory]
    [InlineData(@"C:\Games\Foo\bin\foo.exe", @"C:\Games\Foo", true)]
    [InlineData(@"C:\Games\Foo", @"C:\Games\Foo\", true)]
    [InlineData(@"C:\Games\FooBar\foo.exe", @"C:\Games\Foo", false)]
    [InlineData(@"c:\games\foo\x.exe", @"C:\GAMES\FOO", true)]
    [InlineData(@"D:\x.exe", @"D:\", true)]
    [InlineData(@"C:\x.exe", @"D:\", false)]
    [InlineData("", @"C:\", false)]
    public void PathUtil_is_under(string path, string dir, bool expected) => Assert.Equal(expected, PathUtil.IsUnder(path, dir));
}
