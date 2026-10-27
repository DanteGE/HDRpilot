using System.Net;
using System.Text.Json;
using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.HdrSupport;

/// <summary>Art eines Fehlers beim Abruf von PCGamingWiki.</summary>
public enum PcgwErrorKind
{
    /// <summary>Keine Verbindung, Zeitüberschreitung, abgebrochene Übertragung.</summary>
    Network,

    /// <summary>Zu viele Anfragen (HTTP 429 oder API-Fehler „ratelimited“); PCGamingWiki sperrt dann 60 s.</summary>
    RateLimited,

    /// <summary>Anderer HTTP-Status (etwa 403 oder 5xx).</summary>
    HttpError,

    /// <summary>Anmeldung abgelehnt (falscher Benutzername oder falsches Bot-Passwort).</summary>
    LoginFailed,

    /// <summary>Zu viele Anmeldeversuche, PCGamingWiki lässt eine Weile keine weiteren zu.</summary>
    LoginThrottled,

    /// <summary>Keine Berechtigung (ohne Anmeldung oder Bot-Passwort ohne das Cargo-Recht).</summary>
    PermissionDenied,

    /// <summary>Anderer Fehler der Schnittstelle.</summary>
    ApiError,

    /// <summary>Antwort nicht lesbar.</summary>
    InvalidResponse,

    /// <summary>Gespeicherte Anmeldedaten lassen sich nicht entschlüsseln (anderer Windows-Benutzer, Profil kopiert).</summary>
    AccountUnreadable,

    /// <summary>Anmeldedaten ließen sich nicht speichern.</summary>
    AccountNotSaved,
}

/// <summary>
/// Abstand zwischen zwei Anfragen, über alle Clients eines Dienstes hinweg, gemessen ab dem Ende der vorigen Anfrage.
/// PCGamingWiki nutzt <see cref="WaitTurn"/> und <see cref="MarkDone"/> (Abgleich und Kontotest laufen nacheinander).
/// SteamGridDB nutzt den exklusiven Zug <see cref="Enter"/>, weil dort automatischer Lauf, „Prüfen“ und die Suche in
/// „Bild wählen…“ gleichzeitig auf verschiedenen Threads fragen können (E93).
/// </summary>
public sealed class RequestPacer(IClock clock, TimeSpan interval)
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _turn = new(1, 1);
    private DateTimeOffset? _lastDone;

    public TimeSpan Interval => interval;

    /// <summary>
    /// Exklusiver Zug: höchstens ein Inhaber zugleich, frühestens <see cref="Interval"/> nach dem Ende des vorigen Zugs.
    /// <see cref="RequestTurn.Dispose"/> vermerkt das Ende und gibt den Zug frei (genau einmal). Ein Abbruch beim Warten
    /// gibt nur frei, was er hält.
    /// </summary>
    public RequestTurn Enter(CancellationToken cancellationToken)
    {
        _turn.Wait(cancellationToken);
        try
        {
            DateTimeOffset? last;
            lock (_gate)
            {
                last = _lastDone;
            }

            if (last is { } done && done + interval - clock.UtcNow is { } wait && wait > TimeSpan.Zero)
            {
                clock.Delay(wait, cancellationToken).GetAwaiter().GetResult();
            }
        }
        catch
        {
            _turn.Release();
            throw;
        }

        return new RequestTurn(new Lease(this));
    }

    public void WaitTurn(CancellationToken cancellationToken)
    {
        DateTimeOffset? last;
        lock (_gate)
        {
            last = _lastDone;
        }

        if (last is { } done && done + interval - clock.UtcNow is { } wait && wait > TimeSpan.Zero)
        {
            clock.Delay(wait, cancellationToken).GetAwaiter().GetResult();
        }
    }

    public void MarkDone()
    {
        lock (_gate)
        {
            _lastDone = clock.UtcNow;
        }
    }

    /// <summary>Hält einen Zug; gibt ihn auch bei kopierten <see cref="RequestTurn"/>-Werten nur einmal frei.</summary>
    internal sealed class Lease(RequestPacer pacer)
    {
        private int _released;

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                pacer.MarkDone();
                pacer._turn.Release();
            }
        }
    }
}

/// <summary>Zug von <see cref="RequestPacer.Enter"/>; <c>default</c> hält nichts (ungetaktete Anfragen).</summary>
public readonly struct RequestTurn : IDisposable
{
    private readonly RequestPacer.Lease? _lease;

    internal RequestTurn(RequestPacer.Lease lease) => _lease = lease;

    /// <summary>Vermerkt das Ende des Zugs und gibt ihn frei; weitere Aufrufe tun nichts.</summary>
    public void Dispose() => _lease?.Release();
}

