using System.Net;
using System.Security.Cryptography;
using System.Text;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// SteamGridDB (E89, G10.4) nur mit Attrappe, nie mit dem echten Netz: Schlüssel nur an die Schnittstelle, Bilder nur von
/// steamgriddb.com, Größen- und Anfragegrenze, Abstand zwischen Anfragen, Schlüssel verschlüsselt gespeichert.
/// </summary>
public sealed class SteamGridDbClientTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Key_goes_only_to_the_api_and_images_load_without_it()
    {
        var handler = new SgdbHandler(request => request.RequestUri!.Host == "www.steamgriddb.com"
            ? SgdbHandler.Json("{\"success\":true,\"data\":[{\"id\":7,\"url\":\"https://cdn2.steamgriddb.com/grid/abc.png\",\"width\":600,\"height\":900,\"mime\":\"image/png\"}]}")
            : SgdbHandler.Bytes(new byte[] { 1, 2, 3 }, "image/png"));
        using var client = NewClient(handler);

        var asset = client.FirstAsset(SteamGridDbAssetKind.Grid, "553850", null, CancellationToken.None);
        var (data, extension) = client.Download(asset!, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, data);
        Assert.Equal(".png", extension);
        var api = handler.Requests[0];
        Assert.Equal("https://www.steamgriddb.com/api/v2/grids/steam/553850?dimensions=600x900&types=static&nsfw=false&humor=false", api.Uri);
        Assert.Equal("Bearer " + Key, api.Authorization);
        Assert.Null(handler.Requests[1].Authorization);
        Assert.Equal(2, client.RequestCount);
    }

    [Fact]
    public void Heroes_logos_and_search_use_the_documented_endpoints()
    {
        var handler = new SgdbHandler(request => request.RequestUri!.AbsolutePath.Contains("/search/", StringComparison.Ordinal)
            ? SgdbHandler.Json("{\"success\":true,\"data\":[{\"id\":5247826,\"name\":\"Baldur's Gate 3\"}]}")
            : SgdbHandler.Json("{\"success\":true,\"data\":[]}"));
        using var client = NewClient(handler);

        var id = client.SearchGameId("Baldur's Gate 3", CancellationToken.None);
        Assert.Null(client.FirstAsset(SteamGridDbAssetKind.Hero, null, id, CancellationToken.None));
        Assert.Null(client.FirstAsset(SteamGridDbAssetKind.Logo, null, id, CancellationToken.None));

        Assert.Equal(5247826, id);
        Assert.StartsWith("https://www.steamgriddb.com/api/v2/search/autocomplete/Baldur", handler.Requests[0].Uri, StringComparison.Ordinal);
        Assert.EndsWith("s%20Gate%203", handler.Requests[0].Uri, StringComparison.Ordinal);
        Assert.StartsWith("https://www.steamgriddb.com/api/v2/heroes/game/5247826?", handler.Requests[1].Uri, StringComparison.Ordinal);
        Assert.StartsWith("https://www.steamgriddb.com/api/v2/logos/game/5247826?", handler.Requests[2].Uri, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => client.FirstAsset(SteamGridDbAssetKind.Hero, null, null, CancellationToken.None));
    }

    [Fact]
    public void Foreign_or_insecure_image_addresses_are_never_loaded()
    {
        var handler = new SgdbHandler(_ => SgdbHandler.Json(
            "{\"success\":true,\"data\":[{\"id\":1,\"url\":\"http://cdn2.steamgriddb.com/a.png\"},{\"id\":2,\"url\":\"https://evil.example/a.png\"},{\"id\":3,\"url\":\"https://steamgriddb.com.evil.example/a.png\"}]}"));
        using var client = NewClient(handler);

        Assert.Null(client.FirstAsset(SteamGridDbAssetKind.Hero, "1", null, CancellationToken.None));
        var error = Assert.Throws<SteamGridDbException>(() => client.Download(new SteamGridDbAsset(1, "https://evil.example/a.png", 1, 1, null), CancellationToken.None));
        Assert.Equal(SteamGridDbErrorKind.Blocked, error.Kind);
        Assert.Single(handler.Requests); // nur die Abfrage, kein Download
        Assert.True(SteamGridDbClient.IsAllowedImageUrl("https://cdn2.steamgriddb.com/hero/x.jpg"));
        Assert.False(SteamGridDbClient.IsAllowedImageUrl("https://cdn2.steamgriddb.com:8443/hero/x.jpg"));
        Assert.False(SteamGridDbClient.IsAllowedImageUrl("https://user@cdn2.steamgriddb.com/hero/x.jpg"));
    }

    [Fact]
    public void Errors_are_classified_and_unknown_games_are_empty()
    {
        var status = HttpStatusCode.Unauthorized;
        var handler = new SgdbHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("{\"success\":false}") });
        using var client = NewClient(handler);

        Assert.False(client.CheckKey(CancellationToken.None));
        status = HttpStatusCode.TooManyRequests;
        Assert.Equal(SteamGridDbErrorKind.RateLimited, Assert.Throws<SteamGridDbException>(() => client.CheckKey(CancellationToken.None)).Kind);
        status = HttpStatusCode.NotFound;
        Assert.Null(client.FirstAsset(SteamGridDbAssetKind.Grid, "999999999", null, CancellationToken.None));
        status = HttpStatusCode.InternalServerError;
        Assert.Equal(SteamGridDbErrorKind.HttpError, Assert.Throws<SteamGridDbException>(() => client.SearchGameId("x", CancellationToken.None)).Kind);

        using var broken = NewClient(new SgdbHandler(_ => SgdbHandler.Json("kein json")));
        Assert.Equal(SteamGridDbErrorKind.InvalidResponse, Assert.Throws<SteamGridDbException>(() => broken.SearchGameId("x", CancellationToken.None)).Kind);
        using var offline = NewClient(new SgdbHandler(_ => throw new HttpRequestException("offline")));
        Assert.Equal(SteamGridDbErrorKind.Network, Assert.Throws<SteamGridDbException>(() => offline.CheckKey(CancellationToken.None)).Kind);
    }

    [Fact]
    public void Requests_are_paced_capped_and_downloads_size_limited()
    {
        var clock = new FakeClock();
        var handler = new SgdbHandler(request => request.RequestUri!.Host == "www.steamgriddb.com"
            ? SgdbHandler.Json("{\"success\":true,\"data\":[]}")
            : SgdbHandler.Bytes(new byte[1024], "image/jpeg", declaredLength: SteamGridDbClient.MaxDownloadBytes + 1));
        using var client = new SteamGridDbClient(handler, clock, Key, "Test", maxRequests: 3);

        client.SearchGameId("a", CancellationToken.None);
        client.SearchGameId("b", CancellationToken.None);
        var big = Assert.Throws<SteamGridDbException>(() => client.Download(new SteamGridDbAsset(1, "https://cdn2.steamgriddb.com/x.jpg", 1, 1, null), CancellationToken.None));
        var limit = Assert.Throws<SteamGridDbException>(() => client.SearchGameId("c", CancellationToken.None));

        Assert.Equal(SteamGridDbErrorKind.Blocked, big.Kind);
        Assert.Equal(SteamGridDbErrorKind.RequestLimit, limit.Kind);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([SteamGridDbClient.PauseBetweenRequests, SteamGridDbClient.PauseBetweenRequests], clock.Delays);
    }

    [Fact]
    public void Key_store_encrypts_and_flags_unreadable_files()
    {
        var fs = new InMemoryFileSystem();
        var store = new SteamGridDbKeyStore(fs, @"C:\Data\steamgriddb-key.dat", new XorProtector());

        store.Save(" " + Key + " ");

        var raw = fs.ReadAllText(@"C:\Data\steamgriddb-key.dat");
        Assert.DoesNotContain(Key, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, Encoding.UTF8.GetString(Convert.FromBase64String(raw)), StringComparison.Ordinal);
        Assert.Equal(Key, store.Load(out var unreadable));
        Assert.False(unreadable);

        fs.AddFile(@"C:\Data\steamgriddb-key.dat", "kein base64 !");
        Assert.Null(store.Load(out unreadable));
        Assert.True(unreadable);

        store.Delete();
        Assert.Null(store.Load(out unreadable));
        Assert.False(unreadable);
        Assert.Throws<ArgumentException>(() => store.Save("zu kurz"));
        Assert.False(SteamGridDbKeyStore.IsPlausible("mit leer zeichen 123"));
    }

    [Fact]
    public void Dpapi_entropy_differs_from_the_pcgamingwiki_account()
    {
        var plain = Encoding.UTF8.GetBytes(Key);
        var sgdb = SteamGridDbKeyStore.CreateProtector();
        var cipher = sgdb.Protect(plain);

        Assert.Equal(plain, sgdb.Unprotect(cipher));
        Assert.Throws<CryptographicException>(() => DpapiProtector.Instance.Unprotect(cipher));
    }

    private static SteamGridDbClient NewClient(SgdbHandler handler) =>
        new(handler, new FakeClock(), Key, "HDRpilot/Test");

    /// <summary>Umkehrbar „verschlüsselt“ (Tests ohne DPAPI-Abhängigkeit vom Benutzer).</summary>
    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();

        public byte[] Unprotect(byte[] data) => Protect(data);
    }
}

/// <summary>Aufgezeichnete Anfrage an die SteamGridDB-Attrappe.</summary>
public sealed record SgdbRequest(string Uri, string? Authorization);

/// <summary>HttpMessageHandler ohne Netz für SteamGridDB: beantwortet jede Anfrage über eine Funktion und zeichnet sie auf.</summary>
public sealed class SgdbHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<SgdbRequest> _requests = [];

    public IReadOnlyList<SgdbRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] data, string mime, long? declaredLength = null)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
        if (declaredLength is { } length)
        {
            content.Headers.ContentLength = length;
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(new SgdbRequest(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
        }

        return respond(request);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));
}
