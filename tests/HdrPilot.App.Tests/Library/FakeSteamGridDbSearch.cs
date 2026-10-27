using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// SteamGridDB für Tests des Dialogs „Bild wählen…“ (E93): Zustand des Schlüssels einstellbar, jede Suche eine steuerbare
/// Sitzung, nie das Netz. Gemerkte Zuordnungen werden aufgezeichnet (mit dem Thread, auf dem gemerkt wurde).
/// </summary>
public sealed class FakeSteamGridDbSearch : ISteamGridDbService
{
    private EventHandler? _changed;

    public event EventHandler? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

    public SteamGridDbKeyState Key { get; set; } = SteamGridDbKeyState.Stored;

    public SteamGridDbKeyState KeyState => Key;

    public SteamGridDbStatus Status => SteamGridDbStatus.Off with { Key = Key };

    /// <summary>Wie die nächste Sitzung eingerichtet wird.</summary>
    public Action<FakeSearchSession>? Setup { get; set; }

    public List<FakeSearchSession> Sessions { get; } = [];

    public FakeSearchSession? Session => Sessions.LastOrDefault();

    public List<(string Key, long Id)> Remembered { get; } = [];

    /// <summary>Thread-Kennung je Aufruf von <see cref="RememberMatch"/>.</summary>
    public List<int> RememberedOnThreads { get; } = [];

    /// <summary>Gemerkte SteamGridDB-ID für Spiele ohne Steam-AppID (null: keine).</summary>
    public long? RememberedId { get; set; }

    public ISteamGridDbSearchSession? OpenSearch(GameConfig game)
    {
        if (Key != SteamGridDbKeyState.Stored)
        {
            return null;
        }

        var session = new FakeSearchSession(game.Name, PeekMatch(game));
        Setup?.Invoke(session);
        Sessions.Add(session);
        return session;
    }

    public SteamGridDbMatch? PeekMatch(GameConfig game) =>
        game.Source == HdrPilot.Core.Games.GameSource.Steam && SteamGridDbTarget.IsSteamAppId(game.SourceId)
            ? new SteamGridDbMatch(SteamGridDbMatchKind.SteamAppId, SteamGridDbTarget.Steam(game.SourceId), game.Name, null, false)
            : RememberedId is { } id
                ? new SteamGridDbMatch(SteamGridDbMatchKind.Remembered, SteamGridDbTarget.Game(id), game.Name, null, false)
                : null;

    public void RememberMatch(GameConfig game, long steamGridDbGameId)
    {
        lock (Remembered)
        {
            Remembered.Add((game.Key, steamGridDbGameId));
            RememberedOnThreads.Add(Environment.CurrentManagedThreadId);
        }
    }

    public void RaiseChanged() => _changed?.Invoke(this, EventArgs.Empty);

    public void SetEnabled(bool enabled)
    {
    }

    public Task<SteamGridDbCheckResult> CheckKeyAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(SteamGridDbCheckResult.Valid);

    public void SaveKey(string key)
    {
    }

    public void RemoveKey()
    {
    }

    public Task RunNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void ScheduleStartup(bool allowed)
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Steuerbare Sitzung: Seiten mit erzeugten Einträgen (je Seite einstellbar), Antworten der Namenssuche, Vorschauen sofort
/// oder erst auf Freigabe, Fehler je Aufruf. Zeichnet jeden „Netz“-Aufruf auf.
/// </summary>
public sealed class FakeSearchSession(string gameName, SteamGridDbMatch? initial) : ISteamGridDbSearchSession
{
    private static readonly ImageSource Tiny = CreateTiny();

    public string GameName { get; } = gameName;

    public bool IsSteamGame => InitialMatch?.Kind == SteamGridDbMatchKind.SteamAppId;

    public SteamGridDbMatch? InitialMatch { get; set; } = initial;

    public SteamGridDbSessionUsage Usage => new(ApiCalls.Count, PreviewCalls.Count, 0, Downloads.Count);

    public DateTimeOffset? BlockedUntil { get; set; }

