using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

public sealed class GameIconServiceTests
{
    private readonly InMemoryFileSystem _files = new();

    private IconExecutableResolver Resolver() => new(_files, path => _files.ReadAllBytes(path).Length);

    private InMemoryFileSystem Exe(string path, int size) => _files.AddFile(path, new byte[size]);

    private static GameConfig Game(string installDir, string[]? known = null, string[]? detected = null, string[]? excluded = null) => new()
    {
        Source = GameSource.Steam,
        SourceId = "1",
        Name = "Spiel",
        InstallDir = installDir,
        KnownExecutables = [.. known ?? []],
        DetectedExecutables = [.. detected ?? []],
        ExcludedExecutables = [.. excluded ?? []],
    };

    [Fact]
    public void Detected_executables_win_and_the_largest_is_taken()
    {
        Exe(@"D:\G\Game.exe", 500);
        Exe(@"D:\G\bin\GameReal.exe", 900);
        Exe(@"D:\G\bin\GameSmall.exe", 100);
        Exe(@"D:\G\Huge.exe", 5000);
        var game = Game(@"D:\G", known: [@"D:\G\Game.exe"], detected: [@"D:\G\bin\GameSmall.exe", @"D:\G\bin\GameReal.exe"]);

        Assert.Equal(@"D:\G\bin\GameReal.exe", Resolver().Resolve(game));
    }

    [Fact]
    public void Known_executables_skip_helpers_exclusions_and_missing_files()
    {
        Exe(@"D:\GOG-Spiele\Cyberpunk 2077\REDprelauncher.exe", 9000); // Hilfsprogramm, obwohl am größten
        Exe(@"D:\GOG-Spiele\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe", 5000);
        Exe(@"D:\GOG-Spiele\Cyberpunk 2077\bin\x64\Benchmark.exe", 7000); // vom Nutzer ausgeschlossen
        var game = Game(
            @"D:\GOG-Spiele\Cyberpunk 2077",
            known: [@"D:\GOG-Spiele\Cyberpunk 2077\REDprelauncher.exe", @"D:\GOG-Spiele\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe", @"D:\GOG-Spiele\Cyberpunk 2077\bin\x64\Benchmark.exe", @"D:\GOG-Spiele\Fehlt.exe"],
            detected: [@"D:\GOG-Spiele\Cyberpunk 2077\bin\x64\Weg.exe"],
            excluded: ["benchmark.exe"]);

        Assert.Equal(@"D:\GOG-Spiele\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe", Resolver().Resolve(game));
    }

    [Fact]
    public void Without_known_executables_the_largest_exe_up_to_one_level_deep_is_used()
    {
        // Steam nennt keine EXEs: Ordner bis eine Ebene tief, Hilfsprogramme ausgenommen.
        Exe(@"C:\Steam\common\Hades\Hades.exe", 300);
        Exe(@"C:\Steam\common\Hades\x64\Hades.exe", 800);
        Exe(@"C:\Steam\common\Hades\x64\UnityCrashHandler64.exe", 2000);
        Exe(@"C:\Steam\common\Hades\x64\Deep\Deeper\Biggest.exe", 99999);
        Exe(@"C:\Steam\common\Hades\_CommonRedist\vcredist_x64.exe", 50000);

        Assert.Equal(@"C:\Steam\common\Hades\x64\Hades.exe", Resolver().Resolve(Game(@"C:\Steam\common\Hades")));
    }

