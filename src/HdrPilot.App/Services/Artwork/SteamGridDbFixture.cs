using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.HdrSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Testdaten-Lieferant für SteamGridDB (E93): erzeugte Antworten statt des Netzes, für Prüfläufe
/// (<c>HDRPILOT_TEST_STEAMGRIDDB=fixture</c>, nur mit --config-dir) und Bildschirmfotos. Nie im normalen Betrieb. Ersetzt nur
/// die Übertragung: Rechnerprüfung, Größengrenzen, Zeitgrenzen, Abstand und Schlüsselweg laufen wie sonst. Bilder sind
/// erzeugte Verläufe und Bänder (keine Spielbilder), Autoren erfunden („Testautor 01“ bis „Testautor 72“).
/// </summary>
internal static class SteamGridDbFixture
{
    /// <summary>Wert der Umgebungsvariablen, der den Lieferanten einschaltet.</summary>
    public const string Enabled = "fixture";

    /// <summary>„Prüfen“ meldet ungültig: jede Anfrage an die Schnittstelle bekommt 401.</summary>
    public const string RejectedKey = "HDRPILOT-FIXTURE-ABGELEHNT";

    /// <summary>„Prüfen“ gelingt (search/autocomplete/HDRpilot), jede andere Anfrage an die Schnittstelle bekommt 401.</summary>
    public const string LaterRejectedKey = "HDRPILOT-FIXTURE-SPAETER-ABGELEHNT";

    /// <summary>Schlüssel der Bildschirmfotos (erfunden, nie gespeichert).</summary>
    public const string SnapshotKey = "snapshot-fixture-key";

    /// <summary>Erfundene Steam-AppID, die SteamGridDB „nicht kennt“ (404).</summary>
    public const string UnknownSteamAppId = "9990404";

    private static readonly ConcurrentDictionary<(string Kind, bool Thumb, int Index), byte[]> Images = new();

    /// <summary>
    /// Handler-Fabrik für den Dienst: nur, wenn <paramref name="configDir"/> gesetzt ist (--config-dir) und die Variable
    /// genau „fixture“ lautet; sonst null (dann der echte Handler). <paramref name="logFile"/>: Anfrageprotokoll oder null.
    /// </summary>
    public static Func<HttpMessageHandler>? HandlerFactoryFor(string? configDir, Func<string, string?> environment, string? logFile)
    {
        if (string.IsNullOrWhiteSpace(configDir) || !string.Equals(environment(AppServices.TestSteamGridDbVariable), Enabled, StringComparison.Ordinal))
        {
            return null;
        }

        return () => new SteamGridDbFixtureHandler(logFile);
    }

    /// <summary>Größe der Vorschau (CDN) und des vollen Bildes (CDN und width/height im JSON) je Bildart.</summary>
    public static (int Width, int Height) Size(string kind, bool thumb) => (kind, thumb) switch
    {
        ("cover", true) => (200, 300),
        ("cover", false) => (600, 900),
        ("hero", true) => (400, 129),
        ("hero", false) => (1920, 620),
        ("logo", true) => (300, 116),
        _ => (800, 310),
    };

    /// <summary>Erzeugtes PNG (gemerkt je Art, Größe und Nummer); Logos sind außerhalb eines abgerundeten Bandes durchsichtig.</summary>
    public static byte[] Image(string kind, bool thumb, int index) => Images.GetOrAdd((kind, thumb, index), key =>
    {
        var (width, height) = Size(key.Kind, key.Thumb);
        return FixturePng.Create(width, height, key.Index, transparentFrame: key.Kind == "logo");
    });
}

/// <summary>
/// HttpMessageHandler des Testdaten-Lieferanten: beantwortet jede Anfrage selbst, hat keinen inneren Handler und öffnet nie
/// einen Socket. Unbekannte Pfade und Rechner bekommen 404, Weiterleitungen gibt es nicht. Überschreibt auch das synchrone
/// Send, das der Client nutzt (die Basisklasse würfe NotSupportedException, die der Client als „offline“ läse).
/// </summary>
internal sealed class SteamGridDbFixtureHandler(string? logFile, ManualResetEventSlim? holdFullDownloads = null) : HttpMessageHandler
{
    private const string ApiHost = "www.steamgriddb.com";
    private const string CdnHost = "cdn2.steamgriddb.com";
    private const string ApiPrefix = "/api/v2/";

    private static readonly ConcurrentDictionary<string, object> LogLocks = new(StringComparer.OrdinalIgnoreCase);

