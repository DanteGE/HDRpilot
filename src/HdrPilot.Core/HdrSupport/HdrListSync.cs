using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.HdrSupport;

/// <summary>Ein Spiel der Bibliothek, wie der Abgleich es braucht. Installierte und zuletzt gespielte Spiele kommen zuerst dran.</summary>
public sealed record HdrListGame(string Key, GameSource Source, string SourceId, string Name, bool IsInstalled = true, DateTimeOffset? LastPlayed = null);

public enum HdrListRunOutcome
{
    /// <summary>Abgleich gelaufen (auch wenn es nichts zu prüfen gab).</summary>
    Completed,

    /// <summary>Nicht gelaufen: der letzte Abgleich liegt weniger als eine Stunde zurück.</summary>
    Skipped,

    /// <summary>Abgebrochen (Fehler oder Programmende); bis dahin Geprüftes ist gespeichert.</summary>
    Failed,
}

/// <param name="Requests">Gesendete Anfragen.</param>
/// <param name="Checked">In diesem Lauf geprüfte Spiele.</param>
/// <param name="Found">Davon bei PCGamingWiki gefunden (bzw. in der vollständigen Liste).</param>
public sealed record HdrListRunResult(HdrListRunOutcome Outcome, PcgwErrorKind? Error, int Requests, int Checked, int Found, bool AccountMode)
{
    public static HdrListRunResult Skipped { get; } = new(HdrListRunOutcome.Skipped, null, 0, 0, 0, false);
}

/// <summary>
/// Ein Abgleich der HDR-Liste mit PCGamingWiki (synchron, für einen Hintergrund-Thread).
/// <para>
/// Ohne Konto: höchstens <see cref="MaxBatchRequests"/> Abfragen mit je 50 Titeln, nie geprüfte (oder umbenannte)
/// Spiele zuerst, dann solche, deren Prüfung älter als <see cref="RecheckAfter"/> ist; jeweils installierte und
/// zuletzt gespielte vorn. Steam-Spiele zählen nur, wenn die gefundene Seite ihre AppID nennt; GOG-Spiele, wenn die
/// Seite keine oder ihre GOG-ID nennt; andere nach dem Titel (mit Weiterleitungen des Wikis). Nicht gefundene Spiele
/// bekommen später eine Suche nach dem Namen (höchstens <see cref="MaxSearchRequests"/> je Lauf). Deren Treffer muss
/// die AppID (Steam) bzw. GOG-ID nennen oder, ohne Laden-ID, bis auf Satzzeichen genauso heißen wie das Spiel
/// („Avatar Frontiers of Pandora™“ aus Ubisoft Connect = „Avatar: Frontiers of Pandora“).
/// </para>
/// <para>
/// Mit Konto: Anmeldung, dann die vollständige Liste HDR-fähiger Spiele (Cargo, wenige Seiten zu 500 Zeilen); der
/// Abgleich läuft lokal, es werden keine Spielnamen gesendet. Ohne Netz gilt die zuletzt geladene Liste.
/// </para>
/// Bei HTTP 429 oder einem Netzwerkfehler endet der Lauf sofort (eine Protokollzeile); der Zwischenspeicher bleibt.
/// </summary>
public sealed class HdrListSync(HdrSupportCache cache, IClock clock, ILogger? logger = null)
{
    public const int MaxBatchRequests = 2;
    public const int MaxSearchRequests = 2;

    /// <summary>Obergrenze der Seiten der vollständigen Liste (Schutz vor einer endlosen Folge).</summary>
    public const int MaxCatalogPages = 40;

    public static readonly TimeSpan MinRunInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(14);