    [Fact]
    public void Drive_roots_and_missing_folders_are_never_searched()
    {
        Exe(@"E:\Spiel.exe", 100);

        Assert.Null(Resolver().Resolve(Game(@"E:\")));
        Assert.Null(Resolver().Resolve(Game(@"E:\Gibt es nicht")));
        Assert.Null(Resolver().Resolve(Game(string.Empty)));
    }

    [Fact]
    public async Task Icons_are_loaded_once_cached_and_released()
    {
        Exe(@"D:\G\Game.exe", 10);
        var extracted = new List<string>();
        var image = new DrawingImage();
        image.Freeze();
        var service = new GameIconService(_files, fileLength: p => _files.ReadAllBytes(p).Length, extract: p =>
        {
            lock (extracted)
            {
                extracted.Add(p);
            }

            return image;
        });
        var game = Game(@"D:\G");
        var other = Game(@"D:\G");
        other.SourceId = "2"; // gleiche EXE, anderes Spiel

        Assert.Null(service.GetCachedIcon(game));
        Assert.Same(image, await service.GetIconAsync(game, TestContext.Current.CancellationToken));
        Assert.Same(image, service.GetCachedIcon(game));
        Assert.Same(image, await service.GetIconAsync(game, TestContext.Current.CancellationToken));
        Assert.Same(image, await service.GetIconAsync(other, TestContext.Current.CancellationToken));
        Assert.Equal([@"D:\G\Game.exe"], extracted);

        service.ClearCache();
        Assert.Null(service.GetCachedIcon(game));
    }

    [Fact]
    public async Task Latest_requests_load_first_with_limited_parallelism()
    {
        // Liste scrollt: zuletzt angefragte (gerade sichtbare) Zeilen zuerst, nie mehr als maxParallel gleichzeitig.
        var games = Enumerable.Range(1, 4).Select(i =>
        {
            Exe($@"D:\G{i}\Game{i}.exe", 10);
            var g = Game($@"D:\G{i}");
            g.SourceId = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return g;
        }).ToList();
        using var gate = new ManualResetEventSlim(false);
        var order = new List<string>();
        int running = 0, maxRunning = 0;
        var image = new DrawingImage();
        image.Freeze();
        var service = new GameIconService(_files, fileLength: p => _files.ReadAllBytes(p).Length, maxParallel: 1, extract: p =>
        {
            var now = Interlocked.Increment(ref running);
            lock (order)
            {
                maxRunning = Math.Max(maxRunning, now);
            }

            if (p.EndsWith("Game1.exe", StringComparison.OrdinalIgnoreCase))
            {
                gate.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }

            lock (order)
            {
                order.Add(Path.GetFileName(p));
            }

            Interlocked.Decrement(ref running);
            return image;
        });

        var first = service.GetIconAsync(games[0], TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref running) == 1, TimeSpan.FromSeconds(5)));
        var rest = games.Skip(1).Select(g => service.GetIconAsync(g, TestContext.Current.CancellationToken)).ToList();
        gate.Set();
        await Task.WhenAll([first, .. rest]);

        Assert.Equal(["Game1.exe", "Game4.exe", "Game3.exe", "Game2.exe"], order);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Clearing_the_cache_releases_queued_requests_and_drops_late_results()
    {
        Exe(@"D:\A\A.exe", 10);
        Exe(@"D:\B\B.exe", 10);
        var a = Game(@"D:\A");
        var b = Game(@"D:\B");
        b.SourceId = "2";
        using var gate = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        var image = new DrawingImage();
        image.Freeze();
        var service = new GameIconService(_files, fileLength: p => _files.ReadAllBytes(p).Length, maxParallel: 1, extract: p =>
        {
            started.Set();
            gate.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            return image;
        });

        var loadingA = service.GetIconAsync(a, TestContext.Current.CancellationToken);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var queuedB = service.GetIconAsync(b, TestContext.Current.CancellationToken);

        service.ClearCache(); // Fenster geschlossen
        Assert.Null(await queuedB.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        gate.Set();
        Assert.Same(image, await loadingA.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Null(service.GetCachedIcon(a)); // spätes Ergebnis bleibt nicht im Speicher
    }

    [Fact]
    public async Task Icon_request_overtaken_by_a_newer_state_of_the_game_is_skipped()
    {
        // Erstes Öffnen nach dem Update während des ersten Einlesens: Die Zeile fragt mit dem alten Stand (ohne Hinweise) an,
        // das Einlesen bringt das Steam-Symbol, die Zeile fragt neu an. Die neue Anfrage läuft zuerst (zuletzt angefragte
        // zuerst); die alte durchsucht danach weder die EXEs noch überschreibt sie den Speicher des neuen Stands.
        Exe(@"D:\B\B.exe", 10);
        Exe(@"D:\A\A.exe", 10);
        const string hint = @"C:\Steam\steam\games\a.ico";
        _files.AddFile(hint, new byte[10]);
        var blocker = Game(@"D:\B");
        blocker.SourceId = "2";
        var old = Game(@"D:\A");
        var updated = Game(@"D:\A");
        updated.IconCandidates = [hint];
        using var gate = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        var reads = new List<string>();
        var service = new GameIconService(_files, fileLength: p => _files.ReadAllBytes(p).Length, maxParallel: 1, read: p =>
        {
            lock (reads)
            {
                reads.Add(p);
            }

            if (p.EndsWith("B.exe", StringComparison.OrdinalIgnoreCase))
            {
                started.Set();
                gate.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }

            return IconSelectionTests.Icon(256);
        });

        var loadingB = service.GetIconAsync(blocker, TestContext.Current.CancellationToken);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var oldLoad = service.GetIconAsync(old, TestContext.Current.CancellationToken);
        var newLoad = service.GetIconAsync(updated, TestContext.Current.CancellationToken);
        gate.Set();

        Assert.NotNull(await newLoad.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Null(await oldLoad.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await loadingB;
        Assert.DoesNotContain(@"D:\A\A.exe", reads);
        Assert.NotNull(service.GetCachedIcon(updated));
    }

    [Fact]
    public async Task Games_without_any_exe_get_no_icon()
    {
        _files.AddDirectory(@"D:\Leer");
        var service = new GameIconService(_files, extract: _ => throw new InvalidOperationException("darf nicht laufen"));

        Assert.Null(await service.GetIconAsync(Game(@"D:\Leer"), TestContext.Current.CancellationToken));
        Assert.Null(await service.GetIconAsync(Game(@"D:\Leer"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Real_exe_icon_is_decoded_at_target_size_and_frozen()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        var icon = Assert.IsAssignableFrom<BitmapSource>(GameIconService.ExtractIcon(explorer, GameIconService.IconSize));

        Assert.True(icon.IsFrozen);
        Assert.Equal(GameIconService.IconSize, icon.PixelWidth);
        Assert.Equal(GameIconService.IconSize, icon.PixelHeight);
        Assert.Null(GameIconService.ExtractIcon(Path.Combine(Path.GetTempPath(), "gibt-es-nicht-" + Guid.NewGuid().ToString("N") + ".exe"), 48));
    }
}