    private string _lastTerm = "Testspiel";

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri ?? throw new InvalidOperationException("Anfrage ohne Adresse.");
        var key = request.Headers.Authorization is { Scheme: "Bearer", Parameter: { Length: > 0 } value } ? value : null;
        Log("GET " + uri.Host + " " + uri.PathAndQuery + " schluessel=" + (key is null ? "nein" : "ja"));
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort)
        {
            return Status(HttpStatusCode.NotFound);
        }

        if (uri.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith(ApiPrefix, StringComparison.Ordinal))
        {
            return Api(uri, key);
        }

        return uri.Host.Equals(CdnHost, StringComparison.OrdinalIgnoreCase)
            ? Cdn(uri.AbsolutePath, cancellationToken)
            : Status(HttpStatusCode.NotFound);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(Send(request, cancellationToken));
        }
        catch (OperationCanceledException ex)
        {
            return Task.FromCanceled<HttpResponseMessage>(ex.CancellationToken.IsCancellationRequested ? ex.CancellationToken : cancellationToken);
        }
        catch (Exception ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("{\"success\":false}", Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Json(JsonNode node) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static JsonObject Data(JsonNode? data) => new() { ["success"] = true, ["data"] = data };

    private static JsonObject Hit(long id, string name, int? year, bool verified)
    {
        var hit = new JsonObject { ["id"] = id, ["name"] = name, ["types"] = new JsonArray("steam"), ["verified"] = verified };
        if (year is { } y)
        {
            hit["release_date"] = new DateTimeOffset(y, 6, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        }

        return hit;
    }

    /// <summary>Seite 0 mit 50 Einträgen (7: fremder Rechner, 13: http, also 48 gültige), Seite 1 mit 22, danach leer.</summary>
    private static JsonObject Page(string kind, int page)
    {
        var (first, count) = page switch
        {
            0 => (1, 50),
            1 => (51, 22),
            _ => (0, 0),
        };
        var (width, height) = SteamGridDbFixture.Size(kind, thumb: false);
        var kindBase = kind switch
        {
            "hero" => 100000,
            "logo" => 200000,
            _ => 300000,
        };
        var data = new JsonArray();
        for (var n = first; n < first + count; n++)
        {
            var number = n.ToString(CultureInfo.InvariantCulture);
            var url = n switch
            {
                7 => "https://media.example.org/fixture-7.png",
                13 => "http://" + CdnHost + "/" + kind + "/fixture-13.png",
                _ => "https://" + CdnHost + "/" + kind + "/fixture-" + number + ".png",
            };
            var entry = new JsonObject
            {
                ["id"] = kindBase + n,
                ["score"] = 100 - n,
                ["style"] = "alternate",
                ["width"] = width,
                ["height"] = height,
                ["nsfw"] = false,
                ["humor"] = false,
                ["epilepsy"] = false,
                ["mime"] = "image/png",
                ["language"] = "en",
                ["url"] = url,
                ["tags"] = new JsonArray(),
                ["upvotes"] = 0,
                ["downvotes"] = 0,
            };
            if (n % 10 != 0)
            {
                entry["thumb"] = "https://" + CdnHost + "/thumb/fixture-" + kind + "-" + number + ".png";
            }

            if (n != 5)
            {
                entry["author"] = new JsonObject
                {
                    ["name"] = n == 3 ? "Testautor 03 mit einem besonders langen Namen xy" : "Testautor " + n.ToString("00", CultureInfo.InvariantCulture),
                };
            }

            data.Add(entry);
        }

        var result = Data(data);
        result["page"] = page;
        result["total"] = 72;
        result["limit"] = 50;
        return result;
    }

    private HttpResponseMessage Api(Uri uri, string? key)
    {
        var relative = uri.AbsolutePath[ApiPrefix.Length..];
        if (key is null || key == SteamGridDbFixture.RejectedKey
            || (key == SteamGridDbFixture.LaterRejectedKey && relative != "search/autocomplete/HDRpilot"))
        {
            return Status(HttpStatusCode.Unauthorized);
        }

        var parts = relative.Split('/');
        if (parts is ["search", "autocomplete", var escaped])
        {
            return Search(Uri.UnescapeDataString(escaped));
        }

        if (parts is ["games", "id", var idText] && long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return id switch
            {
                9101 => Json(Data(Hit(9101, _lastTerm, 2019, true))),
                9102 => Json(Data(Hit(9102, _lastTerm + ": Neuauflage", 2023, false))),
                9103 => Json(Data(Hit(9103, _lastTerm + " II", null, false))),
                9104 => Json(Data(Hit(9104, _lastTerm, 2005, false))),
                _ => Status(HttpStatusCode.NotFound),
            };
        }

        if (parts is [var endpoint, var targetKind, var target] && endpoint is "grids" or "heroes" or "logos")
        {
            var kind = endpoint switch
            {
                "heroes" => "hero",
                "logos" => "logo",
                _ => "cover",
            };
            var page = 0;
            foreach (var pair in uri.Query.TrimStart('?').Split('&'))
            {
                if (pair.StartsWith("page=", StringComparison.Ordinal))
                {
                    _ = int.TryParse(pair[5..], NumberStyles.None, CultureInfo.InvariantCulture, out page);
                }
            }

            return (targetKind, target) switch
            {
                ("steam", SteamGridDbFixture.UnknownSteamAppId) => Status(HttpStatusCode.NotFound),
                ("steam", _) when SteamGridDbTarget.IsSteamAppId(target) => Json(Page(kind, page)),
                ("game", "9101" or "9102" or "9104") => Json(Page(kind, page)),
                ("game", "9103") => Json(Data(new JsonArray())),
                _ => Status(HttpStatusCode.NotFound),
            };
        }

        return Status(HttpStatusCode.NotFound);
    }

    /// <summary>Treffer 9101 „&lt;t&gt;“, 9102 „&lt;t&gt;: Neuauflage“, 9103 „&lt;t&gt; II“; Merkwörter steuern Sonderfälle.</summary>
    private HttpResponseMessage Search(string term)
    {
        if (term.Contains("Offline", StringComparison.Ordinal))
        {
            throw new HttpRequestException("Testdaten: keine Verbindung.");
        }

        if (term.Contains("Zuviel", StringComparison.Ordinal))
        {
            return Status(HttpStatusCode.TooManyRequests);
        }

        if (term.Contains("Langsam", StringComparison.Ordinal))
        {
            var stalled = new StreamContent(new StalledStream());
            stalled.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = stalled };
        }

        if (term.Contains("OhneTreffer", StringComparison.Ordinal))
        {
            return Json(Data(new JsonArray()));
        }

        if (term != "HDRpilot")
        {
            _lastTerm = term;
        }

        var hits = new JsonArray(Hit(9101, term, 2019, true), Hit(9102, term + ": Neuauflage", 2023, false), Hit(9103, term + " II", null, false));
        if (term.Contains("Doppelt", StringComparison.Ordinal))
        {
            hits.Add(Hit(9104, term, 2005, false));
        }

        return Json(Data(hits));
    }

    /// <summary>/thumb/fixture-&lt;art&gt;-&lt;n&gt;.png (Vorschau) und /&lt;art&gt;/fixture-&lt;n&gt;.png (volles Bild).</summary>
    private HttpResponseMessage Cdn(string path, CancellationToken cancellationToken)
    {
        var parts = path.TrimStart('/').Split('/');
        string kind;
        bool thumb;
        string name;
        switch (parts)
        {
            case ["thumb", var file] when file.StartsWith("fixture-", StringComparison.Ordinal):
                var dash = file.IndexOf('-', "fixture-".Length);
                if (dash < 0)
                {
                    return Status(HttpStatusCode.NotFound);
                }

                kind = file["fixture-".Length..dash];
                name = file[(dash + 1)..];
                thumb = true;
                break;
            case [var folder, var file] when file.StartsWith("fixture-", StringComparison.Ordinal):
                kind = folder;
                name = file["fixture-".Length..];
                thumb = false;
                break;
            default:
                return Status(HttpStatusCode.NotFound);
        }

        if (kind is not ("cover" or "hero" or "logo") || !name.EndsWith(".png", StringComparison.Ordinal)
            || !int.TryParse(name[..^4], NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index is < 1 or > 72)
        {
            return Status(HttpStatusCode.NotFound);
        }

        if (!thumb && holdFullDownloads is { } hold)
        {
            // Bildschirmfoto „Übernehmen“: Das volle Bild bleibt in der Leitung, bis die Szene es freigibt.
            hold.Wait(cancellationToken);
        }

        var content = new ByteArrayContent(SteamGridDbFixture.Image(kind, thumb, index));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>Eine Zeile je Anfrage; nie der Schlüssel. Ein Schreibfehler scheitert nie die Anfrage.</summary>
    private void Log(string line)
    {
        if (logFile is null)
        {
            return;
        }

        lock (LogLocks.GetOrAdd(logFile, _ => new object()))
        {
            try
            {
                using var stream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                stream.Write(bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Das Protokoll ist nur ein Nachweis.
            }
        }
    }

    /// <summary>Inhalt, der nach den Kopfzeilen hängt: ReadAsync endet erst beim Abbruch, Read erst nach Dispose.</summary>
    private sealed class StalledStream : Stream
    {
        private readonly ManualResetEventSlim _disposed = new(false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _disposed.Wait();
            throw new ObjectDisposedException(nameof(StalledStream));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _disposed.Set();
            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// Kleiner PNG-Schreiber ohne WPF (RGBA, 8 Bit, ZLibStream, CRC32 von Hand: System.IO.Hashing wäre ein NuGet-Paket), damit
/// jeder Thread Testbilder erzeugen kann: Verlauf mit einem Farbton je Nummer und hellen Bändern.
/// </summary>
internal static class FixturePng
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Create(int width, int height, int index, bool transparentFrame)
    {
        var hue = (index * 47) % 360;
        var raw = new byte[(width * 4 + 1) * height];
        var band = Math.Max(1, height / 3);
        var radius = band / 2.0;
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            raw[row] = 0; // Filter: keiner
            for (var x = 0; x < width; x++)
            {
                var light = 0.35 + (0.4 * x / Math.Max(1, width - 1)) + (((y / Math.Max(1, height / 6)) % 2 == 0) ? 0.1 : 0);
                var (r, g, b) = FromHue(hue, Math.Min(0.95, light));
                byte alpha = 255;
                if (transparentFrame)
                {
                    // Deckend nur in einem waagerechten Band mit runden Enden, sonst durchsichtig.
                    var top = (height - band) / 2.0;
                    var inside = y >= top && y < top + band && x >= width * 0.1 && x < width * 0.9;
                    if (inside)
                    {
                        var dx = x < width * 0.1 + radius ? width * 0.1 + radius - x : x > width * 0.9 - radius ? x - (width * 0.9 - radius) : 0;
                        var dy = y - (top + radius);
                        inside = dx == 0 || (dx * dx) + (dy * dy) <= radius * radius;
                    }

                    alpha = inside ? (byte)255 : (byte)0;
                }

                var o = row + 1 + (x * 4);
                raw[o] = r;
                raw[o + 1] = g;
                raw[o + 2] = b;
                raw[o + 3] = alpha;
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // Bittiefe
        header[9] = 6; // RGBA
        Chunk(output, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            Chunk(output, "IDAT", compressed.ToArray());
        }

        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>CRC32 (PNG, Polynom 0xEDB88320).</summary>
    public static uint Crc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in first)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in second)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        var typeBytes = Encoding.ASCII.GetBytes(type);
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(typeBytes);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeBytes, data));
        output.Write(number);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static (byte R, byte G, byte B) FromHue(int hue, double light)
    {
        var c = (1 - Math.Abs((2 * light) - 1)) * 0.6;
        var h = hue / 60.0;
        var x = c * (1 - Math.Abs((h % 2) - 1));
        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        var m = light - (c / 2);
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

/// <summary>
/// SteamGridDB für Bildschirmfotos (E93): Zustand des Schlüssels einstellbar, Suchen laufen als echte Sitzung über den
/// Testdaten-Lieferanten mit einem erfundenen Schlüssel. Kein Protokoll, keine Schlüsseldatei, kein automatisches Laden.
/// </summary>
internal sealed class SteamGridDbFixtureService(SteamGridDbKeyState keyState = SteamGridDbKeyState.Stored, string key = SteamGridDbFixture.SnapshotKey,
    IClock? clock = null, SteamGridDbSearchLimits? limits = null) : ISteamGridDbService
{
    private readonly ManualResetEventSlim _hold = new(true);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IClock _clock = clock ?? SystemClock.Instance;
    private readonly RequestPacer _pacer = new(clock ?? SystemClock.Instance, SteamGridDbClient.PauseBetweenRequests);

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public SteamGridDbStatus Status => SteamGridDbStatus.Off with { Key = keyState };

    public SteamGridDbKeyState KeyState => keyState;

    /// <summary>Hält volle Bilder in der Leitung (Bildschirmfoto „Übernehmen“), bis <see cref="ReleaseFullDownloads"/>.</summary>
    public bool HoldFullDownloads
    {
        get => !_hold.IsSet;
        set
        {
            if (value)
            {
                _hold.Reset();
            }
            else
            {
                _hold.Set();
            }
        }
    }

    public void ReleaseFullDownloads() => _hold.Set();

    public ISteamGridDbSearchSession? OpenSearch(GameConfig game)
    {
        if (keyState != SteamGridDbKeyState.Stored || _lifetime.IsCancellationRequested)
        {
            return null;
        }

        var appId = game.Source == HdrPilot.Core.Games.GameSource.Steam && SteamGridDbTarget.IsSteamAppId(game.SourceId) ? game.SourceId : null;
        var search = limits ?? SteamGridDbSearchLimits.Default;
        return new SteamGridDbSearchSession(new SteamGridDbSearchSetup
        {
            GameName = game.Name,
            SteamAppId = appId,
            CreateClient = budget => new SteamGridDbClient(new SteamGridDbFixtureHandler(null, _hold), _clock, key, SteamGridDbService.UserAgent, pacer: _pacer, budget: budget, timeouts: search.Timeouts),
            Limits = search,
            Clock = _clock,
            Logger = NullLogger.Instance,
            Lifetime = _lifetime.Token,
        });
    }

    public void SetEnabled(bool enabled)
    {
    }

    public Task<SteamGridDbCheckResult> CheckKeyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(SteamGridDbCheckResult.NetworkError);

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
        _hold.Set();
        _lifetime.Cancel();
    }
}