public sealed class PcgwException(PcgwErrorKind kind, string? code, string message, Exception? inner = null) : Exception(message, inner)
{
    public PcgwErrorKind Kind { get; } = kind;

    /// <summary>Fehlercode der Schnittstelle, Ergebnis der Anmeldung oder „http-&lt;Status&gt;“.</summary>
    public string? Code { get; } = code;
}

/// <summary>Ein HDR-fähiger Eintrag der vollständigen Liste (Cargo-Abfrage, nur mit Konto).</summary>
public sealed class HdrCatalogEntry
{
    public string Page { get; set; } = string.Empty;

    public HdrSupportStatus Status { get; set; }

    public List<string>? Steam { get; set; }

    public List<string>? Gog { get; set; }
}

/// <summary>
/// Zugriff auf die MediaWiki-Schnittstelle von PCGamingWiki (https://www.pcgamingwiki.com/w/api.php), synchron für
/// einen eigenen Hintergrund-Thread. Zwischen zwei Anfragen liegen immer mindestens <see cref="PauseBetweenRequests"/>
/// (Grenze des Wikis: 60 Anfragen je Minute, bei HTTP 429 eine Minute Sperre). Fehler werden als
/// <see cref="PcgwException"/> gemeldet; es gibt keine Wiederholungen. Antworten kommen komprimiert (gzip/Brotli,
/// eingestellt am Handler).
/// </summary>
public sealed class PcgwClient : IDisposable
{
    public const string ApiUrl = "https://www.pcgamingwiki.com/w/api.php";

    /// <summary>Höchstzahl Titel je Abfrage (Grenze von MediaWiki für normale Konten).</summary>
    public const int MaxTitlesPerRequest = 50;

    /// <summary>Zeilen je Seite der Cargo-Abfrage.</summary>
    public const int CatalogPageSize = 500;

    public static readonly TimeSpan PauseBetweenRequests = TimeSpan.FromSeconds(2);

    /// <summary>Werte von Video.HDR, die die vollständige Liste enthält.</summary>
    private const string CatalogWhere = "Video.HDR IN (\"true\",\"limited\",\"always on\",\"hackable\")";

    /// <summary>Höchstzahl der verfolgten Weiterleitungen je Titel (Schutz vor Schleifen).</summary>
    private const int MaxRedirectHops = 5;

    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;

    /// <param name="handler">Handler mit Dekomprimierung und Cookie-Speicher (Anmeldung); wird mit dem Client freigegeben.</param>
    /// <param name="pacer">Gemeinsamer Abstand mehrerer Clients; null: eigener mit <see cref="PauseBetweenRequests"/>.</param>
    public PcgwClient(HttpMessageHandler handler, IClock clock, string userAgent, TimeSpan timeout, RequestPacer? pacer = null)
    {
        _pacer = pacer ?? new RequestPacer(clock, PauseBetweenRequests);
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout,
            MaxResponseContentBufferSize = 32 * 1024 * 1024,
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
    }

    /// <summary>Anzahl der gesendeten Anfragen (auch gescheiterte).</summary>
    public int RequestCount { get; private set; }

    /// <summary>
    /// Fragt bis zu 50 Titel samt Wikitext ab und folgt Weiterleitungen (auch Ketten). Ergebnis je angefragtem Titel:
    /// die Seite oder null, wenn es sie nicht gibt (oder der Titel ungültig ist oder in ein anderes Wiki führt). Titel
    /// ohne Antwort (Ergebnis zu groß, Rest käme erst mit einer Fortsetzung) fehlen im Ergebnis und gelten als nicht
    /// geprüft.
    /// </summary>
    public IReadOnlyDictionary<string, PcgwPage?> QueryTitles(IReadOnlyList<string> titles, CancellationToken cancellationToken)
    {
        if (titles.Count is 0 or > MaxTitlesPerRequest)
        {
            throw new ArgumentOutOfRangeException(nameof(titles), titles.Count, "1 bis 50 Titel je Anfrage.");
        }

        using var doc = Get(cancellationToken,
            ("action", "query"), ("format", "json"), ("formatversion", "2"), ("prop", "revisions"), ("rvprop", "content"),
            ("rvslots", "main"), ("redirects", "1"), ("titles", string.Join('|', titles)));

        var result = new Dictionary<string, PcgwPage?>(StringComparer.Ordinal);
        if (!doc.RootElement.TryGetProperty("query", out var query))
        {
            return result;
        }

        var normalized = Mapping(query, "normalized");
        var redirects = Mapping(query, "redirects");
        var pages = new Dictionary<string, PcgwPage?>(StringComparer.Ordinal);
        if (query.TryGetProperty("pages", out var pageList) && pageList.ValueKind == JsonValueKind.Array)
        {
            foreach (var page in pageList.EnumerateArray())
            {
                if (String(page, "title") is not { } title)
                {
                    continue;
                }

                if (Flag(page, "missing") || Flag(page, "invalid"))
                {
                    pages[title] = null;
                }
                else if (Content(page) is { } content)
                {
                    pages[title] = PcgwWikitext.Parse(title, content);
                }
            }
        }

        // Titel mit Interwiki-Präfix (etwa „wikipedia:…“) verweisen auf ein anderes Wiki: hier gibt es sie nicht.
        if (query.TryGetProperty("interwiki", out var interwiki) && interwiki.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in interwiki.EnumerateArray())
            {
                if (String(item, "title") is { } title)
                {
                    pages.TryAdd(title, null);
                }
            }
        }

