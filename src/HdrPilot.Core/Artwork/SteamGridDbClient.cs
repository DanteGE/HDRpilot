using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.Core.Artwork;

/// <summary>Art eines Fehlers beim Abruf von SteamGridDB.</summary>
public enum SteamGridDbErrorKind
{
    /// <summary>Keine Verbindung, Zeitüberschreitung, abgebrochene Übertragung.</summary>
    Network,

    /// <summary>Schlüssel abgelehnt (HTTP 401 oder 403).</summary>
    Unauthorized,

    /// <summary>Zu viele Anfragen (HTTP 429).</summary>
    RateLimited,

    /// <summary>Anderer HTTP-Status (etwa 5xx).</summary>
    HttpError,

    /// <summary>Antwort nicht lesbar.</summary>
    InvalidResponse,

    /// <summary>Bildadresse außerhalb von steamgriddb.com, ohne https oder Bild zu groß: nicht geladen.</summary>
    Blocked,

    /// <summary>Höchstzahl an Anfragen je Lauf erreicht.</summary>
    RequestLimit,
}

public sealed class SteamGridDbException(SteamGridDbErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public SteamGridDbErrorKind Kind { get; } = kind;
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

/// <summary>Ein Bild aus einer Antwort von SteamGridDB (das erste ist das am besten bewertete).</summary>
public sealed record SteamGridDbAsset(long Id, string Url, int Width, int Height, string? Mime);

/// <summary>
/// Zugriff auf die Schnittstelle von SteamGridDB (https://www.steamgriddb.com/api/v2/, E89), synchron für einen eigenen
/// Hintergrund-Thread wie <see cref="PcgwClient"/>. Der Schlüssel des Nutzers geht nur als <c>Authorization: Bearer</c> an
/// www.steamgriddb.com, nie an andere Rechner und nie ins Protokoll. Bilder werden nur von https-Adressen unter
/// steamgriddb.com geladen, höchstens <see cref="MaxDownloadBytes"/> groß. Zwischen zwei Anfragen liegt mindestens
/// <see cref="PauseBetweenRequests"/>, je Lauf sind höchstens <see cref="MaxRequests"/> Anfragen erlaubt. Fehler kommen als
/// <see cref="SteamGridDbException"/>; es gibt keine Wiederholungen.
/// </summary>
public sealed class SteamGridDbClient : IDisposable
{
    public const string ApiBase = "https://www.steamgriddb.com/api/v2/";

    public const int MaxRequests = 200;

    public const long MaxDownloadBytes = 15L * 1024 * 1024;

    public static readonly TimeSpan PauseBetweenRequests = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private const long MaxJsonBytes = 2L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;
    private readonly string _key;
    private readonly int _maxRequests;

    /// <param name="handler">HTTP-Handler (in Tests eine Attrappe); wird mit dem Client freigegeben.</param>
    /// <param name="apiKey">API-Schlüssel des Nutzers.</param>
    public SteamGridDbClient(HttpMessageHandler handler, IClock clock, string apiKey, string userAgent, int maxRequests = MaxRequests, RequestPacer? pacer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _key = apiKey.Trim();
        _maxRequests = maxRequests;
        _pacer = pacer ?? new RequestPacer(clock, PauseBetweenRequests);
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = MaxDownloadBytes,
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
    }

    /// <summary>Anzahl der gesendeten Anfragen (auch gescheiterte, auch Bild-Downloads).</summary>
    public int RequestCount { get; private set; }

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

    /// <summary>ID des ersten Treffers der Suche nach einem Namen; null ohne Treffer.</summary>
    public long? SearchGameId(string name, CancellationToken cancellationToken)
    {
        using var doc = GetJsonOrNotFound("search/autocomplete/" + Uri.EscapeDataString(name.Trim()), cancellationToken);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.TryGetInt64(out var value) && value > 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Erstes (bestbewertetes) Bild einer Art für eine Steam-AppID (<paramref name="steamAppId"/>) oder eine SteamGridDB-ID;
    /// nur unbewegte Bilder ohne NSFW- und Spaß-Markierung, Cover nur 600×900. Null ohne Bild.
    /// </summary>
    public SteamGridDbAsset? FirstAsset(SteamGridDbAssetKind kind, string? steamAppId, long? gameId, CancellationToken cancellationToken)
    {
        var target = steamAppId is { Length: > 0 } appId && appId.All(char.IsAsciiDigit)
            ? "steam/" + appId
            : gameId is > 0 ? "game/" + gameId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
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

    /// <summary>Lädt ein Bild (ohne Schlüssel im Kopf) und liefert die Bytes samt Endung (.png, .jpg oder .webp).</summary>
    public (byte[] Data, string Extension) Download(SteamGridDbAsset asset, CancellationToken cancellationToken)
    {
        if (!IsAllowedImageUrl(asset.Url))
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Bildadresse nicht erlaubt.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        using var response = Send(request, cancellationToken);
        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Bild zu groß.");
        }

        var data = ReadLimited(response, MaxDownloadBytes, cancellationToken);
        var mime = response.Content.Headers.ContentType?.MediaType ?? asset.Mime;
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
            response = Send(request, cancellationToken);
        }
        catch (SteamGridDbException ex) when (ex.Kind == SteamGridDbErrorKind.HttpError && ex.Data["status"] is 404)
        {
            return null;
        }

        using (response)
        {
            try
            {
                var bytes = ReadLimited(response, MaxJsonBytes, cancellationToken);
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

    private HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (RequestCount >= _maxRequests)
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.RequestLimit, "Höchstzahl an Anfragen je Lauf erreicht.");
        }

        _pacer.WaitTurn(cancellationToken);
        RequestCount++;
        HttpResponseMessage response;
        try
        {
            response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or NotSupportedException or InvalidOperationException)
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Network, "SteamGridDB nicht erreichbar: " + ex.GetType().Name, ex);
        }
        finally
        {
            _pacer.MarkDone();
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var code = response.StatusCode;
        var status = (int)code;
        response.Dispose();
        var error = code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new SteamGridDbException(SteamGridDbErrorKind.Unauthorized, "Schlüssel abgelehnt (HTTP " + status + ")."),
            HttpStatusCode.TooManyRequests => new SteamGridDbException(SteamGridDbErrorKind.RateLimited, "Zu viele Anfragen (HTTP 429)."),
            _ => new SteamGridDbException(SteamGridDbErrorKind.HttpError, "Antwort mit HTTP-Status " + status + "."),
        };
        error.Data["status"] = status;
        throw error;
    }

    private static byte[] ReadLimited(HttpResponseMessage response, long limit, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = response.Content.ReadAsStream(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (buffer.Length + read > limit)
                {
                    throw new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Antwort zu groß.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new SteamGridDbException(SteamGridDbErrorKind.Network, "Übertragung abgebrochen: " + ex.GetType().Name, ex);
        }
    }
}
