using System.Diagnostics;
using HdrPilot.App.Services;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// EXE-Wahl für Symbole (E86): Ausschlussliste nur für Symbole, Unreal-Projekte zuerst (*-Shipping.exe vorn), EXEs ohne
/// Symbol oder mit Standardsymbol übersprungen, tiefere Suche begrenzt auf 300 Ordner und eine Zeitgrenze.
/// </summary>
public sealed class IconResolverTests
{
    private const string Dir = IconSelectionTests.Dir;

    private readonly InMemoryFileSystem _files = new();

    [Fact]
    public void Icon_resolver_prefers_the_unreal_shipping_exe_over_the_root_stub()
    {
        Exe(Dir + @"\TheIsle.exe", 9_000);
        Exe(Dir + @"\TheIsle\Binaries\Win64\TheIsleClient-Win64-Shipping.exe", 100);
        Exe(Dir + @"\TheIsle\Binaries\Win64\TheIsleServer.exe", 5_000);

        Assert.Equal(Dir + @"\TheIsle\Binaries\Win64\TheIsleClient-Win64-Shipping.exe", Resolver().Resolve(Game()));
    }

    [Fact]
    public void Icon_resolver_never_takes_unreal_engine_binaries()
    {
        Exe(Dir + @"\Engine\Binaries\Win64\EpicSupportTool.exe", 9_000);
        Exe(Dir + @"\Spiel.exe", 100);

        Assert.Equal(Dir + @"\Spiel.exe", Resolver().Resolve(Game()));
    }

    [Fact]
    public void Icon_resolver_excludes_anti_cheat_also_from_detected_and_known_executables()
    {
        // Erkannte und bekannte EXEs gewinnen sonst immer; ein erkannter Anti-Cheat-Starter darf trotzdem nie das Symbol sein.
        Exe(Dir + @"\start_protected_game.exe", 9_000);
        Exe(Dir + @"\GZWClientEAC.exe", 9_000);
        Exe(Dir + @"\GZW\Binaries\Win64\GZWClientSteam-Win64-Shipping.exe", 100);
        var game = Game();
        game.DetectedExecutables = [Dir + @"\start_protected_game.exe"];
        game.KnownExecutables = [Dir + @"\GZWClientEAC.exe"];

        Assert.Equal(Dir + @"\GZW\Binaries\Win64\GZWClientSteam-Win64-Shipping.exe", Resolver().Resolve(game));
    }

    [Fact]
    public void Icon_resolver_skips_exes_without_icon_or_with_a_generic_icon()
    {
        Exe(Dir + @"\Ohne.exe", 9_000);
        Exe(Dir + @"\Standard.exe", 5_000);
        Exe(Dir + @"\Spiel.exe", 100);
        var icons = new Dictionary<string, IconImage?>(StringComparer.OrdinalIgnoreCase)
        {
            [Dir + @"\Ohne.exe"] = null,
            [Dir + @"\Standard.exe"] = IconSelectionTests.Icon(256, generic: true),
            [Dir + @"\Spiel.exe"] = IconSelectionTests.Icon(32),
        };

        var resolved = Resolver().ResolveWithIcon(Game(), null, p => icons.GetValueOrDefault(p));

        Assert.Equal(Dir + @"\Spiel.exe", resolved!.Path);

        icons[Dir + @"\Spiel.exe"] = null; // nur noch das Standardsymbol: dann eben das
        Assert.Equal(Dir + @"\Standard.exe", Resolver().ResolveWithIcon(Game(), null, p => icons.GetValueOrDefault(p))!.Path);
    }

    [Fact]
    public void Icon_resolver_deep_search_reaches_three_levels_but_not_four()
    {
        Exe(Dir + @"\a\b\c\Drei.exe", 100);
        Exe(Dir + @"\w\x\y\z\Vier.exe", 9_000);

        var seen = new List<string>();
        var resolved = Resolver().ResolveWithIcon(Game(), null, p =>
        {
            seen.Add(p);
            return IconSelectionTests.Icon(256);
        });

        Assert.Equal(Dir + @"\a\b\c\Drei.exe", resolved!.Path);
        Assert.DoesNotContain(Dir + @"\w\x\y\z\Vier.exe", seen);
        Assert.Null(Resolver().ResolveWithIcon(Game(), null, _ => IconSelectionTests.Icon(256), deepSearch: false));
    }

    [Fact]
    public void Icon_resolver_deep_search_visits_at_most_300_directories()
    {
        // 40 × 40 Unterordner, keine EXE: ohne Grenze 1641 Ordner.
        for (var i = 0; i < 40; i++)
        {
            for (var j = 0; j < 40; j++)
            {
                _files.AddDirectory($@"{Dir}\d{i:00}\e{j:00}");
            }
        }

        var counting = new CountingFileSystem(_files);
        var resolver = new IconExecutableResolver(counting, p => 1, TimeSpan.FromSeconds(30));

        Assert.Null(resolver.ResolveWithIcon(Game(), null, _ => IconSelectionTests.Icon(256)));

        // Ersatzsuche höchstens 300 Ordner; dazu die normale Suche (Spielordner und 32 Unterordner).
        Assert.InRange(counting.FileEnumerations, 300, 300 + 1 + IconExecutableResolver.MaxSubdirectories);
    }

    [Fact]
    public void Icon_resolver_deep_search_stops_at_the_time_limit()
    {
        for (var i = 0; i < 30; i++)
        {
            _files.AddDirectory($@"{Dir}\d{i:00}\e");
        }

        var slow = new CountingFileSystem(_files) { DirectoryDelay = TimeSpan.FromMilliseconds(40) };
        var resolver = new IconExecutableResolver(slow, p => 1, TimeSpan.FromMilliseconds(150));
        var clock = Stopwatch.StartNew();

        Assert.Null(resolver.ResolveWithIcon(Game(), null, _ => IconSelectionTests.Icon(256)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), "Ersatzsuche zu lang: " + clock.Elapsed);
        Assert.True(slow.DirectoryEnumerations < 20, "zu viele Ordner trotz Zeitgrenze: " + slow.DirectoryEnumerations);
    }

    [Fact]
    public void Icon_resolver_without_probe_ignores_the_deep_search()
    {
        Exe(Dir + @"\a\b\Tief.exe", 100);

        Assert.Null(Resolver().Resolve(Game()));
    }

    private IconExecutableResolver Resolver() => new(_files, p => _files.ReadAllBytes(p).Length);

    private void Exe(string path, int size) => _files.AddFile(path, new byte[size]);

    private static GameConfig Game() => IconSelectionTests.Game();

    /// <summary>Zählt Ordnerzugriffe; auf Wunsch langsam wie ein kalter Datenträger.</summary>
    private sealed class CountingFileSystem(IFileSystem inner) : IFileSystem
    {
        private int _files;
        private int _directories;

        public TimeSpan DirectoryDelay { get; init; }

        public int FileEnumerations => Volatile.Read(ref _files);

        public int DirectoryEnumerations => Volatile.Read(ref _directories);

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false)
        {
            Interlocked.Increment(ref _files);
            return inner.EnumerateFiles(directory, searchPattern, recursive);
        }

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false)
        {
            Interlocked.Increment(ref _directories);
            if (DirectoryDelay > TimeSpan.Zero)
            {
                Thread.Sleep(DirectoryDelay);
            }

            return inner.EnumerateDirectories(directory, searchPattern, recursive);
        }

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void WriteAllTextAtomic(string path, string contents) => inner.WriteAllTextAtomic(path, contents);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();
    }
}
