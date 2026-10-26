using System.Diagnostics;
using System.Net;
using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Artwork;

/// <summary>
/// SteamGridDB-Suche in „Bild wählen…“ im Kern (E93, G14.1), nur mit Attrappen: Seiten mit Vorschau, Autor und Filtern,
/// Ende erst mit leerer Seite, Trefferliste, bereinigte Suchbegriffe, Vorschauen nur aus thumb und ohne Abstand, Bild-401/403
/// kein abgelehnter Schlüssel, getrennte Budgets, exklusiver Abstand über Threads, Zeitgrenzen und sofortiger Abbruch.
/// </summary>
public sealed class SteamGridDbSearchClientTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private const string Cdn = "https://cdn2.steamgriddb.com/";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SteamGridDbAssetKind.Grid, "grids/steam/553850?dimensions=600x900,342x482,660x930&types=static&nsfw=false&humor=false&epilepsy=false&page=2")]
    [InlineData(SteamGridDbAssetKind.Hero, "heroes/steam/553850?types=static&nsfw=false&humor=false&epilepsy=false&page=2")]
    [InlineData(SteamGridDbAssetKind.Logo, "logos/steam/553850?types=static&nsfw=false&humor=false&epilepsy=false&page=2")]
    public void Asset_pages_send_the_static_safe_filters_the_page_and_no_limit(SteamGridDbAssetKind kind, string expected)
    {
        var handler = Api(Entries(1));
        using var client = NewClient(handler);

        var page = client.Assets(kind, SteamGridDbTarget.Steam("553850"), 2, Ct);
        client.DownloadPreview(page.Items[0], Ct);
        client.Assets(kind, SteamGridDbTarget.Game(9101), 0, Ct);

        Assert.Equal(SteamGridDbClient.ApiBase + expected, handler.Requests[0].Uri);
        Assert.DoesNotContain("limit", handler.Requests[0].Uri, StringComparison.Ordinal);
        Assert.Equal("Bearer " + Key, handler.Requests[0].Authorization);
        Assert.StartsWith(Cdn + "thumb/", handler.Requests[1].Uri, StringComparison.Ordinal);
        Assert.Null(handler.Requests[1].Authorization);
        Assert.Contains("/game/9101?", handler.Requests[2].Uri, StringComparison.Ordinal);
        Assert.EndsWith("&page=0", handler.Requests[2].Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Asset_pages_parse_thumb_author_size_mime_and_style()
    {
        var longName = new string('a', 63) + "\U0001F600" + "bbb";
        var json = Page(
            "{\"id\":11,\"url\":\"" + Cdn + "grid/a.png\",\"thumb\":\"" + Cdn + "thumb/a.png\",\"width\":342,\"height\":482,\"mime\":\"image/png\",\"style\":\"alternate\","
            + "\"author\":{\"name\":\"  Test\\u202Eautor\\u0007 01\\u2066  \",\"steam64\":\"76561190000000001\",\"avatar\":\"https://avatars.example/x.png\"}}",
            "{\"id\":12,\"url\":\"" + Cdn + "grid/b.jpg\",\"author\":{\"name\":\"" + longName + "\"}}",
            "{\"id\":13,\"url\":\"" + Cdn + "grid/c.webp\",\"author\":{\"name\":\" \\u200E \"},\"style\":5}");
        using var client = NewClient(Api(json));

        var page = client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Game(1), 0, Ct);

        Assert.Equal(3, page.Items.Count);
        var first = page.Items[0];
        Assert.Equal(new SteamGridDbAsset(11, Cdn + "grid/a.png", 342, 482, "image/png", Cdn + "thumb/a.png", "Testautor 01", "alternate"), first);
        Assert.DoesNotContain("76561190000000001", first.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("avatars", first.ToString(), StringComparison.Ordinal);
        Assert.Equal(new string('a', 63), page.Items[1].Author); // das Emoji passt nicht mehr ganz hinein
        Assert.Null(page.Items[1].ThumbUrl);
        Assert.Equal(0, page.Items[1].Width);
        Assert.Null(page.Items[2].Author);
        Assert.Null(page.Items[2].Style);
        Assert.Null(page.Items[2].Mime);
    }

    [Theory]
    [InlineData("\"url\":\"http://cdn2.steamgriddb.com/a.png\"", false)]
    [InlineData("\"url\":\"https://evil.example/a.png\"", false)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com:8443/a.png\"", false)]
    [InlineData("\"url\":\"https://user@cdn2.steamgriddb.com/a.png\"", false)]
    [InlineData("\"url\":\"https://s3.amazonaws.com/steamgriddb/a.png\"", false)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.png\",\"nsfw\":true", false)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.png\",\"humor\":true", false)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.png\",\"epilepsy\":true", false)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.png\",\"tags\":[\"NSFW\"]", false)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.png\",\"mime\":\"video/webm\"", false)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.webm\"", false)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.png\",\"thumb\":\"https://evil.example/t.png\"", true)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.png\",\"thumb\":\"https://cdn2.steamgriddb.com/t.webm\"", true)]
    [InlineData("\"url\":\"https://cdn2.steamgriddb.com/a.jpg\",\"mime\":\"image/jpg\",\"nsfw\":false,\"tags\":[\"Minimalist\"]", true)]
    public void Asset_pages_drop_foreign_insecure_animated_or_flagged_entries_and_count_them(string fields, bool kept)
    {
        var json = Page("{\"id\":1," + fields + "}", "{\"id\":2,\"url\":\"" + Cdn + "b.png\",\"thumb\":\"" + Cdn + "t/b.png\"}", "7", "{\"id\":0,\"url\":\"" + Cdn + "c.png\"}");
        using var client = NewClient(Api(json));

        var page = client.Assets(SteamGridDbAssetKind.Hero, SteamGridDbTarget.Game(1), 0, Ct);

        Assert.Equal(4, page.RawCount);
        Assert.Equal(kept ? 2 : 1, page.Items.Count);
        Assert.Equal(kept ? 2 : 3, page.Skipped);
        if (kept)
        {
            Assert.Null(page.Items[0].ThumbUrl); // fremde oder bewegte Vorschau: Eintrag bleibt, Vorschau nicht
        }

        Assert.Equal(2, page.Items[^1].Id);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(22, true)]
    [InlineData(0, false)]
    public void Has_more_until_an_empty_page(int count, bool more)
    {
        var entries = Enumerable.Range(1, count).Select(i => Entry(i)).ToArray();
        var json = "{\"success\":true,\"page\":0,\"total\":" + (count == 0 ? 500 : 1) + ",\"limit\":" + (count == 50 ? 50 : 10) + ",\"data\":[" + string.Join(",", entries) + "]}";
        using var client = NewClient(Api(json));

        var page = client.Assets(SteamGridDbAssetKind.Logo, SteamGridDbTarget.Steam("1"), 3, Ct);

        Assert.Equal(more, page.HasMore);
        Assert.Equal(count, page.Items.Count);
        Assert.False(page.GameUnknown);
    }

    [Fact]
    public void Unknown_game_only_on_page_zero_and_a_later_404_ends_the_list()
    {
        using var client = NewClient(new SgdbHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"success\":false}") }));

        var first = client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Steam("9990404"), 0, Ct);
        var later = client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Steam("9990404"), 1, Ct);

        Assert.True(first.GameUnknown);
        Assert.False(first.HasMore);
        Assert.False(later.GameUnknown);
        Assert.False(later.HasMore);
        Assert.Equal(1, later.Page);
    }

    [Fact]
    public void Game_search_returns_all_valid_hits_with_year_and_verified()
    {
        var hits = new List<string>
        {
            "{\"id\":9101,\"name\":\"  Nebel\\u202Efahrt \",\"release_date\":1561939200,\"verified\":true}",
            "{\"id\":9102,\"name\":\"Nebelfahrt: Neuauflage\",\"release_date\":0}",
            "{\"id\":9103,\"name\":\"Nebelfahrt II\"}",
            "{\"id\":9104,\"name\":\"Alt\",\"release_date\":-5}",
            "{\"id\":9105,\"name\":\"Fern\",\"release_date\":4200000000}",
            "{\"id\":0,\"name\":\"ohne id\"}",
            "{\"id\":9106,\"name\":\" \\u0007 \"}",
            "{\"id\":9107}",
            "\"kein Objekt\"",
        };
        hits.AddRange(Enumerable.Range(1, 30).Select(i => "{\"id\":" + (9200 + i) + ",\"name\":\"Kupferwald " + i + "\"}"));
        using var client = NewClient(Api("{\"success\":true,\"data\":[" + string.Join(",", hits) + "]}"));

        var result = client.SearchGames("Nebelfahrt", Ct);

        Assert.Equal(SteamGridDbClient.MaxHits, result.Count);
        Assert.Equal(new SteamGridDbGameHit(9101, "Nebelfahrt", 2019, true), result[0]);
        Assert.Equal(new SteamGridDbGameHit(9102, "Nebelfahrt: Neuauflage", null, false), result[1]);
        Assert.Null(result[2].ReleaseYear);
        Assert.Null(result[3].ReleaseYear);
        Assert.Null(result[4].ReleaseYear);
        Assert.Equal(9201, result[5].Id);
        Assert.Equal("Kupferwald 15", result[^1].Name);

        using var empty = NewClient(Api("{\"success\":true,\"data\":[]}"));
        Assert.Empty(empty.SearchGames("Nebelfahrt", Ct));
        using var missing = NewClient(new SgdbHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Empty(missing.SearchGames("Nebelfahrt", Ct));
        Assert.Empty(missing.SearchGames(" / ", Ct)); // nichts zu suchen: keine Anfrage
    }

    [Fact]
    public void Game_by_id_returns_name_and_year_or_null_for_404()
    {
        var handler = new SgdbHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/9101", StringComparison.Ordinal)
            ? SgdbHandler.Json("{\"success\":true,\"data\":{\"id\":9101,\"name\":\"Kupferwald\",\"release_date\":1546300800,\"verified\":false}}")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = NewClient(handler);

        Assert.Equal(new SteamGridDbGameHit(9101, "Kupferwald", 2019, false), client.GameById(9101, Ct));
        Assert.Null(client.GameById(9102, Ct));
        Assert.Equal(SteamGridDbClient.ApiBase + "games/id/9101", handler.Requests[0].Uri);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.GameById(0, Ct));
    }

    [Theory]
    [InlineData("Half/Life", "Half%20Life")]
    [InlineData("Spiel+", "Spiel")]
    [InlineData("100%", "100")]
    [InlineData("Name?#", "Name")]
    [InlineData(" Back\\slash \t\u0001 Zwei  ", "Back%20slash%20Zwei")]
    [InlineData("Kupferäwald", "Kupfer%C3%A4wald")]
    [InlineData("///", "")]
    public void Search_terms_are_sanitized_encoded_once_and_never_split_a_surrogate(string name, string expected)
    {
        Assert.Equal(expected, SteamGridDbClient.SearchTerm(name));
    }

    [Fact]
    public void Search_terms_are_cut_to_one_hundred_characters_at_a_text_element()
    {
        var term = Uri.UnescapeDataString(SteamGridDbClient.SearchTerm(new string('x', 300)));
        Assert.Equal(100, term.Length);

        var emoji = new string('y', 99) + "\U0001F600" + "zzz";
        var cut = Uri.UnescapeDataString(SteamGridDbClient.SearchTerm(emoji));
        Assert.Equal(new string('y', 99), cut);
        Assert.False(char.IsSurrogate(cut[^1]));

        // Kodiert genau einmal: ein Prozentzeichen im Namen wird nie zu „%25“.
        var handler = Api("{\"success\":true,\"data\":[]}");
        using var client = NewClient(handler);
        client.SearchGames("100% Nebel", Ct);
        client.SearchGameId("100% Nebel", Ct);
        Assert.All(handler.Requests, r => Assert.EndsWith("/search/autocomplete/100%20Nebel", r.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void Previews_use_the_thumb_without_key_and_without_api_pacing()
    {
        var clock = new FakeClock();
        var handler = Api(Entries(3));
        using var client = new SteamGridDbClient(handler, clock, Key, "Test", pacer: new RequestPacer(clock, SteamGridDbClient.PauseBetweenRequests));

        var page = client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Game(1), 0, Ct);
        foreach (var asset in page.Items)
        {
            client.DownloadPreview(asset, Ct);
        }

        client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Game(1), 1, Ct);

        Assert.Equal(5, handler.Requests.Count);
        Assert.All(handler.Requests.Skip(1).Take(3), r => Assert.Null(r.Authorization));
        Assert.All(handler.Requests.Skip(1).Take(3), r => Assert.Contains("/thumb/", r.Uri, StringComparison.Ordinal));
        Assert.Equal([SteamGridDbClient.PauseBetweenRequests], clock.Delays); // nur zwischen den beiden API-Anfragen
        Assert.Equal(2, client.ApiRequestCount);
        Assert.Equal(3, client.ImageRequestCount);
        Assert.Equal(5, client.RequestCount);
    }

    [Fact]
    public void Previews_need_a_thumb_and_stay_under_four_megabytes()
    {
        var size = 0;
        long? declared = null;
        var handler = new SgdbHandler(_ => size < 0
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new TrickleStream(SteamGridDbClient.MaxPreviewBytes + 1)) }
            : SgdbHandler.Bytes(new byte[size], "image/png", declared));
        using var client = NewClient(handler);
        var asset = new SteamGridDbAsset(1, Cdn + "grid/full.png", 600, 900, null);

        var none = Assert.Throws<SteamGridDbException>(() => client.DownloadPreview(asset, Ct));
        Assert.Equal(SteamGridDbErrorKind.Blocked, none.Kind);
        Assert.Empty(handler.Requests); // nie das volle Bild als Ersatz

        var withThumb = asset with { ThumbUrl = Cdn + "thumb/a.png" };
        size = 1000;
        declared = 5L * 1024 * 1024;
        Assert.Equal(SteamGridDbErrorKind.Blocked, Assert.Throws<SteamGridDbException>(() => client.DownloadPreview(withThumb, Ct)).Kind);
        size = -1; // 4 MB + 1 Byte ohne Längenangabe
        Assert.Equal(SteamGridDbErrorKind.Blocked, Assert.Throws<SteamGridDbException>(() => client.DownloadPreview(withThumb, Ct)).Kind);
        size = 1000;
        declared = null;
        Assert.Equal(1000, client.DownloadPreview(withThumb, Ct).Length);
        Assert.All(handler.Requests, r => Assert.Equal(Cdn + "thumb/a.png", r.Uri));
    }

    [Fact]
    public void Preview_and_picked_downloads_reject_a_redirect_to_another_host()
    {
        var handler = new SgdbHandler(request =>
        {
            var response = SgdbHandler.Bytes([1, 2, 3], "image/png");
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, request.RequestUri!.AbsolutePath.Contains("umweg", StringComparison.Ordinal)
                ? "https://evil.example/a.png"
                : request.RequestUri.AbsoluteUri);
            return response;
        });
        using var client = NewClient(handler);
        var detour = new SteamGridDbAsset(1, Cdn + "umweg/full.png", 1, 1, null, Cdn + "umweg/thumb.png");
        var direct = new SteamGridDbAsset(2, Cdn + "full.png", 1, 1, null, Cdn + "thumb.png");

        Assert.Equal(SteamGridDbErrorKind.Blocked, Assert.Throws<SteamGridDbException>(() => client.DownloadPreview(detour, Ct)).Kind);
        Assert.Equal(SteamGridDbErrorKind.Blocked, Assert.Throws<SteamGridDbException>(() => client.Download(detour, SteamGridDbClient.MaxPickedImageBytes, paced: false, Ct)).Kind);
        Assert.Equal(3, client.DownloadPreview(direct, Ct).Length);
        Assert.Equal(".png", client.Download(direct, SteamGridDbClient.MaxPickedImageBytes, paced: false, Ct).Extension);
    }

    [Theory]
    [InlineData("preview", HttpStatusCode.Forbidden)]
    [InlineData("preview", HttpStatusCode.Unauthorized)]
    [InlineData("picked", HttpStatusCode.Forbidden)]
    [InlineData("picked", HttpStatusCode.Unauthorized)]
    [InlineData("automatic", HttpStatusCode.Forbidden)]
    [InlineData("automatic", HttpStatusCode.Unauthorized)]
    public void Image_401_or_403_is_an_http_error_not_a_rejected_key(string route, HttpStatusCode status)
    {
        using var client = NewClient(new SgdbHandler(_ => new HttpResponseMessage(status)));
        var asset = new SteamGridDbAsset(1, Cdn + "grid/a.png", 1, 1, null, Cdn + "thumb/a.png");

        var error = Assert.Throws<SteamGridDbException>(() =>
        {
            switch (route)
            {
                case "preview":
                    client.DownloadPreview(asset, Ct);
                    break;
                case "picked":
                    client.Download(asset, SteamGridDbClient.MaxPickedImageBytes, paced: false, Ct);
                    break;
                default:
                    client.Download(asset, Ct);
                    break;
            }
        });

        Assert.Equal(SteamGridDbErrorKind.HttpError, error.Kind);
        Assert.Equal((int)status, error.Data["status"]);

        // Gegenprobe: dieselben Codes der Schnittstelle sind ein abgelehnter Schlüssel.
        Assert.Equal(SteamGridDbErrorKind.Unauthorized, Assert.Throws<SteamGridDbException>(() => client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Game(1), 0, Ct)).Kind);
    }

    [Fact]
    public void Picked_images_may_reach_32_mb_while_the_automatic_run_keeps_15_mb()
    {
        long length = 20L * 1024 * 1024;
        var handler = new SgdbHandler(_ => SgdbHandler.Bytes(new byte[length], "image/jpeg"));
        using var client = NewClient(handler);
        var asset = new SteamGridDbAsset(1, Cdn + "grid/a.jpg", 1, 1, null);

        Assert.Equal(length, client.Download(asset, SteamGridDbClient.MaxPickedImageBytes, paced: false, Ct).Data.Length);
        Assert.Equal(SteamGridDbErrorKind.Blocked, Assert.Throws<SteamGridDbException>(() => client.Download(asset, Ct)).Kind);
        length = SteamGridDbClient.MaxPickedImageBytes + 1;
        Assert.Equal(SteamGridDbErrorKind.Blocked, Assert.Throws<SteamGridDbException>(() => client.Download(asset, SteamGridDbClient.MaxPickedImageBytes, paced: false, Ct)).Kind);
        Assert.Equal(32L * 1024 * 1024, SteamGridDbClient.MaxPickedImageBytes);
    }

    [Fact]
    public void Budget_counts_api_requests_images_and_bytes_separately_and_stops_without_waiting()
    {
        var clock = new FakeClock();
        var handler = Api(Entries(3), imageBytes: 400);
        var budget = new SteamGridDbBudget(maxApiRequests: 2, maxImageRequests: 3, maxPreviewBytes: 1000);
        using var client = new SteamGridDbClient(handler, clock, Key, "Test", maxRequests: 1, budget: budget);

        var page = client.Assets(SteamGridDbAssetKind.Logo, SteamGridDbTarget.Game(1), 0, Ct);
        client.Assets(SteamGridDbAssetKind.Logo, SteamGridDbTarget.Game(1), 1, Ct);
        var delays = clock.Delays.Count;
        var api = Assert.Throws<SteamGridDbException>(() => client.Assets(SteamGridDbAssetKind.Logo, SteamGridDbTarget.Game(1), 2, Ct));
        client.DownloadPreview(page.Items[0], Ct);
        client.DownloadPreview(page.Items[1], Ct);
        var bytes = Assert.Throws<SteamGridDbException>(() => client.DownloadPreview(page.Items[2], Ct)); // 1200 > 1000
        var images = Assert.Throws<SteamGridDbException>(() => client.DownloadPreview(page.Items[0], Ct));

        Assert.Equal(SteamGridDbErrorKind.RequestLimit, api.Kind);
        Assert.Equal(SteamGridDbErrorKind.RequestLimit, bytes.Kind);
        Assert.Equal(SteamGridDbErrorKind.RequestLimit, images.Kind);
        Assert.Equal(delays, clock.Delays.Count); // erschöpft: kein Warten auf den Abstand
        Assert.Equal(2, budget.ApiRequests);
        Assert.Equal(3, budget.ImageRequests);
        Assert.Equal(800, budget.PreviewBytes);
        Assert.Equal(5, handler.Requests.Count);

        // Ohne Budget gilt weiter die Gesamtgrenze je Client.
        using var legacy = new SteamGridDbClient(Api(Entries(1)), clock, Key, "Test", maxRequests: 2);
        var first = legacy.Assets(SteamGridDbAssetKind.Logo, SteamGridDbTarget.Game(1), 0, Ct);
        legacy.DownloadPreview(first.Items[0], Ct);
        Assert.Equal(SteamGridDbErrorKind.RequestLimit, Assert.Throws<SteamGridDbException>(() => legacy.DownloadPreview(first.Items[0], Ct)).Kind);
    }

    [Fact]
    public void Parallel_image_downloads_are_counted_exactly()
    {
        var handler = Api(Entries(1));
        using var client = NewClient(handler);
        var asset = new SteamGridDbAsset(1, Cdn + "grid/a.png", 1, 1, null, Cdn + "thumb/a.png");

        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ => client.DownloadPreview(asset, CancellationToken.None));

        Assert.Equal(32, client.ImageRequestCount);
        Assert.Equal(0, client.ApiRequestCount);
        Assert.Equal(32, client.RequestCount);
        Assert.Equal(32, handler.Requests.Count);
    }

    [Theory]
    [InlineData(401, SteamGridDbErrorKind.Unauthorized)]
    [InlineData(403, SteamGridDbErrorKind.Unauthorized)]
    [InlineData(429, SteamGridDbErrorKind.RateLimited)]
    [InlineData(500, SteamGridDbErrorKind.HttpError)]
    [InlineData(-1, SteamGridDbErrorKind.InvalidResponse)]
    [InlineData(-2, SteamGridDbErrorKind.InvalidResponse)]
    [InlineData(-3, SteamGridDbErrorKind.Blocked)]
    [InlineData(-4, SteamGridDbErrorKind.Network)]
    public void Errors_map_to_kinds_for_pages_and_search(int code, SteamGridDbErrorKind expected)
    {
        var handler = new SgdbHandler(_ => code switch
        {
            -1 => SgdbHandler.Json("kein json"),
            -2 => SgdbHandler.Json("{\"success\":false,\"errors\":[\"x\"]}"),
            -3 => SgdbHandler.Json("{\"success\":true,\"data\":[\"" + new string('x', 2 * 1024 * 1024) + "\"]}"),
            -4 => throw new HttpRequestException("offline"),
            _ => new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("{\"success\":false}") },
        });
        using var client = NewClient(handler);

        Assert.Equal(expected, Assert.Throws<SteamGridDbException>(() => client.Assets(SteamGridDbAssetKind.Hero, SteamGridDbTarget.Game(1), 0, Ct)).Kind);
        Assert.Equal(expected, Assert.Throws<SteamGridDbException>(() => client.SearchGames("Nebelfahrt", Ct)).Kind);
        Assert.Equal(expected, Assert.Throws<SteamGridDbException>(() => client.GameById(9101, Ct)).Kind);
    }

    [Fact]
    public async Task Cancelling_sends_nothing_before_the_request_and_aborts_a_stalled_body_at_once()
    {
        // Der Inhalt beachtet das Token nicht (wie ein hängender Socket): nur das Freigeben der Antwort beendet ihn.
        var stalled = new StalledStream(honorToken: false);
        var handler = new SgdbHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stalled) });
        using var client = NewClient(handler, new SteamGridDbTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => client.SearchGames("Nebelfahrt", cancelled.Token));
        Assert.Empty(handler.Requests);

        using var cts = new CancellationTokenSource();
        var asset = new SteamGridDbAsset(1, Cdn + "grid/a.png", 1, 1, null, Cdn + "thumb/a.png");
        var watch = Stopwatch.StartNew();
        var task = Task.Run(() => client.DownloadPreview(asset, cts.Token), Ct);
        Assert.True(stalled.Reading.Wait(TimeSpan.FromSeconds(10), Ct));
        cts.Cancel();

        // Hängt die Übertragung weiter, endet das Warten mit TimeoutException statt mit dem Abbruch.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        stalled.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_stalled_body_times_out_as_network_with_the_timeout_flag(bool idle)
    {
        Stream body = idle ? new StalledStream() : new TrickleStream(long.MaxValue, TimeSpan.FromMilliseconds(20));
        var timeouts = idle
            ? new SteamGridDbTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30))
            : new SteamGridDbTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        using var client = NewClient(new SgdbHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }), timeouts);

        var watch = Stopwatch.StartNew();

        // Ohne Zeitgrenze hinge der Aufruf: Das Warten endet dann mit TimeoutException statt mit dem Fehler.
        var error = await Assert.ThrowsAsync<SteamGridDbException>(() => Task.Run(() => client.SearchGames("Nebelfahrt", Ct), Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct));

        Assert.Equal(SteamGridDbErrorKind.Network, error.Kind);
        Assert.True(error.IsTimeout);
        Assert.Equal(true, error.Data["timeout"]);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_header_timeout_is_flagged_as_well()
    {
        var handler = new SgdbHandler(_ => throw new TaskCanceledException("Zeitgrenze", new TimeoutException()));
        using var client = NewClient(handler);

        var error = Assert.Throws<SteamGridDbException>(() => client.SearchGames("Nebelfahrt", Ct));

        Assert.Equal(SteamGridDbErrorKind.Network, error.Kind);
        Assert.True(error.IsTimeout);
    }

    [Fact]
    public void The_key_never_appears_in_any_exception()
    {
        var codes = new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => SgdbHandler.Json("kein json"),
            _ => SgdbHandler.Json("{\"success\":false}"),
            _ => throw new HttpRequestException("offline"),
            _ => throw new TaskCanceledException("Zeitgrenze", new TimeoutException()),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) },
        };
        var timeouts = new SteamGridDbTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        var seen = new HashSet<SteamGridDbErrorKind>();
        foreach (var respond in codes)
        {
            using var client = NewClient(new SgdbHandler(respond), timeouts, budget: new SteamGridDbBudget(1, 0, 0));
            foreach (var call in new Action[]
            {
                () => client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Game(1), 0, Ct),
                () => client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Game(1), 0, Ct), // Budget erschöpft
                () => client.DownloadPreview(new SteamGridDbAsset(1, "https://evil.example/a.png", 1, 1, null), Ct),
            })
            {
                var error = Assert.Throws<SteamGridDbException>(call);
                seen.Add(error.Kind);
                Assert.DoesNotContain(Key, error.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(Key, error.ToString(), StringComparison.Ordinal);
                foreach (System.Collections.DictionaryEntry entry in error.Data)
                {
                    Assert.DoesNotContain(Key, entry.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
                }
            }
        }

        Assert.Equal(7, seen.Count); // alle sieben Fehlerarten kamen vor
    }

    [Fact]
    public void Target_accepts_only_digits_or_positive_ids_and_never_a_comma()
    {
        Assert.Equal("steam/553850", SteamGridDbTarget.Steam("553850").ToString());
        Assert.Equal("game/9101", SteamGridDbTarget.Game(9101).ToString());
        Assert.Equal("553850", SteamGridDbTarget.Steam("553850").SteamAppId);
        Assert.Equal(9101, SteamGridDbTarget.Game(9101).GameId);
        Assert.Equal(SteamGridDbTarget.Game(9101), SteamGridDbTarget.Game(9101));
        Assert.NotEqual(SteamGridDbTarget.Game(9101), SteamGridDbTarget.Game(9102));
        foreach (var bad in new[] { "", "1,2", "12a", " 1", "١٢", "-1" })
        {
            Assert.Throws<ArgumentException>(() => SteamGridDbTarget.Steam(bad));
        }

        Assert.ThrowsAny<ArgumentException>(() => SteamGridDbTarget.Steam(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => SteamGridDbTarget.Game(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SteamGridDbTarget.Game(-4));
        Assert.False(SteamGridDbTarget.IsSteamAppId(""));
    }

    [Fact]
    public async Task Pacer_turns_exclude_each_other_across_threads_and_keep_one_second_from_the_end()
    {
        var clock = new BlockingClock();
        var pacer = new RequestPacer(clock, TimeSpan.FromSeconds(1));

        var first = pacer.Enter(Ct); // ohne vorigen Zug: sofort
        var second = Task.Run(() => pacer.Enter(Ct), Ct);
        Assert.False(await Finishes(second, 200)); // der erste hält den Zug
        Assert.Empty(clock.Delays);

        clock.Advance(TimeSpan.FromMilliseconds(400)); // die Anfrage dauert
        first.Dispose();
        first.Dispose(); // doppelt freigeben schadet nicht
        Assert.True(clock.DelayStarted.Wait(TimeSpan.FromSeconds(10), Ct));
        Assert.Equal([TimeSpan.FromSeconds(1)], clock.Delays); // ab dem Ende des ersten, nicht ab seinem Beginn
        Assert.False(second.IsCompleted);

        var third = Task.Run(() => pacer.Enter(Ct), Ct);
        clock.Release();
        var turn = await second;
        Assert.False(await Finishes(third, 200)); // der zweite hält jetzt den Zug
        turn.Dispose();
        (await third).Dispose();
    }

    [Fact]
    public async Task Pacer_turn_is_released_when_waiting_is_cancelled()
    {
        var clock = new BlockingClock();
        var pacer = new RequestPacer(clock, TimeSpan.FromSeconds(1));
        var holder = pacer.Enter(Ct);

        // Abbruch beim Warten auf den Zug: nichts gehalten, nichts freigegeben.
        using var waiting = new CancellationTokenSource();
        var blocked = Task.Run(() => pacer.Enter(waiting.Token), Ct);
        Assert.False(await Finishes(blocked, 100));
        waiting.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        holder.Dispose();

        // Abbruch während des Abstands: der Zug wird zurückgegeben.
        using var pausing = new CancellationTokenSource();
        var paused = Task.Run(() => pacer.Enter(pausing.Token), Ct);
        Assert.True(clock.DelayStarted.Wait(TimeSpan.FromSeconds(10), Ct));
        pausing.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => paused);

        clock.Release();
        var next = Task.Run(() => pacer.Enter(Ct), Ct);
        Assert.True(await Finishes(next, 10000));
        var exclusive = Task.Run(() => pacer.Enter(Ct), Ct);
        Assert.False(await Finishes(exclusive, 200)); // weiter genau ein Inhaber
        (await next).Dispose();
        (await exclusive).Dispose();
    }

    private static async Task<bool> Finishes(Task task, int milliseconds) =>
        await Task.WhenAny(task, Task.Delay(milliseconds, Ct)) == task;

    private static SteamGridDbClient NewClient(SgdbHandler handler, SteamGridDbTimeouts? timeouts = null, SteamGridDbBudget? budget = null) =>
        new(handler, new FakeClock(), Key, "HDRpilot/Test", budget: budget, timeouts: timeouts);

    /// <summary>Schnittstelle antwortet mit <paramref name="json"/>, der CDN mit erzeugten Bytes.</summary>
    private static SgdbHandler Api(string json, int imageBytes = 16) => new(request => request.RequestUri!.Host == "www.steamgriddb.com"
        ? SgdbHandler.Json(json)
        : SgdbHandler.Bytes(new byte[imageBytes], "image/png"));

    private static string Entry(int i) =>
        "{\"id\":" + i + ",\"url\":\"" + Cdn + "grid/" + i + ".png\",\"thumb\":\"" + Cdn + "thumb/" + i + ".png\",\"width\":600,\"height\":900,\"mime\":\"image/png\",\"author\":{\"name\":\"Testautor " + i + "\"}}";

    private static string Entries(int count) => Page(Enumerable.Range(1, count).Select(Entry).ToArray());

    private static string Page(params string[] entries) => "{\"success\":true,\"page\":0,\"total\":1,\"limit\":50,\"data\":[" + string.Join(",", entries) + "]}";

    /// <summary>Uhr, deren Delay blockiert, bis der Test es freigibt (Wettläufe des Abstands).</summary>
    private sealed class BlockingClock : IClock
    {
        private readonly object _gate = new();
        private readonly ManualResetEventSlim _release = new(false);
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow
        {
            get
            {
                lock (_gate)
                {
                    return _now;
                }
            }

            private set
            {
                lock (_gate)
                {
                    _now = value;
                }
            }
        }

        public List<TimeSpan> Delays { get; } = [];

        public ManualResetEventSlim DelayStarted { get; } = new(false);

        public void Advance(TimeSpan by) => UtcNow += by;

        public void Release() => _release.Set();

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            lock (Delays)
            {
                Delays.Add(delay);
            }

            DelayStarted.Set();
            _release.Wait(cancellationToken);
            UtcNow += delay;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Antwortinhalt, der hängt: ReadAsync endet erst beim Abbruch (oder, mit <c>honorToken: false</c> wie ein Socket, der das
/// Token nicht beachtet, erst nach Dispose), Read erst nach Dispose.
/// </summary>
public sealed class StalledStream(bool honorToken = true) : Stream
{
    private readonly ManualResetEventSlim _disposed = new(false);

    public ManualResetEventSlim Reading { get; } = new(false);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Reading.Set();
        _disposed.Wait();
        throw new ObjectDisposedException(nameof(StalledStream));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Reading.Set();
        if (honorToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        else
        {
            await Task.Run(() => _disposed.Wait(), CancellationToken.None);
            throw new ObjectDisposedException(nameof(StalledStream));
        }

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

/// <summary>Antwortinhalt ohne Längenangabe, der <c>length</c> Bytes liefert, auf Wunsch tröpfchenweise.</summary>
public sealed class TrickleStream(long length, TimeSpan? pause = null) : Stream
{
    private long _sent;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (pause is { } wait)
        {
            await Task.Delay(wait, cancellationToken);
        }

        var count = (int)Math.Min(Math.Min(buffer.Length, pause is null ? 65536 : 1), length - _sent);
        buffer.Span[..count].Fill(0x42);
        _sent += count;
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
