using HdrPilot.App.Services;
using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Gemerkte Symbolwahl (E86): Ein Kaltstart liest nur die gemerkte Datei statt die Spielordner zu durchsuchen. Neu gewählt
/// wird, wenn sich die Datei, das Spiel oder die zulässigen Quellen ändern, und eine Wahl ohne gutes Symbol nach einer Woche.
/// </summary>
public sealed class IconChoiceCacheTests
{
    private const string Dir = IconSelectionTests.Dir;
    private const string CacheFile = @"C:\Daten\icon-cache.json";
    private const string Hint = @"C:\Steam\steam\games\abc.ico";
    private const string Exe = Dir + @"\Spiel.exe";

    private readonly InMemoryFileSystem _files = new();
    private readonly Dictionary<string, IconImage?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _reads = [];
    private readonly List<string> _stamped = [];
    private DateTimeOffset _now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Icon_choice_is_remembered_so_a_cold_start_reads_only_that_file()
    {
        Add(Hint, IconSelectionTests.Icon(32));
        Add(Exe, IconSelectionTests.Icon(256));
        Assert.Equal(Exe, Service().Select(Game(), remember: true)!.Path);
        Assert.Contains(Hint, _reads);
        _reads.Clear();

        var again = Service().Select(Game(), remember: true);

        Assert.Equal((IconOrigin.Executable, Exe), (again!.Origin, again.Path));
        Assert.Equal([Exe], _reads);
    }

