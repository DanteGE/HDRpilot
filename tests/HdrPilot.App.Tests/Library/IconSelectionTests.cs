using HdrPilot.App.Services;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Kette der Symbolquellen (E86, Entscheidungen des Nutzers): offizielles Steam-Symbol zuerst; die EXE nur, wenn es fehlt, ein
/// Standardsymbol ist oder unter 48 px liegt, während die EXE mindestens 48 px hat. Die Dateien sind Attrappen, gelesen wird
/// über eine eingesetzte Lesefunktion, die mitschreibt.
/// </summary>
public sealed class IconSelectionTests
{
    internal const string Dir = @"D:\Steam-Bibliothek\steamapps\common\Spiel";
    private const string SteamIco = @"C:\Program Files (x86)\Steam\steam\games\0f3c5a7e9b1d2f4a6c8e0b2d4f6a8c0e2b4d6f8a.ico";
    private const string Exe = Dir + @"\Spiel.exe";

    private readonly InMemoryFileSystem _files = new();
    private readonly Dictionary<string, IconImage?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _reads = [];

    internal static IconImage Icon(int size, bool generic = false) => new(IconFixtures.Colorful(48, 48), size, generic);

    [Fact]
    public void Icon_chain_takes_the_official_steam_icon_when_it_is_sharp()
    {
        Add(SteamIco, Icon(256));
        Add(Exe, Icon(256));

        var chosen = Service().Select(Game(SteamIco));

        Assert.Equal((IconOrigin.Launcher, SteamIco), (chosen!.Origin, chosen.Path));
        Assert.DoesNotContain(Exe, _reads); // die EXE wird gar nicht erst gesucht
    }

    [Fact]
    public void Icon_chain_takes_a_sharp_exe_instead_of_a_blurry_steam_icon()
    {
        Add(SteamIco, Icon(32));
        Add(Exe, Icon(256));

        var chosen = Service().Select(Game(SteamIco));

        Assert.Equal((IconOrigin.Executable, Exe), (chosen!.Origin, chosen.Path));
    }

    [Theory]
    [InlineData(44)]
    [InlineData(32)]
    [InlineData(0)]
    public void Icon_chain_keeps_the_blurry_steam_icon_when_the_exe_is_not_sharp(int exeSize)
    {
        Add(SteamIco, Icon(32));
        Add(Exe, exeSize == 0 ? null : Icon(exeSize));

        var chosen = Service().Select(Game(SteamIco));

        Assert.Equal((IconOrigin.Launcher, SteamIco), (chosen!.Origin, chosen.Path));
    }

    [Fact]
    public void Icon_chain_replaces_a_generic_steam_icon_with_any_real_exe_icon()
    {
        Add(SteamIco, Icon(256, generic: true));
        Add(Exe, Icon(32));

        var chosen = Service().Select(Game(SteamIco));

        Assert.Equal((IconOrigin.Executable, Exe), (chosen!.Origin, chosen.Path));
    }

    [Fact]
    public void Icon_chain_without_hints_uses_the_exe()
    {
        Add(Exe, Icon(256));

        var chosen = Service().Select(Game());

        Assert.Equal((IconOrigin.Executable, Exe), (chosen!.Origin, chosen.Path));
    }

    [Fact]
    public void Icon_chain_shows_a_generic_icon_only_when_nothing_else_exists()
    {
        Add(SteamIco, Icon(256, generic: true));
        Add(Exe, Icon(256, generic: true));

        var chosen = Service().Select(Game(SteamIco));

        Assert.True(chosen!.Icon.IsGeneric);
        Assert.Equal(SteamIco, chosen.Path);

        var nothing = Game(@"C:\fehlt.ico");
        nothing.InstallDir = @"D:\Leer";
        _files.AddDirectory(@"D:\Leer");
        Assert.Null(Service().Select(nothing));
    }

