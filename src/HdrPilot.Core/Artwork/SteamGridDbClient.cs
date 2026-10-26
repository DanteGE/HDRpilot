using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.Core.Artwork;

/// <summary>Art eines Fehlers beim Abruf von SteamGridDB.</summary>
public enum SteamGridDbErrorKind
{
    /// <summary>Keine Verbindung, Zeitüberschreitung (dann <c>Data["timeout"] = true</c>), abgebrochene Übertragung.</summary>
    Network,

    /// <summary>Schlüssel abgelehnt (HTTP 401 oder 403 der Schnittstelle; bei Bildern nie, siehe E93).</summary>
    Unauthorized,

    /// <summary>Zu viele Anfragen (HTTP 429).</summary>
    RateLimited,

    /// <summary>Anderer HTTP-Status (etwa 5xx, auch 401/403 eines Bildes).</summary>
    HttpError,

    /// <summary>Antwort nicht lesbar.</summary>
    InvalidResponse,

    /// <summary>Bildadresse außerhalb von steamgriddb.com, ohne https, ohne Vorschau oder Antwort zu groß: nicht geladen.</summary>
    Blocked,

    /// <summary>Höchstzahl an Anfragen je Lauf oder je Suche erreicht.</summary>
    RequestLimit,
}

public sealed class SteamGridDbException(SteamGridDbErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public SteamGridDbErrorKind Kind { get; } = kind;

    /// <summary>True, wenn eine Zeitgrenze die Anfrage beendet hat (<c>Data["timeout"]</c>).</summary>
    public bool IsTimeout => Data["timeout"] is true;
}

/// <summary>Bildarten von SteamGridDB (Endpunkte grids, heroes, logos).</summary>
public enum SteamGridDbAssetKind
{
    /// <summary>Hochformat 600×900 (Cover).</summary>
    Grid,

    /// <summary>Titelbild.</summary>
    Hero,

    Logo,
}

/// <summary>
/// Ein Bild aus einer Antwort von SteamGridDB (beim automatischen Lauf ist das erste das am besten bewertete).
/// <see cref="Width"/> und <see cref="Height"/> sind die Größe des vollen Bildes, nie die der Vorschau.
/// <see cref="ThumbUrl"/> ist die geprüfte Vorschau-Adresse (null ohne brauchbare), <see cref="Author"/> der bereinigte
/// Autorenname.
/// </summary>
public sealed record SteamGridDbAsset(long Id, string Url, int Width, int Height, string? Mime,
    string? ThumbUrl = null, string? Author = null, string? Style = null);

/// <summary>
/// Zugriff auf die Schnittstelle von SteamGridDB (https://www.steamgriddb.com/api/v2/, E89, E93), synchron für einen
/// eigenen Hintergrund-Thread wie <see cref="PcgwClient"/>. Der Schlüssel des Nutzers geht nur als
/// <c>Authorization: Bearer</c> an www.steamgriddb.com, nie an andere Rechner und nie ins Protokoll. Bilder werden nur
/// von https-Adressen unter steamgriddb.com geladen (auch nach Weiterleitungen geprüft), mit Größengrenze je Zweck
/// (<see cref="MaxDownloadBytes"/> automatisch, <see cref="MaxPreviewBytes"/> je Vorschau,
/// <see cref="MaxPickedImageBytes"/> für ein in „Bild wählen…“ gewähltes Bild). Jede Anfrage an die Schnittstelle nimmt
/// einen exklusiven Zug des gemeinsamen <see cref="RequestPacer"/> (mindestens <see cref="PauseBetweenRequests"/> vom
/// Ende der vorigen); Bilder des automatischen Laufs ebenso, Vorschauen und gewählte Bilder nie. Jede Übertragung hat
/// Zeitgrenzen (<see cref="SteamGridDbTimeouts"/>) und bricht beim Abbruch des Aufrufers sofort ab. Ohne
/// <see cref="SteamGridDbBudget"/> gelten höchstens <c>maxRequests</c> Anfragen je Client. Fehler kommen als
/// <see cref="SteamGridDbException"/>; es gibt keine Wiederholungen.
/// </summary>
public sealed class SteamGridDbClient : IDisposable
{
    public const string ApiBase = "https://www.steamgriddb.com/api/v2/";

