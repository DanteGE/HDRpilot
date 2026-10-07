using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace HdrPilot.Tests.TestSupport;

/// <summary>Eine aufgezeichnete Anfrage an die Attrappe (Parameter aus Adresse und Formular, dekodiert).</summary>
public sealed record PcgwRequest(HttpMethod Method, Uri Uri, NameValueCollection Parameters, string? Body, string? UserAgent, int ManagedThreadId, ThreadPriority Priority)
{
    public string? this[string name] => Parameters[name];

    public string Action => Parameters["action"] ?? string.Empty;

    /// <summary>Gesamte Anfrage als Text (Adresse und Rumpf), um zu prüfen, was gesendet wurde.</summary>
    public string Raw => Uri + "\n" + Body;
}

/// <summary>
/// HttpMessageHandler ohne Netz: beantwortet jede Anfrage über <see cref="Respond"/> und zeichnet sie auf. Unterstützt
/// den synchronen Weg (HttpClient.Send), den der Abgleich benutzt, und den asynchronen.
/// </summary>
public sealed class FakeHttpHandler(Func<PcgwRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<PcgwRequest> _requests = [];

    public IReadOnlyList<PcgwRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public bool Disposed { get; private set; }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Json(object value) => Json(JsonSerializer.Serialize(value));

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent(string.Empty) };

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        var parameters = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        if (body is not null && request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded")
        {
            parameters.Add(HttpUtility.ParseQueryString(body));
        }

        var recorded = new PcgwRequest(
            request.Method, request.RequestUri, parameters, body,
            request.Headers.TryGetValues("User-Agent", out var ua) ? string.Join(" ", ua) : null,
            Environment.CurrentManagedThreadId, Thread.CurrentThread.Priority);
        lock (_gate)
        {
            _requests.Add(recorded);
        }

        return respond(recorded);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>
/// Nachbildung der Teile von PCGamingWiki, die HdrPilot benutzt (Antworten im Aufbau wie aufgezeichnet, formatversion=2):
/// Titelabfrage mit Normalisierung und Weiterleitungen, Volltextsuche (ein Treffer), Anmeldung mit Bot-Passwort und
/// die Cargo-Abfrage der HDR-Liste (nur angemeldet und mit Cargo-Recht). Fehler lassen sich je Anfrage einspielen.
/// </summary>
public sealed class FakeWiki
{
    private readonly object _gate = new();
    private bool _loggedIn;

    public Dictionary<string, string> Pages { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Redirects { get; } = new(StringComparer.Ordinal);

    /// <summary>Suchbegriff → Titel des ersten Treffers.</summary>
    public Dictionary<string, string> Search { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bot-Passwörter: „Name@BotName“ → Passwort.</summary>
    public Dictionary<string, string> Accounts { get; } = new(StringComparer.Ordinal);

    /// <summary>Hat das Bot-Passwort das Cargo-Recht?</summary>
    public bool CargoGranted { get; set; } = true;

    /// <summary>Zeilen der Cargo-Abfrage (nur HDR-fähige, wie die where-Bedingung).</summary>
    public List<(string Page, string? Steam, string? Gog, string Hdr)> CargoRows { get; } = [];

    /// <summary>Antwort für die n-te Anfrage (ab 1) statt der normalen, etwa HTTP 429.</summary>
    public Dictionary<int, Func<HttpResponseMessage>> Failures { get; } = [];

    public int RequestNumber { get; private set; }

    public FakeHttpHandler CreateHandler() => new(Respond);

    /// <summary>Wikitext einer Seite mit den Feldern, die HdrPilot liest.</summary>
    public static string Wikitext(string? steam, string hdr, string? gog = null) =>
        "{{Infobox game\n|steam appid  = " + steam + "\n|steam appid side = \n|gogcom id    = " + gog + "\n|gogcom id side = \n}}\n"
        + "{{Video\n|hdr                        = " + hdr + "\n|hdr notes                  = \n}}";

    public FakeWiki Page(string title, string? steam, string hdr, string? gog = null)
    {
        Pages[title] = Wikitext(steam, hdr, gog);
        return this;
    }

    private HttpResponseMessage Respond(PcgwRequest request)
    {
        int number;
        lock (_gate)
        {
            number = ++RequestNumber;
        }

        if (Failures.TryGetValue(number, out var failure))
        {
            return failure();
        }

        return request.Action switch
        {
            "query" when request["meta"] == "tokens" => FakeHttpHandler.Json(new { batchcomplete = true, query = new { tokens = new { logintoken = "abc123+\\" } } }),
            "query" when request["generator"] == "search" => SearchResponse(request["gsrsearch"] ?? string.Empty),
            "query" => TitlesResponse((request["titles"] ?? string.Empty).Split('|')),
            "login" => Login(request),
            "cargoquery" => Cargo(request),
            _ => FakeHttpHandler.Json(new { error = new { code = "badvalue", info = "Unbekannte Aktion" } }),
        };
    }

    private HttpResponseMessage TitlesResponse(string[] titles)
    {
        var normalized = new List<object>();
        var redirects = new List<object>();
        var pages = new List<object>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requested in titles)
        {
            if (requested.IndexOfAny(['[', ']', '{', '}', '<', '>', '#']) >= 0)
            {
                pages.Add(new { title = requested, invalidreason = "invalid characters", invalid = true });
                continue;
            }

            var title = Normalize(requested);
            if (title != requested)
            {
                normalized.Add(new { fromencoded = false, from = requested, to = title });
            }

            if (Redirects.TryGetValue(title, out var target))
            {
                redirects.Add(new { from = title, to = target });
                title = target;
            }

            if (!seen.Add(title))
            {
                continue;
            }

            pages.Add(Pages.TryGetValue(title, out var content)
                ? PageObject(title, content)
                : new { ns = 0, title, missing = true });
        }

        var query = new Dictionary<string, object>();
        if (normalized.Count > 0)
        {
            query["normalized"] = normalized;
        }

        if (redirects.Count > 0)
        {
            query["redirects"] = redirects;
        }

        query["pages"] = pages;
        return FakeHttpHandler.Json(new { batchcomplete = true, query });
    }

    private HttpResponseMessage SearchResponse(string text) =>
        Search.TryGetValue(text, out var title) && Pages.TryGetValue(title, out var content)
            ? FakeHttpHandler.Json(new { batchcomplete = true, query = new { pages = new[] { PageObject(title, content) } } })
            : FakeHttpHandler.Json(new { batchcomplete = true });

    private HttpResponseMessage Login(PcgwRequest request)
    {
        if (request.Method != HttpMethod.Post || request["lgtoken"] != "abc123+\\")
        {
            return FakeHttpHandler.Json(new { login = new { result = "WrongToken" } });
        }

        if (request["lgname"] is { } name && Accounts.TryGetValue(name, out var password) && password == request["lgpassword"])
        {
            _loggedIn = true;
            return FakeHttpHandler.Json(new { login = new { result = "Success", lguserid = 1, lgusername = name.Split('@')[0] } });
        }

        return FakeHttpHandler.Json(new { login = new { result = "Failed", reason = "Incorrect username or password entered. Please try again." } });
    }

    private HttpResponseMessage Cargo(PcgwRequest request)
    {
        if (!_loggedIn || !CargoGranted)
        {
            return FakeHttpHandler.Json(File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Pcgw", "cargo-permissiondenied.json")));
        }

        var offset = int.Parse(request["offset"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var limit = int.Parse(request["limit"] ?? "50", System.Globalization.CultureInfo.InvariantCulture);
        var rows = CargoRows.Skip(offset).Take(limit)
            .Select(r => new { title = new Dictionary<string, string?> { ["Page"] = r.Page, ["Steam"] = r.Steam, ["GOG"] = r.Gog, ["HDR"] = r.Hdr } })
            .ToList();
        return FakeHttpHandler.Json(new { limits = new { cargoquery = 500 }, cargoquery = rows });
    }

    private static object PageObject(string title, string content) => new
    {
        pageid = Math.Abs(title.GetHashCode(StringComparison.Ordinal)) % 100000,
        ns = 0,
        title,
        revisions = new[] { new { slots = new { main = new { contentmodel = "wikitext", contentformat = "text/x-wiki", content } } } },
    };

    /// <summary>Wie MediaWiki: Unterstriche als Leerzeichen, erster Buchstabe groß.</summary>
    private static string Normalize(string title)
    {
        var t = title.Replace('_', ' ').Trim();
        return t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];
    }
}
