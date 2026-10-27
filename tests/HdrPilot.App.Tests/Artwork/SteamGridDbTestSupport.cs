using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>Aufgezeichnete Anfrage an eine SteamGridDB-Attrappe.</summary>
public sealed record SgdbCall(string Uri, string? Authorization, string? Thread)
{
    public bool IsApi => Uri.StartsWith(SteamGridDbClient.ApiBase, StringComparison.Ordinal);
}

/// <summary>
/// Attrappe für SteamGridDB (nie das Netz): beantwortet Anfragen über <see cref="Respond"/>, zeichnet sie auf, zählt
/// gleichzeitige Bildanfragen und hält auf Wunsch Anfragen fest, bis der Test sie freigibt (oder abbricht).
/// </summary>
public sealed class ScriptedSgdbHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<SgdbCall> _calls = [];
    private readonly object _gate = new();
    private int _imagesNow;

    public IReadOnlyList<SgdbCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Anfragen, auf die dies zutrifft, warten auf <see cref="Release"/> (oder den Abbruch).</summary>
    public Func<HttpRequestMessage, bool>? Hold { get; set; }

    public ManualResetEventSlim Release { get; } = new(false);

    /// <summary>Signal: eine festgehaltene Anfrage wartet.</summary>
    public SemaphoreSlim Held { get; } = new(0);

    public int MaxParallelImages { get; private set; }

    public bool Disposed { get; private set; }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var image = request.RequestUri!.Host != "www.steamgriddb.com";
        lock (_gate)
        {
            _calls.Add(new SgdbCall(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), Thread.CurrentThread.Name));
            if (image)
            {
                _imagesNow++;
                MaxParallelImages = Math.Max(MaxParallelImages, _imagesNow);
            }
        }

        try
        {
            if (Hold?.Invoke(request) == true)
            {
                Held.Release();
                Release.Wait(cancellationToken);
            }

            return respond(request);
        }
        finally
        {
            if (image)
            {
                lock (_gate)
                {
                    _imagesNow--;
                }
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>Bilderdienst als Attrappe für SteamGridDB: was fehlt, was gefragt, gespeichert und neu zugeordnet wurde.</summary>
public sealed class FakeSgdbArtwork : ISteamGridDbArtwork
{
    private int _missingCalls;

    public Dictionary<string, List<ArtworkKind>> Missing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<(string, ArtworkKind), DateTimeOffset> Checked { get; } = [];

    public Dictionary<string, long> GameIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Key, ArtworkKind Kind, string Extension)> Stored { get; } = [];

    public List<(string Key, long Id)> Replaced { get; } = [];

    public DateTimeOffset? SteamGridDbLastRun { get; set; }

    public int Flushes { get; private set; }

    /// <summary>Wird vor dem Speichern eines Bildes aufgerufen (etwa um mitten im Lauf die Zuordnung zu ändern).</summary>
    public Action<GameConfig>? BeforeStore { get; set; }

    /// <summary>Wird vor dem Setzen der ID nach einer Namenssuche aufgerufen (zwischen Prüfen und Setzen eines Laufs).</summary>
    public Action<GameConfig>? BeforeSetGameId { get; set; }

    /// <summary>Aufrufe von <see cref="MissingKinds"/> (Nachweis: der Zustand des Schlüssels zählt nichts).</summary>
    public int MissingCalls => Volatile.Read(ref _missingCalls);

    public IReadOnlyList<ArtworkKind> MissingKinds(GameConfig game)
    {
        Interlocked.Increment(ref _missingCalls);
        return Missing.TryGetValue(game.Key, out var kinds) ? [.. kinds] : [];
    }

    public DateTimeOffset? SteamGridDbChecked(GameConfig game, ArtworkKind kind) => Checked.TryGetValue((game.Key, kind), out var when) ? when : null;

    public void MarkSteamGridDbChecked(GameConfig game, ArtworkKind kind, DateTimeOffset when) => Checked[(game.Key, kind)] = when;

    public long? SteamGridDbGameId(GameConfig game)
    {
        lock (GameIds)
        {
            return GameIds.TryGetValue(game.Key, out var id) ? id : null;
        }
    }

    public void SetSteamGridDbGameId(GameConfig game, long id)
    {
        lock (GameIds)
        {
            GameIds[game.Key] = id;
        }
    }

    public bool TrySetSteamGridDbGameId(GameConfig game, long? expected, long id)
    {
        BeforeSetGameId?.Invoke(game);
        lock (GameIds)
        {
            if ((GameIds.TryGetValue(game.Key, out var current) ? current : (long?)null) != expected)
            {
                return false;
            }

            GameIds[game.Key] = id;
            return true;
        }
    }

    public void ReplaceSteamGridDbGame(GameConfig game, long id)
    {
        Replaced.Add((game.Key, id));
        SetSteamGridDbGameId(game, id);
        foreach (var key in Checked.Keys.Where(k => k.Item1 == game.Key).ToList())
        {
            Checked.Remove(key);
        }

        Stored.RemoveAll(s => s.Key == game.Key);
    }

    public void StoreSteamGridDbImage(GameConfig game, ArtworkKind kind, byte[] data, string extension, DateTimeOffset when) =>
        TryStoreSteamGridDbImage(game, kind, data, extension, when, expectedGameId: null);

    /// <summary>Prüft die ID wie der Bilderdienst unter seiner Sperre: nach <see cref="BeforeStore"/>, zusammen mit dem Eintragen.</summary>
    public bool TryStoreSteamGridDbImage(GameConfig game, ArtworkKind kind, byte[] data, string extension, DateTimeOffset when, long? expectedGameId)
    {
        BeforeStore?.Invoke(game);
        lock (GameIds)
        {
            if (expectedGameId is not null && (GameIds.TryGetValue(game.Key, out var current) ? current : (long?)null) != expectedGameId)
            {
                return false;
            }
        }

        Stored.Add((game.Key, kind, extension));
        if (Missing.TryGetValue(game.Key, out var kinds))
        {
            kinds.Remove(kind);
        }

        Checked[(game.Key, kind)] = when;
        return true;
    }

    public void FlushIndex() => Flushes++;
}

/// <summary>Umkehrbar „verschlüsselt“ (Tests ohne DPAPI-Abhängigkeit vom Benutzer).</summary>
public sealed class XorSecretProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x33)).ToArray();

    public byte[] Unprotect(byte[] data) => Protect(data);
}