    public const int MaxRequests = 200;

    public const long MaxDownloadBytes = 15L * 1024 * 1024;

    /// <summary>Standardgröße einer Seite der Schnittstelle; <c>limit</c> wird nie gesendet.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Höchstzahl der Treffer für die Spielwahl.</summary>
    public const int MaxHits = 20;

    /// <summary>Größengrenze je Vorschau.</summary>
    public const long MaxPreviewBytes = 4L * 1024 * 1024;

    /// <summary>Größengrenze eines in „Bild wählen…“ gewählten Bildes (= ArtworkImaging.MaxFileBytes der App).</summary>
    public const long MaxPickedImageBytes = 32L * 1024 * 1024;

    public static readonly TimeSpan PauseBetweenRequests = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private const long MaxJsonBytes = 2L * 1024 * 1024;

    private const int MaxSearchTermLength = 100;

    private const int MaxNameLength = 64;

    private static readonly string[] ImageMimes = ["image/png", "image/jpeg", "image/jpg", "image/webp"];

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    private static readonly string[] AnimatedExtensions = [".webm", ".mp4", ".gif"];

    private static readonly string[] BlockedTags = ["nsfw", "humor", "epilepsy"];

    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;
    private readonly SteamGridDbBudget? _budget;
    private readonly SteamGridDbTimeouts _timeouts;
    private readonly string _key;
    private readonly int _maxRequests;
    private int _reserved;
    private int _apiRequests;
    private int _imageRequests;

    /// <param name="handler">HTTP-Handler (in Tests eine Attrappe); wird mit dem Client freigegeben.</param>
    /// <param name="apiKey">API-Schlüssel des Nutzers.</param>
    /// <param name="maxRequests">Höchstzahl aller Anfragen, nur ohne <paramref name="budget"/> (automatischer Lauf).</param>
    /// <param name="pacer">Gemeinsamer Abstand des Dienstes; ohne einen eigener je Client.</param>
    /// <param name="budget">Getrennte Grenzen einer Suche in „Bild wählen…“; null = Gesamtgrenze wie bisher.</param>
    /// <param name="timeouts">Zeitgrenzen; null = <see cref="SteamGridDbTimeouts.Default"/>.</param>
    public SteamGridDbClient(HttpMessageHandler handler, IClock clock, string apiKey, string userAgent, int maxRequests = MaxRequests,
        RequestPacer? pacer = null, SteamGridDbBudget? budget = null, SteamGridDbTimeouts? timeouts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _key = apiKey.Trim();
        _maxRequests = maxRequests;
        _budget = budget;
        _timeouts = timeouts ?? SteamGridDbTimeouts.Default;
        _pacer = pacer ?? new RequestPacer(clock, PauseBetweenRequests);
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = _timeouts.Headers,
            MaxResponseContentBufferSize = MaxPickedImageBytes,
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
    }

    private enum RequestClass
    {
        Api,
        Image,
    }

    /// <summary>Anzahl der gesendeten Anfragen (auch gescheiterte, auch Bild-Downloads) = API- plus Bildanfragen.</summary>
    public int RequestCount => ApiRequestCount + ImageRequestCount;

    /// <summary>Gesendete Anfragen an die Schnittstelle.</summary>
    public int ApiRequestCount => Volatile.Read(ref _apiRequests);

    /// <summary>Gesendete Bildanfragen (Vorschauen und volle Bilder).</summary>
    public int ImageRequestCount => Volatile.Read(ref _imageRequests);