    /// <summary>„Netz“-Aufrufe der Schnittstelle: „resolve“, „byname“, „describe:&lt;id&gt;“, „games:&lt;Begriff&gt;“, „assets:&lt;Art&gt;:&lt;Ziel&gt;:&lt;Seite&gt;“.</summary>
    public List<string> ApiCalls { get; } = [];

    public List<SteamGridDbAsset> PreviewCalls { get; } = [];

    public List<(SteamGridDbAsset Asset, ArtworkKind Kind)> Downloads { get; } = [];

    public List<CancellationToken> QueueTokens { get; } = [];

    /// <summary>Ziele, die SteamGridDB „nicht kennt“ (Seite 0: Spiel unbekannt), etwa „steam/9990150“.</summary>
    public HashSet<string> UnknownTargets { get; } = [];

    /// <summary>Einträge je Seite (Seite → Anzahl gültiger Einträge); fehlende Seiten sind leer.</summary>
    public Dictionary<int, int> PageSizes { get; } = new() { [0] = 50, [1] = 22 };

    /// <summary>Seiten, deren 50 Einträge alle verworfen wurden (keine Kachel, aber die Liste geht weiter).</summary>
    public HashSet<int> FilteredPages { get; } = [];

    /// <summary>Ergebnis der Namenssuche (Standard: ein Treffer mit gleichem Namen, 9101, 2019).</summary>
    public Func<SteamGridDbResolveResult>? Resolve { get; set; }

    /// <summary>Treffer der Spielwahl je Begriff (Standard: drei Treffer).</summary>
    public Func<string, IReadOnlyList<SteamGridDbGameHit>>? Hits { get; set; }

    /// <summary>Fehler des nächsten Aufrufs, der dazu passt (Aufrufname wie in <see cref="ApiCalls"/>).</summary>
    public Func<string, Exception?> Fail { get; set; } = _ => null;

    /// <summary>Gesetzt: Aufrufe der Schnittstelle warten auf diese Quelle.</summary>
    public TaskCompletionSource? HoldApi { get; set; }

    /// <summary>Gesetzt: Vorschauen warten auf diese Quelle.</summary>
    public TaskCompletionSource? HoldPreviews { get; set; }

    /// <summary>
    /// Gesetzt: Vorschauen warten auf diese Quelle, ohne das Token der Warteschlange zu beachten (wie eine schon laufende
    /// Übertragung, die nur das Token der Sitzung beendet).
    /// </summary>
    public TaskCompletionSource? HoldRunningPreviews { get; set; }

    /// <summary>Gesetzt: volle Bilder warten auf diese Quelle.</summary>
    public TaskCompletionSource? HoldDownloads { get; set; }

    /// <summary>Fehler einer Vorschau (null: geladen).</summary>
    public Func<SteamGridDbAsset, Exception?> PreviewFail { get; set; } = _ => null;

    /// <summary>Fehler eines vollen Bildes (null: geladen).</summary>
    public Func<SteamGridDbAsset, Exception?> DownloadFail { get; set; } = _ => null;

    public long PreviewBytes { get; set; } = 4;

    public bool Disposed { get; private set; }

    public static SteamGridDbGameHit Hit(long id, string name, int? year = null, bool verified = false) => new(id, name, year, verified);

    public static SteamGridDbAsset Asset(int n, ArtworkKind kind, bool thumb = true, string? author = "auto", int width = 0, int height = 0)
    {
        var (w, h) = width > 0 ? (width, height) : kind switch
        {
            ArtworkKind.Cover => (600, 900),
            ArtworkKind.Hero => (1920, 620),
            _ => (800, 310),
        };
        return new SteamGridDbAsset(
            n,
            "https://cdn2.steamgriddb.com/" + kind.ToString().ToLowerInvariant() + "/fixture-" + n + ".png",
            w,
            h,
            "image/png",
            thumb ? "https://cdn2.steamgriddb.com/thumb/" + n + ".png" : null,
            author == "auto" ? "Testautor " + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : author);
    }