    [Fact]
    public async Task Icon_choice_of_the_list_is_written_when_no_icon_is_pending()
    {
        Add(Hint, IconSelectionTests.Icon(256));

        Assert.NotNull(await Service().GetIconAsync(Game(), TestContext.Current.CancellationToken));

        Assert.True(SpinWait.SpinUntil(() => _files.FileExists(CacheFile), TimeSpan.FromSeconds(5)));
        Assert.Contains("\"origin\": \"Launcher\"", _files.ReadAllText(CacheFile).Replace("\":\"", "\": \"", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Icon_choice_is_renewed_when_the_chosen_file_changes()
    {
        Add(Hint, IconSelectionTests.Icon(256));
        Service().Select(Game(), remember: true);
        _reads.Clear();

        _stamps[Hint] = "anders"; // Steam hat die .ico ersetzt
        Service().Select(Game(), remember: true);

        Assert.Equal([Hint], _reads);
        Assert.Contains("anders", _files.ReadAllText(CacheFile), StringComparison.Ordinal); // neu gemerkt
    }

    [Fact]
    public void Icon_choice_is_renewed_when_the_game_changes()
    {
        Add(Hint, IconSelectionTests.Icon(256));
        Add(@"C:\Steam\steam\games\neu.ico", IconSelectionTests.Icon(256));
        Service().Select(Game(), remember: true);
        _reads.Clear();

        var chosen = Service().Select(Game(@"C:\Steam\steam\games\neu.ico"), remember: true);

        Assert.Equal(@"C:\Steam\steam\games\neu.ico", chosen!.Path);
        Assert.Equal([@"C:\Steam\steam\games\neu.ico"], _reads);
    }

    [Fact]
    public void Icon_choice_without_a_good_icon_is_rechecked_after_a_week()
    {
        _files.AddDirectory(Dir);
        Add(Hint, IconSelectionTests.Icon(32));
        Service().Select(Game(), remember: true);
        Add(Exe, IconSelectionTests.Icon(256)); // ein Update des Spiels bringt eine scharfe EXE
        _reads.Clear();

        _now += TimeSpan.FromDays(6);
        Assert.Equal(Hint, Service().Select(Game(), remember: true)!.Path);
        Assert.Equal([Hint], _reads); // noch gemerkt: nur die eine Datei

        _now += TimeSpan.FromDays(2);
        Assert.Equal(Exe, Service().Select(Game(), remember: true)!.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Icon_choice_without_a_good_icon_is_not_remembered_while_the_game_folder_is_missing(bool blurryHint)
    {
        // Nicht installiert, Laufwerk getrennt: Nach der Installation am selben Ort ändern sich die Eingaben nicht, eine
        // gemerkte Wahl ohne (gutes) Symbol hielte sich sonst bis zu einer Woche.
        var game = blurryHint ? Game() : IconSelectionTests.Game();
        if (blurryHint)
        {
            Add(Hint, IconSelectionTests.Icon(32));
        }

        Assert.Equal(blurryHint ? Hint : null, Service().Select(game, remember: true)?.Path);
        Add(Exe, IconSelectionTests.Icon(256)); // installiert: Ordner und EXE sind da

        Assert.Equal(Exe, Service().Select(game, remember: true)!.Path);
    }

    [Fact]
    public void Icon_choice_is_renewed_when_the_game_is_installed_again()
    {
        // Nach der Deinstallation bleibt der Ordner (Spielstände), die EXE fehlt: ohne Symbol gemerkt. Nach der Installation
        // ist das Spiel wieder „installiert“, und es wird neu gewählt.
        _files.AddDirectory(Dir);
        var uninstalled = IconSelectionTests.Game();
        uninstalled.IsInstalled = false;
        var installed = IconSelectionTests.Game();
        Assert.Null(Service().Select(uninstalled, remember: true));
        Add(Exe, IconSelectionTests.Icon(256));
        Assert.Null(Service().Select(uninstalled, remember: true)); // gleiche Eingaben: gemerkt

        Assert.Equal(Exe, Service().Select(installed, remember: true)!.Path);
    }

    [Fact]
    public void Icon_choice_never_reads_a_network_path_even_when_both_files_name_it()
    {
        // Von Hand geänderte config.json (Hinweis) und icon-cache.json (gemerkte Wahl mit passender Kennung): kein Zugriff auf
        // einen fremden Rechner, weder für Größe und Änderungszeit noch für das Symbol.
        const string unc = @"\\Server\Freigabe\spiel.ico";
        _icons[unc] = IconSelectionTests.Icon(256);
        _stamps[unc] = "1";
        Add(Exe, IconSelectionTests.Icon(256));
        _files.AddFile(CacheFile, Remembered(IconSignature.Of(Game(unc), null), unc).Replace("Executable", "Launcher", StringComparison.Ordinal));

        Assert.Equal(Exe, Service().Select(Game(unc), remember: true)!.Path);
        Assert.DoesNotContain(unc, _reads);
        Assert.DoesNotContain(unc, _stamped);
    }

    [Fact]
    public void Icon_choice_of_the_list_is_used_for_shortcuts_when_a_shortcut_can_show_it()
    {
        // „Mit HDR starten“ zeigt dasselbe Symbol wie die Liste (etwa das Steam-Symbol statt eines Starters mit
        // EasyAntiCheat-Symbol), sofern es eine .ico oder .exe ist; gelesen wird dafür kein Symbol.
        Add(Hint, IconSelectionTests.Icon(256));
        var service = Service();
        Assert.Equal(Hint, service.ShortcutIconFile(Game())); // noch nichts gewählt: dieselbe Wahl wie in der Liste
        service.Select(Game(), remember: true);
        _reads.Clear();

        Assert.Equal(Hint, service.ShortcutIconFile(Game()));
        Assert.Empty(_reads);

        const string neu = @"C:\Steam\steam\games\neu.ico";
        Add(neu, IconSelectionTests.Icon(256));
        Assert.Equal(neu, service.ShortcutIconFile(Game(neu))); // Spiel geändert: gemerkte Wahl gilt nicht mehr
        const string logo = @"C:\XboxGames\Spiel\Content\Logo.png";
        Add(logo, IconSelectionTests.Icon(256));
        service.Select(Game(logo), remember: true);
        Assert.Null(service.ShortcutIconFile(Game(logo))); // ein Bild kann eine Verknüpfung nicht zeigen, und eine EXE gibt es nicht
        Add(Exe, IconSelectionTests.Icon(32));
        Assert.Equal(Exe, service.ShortcutIconFile(Game(logo))); // dann die EXE wie in der Liste
    }

    [Fact]
    public void Icon_for_shortcuts_without_a_remembered_choice_takes_the_official_steam_icon()
    {
        // Nichts gemerkt (etwa weil ein eigenes Symbol galt, als die Liste das Spiel zuletzt zeigte) und die EXE trägt nur das
        // Unity-Symbol: Die Verknüpfung zeigt wie die Liste das offizielle Steam-Symbol, nicht das Standardsymbol der EXE.
        Add(Hint, IconSelectionTests.Icon(256));
        Add(Exe, IconSelectionTests.Icon(256, generic: true));

        Assert.Equal(Hint, Service().ShortcutIconFile(Game()));

        // Ist das Steam-Symbol unscharf und die EXE scharf, nimmt die Liste die EXE, die Verknüpfung ebenso.
        _icons[Hint] = IconSelectionTests.Icon(32);
        _icons[Exe] = IconSelectionTests.Icon(256);
        Assert.Equal(Exe, Service().ShortcutIconFile(Game()));
    }

    [Fact]
    public void Icon_for_shortcuts_never_searches_deeper()
    {
        // Verknüpfungen entstehen per Klick und sollen gleich da sein: Die tiefere EXE-Suche (bis 1 s) läuft dafür nie, die
        // Liste findet die EXE.
        const string deep = Dir + @"\a\b\c\Tief.exe";
        Add(deep, IconSelectionTests.Icon(256));

        Assert.Null(Service().ShortcutIconFile(Game(@"C:\fehlt.ico")));
        Assert.Equal(deep, Service().Select(Game(@"C:\fehlt.ico"))!.Path);
    }

    [Fact]
    public void Icon_for_shortcuts_without_a_remembered_choice_skips_default_icons()
    {
        // Nichts gemerkt (etwa weil ein eigenes Symbol gilt) und nur der Starter erkannt: nicht dessen EasyAntiCheat-Symbol,
        // sondern die EXE wie in der Liste, ohne tiefere Suche.
        const string starter = Dir + @"\TheIsle.exe";
        const string shipping = Dir + @"\TheIsle\Binaries\Win64\TheIsleClient-Win64-Shipping.exe";
        Add(starter, IconSelectionTests.Icon(256, generic: true));
        Add(shipping, IconSelectionTests.Icon(256));
        var game = IconSelectionTests.Game();
        game.DetectedExecutables = [starter];

        Assert.Equal(shipping, Service().ShortcutIconFile(game));

        game.InstallDir = @"D:\Fehlt";
        game.DetectedExecutables = [];
        Assert.Null(Service().ShortcutIconFile(game)); // gar keine EXE: Symbol von HDRpilot
    }

    [Fact]
    public void Icon_choice_pointing_to_an_excluded_exe_is_not_trusted()
    {
        // Etwa nach einer Erweiterung der Ausschlussliste oder bei einer von Hand geänderten Datei.
        var starter = Dir + @"\start_protected_game.exe";
        Add(starter, IconSelectionTests.Icon(256));
        Add(Exe, IconSelectionTests.Icon(256));
        _files.AddFile(CacheFile, Remembered(IconSignature.Of(Game(), null), starter));

        var chosen = Service().Select(Game(), remember: true);

        Assert.Equal(Exe, chosen!.Path);
        Assert.DoesNotContain(starter, _reads);
    }

    [Fact]
    public void Icon_choice_pointing_outside_the_game_is_not_trusted()
    {
        Add(Hint, IconSelectionTests.Icon(256));
        Add(@"C:\Windows\System32\fremd.exe", IconSelectionTests.Icon(256));
        _files.AddFile(CacheFile, Remembered(IconSignature.Of(Game(), null), @"C:\Windows\System32\fremd.exe"));

        Assert.Equal(Hint, Service().Select(Game(), remember: true)!.Path);
        Assert.DoesNotContain(@"C:\Windows\System32\fremd.exe", _reads);
    }

    [Theory]
    [InlineData("kein JSON")]
    [InlineData("""{ "version": 99, "games": {} }""")]
    [InlineData("""{ "version": 1, "games": { "Steam:1": null } }""")]
    public void Icon_choice_cache_that_is_broken_or_from_another_version_is_ignored(string text)
    {
        Add(Hint, IconSelectionTests.Icon(256));
        _files.AddFile(CacheFile, text);

        Assert.Equal(Hint, Service().Select(Game(), remember: true)!.Path);
    }

    private static string Remembered(string signature, string path) => $$"""
        { "version": 1, "games": { "Steam:1": { "signature": "{{signature}}", "path": "{{path.Replace(@"\", @"\\", StringComparison.Ordinal)}}",
          "origin": "Executable", "stamp": "1", "nativeSize": 256, "good": true, "checked": "2026-10-10T11:00:00+00:00" } } }
        """;

    private void Add(string path, IconImage? icon)
    {
        _files.AddFile(path, new byte[100]);
        _icons[path] = icon;
        _stamps[path] = "1";
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
        },
        choiceCacheFile: CacheFile,
        fileStamp: p =>
        {
            lock (_stamped)
            {
                _stamped.Add(p);
            }

            return _stamps.GetValueOrDefault(p);
        },
        now: () => _now);

    private static GameConfig Game(params string[] hints) => IconSelectionTests.Game(hints.Length == 0 ? [Hint] : hints);
}
