using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Games;

/// <summary>Symbol-Hinweise der Scanner (E86): Lesen von DisplayIcon/IconFile, nur lokale, vorhandene Dateien, Namen eigener Symbole.</summary>
public sealed class IconFilesTests
{
    [Theory]
    [InlineData(@"""C:\Program Files (x86)\Steam\steam\games\abc.ico"",0", @"C:\Program Files (x86)\Steam\steam\games\abc.ico")]
    [InlineData(@"C:\Program Files (x86)\Steam\steam\games\abc.ico", @"C:\Program Files (x86)\Steam\steam\games\abc.ico")]
    [InlineData(@"C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/data/games/x.ico", @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\data\games\x.ico")]
    [InlineData(@"D:\Games\Spiel\spiel.exe,-101", @"D:\Games\Spiel\spiel.exe")]
    [InlineData(@"D:\Games\Foo, Bar\x.ico", @"D:\Games\Foo, Bar\x.ico")]
    [InlineData(@"%ProgramFiles%\x.ico", null)]
    [InlineData(@"x.ico", null)]
    [InlineData(@"", null)]
    [InlineData(null, null)]
    public void Icon_location_parsing_strips_quotes_index_and_slashes(string? raw, string? expected) =>
        Assert.Equal(expected, IconFiles.ParseIconLocation(raw));

    [Fact]
    public void Icon_candidates_are_local_supported_existing_and_unique()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"C:\Icons\a.ico", "x").AddFile(@"C:\Icons\b.png", "x").AddFile(@"C:\Icons\c.webp", "x")
            .AddFile(@"C:\Icons\d.jpg", "x").AddFile(@"C:\Icons\e.exe", "MZ").AddFile(@"C:\Icons\f.txt", "x");
        var list = new List<string>();

        Assert.True(IconFiles.AddExisting(list, fs, @"C:\Icons\a.ico"));
        Assert.False(IconFiles.AddExisting(list, fs, @"c:\icons\A.ICO")); // doppelt
        Assert.False(IconFiles.AddExisting(list, fs, @"C:\Icons\fehlt.ico"));
        Assert.False(IconFiles.AddExisting(list, fs, @"C:\Icons\f.txt"));
        Assert.False(IconFiles.AddExisting(list, fs, @"\\server\share\a.ico")); // nie übers Netz
        Assert.False(IconFiles.AddExisting(list, fs, @"\\?\C:\Icons\a.ico"));
        Assert.False(IconFiles.AddExisting(list, fs, null));
        Assert.True(IconFiles.AddExisting(list, fs, @"C:\Icons\b.png"));
        Assert.True(IconFiles.AddExisting(list, fs, @"C:\Icons\c.webp"));
        Assert.True(IconFiles.AddExisting(list, fs, @"C:\Icons\d.jpg"));
        Assert.False(IconFiles.AddExisting(list, fs, @"C:\Icons\e.exe")); // höchstens vier

        Assert.Equal([@"C:\Icons\a.ico", @"C:\Icons\b.png", @"C:\Icons\c.webp", @"C:\Icons\d.jpg"], list);
    }

    [Fact]
    public void Icon_candidate_check_failing_counts_as_missing()
    {
        var fs = new ThrowingFileSystem();
        var list = new List<string>();

        Assert.False(IconFiles.AddExisting(list, fs, @"C:\Icons\a.ico"));
        Assert.Empty(list);
    }

    [Fact]
    public void CustomIcon_file_names_are_plain_stable_and_distinct()
    {
        var a = CustomIcons.FileNameFor("Steam:1091500");
        var b = CustomIcons.FileNameFor("Steam_1091500");
        var evil = CustomIcons.FileNameFor(@"Manual:..\..\Windows\System32\x");

        Assert.Equal(a, CustomIcons.FileNameFor("Steam:1091500"));
        Assert.StartsWith("Steam_1091500_", a, StringComparison.Ordinal);
        Assert.NotEqual(a, b);
        foreach (var name in new[] { a, b, evil })
        {
            Assert.True(CustomIcons.IsValidFileName(name), name);
            Assert.Equal(name, Path.GetFileName(name));
            Assert.EndsWith(".png", name, StringComparison.Ordinal);
            Assert.Equal(@"C:\Daten\icons", Path.GetDirectoryName(Path.GetFullPath(Path.Combine(@"C:\Daten\icons", name))));
        }

        Assert.DoesNotContain('\\', evil);
        Assert.DoesNotContain(':', evil);
    }

    [Theory]
    [InlineData(@"..\evil.png")]
    [InlineData(@"C:\Windows\evil.png")]
    [InlineData(@"sub\x.png")]
    [InlineData(@".hidden.png")]
    [InlineData(@"x.exe")]
    [InlineData(@"")]
    public void CustomIcon_names_with_paths_or_other_types_are_invalid(string name) => Assert.False(CustomIcons.IsValidFileName(name));

    [Fact]
    public void Old_config_without_icon_fields_loads_and_bad_CustomIcon_is_dropped()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"C:\Cfg\config.json", """
            {
              "version": 1,
              "games": [
                { "source": "Steam", "sourceId": "1", "name": "Alt", "installDir": "D:\\A" },
                { "source": "Steam", "sourceId": "2", "name": "Pfad", "installDir": "D:\\B", "customIcon": "..\\..\\evil.png", "iconCandidates": null },
                { "source": "Steam", "sourceId": "3", "name": "Gut", "installDir": "D:\\C", "customIcon": "Steam_3_ABCDEF01.png",
                  "iconCandidates": [ "C:\\Steam\\steam\\games\\abc.ico" ] }
              ]
            }
            """);

        var config = new ConfigStore(fs, @"C:\Cfg\config.json", new FakeClock()).Load().Config;

        Assert.Empty(config.Games[0].IconCandidates);
        Assert.Null(config.Games[0].CustomIcon);
        Assert.Empty(config.Games[1].IconCandidates);
        Assert.Null(config.Games[1].CustomIcon);
        Assert.Equal("Steam_3_ABCDEF01.png", config.Games[2].CustomIcon);
        Assert.Equal([@"C:\Steam\steam\games\abc.ico"], config.Games[2].IconCandidates);
    }

    [Fact]
    public void Icon_candidates_are_merged_like_known_executables_and_CustomIcon_is_kept()
    {
        var config = new AppConfig();
        var first = new GameEntry
        {
            Source = GameSource.Steam, SourceId = "7", Name = "Spiel", InstallDir = @"D:\S", Executables = [],
            IconCandidates = [@"C:\Steam\steam\games\alt.ico"],
        };
        LibraryMerger.Apply(config, Report(first), new InMemoryFileSystem(), new FakeClock().UtcNow);
        config.Games[0].CustomIcon = "Steam_7_00000000.png";

        LibraryMerger.Apply(config, Report(first with { IconCandidates = [@"C:\Steam\steam\games\neu.ico"] }), new InMemoryFileSystem(), new FakeClock().UtcNow);

        var game = Assert.Single(config.Games);
        Assert.Equal([@"C:\Steam\steam\games\neu.ico"], game.IconCandidates);
        Assert.Equal("Steam_7_00000000.png", game.CustomIcon); // eigenes Symbol bleibt beim Einlesen
    }

    private static LibraryScanReport Report(GameEntry entry) => new(
    [
        new ScannerReport(new ScanResult { Source = entry.Source, ScannerName = "Steam", LauncherDetected = true, Games = [entry] }, TimeSpan.Zero),
    ]);

    private sealed class ThrowingFileSystem : HdrPilot.Core.Abstractions.IFileSystem
    {
        public bool FileExists(string path) => throw new UnauthorizedAccessException("verweigert");

        public bool DirectoryExists(string path) => throw new UnauthorizedAccessException("verweigert");

        public string ReadAllText(string path) => throw new NotSupportedException();

        public byte[] ReadAllBytes(string path) => throw new NotSupportedException();

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) => throw new NotSupportedException();

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) => throw new NotSupportedException();

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void WriteAllTextAtomic(string path, string contents) => throw new NotSupportedException();

        public void DeleteFile(string path) => throw new NotSupportedException();

        public IReadOnlyList<string> GetFixedDriveRoots() => [];
    }
}