    /// <summary>Installierte vor nicht installierten, dann zuletzt gespielte zuerst.</summary>
    private static readonly Comparer<HdrListGame> Priority = Comparer<HdrListGame>.Create((a, b) =>
    {
        var installed = b.IsInstalled.CompareTo(a.IsInstalled);
        return installed != 0 ? installed : Nullable.Compare(b.LastPlayed, a.LastPlayed);
    });

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <param name="force">„Jetzt abgleichen“: auch innerhalb einer Stunde nach dem letzten Abgleich.</param>
    public HdrListRunResult Run(IReadOnlyList<HdrListGame> games, PcgwAccount? account, Func<PcgwClient> createClient, bool force, CancellationToken cancellationToken)
    {
        var data = cache.Load();
        var started = clock.UtcNow;
        if (!force && data.LastRunAt is { } last && last <= started && started - last < MinRunInterval)
        {
            return HdrListRunResult.Skipped;
        }

        // Beginn sofort merken: Auch ein Absturz mitten im Abgleich zählt für die Stundenregel.
        data.LastRunAt = started;
        TrySave(data);

        var counts = new Counts();
        PcgwErrorKind? error = null;
        var outcome = HdrListRunOutcome.Completed;
        var client = createClient();
        try
        {
            if (account is not null)
            {
                RunWithAccount(client, data, games, account, counts, cancellationToken);
            }
            else
            {
                RunAnonymous(client, data, games, counts, cancellationToken);
            }

            data.LastError = null;
            data.LastSuccessAt = clock.UtcNow;
            data.AccountMode = account is not null;
            _logger.LogInformation(
                "HDR-Liste abgeglichen ({Mode}): {Requests} Anfragen, {Checked} Spiele geprüft, {Found} bei PCGamingWiki gefunden, {Capable} davon HDR-fähig.",
                account is null ? "ohne Konto" : "mit Konto", client.RequestCount, counts.Checked.Count, counts.Found, counts.Capable);
        }
        catch (PcgwException ex)
        {
            outcome = HdrListRunOutcome.Failed;
            error = ex.Kind;
            data.LastError = ex.Kind;
            _logger.LogWarning("HDR-Liste: Abgleich abgebrochen nach {Requests} Anfragen ({Kind}{Code}). Zwischenspeicher bleibt.",
                client.RequestCount, ex.Kind, ex.Code is null ? string.Empty : ", " + ex.Code);
            if (account is not null && cache.LoadCatalog() is { } stored)
            {
                // Ohne Netz (oder bei abgelehnter Anmeldung): neue oder umbenannte Spiele mit der zuletzt geladenen
                // Liste abgleichen, lokal.
                ApplyCatalog(data, stored, games, counts, onlyUnchecked: true);
            }
        }
        catch (OperationCanceledException)
        {
            outcome = HdrListRunOutcome.Failed; // Programmende: bis hierher Geprüftes bleibt.
        }
        finally
        {
            client.Dispose();
            Prune(data, games);
            TrySave(data);
        }

        return new HdrListRunResult(outcome, error, client.RequestCount, counts.Checked.Count, counts.Found, account is not null);
    }

    /// <summary>
    /// Anmeldung und eine Zeile der vollständigen Liste: prüft Benutzername, Bot-Passwort und das Cargo-Recht
    /// („Speichern und testen“). Wirft <see cref="PcgwException"/>.
    /// </summary>
    public static void TestAccount(PcgwClient client, PcgwAccount account, CancellationToken cancellationToken)
    {
        client.Login(account.UserName, account.Password, cancellationToken);
        client.QueryCatalogPage(0, 1, cancellationToken);
    }

    /// <summary>
    /// Spiele, die eine Abfrage brauchen: nie geprüfte und umbenannte zuerst, dann die fälligen Wiederholungen; jeweils
    /// installierte und zuletzt gespielte vorn, dann die ältesten Prüfungen.
    /// </summary>
    internal static List<HdrListGame> DueGames(HdrSupportData data, IReadOnlyList<HdrListGame> games, DateTimeOffset now) =>
        games
            .Select(g => (Game: g, Entry: data.Games.GetValueOrDefault(g.Key)))
            .Where(x => IsUnchecked(x.Game, x.Entry) || x.Entry!.CheckedAt > now || now - x.Entry.CheckedAt >= RecheckAfter)
            .OrderBy(x => IsUnchecked(x.Game, x.Entry) ? 0 : 1)
            .ThenBy(x => x.Game, Priority)
            .ThenBy(x => x.Entry?.CheckedAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Game.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Game)
            .ToList();

    private static bool IsUnchecked(HdrListGame game, HdrSupportEntry? entry) =>
        entry is null || !string.Equals(entry.Name, game.Name, StringComparison.Ordinal);