    /// <summary>True für eine https-Adresse unter steamgriddb.com (nur von dort werden Bilder geladen).</summary>
    public static bool IsAllowedImageUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Host.Equals("steamgriddb.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".steamgriddb.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>„Prüfen“: eine Suche mit dem Schlüssel. True, wenn er angenommen wird; false bei 401/403.</summary>
    public bool CheckKey(CancellationToken cancellationToken)
    {
        try
        {
            using var doc = GetJson("search/autocomplete/" + Uri.EscapeDataString("HDRpilot"), cancellationToken);
            return true;
        }
        catch (SteamGridDbException ex) when (ex.Kind == SteamGridDbErrorKind.Unauthorized)
        {
            return false;
        }
    }

    /// <summary>
    /// ID des ersten Treffers der Suche, dessen Name dem gesuchten entspricht (<see cref="NormalizeName"/>); null ohne
    /// passenden Treffer. Die Suche ergänzt Namen nur: Ein allgemeiner Name oder ein EXE-Name träfe sonst ein anderes Spiel.
    /// </summary>
    public long? SearchGameId(string name, CancellationToken cancellationToken)
    {
        var wanted = NormalizeName(name);
        var term = SearchTerm(name);
        if (wanted.Length == 0 || term.Length == 0)
        {
            return null;
        }

        using var doc = GetJsonOrNotFound("search/autocomplete/" + term, cancellationToken);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("id", out var id) && id.TryGetInt64(out var value) && value > 0
                && item.TryGetProperty("name", out var found) && found.ValueKind == JsonValueKind.String
                && NormalizeName(found.GetString()) == wanted)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Name zum Vergleichen: Kleinbuchstaben, nur Buchstaben und Ziffern (Satzzeichen, Leerzeichen, ™ und ® fallen weg),
    /// Akzente entfernt. „Baldur's Gate 3“ und „Baldurs Gate 3“ sind gleich, „Gate“ und „Baldur's Gate 3“ nicht.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Suchbegriff für ein Pfadsegment: '/', '\', '?', '#', '%', '+' und Steuerzeichen werden zu Leerzeichen, Leerraum
    /// zusammengefasst und getrimmt, höchstens 100 Zeichen an einer Textelement-Grenze (nie ein halbes Ersatzzeichenpaar),
    /// dann genau einmal kodiert. Leer, wenn nichts bleibt.
    /// </summary>
    public static string SearchTerm(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var space = false;
        foreach (var c in name)
        {
            var blank = c is '/' or '\\' or '?' or '#' or '%' or '+' || char.IsControl(c) || char.IsWhiteSpace(c);
            if (blank)
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        var cut = CutAtTextElement(builder.ToString(), MaxSearchTermLength).TrimEnd();
        return cut.Length == 0 ? string.Empty : Uri.EscapeDataString(cut);
    }

    /// <summary>
    /// Fremder Text (Autor, Spielname) für die Anzeige: Steuer- und Richtungszeichen entfernt, getrimmt, höchstens
    /// <paramref name="maxLength"/> Zeichen an einer Textelement-Grenze; null, wenn nichts bleibt.
    /// </summary>
    public static string? CleanText(string? value, int maxLength = MaxNameLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsControl(c) && !IsBidiControl(c))
            {
                builder.Append(c);
            }
        }

        var text = CutAtTextElement(builder.ToString().Trim(), maxLength).TrimEnd();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Erstes (bestbewertetes) Bild einer Art für eine Steam-AppID (<paramref name="steamAppId"/>) oder eine SteamGridDB-ID;
    /// nur unbewegte Bilder ohne NSFW- und Spaß-Markierung, Cover nur 600×900. Null ohne Bild.
    /// </summary>
    public SteamGridDbAsset? FirstAsset(SteamGridDbAssetKind kind, string? steamAppId, long? gameId, CancellationToken cancellationToken)
    {
        var target = steamAppId is { Length: > 0 } appId && appId.All(char.IsAsciiDigit)
            ? "steam/" + appId
            : gameId is > 0 ? "game/" + gameId.Value.ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentException("Steam-AppID oder SteamGridDB-ID nötig.", nameof(gameId));
        var path = kind switch
        {
            SteamGridDbAssetKind.Grid => "grids/" + target + "?dimensions=600x900&types=static&nsfw=false&humor=false",
            SteamGridDbAssetKind.Hero => "heroes/" + target + "?types=static&nsfw=false&humor=false",
            _ => "logos/" + target + "?types=static&nsfw=false&humor=false",
        };
        using var doc = GetJsonOrNotFound(path, cancellationToken);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("url", out var urlElement) || urlElement.GetString() is not { } url
                || !IsAllowedImageUrl(url))
            {
                continue;
            }

            long id = item.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var v) ? v : 0;
            int width = item.TryGetProperty("width", out var w) && w.TryGetInt32(out var wv) ? wv : 0;
            int height = item.TryGetProperty("height", out var h) && h.TryGetInt32(out var hv) ? hv : 0;
            var mime = item.TryGetProperty("mime", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return new SteamGridDbAsset(id, url, width, height, mime);
        }

