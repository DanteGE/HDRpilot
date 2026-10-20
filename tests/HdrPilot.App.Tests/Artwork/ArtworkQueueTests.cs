using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// Warteschlange und Lebensdauer des Bilderdienstes (G10.9): Vorab-Laden kommt erst dran, wenn sonst nichts ansteht, eine
/// normale Anfrage zieht es vor; solange ein Spiel läuft, wird keine helle Fassung berechnet; was eine laufende Arbeit nach
/// dem Schließen des Fensters noch lädt, bleibt nicht im Speicher, und <see cref="ArtworkService.WhenReleasedAsync"/> meldet,
/// wann aufgeräumt ist.
/// </summary>
public sealed class ArtworkQueueTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly ArtworkFixture _art = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _art.Dispose();

    private static string? Stamp(string path) =>
        File.Exists(path) ? new FileInfo(path).Length.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// Dienst mit einem Arbeits-Thread, der beim ersten Spiel hält, bis <paramref name="release"/> gesetzt ist, und die
    /// Reihenfolge der Spiele mitschreibt, in der er sie bearbeitet (erster Plattenzugriff je Spiel).
    /// </summary>
    private ArtworkService Ordered(string[] ids, List<string> order, ManualResetEventSlim entered, ManualResetEventSlim release)
    {
        foreach (var id in ids)
        {
            _art.SteamFile(id, "library_hero.jpg", ArtworkFixture.Solid(640, 206, colorful: true));
        }

        var gate = new object();
        return _art.Service(maxParallel: 1, stamp: path =>
        {
            var id = ids.FirstOrDefault(i => path.Contains(Path.DirectorySeparatorChar + i + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            lock (gate)
            {
                if (id is not null && !order.Contains(id))
                {
                    order.Add(id);
                }
            }

            if (id == ids[0])
            {
                entered.Set();
                release.Wait(Wait);
            }

            return Stamp(path);
        });
    }

    [Fact]
    public async Task Prefetch_runs_only_when_nothing_else_is_queued()
    {
        string[] ids = ["921", "922", "923", "924"];
        var order = new List<string>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = Ordered(ids, order, entered, release);
        using var lease = service.Acquire();

        var first = service.GetImageAsync(ArtworkFixture.Steam("921"), ArtworkKind.Hero, 640, Ct);
        Assert.True(entered.Wait(Wait, Ct));
        var second = service.GetImageAsync(ArtworkFixture.Steam("922"), ArtworkKind.Hero, 640, Ct);
        var third = service.GetImageAsync(ArtworkFixture.Steam("923"), ArtworkKind.Hero, 640, Ct);

        // Zuletzt angefragt, aber nur vorab: kommt nach den sichtbaren Bildern dran.
        var prefetch = service.PrefetchHeroBoostAsync(ArtworkFixture.Steam("924"), 640, Ct);
        release.Set();
        await Task.WhenAll(first, second, third, prefetch).WaitAsync(Wait, Ct);

        Assert.Equal(["921", "923", "922", "924"], order);
        Assert.NotNull(await prefetch);
    }

    [Fact]
    public async Task Normal_request_moves_a_prefetch_to_the_front()
    {
        string[] ids = ["931", "932", "933", "934"];
        var order = new List<string>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = Ordered(ids, order, entered, release);
        using var lease = service.Acquire();

        var first = service.GetImageAsync(ArtworkFixture.Steam("931"), ArtworkKind.Hero, 640, Ct);
        Assert.True(entered.Wait(Wait, Ct));
        var prefetch = service.PrefetchHeroBoostAsync(ArtworkFixture.Steam("934"), 640, Ct);
        var second = service.GetImageAsync(ArtworkFixture.Steam("932"), ArtworkKind.Hero, 640, Ct);
        var third = service.GetImageAsync(ArtworkFixture.Steam("933"), ArtworkKind.Hero, 640, Ct);

        // HDR geht an: jetzt gebraucht, dieselbe Arbeit rückt nach vorn (einmal berechnet, beide bekommen sie).
        var needed = service.GetHeroBoostAsync(ArtworkFixture.Steam("934"), 640, Ct);
        release.Set();
        await Task.WhenAll(first, second, third, prefetch, needed).WaitAsync(Wait, Ct);

        Assert.Equal(["931", "934", "933", "932"], order);
        Assert.Same(await prefetch, await needed);
    }

    [Fact]
    public async Task Bright_variant_is_not_computed_while_a_game_runs()
    {
        _art.SteamFile("940", "library_hero.jpg", ArtworkFixture.Solid(1280, 413, colorful: true));
        var running = true;
        var service = _art.Service(gameRunning: () => running);
        var game = ArtworkFixture.Steam("940");
        var boostDir = Path.Combine(_art.ArtDirectory, "boost");

        Assert.Null(await service.GetHeroBoostAsync(game, 1000, Ct));
        Assert.Null(await service.PrefetchHeroBoostAsync(game, 1000, Ct));
        Assert.False(Directory.Exists(boostDir), "nichts berechnet, solange ein Spiel läuft");

        running = false;
        Assert.NotNull(await service.GetHeroBoostAsync(game, 1000, Ct));
        Assert.Single(Directory.GetFiles(boostDir));
    }

    [Fact]
    public async Task Images_a_running_load_finishes_after_the_window_closed_are_not_kept()
    {
        _art.SteamFile("950", "library_hero.jpg", ArtworkFixture.Hero());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = _art.Service(stamp: path =>
        {
            entered.Set();
            release.Wait(Wait);
            return Stamp(path);
        });
        var game = ArtworkFixture.Steam("950");
        var lease = service.Acquire();
        Assert.True(service.WhenReleasedAsync().IsCompleted); // Freigabe besteht: nichts aufzuräumen
        var load = service.GetArtworkAsync(game, 640, Ct);
        Assert.True(entered.Wait(Wait, Ct));

        // Fenster zu, während ein Bildsatz lädt: Das Aufräumen wartet auf die laufende Arbeit.
        lease.Dispose();
        var released = service.WhenReleasedAsync();
        Assert.False(released.IsCompleted);
        release.Set();
        await released.WaitAsync(Wait, Ct);

        // Der Satz wurde fertig geladen und abgelegt, bleibt aber nicht im Speicher.
        Assert.NotNull((await load).Hero);
        Assert.Equal(0, service.MemoryBytes);
        Assert.Null(service.GetCachedArtwork(game));
        Assert.Equal(0, service.Workers);
        Assert.True(File.Exists(Path.Combine(_art.ArtDirectory, "index.json")));
    }
}
