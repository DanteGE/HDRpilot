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
        fileStamp: p => _stamps.GetValueOrDefault(p),
        now: () => _now);

    private static GameConfig Game(params string[] hints) => IconSelectionTests.Game(hints.Length == 0 ? [Hint] : hints);
}