/// <summary>
/// Aufbau für Tests des SteamGridDB-Dienstes mit Suche (E93): Konfiguration, Attrappen, Uhr, Protokoll, Testprotokoll und
/// erzeugte Antworten (erfundene Namen, erzeugtes 1×1-PNG).
/// </summary>
public sealed class SgdbRig
{
    public const string Key = "0123456789abcdef0123456789abcdef";

    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public AppConfig Config { get; } = new();

    public FakeSgdbArtwork Artwork { get; } = new();

    public InMemoryFileSystem FileSystem { get; } = new();

    public FakeClock Clock { get; } = new();

    public ListLogger<SgdbRig> Log { get; } = new();

    public List<ScriptedSgdbHandler> Handlers { get; } = [];

    public List<string> TestLog { get; } = [];

    /// <summary>Uhrzeit (der Attrappe) jeder Anfrage an die Schnittstelle.</summary>
    public List<DateTimeOffset> ApiTimes { get; } = [];

    public bool GameRunning { get; set; }

    /// <summary>Einträge je Seite (Seite → Anzahl); fehlende Seiten sind leer.</summary>
    public Dictionary<int, int> PageSizes { get; } = new() { [0] = 30 };

    /// <summary>Treffer der Namenssuche als JSON-Liste; null: ein Treffer mit genau dem gesuchten Namen (9101) und 9102.</summary>
    public string? SearchHits { get; set; }

    /// <summary>Statt der Antwort: dieser Status für passende Anfragen.</summary>
    public Func<HttpRequestMessage, HttpStatusCode?> Status { get; set; } = _ => null;

    public Func<HttpRequestMessage, bool>? Hold { get; set; }

    /// <summary>Eigene Antwort für passende Anfragen (null: die übliche).</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

    public ScriptedSgdbHandler? LastHandler
    {
        get
        {
            lock (Handlers)
            {
                return Handlers.LastOrDefault();
            }
        }
    }

    public IReadOnlyList<SgdbCall> Calls
    {
        get
        {
            lock (Handlers)
            {
                return [.. Handlers.SelectMany(h => h.Calls)];
            }
        }
    }

    public static GameConfig Steam(string id, string name = "Kupferwald") => new() { Source = GameSource.Steam, SourceId = id, Name = name };

    public static GameConfig Manual(string name = "Nebelfahrt") => new() { Source = GameSource.Gog, SourceId = "1207658930", Name = name };

    public static string Entry(int i, bool thumb = true, string? author = null) =>
        "{\"id\":" + i + ",\"url\":\"https://cdn2.steamgriddb.com/grid/" + i + ".png\""
        + (thumb ? ",\"thumb\":\"https://cdn2.steamgriddb.com/thumb/" + i + ".png\"" : string.Empty)
        + ",\"width\":600,\"height\":900,\"mime\":\"image/png\",\"author\":{\"name\":\"" + (author ?? "Testautor " + i) + "\",\"steam64\":\"76561190000000009\"}}";