        return null;
    }

    /// <summary>
    /// „Bild wählen…“: eine Seite (0-basiert, Standardgröße der Schnittstelle) unbewegter Bilder ohne NSFW-, Humor- oder
    /// Epilepsie-Markierung, Cover im Hochformat (600×900, 342×482, 660×930). 404 auf Seite 0 ergibt
    /// <see cref="SteamGridDbAssetPage.Unknown"/>, ab Seite 1 <see cref="SteamGridDbAssetPage.End"/>.
    /// </summary>
    public SteamGridDbAssetPage Assets(SteamGridDbAssetKind kind, SteamGridDbTarget target, int page, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        var number = page.ToString(CultureInfo.InvariantCulture);
        var path = kind switch
        {
            SteamGridDbAssetKind.Grid => "grids/" + target.PathSegment + "?dimensions=600x900,342x482,660x930&types=static&nsfw=false&humor=false&epilepsy=false&page=" + number,
            SteamGridDbAssetKind.Hero => "heroes/" + target.PathSegment + "?types=static&nsfw=false&humor=false&epilepsy=false&page=" + number,
            _ => "logos/" + target.PathSegment + "?types=static&nsfw=false&humor=false&epilepsy=false&page=" + number,
        };
        using var doc = GetJsonOrNotFound(path, cancellationToken);
        if (doc is null)
        {
            return page == 0 ? SteamGridDbAssetPage.Unknown() : SteamGridDbAssetPage.End(page);
        }

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.InvalidResponse, "Antwort ohne Liste.");
        }

        var items = new List<SteamGridDbAsset>();
        var seen = new HashSet<long>();
        var raw = 0;
        var skipped = 0;
        foreach (var item in data.EnumerateArray())
        {
            raw++;
            if (ParseAsset(item) is { } asset && seen.Add(asset.Id))
            {
                items.Add(asset);
            }
            else
            {
                skipped++;
            }
        }

        return new SteamGridDbAssetPage(items, page, raw, skipped, GameUnknown: false);
    }

    /// <summary>Spielwahl: alle gültigen Treffer (höchstens <see cref="MaxHits"/>) in der Reihenfolge der Schnittstelle.</summary>
    public IReadOnlyList<SteamGridDbGameHit> SearchGames(string term, CancellationToken cancellationToken)
    {
        var segment = SearchTerm(term);
        if (segment.Length == 0)
        {
            return [];
        }

        using var doc = GetJsonOrNotFound("search/autocomplete/" + segment, cancellationToken);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<SteamGridDbGameHit>();
        var seen = new HashSet<long>();
        foreach (var item in data.EnumerateArray())
        {
            if (ParseHit(item) is { } hit && seen.Add(hit.Id))
            {
                hits.Add(hit);
                if (hits.Count == MaxHits)
                {
                    break;
                }
            }
        }

        return hits;
    }

    /// <summary>Name und Jahr eines gemerkten Spiels (games/id); null bei 404 oder ohne gültigen Eintrag.</summary>
    public SteamGridDbGameHit? GameById(long id, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        using var doc = GetJsonOrNotFound("games/id/" + id.ToString(CultureInfo.InvariantCulture), cancellationToken);
        return doc is not null && doc.RootElement.TryGetProperty("data", out var data) ? ParseHit(data) : null;
    }

    /// <summary>
    /// Vorschau-Bytes nur aus <see cref="SteamGridDbAsset.ThumbUrl"/> (ohne eine: <see cref="SteamGridDbErrorKind.Blocked"/>
    /// ohne Anfrage); ohne Schlüssel, ohne Abstand, höchstens <see cref="MaxPreviewBytes"/>, im Budget gezählt.
    /// </summary>
    public byte[] DownloadPreview(SteamGridDbAsset asset, CancellationToken cancellationToken)
    {
        if (asset.ThumbUrl is not { } thumb || !IsAllowedImageUrl(thumb))
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Keine Vorschau-Adresse.");
        }

        var data = FetchImage(thumb, MaxPreviewBytes, paced: false, _timeouts.TotalPreview, cancellationToken, out _);
        if (_budget is not null && !_budget.TryAddPreviewBytes(data.Length))
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.RequestLimit, "Höchstmenge an Vorschau-Daten erreicht.");
        }

        return data;
    }

    /// <summary>Lädt ein Bild für den automatischen Lauf (höchstens <see cref="MaxDownloadBytes"/>, im Abstand).</summary>
    public (byte[] Data, string Extension) Download(SteamGridDbAsset asset, CancellationToken cancellationToken) =>
        Download(asset, MaxDownloadBytes, paced: true, cancellationToken);

    /// <summary>
    /// Lädt ein volles Bild (ohne Schlüssel im Kopf) mit ausdrücklicher Größengrenze und liefert die Bytes samt Endung
    /// (.png, .jpg oder .webp). <paramref name="paced"/> ist für „Bild wählen…“ false.
    /// </summary>
    public (byte[] Data, string Extension) Download(SteamGridDbAsset asset, long maxBytes, bool paced, CancellationToken cancellationToken)
    {
        if (!IsAllowedImageUrl(asset.Url))
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Bildadresse nicht erlaubt.");
        }

        var data = FetchImage(asset.Url, maxBytes, paced, _timeouts.TotalPicked, cancellationToken, out var mediaType);
        var mime = mediaType ?? asset.Mime;
        var extension = mime switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" or "image/jpg" => ".jpg",
            _ => Path.GetExtension(new Uri(asset.Url).AbsolutePath).ToLowerInvariant() switch
            {
                ".png" => ".png",
                ".webp" => ".webp",
                ".jpg" or ".jpeg" => ".jpg",
                _ => throw new SteamGridDbException(SteamGridDbErrorKind.InvalidResponse, "Kein Bild: " + mime),
            },
        };
        return (data, extension);
    }

    public void Dispose() => _http.Dispose();

    private static bool IsBidiControl(char c) => c is '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');

    /// <summary>Höchstens <paramref name="max"/> Zeichen, nur an einer Textelement-Grenze geschnitten.</summary>
    private static string CutAtTextElement(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var length = 0;
        while (length < text.Length)
        {
            var next = StringInfo.GetNextTextElementLength(text, length);
            if (length + next > max)
            {
                break;
            }

            length += next;
        }

        return text[..length];
    }

    private static bool IsTrue(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? PathExtension(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() : null;

    private static SteamGridDbAsset? ParseAsset(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id <= 0
            || !item.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String
            || urlElement.GetString() is not { } url || !IsAllowedImageUrl(url)
            || IsTrue(item, "nsfw") || IsTrue(item, "humor") || IsTrue(item, "epilepsy"))
        {
            return null;
        }

        var mime = item.TryGetProperty("mime", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        var known = string.IsNullOrEmpty(mime)
            ? ImageExtensions.Contains(PathExtension(url))
            : ImageMimes.Contains(mime, StringComparer.OrdinalIgnoreCase);
        if (!known)
        {
            return null;
        }

        if (item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String && BlockedTags.Contains(tag.GetString(), StringComparer.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
        }

        string? thumb = null;
        if (item.TryGetProperty("thumb", out var thumbElement) && thumbElement.ValueKind == JsonValueKind.String
            && thumbElement.GetString() is { } thumbUrl && IsAllowedImageUrl(thumbUrl)
            && !AnimatedExtensions.Contains(PathExtension(thumbUrl)))
        {
            thumb = thumbUrl;
        }

        string? author = null;
        if (item.TryGetProperty("author", out var authorElement) && authorElement.ValueKind == JsonValueKind.Object
            && authorElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            author = CleanText(name.GetString());
        }

        int width = item.TryGetProperty("width", out var w) && w.TryGetInt32(out var wv) ? wv : 0;
        int height = item.TryGetProperty("height", out var h) && h.TryGetInt32(out var hv) ? hv : 0;
        var style = item.TryGetProperty("style", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        return new SteamGridDbAsset(id, url, width, height, mime, thumb, author, style);
    }

    private static SteamGridDbGameHit? ParseHit(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id <= 0
            || !item.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String
            || CleanText(nameElement.GetString()) is not { } name)
        {
            return null;
        }

        int? year = null;
        if (item.TryGetProperty("release_date", out var date) && date.ValueKind == JsonValueKind.Number
            && date.TryGetInt64(out var seconds) && seconds > 0 && seconds < 253402300799L)
        {
            var value = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.Year;
            year = value is >= 1950 and <= 2100 ? value : null;
        }

        return new SteamGridDbGameHit(id, name, year, IsTrue(item, "verified"));
    }

    private JsonDocument GetJson(string relative, CancellationToken cancellationToken) =>
        GetJsonOrNotFound(relative, cancellationToken) ?? throw new SteamGridDbException(SteamGridDbErrorKind.HttpError, "Nicht gefunden (HTTP 404).");

    /// <summary>JSON einer Anfrage an die Schnittstelle; null bei 404 (Spiel bei SteamGridDB unbekannt).</summary>
    private JsonDocument? GetJsonOrNotFound(string relative, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + relative);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        HttpResponseMessage response;
        try
        {
            response = Send(request, RequestClass.Api, paced: true, cancellationToken);
        }
        catch (SteamGridDbException ex) when (ex.Kind == SteamGridDbErrorKind.HttpError && ex.Data["status"] is 404)
        {
            return null;
        }

        using (response)
        {
            try
            {
                var bytes = ReadLimited(response, MaxJsonBytes, _timeouts.TotalJson, cancellationToken);
                var doc = JsonDocument.Parse(bytes);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || (doc.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False))
                {
                    doc.Dispose();
                    throw new SteamGridDbException(SteamGridDbErrorKind.InvalidResponse, "Antwort meldet keinen Erfolg.");
                }

                return doc;
            }
            catch (JsonException ex)
            {
                throw new SteamGridDbException(SteamGridDbErrorKind.InvalidResponse, "Antwort ist kein gültiges JSON.", ex);
            }
        }
    }

    /// <summary>Bild ohne Schlüssel; prüft die Adresse der Antwort (nach Weiterleitungen), die Größe und die Zeit.</summary>
    private byte[] FetchImage(string url, long maxBytes, bool paced, TimeSpan total, CancellationToken cancellationToken, out string? mediaType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = Send(request, RequestClass.Image, paced, cancellationToken);

        // Hat ein Handler Weiterleitungen befolgt, zählt die Adresse, von der die Antwort kam.
        if (response.RequestMessage?.RequestUri is { } final && !IsAllowedImageUrl(final.AbsoluteUri))
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Bild über eine Weiterleitung außerhalb von steamgriddb.com.");
        }

        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Bild zu groß.");
        }

        mediaType = response.Content.Headers.ContentType?.MediaType;
        return ReadLimited(response, maxBytes, total, cancellationToken);
    }

    /// <summary>
    /// Nimmt einen Platz im Budget (oder in der Gesamtgrenze) vor dem Zug des Abstands, damit eine erschöpfte Grenze nie
    /// wartet.
    /// </summary>
    private void Reserve(RequestClass kind)
    {
        if (_budget is not null)
        {
            if (!(kind == RequestClass.Api ? _budget.TryTakeApi() : _budget.TryTakeImage()))
            {
                throw new SteamGridDbException(SteamGridDbErrorKind.RequestLimit, "Höchstzahl an Anfragen erreicht.");
            }

            return;
        }

        if (Interlocked.Increment(ref _reserved) > _maxRequests)
        {
            Interlocked.Decrement(ref _reserved);
            throw new SteamGridDbException(SteamGridDbErrorKind.RequestLimit, "Höchstzahl an Anfragen je Lauf erreicht.");
        }
    }

    private HttpResponseMessage Send(HttpRequestMessage request, RequestClass kind, bool paced, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reserve(kind);

        // Der Zug gilt bis zu den Kopfzeilen der Antwort oder bis zum Fehler (wie früher MarkDone).
        using var turn = paced ? _pacer.Enter(cancellationToken) : default;
        if (kind == RequestClass.Api)
        {
            Interlocked.Increment(ref _apiRequests);
        }
        else
        {
            Interlocked.Increment(ref _imageRequests);
        }

        HttpResponseMessage response;
        try
        {
            response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is ObjectDisposedException && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Abgebrochen.", ex, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient.Timeout: keine Kopfzeilen innerhalb der Zeitgrenze.
            throw Timeout("SteamGridDB antwortet nicht (Zeitgrenze).", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException or InvalidOperationException)
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Network, "SteamGridDB nicht erreichbar: " + ex.GetType().Name, ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var code = response.StatusCode;
        var status = (int)code;
        response.Dispose();

        // Nur die Schnittstelle kann den Schlüssel ablehnen: Ein Bildspeicher antwortet auf fehlende Dateien oft mit 403.
        var error = code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden when kind == RequestClass.Api =>
                new SteamGridDbException(SteamGridDbErrorKind.Unauthorized, "Schlüssel abgelehnt (HTTP " + status + ")."),
            HttpStatusCode.TooManyRequests => new SteamGridDbException(SteamGridDbErrorKind.RateLimited, "Zu viele Anfragen (HTTP 429)."),
            _ => new SteamGridDbException(SteamGridDbErrorKind.HttpError, "Antwort mit HTTP-Status " + status + "."),
        };
        error.Data["status"] = status;
        throw error;
    }

    private static SteamGridDbException Timeout(string message, Exception? inner)
    {
        var error = new SteamGridDbException(SteamGridDbErrorKind.Network, message, inner);
        error.Data["timeout"] = true;
        return error;
    }

    /// <summary>
    /// Liest den Inhalt bis <paramref name="limit"/> Bytes. Ein Abbruch des Aufrufers gibt die Antwort sofort frei (auch
    /// eine im Socket hängende Übertragung endet); eine Gesamt- und eine Ruhezeitgrenze beenden langsame Antworten als
    /// <see cref="SteamGridDbErrorKind.Network"/> mit <c>Data["timeout"]</c>.
    /// </summary>
    private byte[] ReadLimited(HttpResponseMessage response, long limit, TimeSpan total, CancellationToken cancellationToken)
    {
        using var abort = cancellationToken.Register(static state => ((HttpResponseMessage)state!).Dispose(), response);
        using var totalTimer = new CancellationTokenSource(total);
        using var idleTimer = new CancellationTokenSource(_timeouts.Idle);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, totalTimer.Token, idleTimer.Token);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = response.Content.ReadAsStream(linked.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                // Synchron über asynchron auf einem Hintergrund-Thread ohne Synchronisationskontext: nur ReadAsync
                // beachtet das Token, das synchrone Read nicht.
                var read = stream.ReadAsync(chunk.AsMemory(), linked.Token).AsTask().GetAwaiter().GetResult();
                if (read == 0)
                {
                    break;
                }

                idleTimer.CancelAfter(_timeouts.Idle);
                if (buffer.Length + read > limit)
                {
                    throw new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Antwort zu groß.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or HttpRequestException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Abgebrochen.", ex, cancellationToken);
            }

            if (totalTimer.IsCancellationRequested || idleTimer.IsCancellationRequested)
            {
                throw Timeout("Übertragung zu langsam (Zeitgrenze).", ex);
            }

            throw new SteamGridDbException(SteamGridDbErrorKind.Network, "Übertragung abgebrochen: " + ex.GetType().Name, ex);
        }
    }
}