    private void RunAnonymous(PcgwClient client, HdrSupportData data, IReadOnlyList<HdrListGame> games, Counts counts, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var budget = MaxBatchRequests * PcgwClient.MaxTitlesPerRequest;
        var titles = new List<string>();
        var planned = new List<(HdrListGame Game, IReadOnlyList<string> Variants)>();
        foreach (var game in DueGames(data, games, now))
        {
            var variants = GameTitles.Variants(game.Name);
            if (variants.Count == 0)
            {
                // Name lässt sich nicht als Wiki-Titel abfragen (etwa mit „|“ oder „#“): als geprüft merken.
                Record(data, game, page: null, now, counts, searchPending: false);
                continue;
            }

            var added = variants.Where(t => !titles.Contains(t, StringComparer.Ordinal)).ToList();
            if (titles.Count + added.Count > budget)
            {
                break;
            }

            titles.AddRange(added);
            planned.Add((game, variants));
        }

        var answers = new Dictionary<string, PcgwPage?>(StringComparer.Ordinal);
        try
        {
            foreach (var batch in titles.Chunk(PcgwClient.MaxTitlesPerRequest))
            {
                foreach (var (title, page) in client.QueryTitles(batch, cancellationToken))
                {
                    answers[title] = page;
                }
            }
        }
        finally
        {
            // Auch nach einem Abbruch: was beantwortet ist, wird übernommen.
            foreach (var (game, variants) in planned)
            {
                PcgwPage? match = null;
                var complete = true;
                foreach (var title in variants)
                {
                    if (!answers.TryGetValue(title, out var page))
                    {
                        complete = false;
                    }
                    else if (page is not null && Confirms(game, page))
                    {
                        match = page;
                        break;
                    }
                }

                if (match is not null || complete)
                {
                    Record(data, game, match, now, counts, searchPending: match is null);
                }
            }
        }

        // Unter dem Namen nicht gefunden: Suche, deren Treffer die Gegenprobe bestehen muss (ConfirmsSearchHit).
        var searches = games
            .Where(g => data.Games.GetValueOrDefault(g.Key) is { SearchPending: true } e && e.Name == g.Name)
            .OrderBy(g => g, Priority)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchRequests)
            .ToList();
        foreach (var game in searches)
        {
            var page = client.SearchFirst(GameTitles.Clean(game.Name), cancellationToken);
            Record(data, game, page is not null && ConfirmsSearchHit(game, page) ? page : null, clock.UtcNow, counts, searchPending: false);
        }
    }

    /// <summary>Gegenprobe einer unter dem Namen gefundenen Seite über die Laden-IDs.</summary>
    internal static bool Confirms(HdrListGame game, PcgwPage page) => game.Source switch
    {
        GameSource.Steam => page.HasSteamAppId(game.SourceId),
        GameSource.Gog => page.GogIds.Count == 0 || page.HasGogId(game.SourceId),
        _ => true,
    };

    /// <summary>
    /// Gegenprobe eines Suchtreffers (die Suche liefert auch Ähnliches): Steam über die AppID; GOG über die GOG-ID,
    /// wenn die Seite welche nennt; sonst muss der Seitentitel bis auf Satzzeichen dem Namen gleichen.
    /// </summary>
    internal static bool ConfirmsSearchHit(HdrListGame game, PcgwPage page) => game.Source switch
    {
        GameSource.Steam => page.HasSteamAppId(game.SourceId),
        GameSource.Gog when page.GogIds.Count > 0 => page.HasGogId(game.SourceId),
        _ => GameTitles.LooselyEqual(game.Name, page.Title),
    };

    private void RunWithAccount(PcgwClient client, HdrSupportData data, IReadOnlyList<HdrListGame> games, PcgwAccount account, Counts counts, CancellationToken cancellationToken)
    {
        client.Login(account.UserName, account.Password, cancellationToken);
        var entries = new List<HdrCatalogEntry>();
        var complete = false;
        for (var page = 0; page < MaxCatalogPages; page++)
        {
            var rows = client.QueryCatalogPage(page * PcgwClient.CatalogPageSize, PcgwClient.CatalogPageSize, cancellationToken);
            entries.AddRange(rows);
            if (rows.Count < PcgwClient.CatalogPageSize)
            {
                complete = true;
                break;
            }
        }

        if (!complete)
        {
            _logger.LogWarning("HDR-Liste: nach {Pages} Seiten abgebrochen, die Liste ist womöglich unvollständig.", MaxCatalogPages);
        }

        var catalog = new HdrCatalogData { FetchedAt = clock.UtcNow, Entries = entries };
        try
        {
            cache.SaveCatalog(catalog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("HDR-Liste: vollständige Liste nicht gespeichert ({Error}).", ex.Message);
        }

        ApplyCatalog(data, catalog, games, counts, onlyUnchecked: false);
    }

    /// <summary>Abgleich mit der vollständigen Liste: Steam über die AppID, GOG über die GOG-ID, sonst über den Namen.</summary>
    internal static void ApplyCatalog(HdrSupportData data, HdrCatalogData catalog, IReadOnlyList<HdrListGame> games, Counts counts, bool onlyUnchecked)
    {
        var bySteam = new Dictionary<string, HdrCatalogEntry>(StringComparer.Ordinal);
        var byGog = new Dictionary<string, HdrCatalogEntry>(StringComparer.Ordinal);
        var byName = new Dictionary<string, HdrCatalogEntry>(StringComparer.Ordinal);
        var byLooseName = new Dictionary<string, HdrCatalogEntry?>(StringComparer.Ordinal); // null: mehrdeutig
        foreach (var entry in catalog.Entries)
        {
            foreach (var id in entry.Steam ?? [])
            {
                bySteam.TryAdd(id, entry);
            }

            foreach (var id in entry.Gog ?? [])
            {
                byGog.TryAdd(id, entry);
            }

            byName.TryAdd(GameTitles.Normalize(entry.Page), entry);
            var loose = GameTitles.LooseKey(entry.Page);
            if (loose.Length > 0 && !byLooseName.TryAdd(loose, entry))
            {
                byLooseName[loose] = null;
            }
        }

        foreach (var game in games)
        {
            if (onlyUnchecked && !IsUnchecked(game, data.Games.GetValueOrDefault(game.Key)))
            {
                continue;
            }

            var hit = game.Source switch
            {
                GameSource.Steam => bySteam.GetValueOrDefault(game.SourceId),
                GameSource.Gog => byGog.GetValueOrDefault(game.SourceId) ?? ByName(game, e => e.Gog is null),
                _ => ByName(game, _ => true),
            };
            Record(data, game, hit is null ? null : new PcgwPage(hit.Page, hit.Status, [], []), catalog.FetchedAt, counts, searchPending: false);
        }

        // Erst der genaue Name (auch als Variante), dann bis auf Satzzeichen, wenn das eindeutig ist.
        HdrCatalogEntry? ByName(HdrListGame game, Func<HdrCatalogEntry, bool> accept)
        {
            foreach (var title in GameTitles.Variants(game.Name))
            {
                if (byName.TryGetValue(GameTitles.Normalize(title), out var entry) && accept(entry))
                {
                    return entry;
                }
            }

            return byLooseName.GetValueOrDefault(GameTitles.LooseKey(game.Name)) is { } loose && accept(loose) ? loose : null;
        }
    }

    private static void Record(HdrSupportData data, HdrListGame game, PcgwPage? page, DateTimeOffset at, Counts counts, bool searchPending)
    {
        data.Games[game.Key] = new HdrSupportEntry
        {
            Status = page?.Status ?? HdrSupportStatus.Unknown,
            PcgwTitle = page?.Title,
            CheckedAt = at,
            Name = game.Name,
            SearchPending = searchPending,
        };
        counts.Checked.Add(game.Key); // ein Spiel zählt einmal, auch wenn die Suche es später findet
        if (page is not null)
        {
            counts.Found++;
            counts.Capable += HdrSupportValues.IsHdrCapable(page.Status) ? 1 : 0;
        }
    }

    /// <summary>Einträge entfernter Spiele verwerfen (nicht bei leerer Bibliothek, etwa nach einem Lesefehler).</summary>
    private static void Prune(HdrSupportData data, IReadOnlyList<HdrListGame> games)
    {
        if (games.Count == 0)
        {
            return;
        }

        var keys = new HashSet<string>(games.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var key in data.Games.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            data.Games.Remove(key);
        }
    }

    private void TrySave(HdrSupportData data)
    {
        try
        {
            cache.Save(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("HDR-Liste: Zwischenspeicher nicht gespeichert ({Error}).", ex.Message);
        }
    }

    internal sealed class Counts
    {
        public HashSet<string> Checked { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Found { get; set; }

        public int Capable { get; set; }
    }
}