    public SteamGridDbService Service(SteamGridDbSearchLimits? limits = null, TimeSpan? pause = null) => new(new SteamGridDbServiceOptions
    {
        Artwork = Artwork,
        Current = () => Config,
        Update = mutate => mutate(Config),
        FileSystem = FileSystem,
        KeyFile = @"C:\Data\steamgriddb-key.dat",
        Protector = new XorSecretProtector(),
        Clock = Clock,
        GameRunning = () => GameRunning,
        HandlerFactory = () =>
        {
            var handler = new ScriptedSgdbHandler(Respond) { Hold = Hold };
            lock (Handlers)
            {
                Handlers.Add(handler);
            }

            return handler;
        },
        StartupDelay = TimeSpan.FromMilliseconds(10),
        Pause = pause ?? TimeSpan.Zero,
        Logger = Log,
        Search = limits ?? SteamGridDbSearchLimits.Default with { PreviewIdleExit = TimeSpan.FromMilliseconds(200) },
        TestLog = line =>
        {
            lock (TestLog)
            {
                TestLog.Add(line);
            }
        },
    });

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        if (Override?.Invoke(request) is { } custom)
        {
            return custom;
        }

        if (Status(request) is { } status)
        {
            return new HttpResponseMessage(status) { Content = new StringContent("{\"success\":false}") };
        }

        var uri = request.RequestUri!;
        if (uri.Host != "www.steamgriddb.com")
        {
            var image = new ByteArrayContent(Png);
            image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = image };
        }

        lock (ApiTimes)
        {
            ApiTimes.Add(Clock.UtcNow);
        }

        var path = uri.AbsolutePath;
        string json;
        if (path.Contains("/search/", StringComparison.Ordinal))
        {
            var term = JsonSerializer.Serialize(Uri.UnescapeDataString(path.Split('/')[^1]));
            var hits = SearchHits ?? "[{\"id\":9101,\"name\":" + term + ",\"release_date\":1561939200,\"verified\":true},{\"id\":9102,\"name\":\"Anderes Spiel\"}]";
            json = "{\"success\":true,\"data\":" + hits + "}";
        }
        else if (path.Contains("/games/id/", StringComparison.Ordinal))
        {
            json = "{\"success\":true,\"data\":{\"id\":" + path.Split('/')[^1] + ",\"name\":\"Kupferwald (SteamGridDB)\",\"release_date\":1546300800}}";
        }
        else
        {
            var page = 0;
            var query = uri.Query;
            var at = query.IndexOf("page=", StringComparison.Ordinal);
            if (at >= 0)
            {
                page = int.Parse(query[(at + 5)..], System.Globalization.CultureInfo.InvariantCulture);
            }

            var count = PageSizes.TryGetValue(page, out var size) ? size : 0;
            var offset = PageSizes.Where(p => p.Key < page).Sum(p => p.Value);
            json = "{\"success\":true,\"data\":[" + string.Join(",", Enumerable.Range(offset + 1, count).Select(i => Entry(i))) + "]}";
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    public static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs && !condition(); waited += 20)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}

/// <summary>Antwortinhalt, der hängt und weder Token noch Zeit beachtet (wie ein hängender Socket): nur Dispose beendet ihn.</summary>
public sealed class HangingStream : Stream
{
    private readonly ManualResetEventSlim _disposed = new(false);

    public ManualResetEventSlim Reading { get; } = new(false);

    public bool IsDisposed => _disposed.IsSet;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Reading.Set();
        _disposed.Wait();
        throw new ObjectDisposedException(nameof(HangingStream));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Reading.Set();
        await Task.Run(() => _disposed.Wait(), CancellationToken.None);
        throw new ObjectDisposedException(nameof(HangingStream));
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

/// <summary>Erzeugte PNG-Dateien für Tests (graue Fläche beliebiger Größe, klein durch Kompression).</summary>
public static class TestPng
{
    /// <summary>Graues PNG (8 Bit); nur der Kopf, wenn gewünscht (für Größenprüfungen ohne Dekodieren).</summary>
    public static byte[] Gray(int width, int height, bool headerOnly = false)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // Bittiefe
        header[9] = 0; // Graustufen
        Chunk(output, "IHDR", header);
        if (!headerOnly)
        {
            using var raw = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(raw, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[width + 1];
                Array.Fill(row, (byte)128, 1, width);
                for (var y = 0; y < height; y++)
                {
                    zlib.Write(row);
                }
            }

            Chunk(output, "IDAT", raw.ToArray());
        }

        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var number = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(typeBytes);
        output.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in typeBytes.Concat(data))
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }
}