    public Task<SteamGridDbResolveResult> ResolveAsync(CancellationToken cancellationToken) =>
        InitialMatch is { } known
            ? Task.FromResult(new SteamGridDbResolveResult(known, [], false))
            : Api("resolve", () => Resolve?.Invoke() ?? new SteamGridDbResolveResult(new SteamGridDbMatch(SteamGridDbMatchKind.ExactName, SteamGridDbTarget.Game(9101), GameName, 2019, true), [Hit(9101, GameName, 2019, true), Hit(9102, GameName + ": Neuauflage", 2023)], false), cancellationToken);

    public Task<SteamGridDbResolveResult> ResolveByNameAsync(CancellationToken cancellationToken) =>
        Api("byname", () => Resolve?.Invoke() ?? new SteamGridDbResolveResult(new SteamGridDbMatch(SteamGridDbMatchKind.ExactName, SteamGridDbTarget.Game(9101), GameName, 2019, true), [Hit(9101, GameName, 2019, true)], IsSteamGame), cancellationToken);

    public Task<SteamGridDbGameHit?> DescribeAsync(long gameId, CancellationToken cancellationToken) =>
        Api("describe:" + gameId, () => (SteamGridDbGameHit?)Hit(gameId, "Kupferwald (SteamGridDB)", 2018), cancellationToken);

    public Task<IReadOnlyList<SteamGridDbGameHit>> SearchGamesAsync(string term, CancellationToken cancellationToken) =>
        Api("games:" + term, () => Hits?.Invoke(term) ?? (IReadOnlyList<SteamGridDbGameHit>)[Hit(9101, term, 2019, true), Hit(9102, term + ": Neuauflage", 2023), Hit(9103, term + " II")], cancellationToken);

    public Task<SteamGridDbAssetPage> GetAssetsAsync(SteamGridDbMatch match, ArtworkKind kind, int page, CancellationToken cancellationToken) =>
        Api("assets:" + kind + ":" + match.Target + ":" + page, () =>
        {
            if (UnknownTargets.Contains(match.Target.ToString()))
            {
                return page == 0 ? SteamGridDbAssetPage.Unknown() : SteamGridDbAssetPage.End(page);
            }

            if (FilteredPages.Contains(page))
            {
                return new SteamGridDbAssetPage([], page, 50, 50, false);
            }

            var count = PageSizes.TryGetValue(page, out var size) ? size : 0;
            var offset = PageSizes.Where(p => p.Key < page).Sum(p => p.Value);
            var items = Enumerable.Range(offset + 1, count).Select(n => Asset(n + (int)(match.Target.GameId ?? 0) * 1000, kind, thumb: n % 10 != 0)).ToList();
            return new SteamGridDbAssetPage(items, page, count, 0, false);
        }, cancellationToken);

    public async Task<SteamGridDbPreview> LoadPreviewAsync(SteamGridDbAsset asset, int decodeWidth, CancellationToken queueToken)
    {
        QueueTokens.Add(queueToken);
        if (HoldPreviews is { } hold)
        {
            await hold.Task.WaitAsync(queueToken);
        }

        if (HoldRunningPreviews is { } running)
        {
            await running.Task;
        }
        else
        {
            queueToken.ThrowIfCancellationRequested();
        }

        PreviewCalls.Add(asset);
        if (PreviewFail(asset) is { } error)
        {
            throw error;
        }

        return new SteamGridDbPreview(Tiny, PreviewBytes);
    }

    public async Task<SteamGridDbImageData> DownloadAsync(SteamGridDbAsset asset, ArtworkKind kind, CancellationToken cancellationToken)
    {
        Downloads.Add((asset, kind));
        if (HoldDownloads is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (DownloadFail(asset) is { } error)
        {
            throw error;
        }

        return new SteamGridDbImageData([1, 2, 3, (byte)asset.Id], ".png", asset.Width, asset.Height);
    }

    public void Dispose() => Disposed = true;

    private static ImageSource CreateTiny()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 40, 80, 120, 255 }, 4);
        bitmap.Freeze();
        return bitmap;
    }

    private async Task<T> Api<T>(string call, Func<T> answer, CancellationToken cancellationToken)
    {
        ApiCalls.Add(call);
        if (HoldApi is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Disposed)
        {
            throw new OperationCanceledException();
        }

        if (Fail(call) is { } error)
        {
            throw error;
        }

        await Task.Yield();
        return answer();
    }
}