        foreach (var requested in titles)
        {
            // Weiterleitungsketten (A → B → C) stehen Schritt für Schritt in "redirects", in "pages" nur das Ziel.
            var title = normalized.GetValueOrDefault(requested, requested);
            for (var hop = 0; hop < MaxRedirectHops && redirects.TryGetValue(title, out var next); hop++)
            {
                title = next;
            }

            if (pages.TryGetValue(title, out var page))
            {
                result[requested] = page;
            }
        }

        return result;
    }

    /// <summary>Erster Treffer der Volltextsuche im Artikel-Namensraum, samt Wikitext; null ohne Treffer.</summary>
    public PcgwPage? SearchFirst(string text, CancellationToken cancellationToken)
    {
        using var doc = Get(cancellationToken,
            ("action", "query"), ("format", "json"), ("formatversion", "2"), ("generator", "search"), ("gsrsearch", text),
            ("gsrlimit", "1"), ("gsrnamespace", "0"), ("prop", "revisions"), ("rvprop", "content"), ("rvslots", "main"));
        if (!doc.RootElement.TryGetProperty("query", out var query)
            || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var page in pages.EnumerateArray())
        {
            if (String(page, "title") is { } title && !Flag(page, "missing") && Content(page) is { } content)
            {
                return PcgwWikitext.Parse(title, content);
            }
        }

        return null;
    }

    /// <summary>
    /// Anmeldung mit Bot-Passwort (MediaWiki API:Login): Anmelde-Token holen, dann action=login. Die Sitzung steckt
    /// danach in den Cookies des Handlers. Das Passwort wird nur im Rumpf der POST-Anfrage gesendet.
    /// </summary>
    public void Login(string userName, string password, CancellationToken cancellationToken)
    {
        string token;
        using (var doc = Get(cancellationToken, ("action", "query"), ("meta", "tokens"), ("type", "login"), ("format", "json"), ("formatversion", "2")))
        {
            token = doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("tokens", out var t) && String(t, "logintoken") is { } value
                ? value
                : throw new PcgwException(PcgwErrorKind.InvalidResponse, null, "Kein Anmelde-Token erhalten.");
        }

        using var form = new FormUrlEncodedContent(
        [
            new("action", "login"), new("format", "json"), new("formatversion", "2"),
            new("lgname", userName), new("lgpassword", password), new("lgtoken", token),
        ]);
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = form };
        using var response = Send(request, cancellationToken);
        var login = response.RootElement.TryGetProperty("login", out var l) ? l : default;
        var result = login.ValueKind == JsonValueKind.Object ? String(login, "result") : null;
        switch (result)
        {
            case "Success":
                return;
            case "Throttled": // MediaWiki vor 1.27
            case "Failed" when IsThrottled(login):
                throw new PcgwException(PcgwErrorKind.LoginThrottled, result, "Zu viele Anmeldeversuche.");
            case null:
                throw new PcgwException(PcgwErrorKind.InvalidResponse, null, "Antwort der Anmeldung nicht lesbar.");
            default:
                // Failed, WrongToken, NeedToken, Aborted: Grund steht in "reason" (enthält nie das Passwort).
                throw new PcgwException(PcgwErrorKind.LoginFailed, result, "Anmeldung abgelehnt: " + result);
        }
    }

    /// <summary>Eine Seite der vollständigen Liste HDR-fähiger Spiele (Cargo, braucht Anmeldung mit Cargo-Recht).</summary>
    public IReadOnlyList<HdrCatalogEntry> QueryCatalogPage(int offset, int limit, CancellationToken cancellationToken)
    {
        using var doc = Get(cancellationToken,
            ("action", "cargoquery"), ("format", "json"), ("tables", "Game,Video"), ("join_on", "Game._pageID=Video._pageID"),
            ("fields", "Game._pageName=Page,Game.Steam_AppID=Steam,Game.GOGcom_ID=GOG,Video.HDR=HDR"),
            ("where", CatalogWhere), ("order_by", "Game._pageID"),
            ("limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (!doc.RootElement.TryGetProperty("cargoquery", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            throw new PcgwException(PcgwErrorKind.InvalidResponse, null, "Antwort der Cargo-Abfrage nicht lesbar.");
        }

        var entries = new List<HdrCatalogEntry>(rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("title", out var fields) || fields.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var page = Decoded(fields, "Page");
            if (string.IsNullOrWhiteSpace(page))
            {
                continue;
            }

            var steam = PcgwWikitext.ParseIds(Decoded(fields, "Steam"));
            var gog = PcgwWikitext.ParseIds(Decoded(fields, "GOG"));
            entries.Add(new HdrCatalogEntry
            {
                Page = page,
                Status = HdrSupportValues.Parse(Decoded(fields, "HDR")),
                Steam = steam.Count > 0 ? [.. steam] : null,
                Gog = gog.Count > 0 ? [.. gog] : null,
            });
        }

        return entries;
    }

    public void Dispose() => _http.Dispose();

    private JsonDocument Get(CancellationToken cancellationToken, params (string Name, string Value)[] parameters)
    {
        var query = string.Join('&', parameters.Select(p => Uri.EscapeDataString(p.Name) + "=" + Uri.EscapeDataString(p.Value)));
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl + "?" + query);
        return Send(request, cancellationToken);
    }

    private JsonDocument Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _pacer.WaitTurn(cancellationToken);
        RequestCount++;
        HttpResponseMessage response;
        try
        {
            response = _http.Send(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or NotSupportedException)
        {
            // OperationCanceledException ohne eigenen Abbruch: Zeitüberschreitung.
            throw new PcgwException(PcgwErrorKind.Network, null, "PCGamingWiki nicht erreichbar: " + ex.Message, ex);
        }
        finally
        {
            _pacer.MarkDone();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new PcgwException(PcgwErrorKind.RateLimited, "http-429", "Zu viele Anfragen (HTTP 429).");
            }

            if (!response.IsSuccessStatusCode)
            {
                var code = "http-" + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
                throw new PcgwException(PcgwErrorKind.HttpError, code, "Antwort mit HTTP-Status " + (int)response.StatusCode + ".");
            }

            JsonDocument doc;
            try
            {
                using var stream = response.Content.ReadAsStream(cancellationToken);
                doc = JsonDocument.Parse(stream);
            }
            catch (JsonException ex)
            {
                throw new PcgwException(PcgwErrorKind.InvalidResponse, null, "Antwort ist kein gültiges JSON.", ex);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                throw new PcgwException(PcgwErrorKind.Network, null, "Übertragung abgebrochen: " + ex.Message, ex);
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.ValueKind == JsonValueKind.Object ? String(error, "code") : null;
                doc.Dispose();
                var kind = code switch
                {
                    "ratelimited" => PcgwErrorKind.RateLimited,
                    "permissiondenied" or "readapidenied" => PcgwErrorKind.PermissionDenied,
                    _ => PcgwErrorKind.ApiError,
                };
                throw new PcgwException(kind, code, "Fehler der Schnittstelle: " + (code ?? "unbekannt"));
            }

            return doc;
        }
    }

    private static Dictionary<string, string> Mapping(JsonElement query, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (query.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (String(item, "from") is { } from && String(item, "to") is { } to)
                {
                    map[from] = to;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Sperre nach zu vielen Anmeldeversuchen: Seit MediaWiki 1.27 kommt sie als „Failed“, erkennbar nur am Grund. Im
    /// voreingestellten Fehlerformat ist das englischer Text („You have made too many recent login attempts …“), in
    /// den neueren ein Objekt mit dem Code „login-throttled“.
    /// </summary>
    private static bool IsThrottled(JsonElement login) =>
        login.TryGetProperty("reason", out var reason) && reason.ValueKind switch
        {
            JsonValueKind.String => reason.GetString()!.Contains("too many recent login attempts", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Object => String(reason, "code") == "login-throttled",
            _ => false,
        };

    private static string? Content(JsonElement page)
    {
        if (!page.TryGetProperty("revisions", out var revisions) || revisions.ValueKind != JsonValueKind.Array || revisions.GetArrayLength() == 0)
        {
            return null;
        }

        var revision = revisions[0];
        if (revision.TryGetProperty("slots", out var slots) && slots.TryGetProperty("main", out var main))
        {
            return String(main, "content") ?? String(main, "*");
        }

        return String(revision, "content") ?? String(revision, "*");
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.False or JsonValueKind.Null);

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Cargo liefert Werte HTML-kodiert (etwa „&amp;amp;“ und „&amp;#039;“).</summary>
    private static string? Decoded(JsonElement fields, string name)
    {
        foreach (var property in fields.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(property.Value.GetString()) : null;
            }
        }

        return null;
    }
}