    [Fact]
    public void Icon_chain_never_reads_network_or_unsupported_hint_paths()
    {
        Add(Exe, Icon(256));
        _icons[@"\\nas\spiele\x.ico"] = Icon(256);
        _icons[@"C:\x.txt"] = Icon(256);
        _icons[@"x.ico"] = Icon(256);

        var chosen = Service().Select(Game(@"\\nas\spiele\x.ico", @"C:\x.txt", "x.ico"));

        Assert.Equal(Exe, chosen!.Path);
        Assert.Equal([Exe], _reads);
    }

    [Fact]
    public void Icon_chain_searches_deeper_only_without_any_real_icon()
    {
        // Ein richtiges 32-px-Steam-Symbol wird nie gegen eine EXE aus der Tiefe getauscht.
        Add(SteamIco, Icon(32));
        Add(Dir + @"\a\b\c\Deep.exe", Icon(256));

        var withHint = Service().Select(Game(SteamIco));
        var withoutHint = Service().Select(Game());

        Assert.Equal(SteamIco, withHint!.Path);
        Assert.Equal(Dir + @"\a\b\c\Deep.exe", withoutHint!.Path);
    }

    [Fact]
    public void Icon_chain_skips_the_anti_cheat_starter_of_funkenflug_tag_eins()
    {
        // Gemessen: start_protected_game.exe (EasyAntiCheat-Symbol) statt FunkTagProjekt\Binaries\Win64\FunkTag-Steam.exe.
        Add(Dir + @"\start_protected_game.exe", Icon(256, generic: true), size: 5_000_000);
        Add(Dir + @"\FunkTagProjekt\Binaries\Win64\FunkTag-Steam.exe", Icon(256), size: 1_000);

        var chosen = Service().Select(Game());

        Assert.Equal(Dir + @"\FunkTagProjekt\Binaries\Win64\FunkTag-Steam.exe", chosen!.Path);
        Assert.DoesNotContain(Dir + @"\start_protected_game.exe", _reads);
    }

    [Fact]
    public void Icon_chain_prefers_the_unreal_shipping_exe_of_the_isle()
    {
        // Gemessen: TheIsle.exe im Spielordner trägt das EasyAntiCheat-Symbol, das Spiel liegt unter TheIsle\Binaries\Win64.
        Add(Dir + @"\TheIsle.exe", Icon(256), size: 9_000_000);
        Add(Dir + @"\TheIsle\Binaries\Win64\TheIsleClient-Win64-Shipping.exe", Icon(256), size: 1_000);

        var chosen = Service().Select(Game());

        Assert.Equal(Dir + @"\TheIsle\Binaries\Win64\TheIsleClient-Win64-Shipping.exe", chosen!.Path);
    }

    [Fact]
    public void Icon_chain_reads_each_file_once_while_the_window_is_open()
    {
        Add(SteamIco, Icon(256));
        var service = Service();
        var first = Game(SteamIco);
        var second = Game(SteamIco);
        second.SourceId = "2"; // zwei Einträge, ein Symbol (wie die drei Boltgun-Einträge)

        Assert.Equal(SteamIco, service.Select(first)!.Path);
        Assert.Equal(SteamIco, service.Select(second)!.Path);
        Assert.Single(_reads);
        Assert.False(service.Select(second)!.Icon.IsGeneric);
    }

    private void Add(string path, IconImage? icon, int size = 100)
    {
        _files.AddFile(path, new byte[size]);
        _icons[path] = icon;
    }

    private GameIconService Service() => new(
        _files,
        fileLength: p => _files.ReadAllBytes(p).Length,
        read: p =>
        {
            lock (_reads)
            {
                _reads.Add(p);
            }

            return _icons.GetValueOrDefault(p);
        });

    internal static GameConfig Game(params string[] hints) => new()
    {
        Source = GameSource.Steam,
        SourceId = "1",
        Name = "Spiel",
        InstallDir = Dir,
        IconCandidates = [.. hints],
    };
}
