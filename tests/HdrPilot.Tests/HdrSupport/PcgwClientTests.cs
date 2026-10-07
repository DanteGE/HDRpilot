using System.Net;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.HdrSupport;

/// <summary>Client der PCGamingWiki-Schnittstelle gegen aufgezeichnete Antworten und eine Attrappe (nie das Internet).</summary>
public sealed class PcgwClientTests
{
    private const string UserAgent = "HdrPilot/1.0.0 (Windows desktop app; HDR list)";

    private readonly FakeClock _clock = new();

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Pcgw", name));

    private PcgwClient Client(Func<PcgwRequest, HttpResponseMessage> respond, out FakeHttpHandler handler)
    {
        handler = new FakeHttpHandler(respond);
        return new PcgwClient(handler, _clock, UserAgent, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Title_query_maps_every_requested_title_like_the_recorded_answer()
    {
        using var client = Client(_ => FakeHttpHandler.Json(Fixture("query-titles.json")), out var handler);
        string[] titles = ["Cyberpunk 2077", "Avatar Frontiers of Pandora", "Dying Light - The Beast", "Nonexistent Game XYZ 123", "Bad[Title", "palworld"];

        var result = client.QueryTitles(titles, CancellationToken.None);

        Assert.Equal(6, result.Count);
        var cyberpunk = result["Cyberpunk 2077"]!;
        Assert.Equal(HdrSupportStatus.Limited, cyberpunk.Status);
        Assert.True(cyberpunk.HasSteamAppId("1091500"));
        Assert.True(cyberpunk.HasGogId("1423049311"));
        var palworld = result["palworld"]!; // über "normalized"
        Assert.Equal("Palworld", palworld.Title);
        Assert.Equal(HdrSupportStatus.None, palworld.Status);
        Assert.True(palworld.HasSteamAppId("1623730"));
        Assert.Null(result["Avatar Frontiers of Pandora"]);
        Assert.Null(result["Dying Light - The Beast"]);
        Assert.Null(result["Nonexistent Game XYZ 123"]);
        Assert.Null(result["Bad[Title"]); // ungültig

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://www.pcgamingwiki.com/w/api.php", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("query", request.Action);
        Assert.Equal("json", request["format"]);
        Assert.Equal("2", request["formatversion"]);
        Assert.Equal("revisions", request["prop"]);
        Assert.Equal("content", request["rvprop"]);
        Assert.Equal("main", request["rvslots"]);
        Assert.Equal("1", request["redirects"]);
        Assert.Equal(string.Join('|', titles), request["titles"]);
        Assert.Equal(UserAgent, request.UserAgent);
    }

    [Fact]
    public void Redirects_are_followed_and_pages_without_content_count_as_unanswered()
    {
        const string json = """
            {"batchcomplete":true,"continue":{"rvcontinue":"1|2","continue":"||"},"query":{
              "normalized":[{"fromencoded":false,"from":"doom eternal","to":"Doom eternal"}],
              "redirects":[{"from":"Doom eternal","to":"Doom Eternal"}],
              "pages":[{"pageid":1,"ns":0,"title":"Doom Eternal","revisions":[{"slots":{"main":{"content":"{{Video\n|hdr = true\n}}"}}}]},
                       {"pageid":2,"ns":0,"title":"Sehr lange Seite"}]}}
            """;
        using var client = Client(_ => FakeHttpHandler.Json(json), out _);

        var result = client.QueryTitles(["doom eternal", "Sehr lange Seite"], CancellationToken.None);

        Assert.Equal("Doom Eternal", result["doom eternal"]!.Title);
        Assert.Equal(HdrSupportStatus.Native, result["doom eternal"]!.Status);
        Assert.False(result.ContainsKey("Sehr lange Seite")); // Rest käme erst mit der Fortsetzung
    }

    [Fact]
    public void At_most_fifty_titles_per_request()
    {
        using var client = Client(_ => FakeHttpHandler.Json("{}"), out var handler);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.QueryTitles([.. Enumerable.Range(0, 51).Select(i => "T" + i)], CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.QueryTitles([], CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Search_returns_the_first_hit_with_its_ids()
    {
        using var client = Client(_ => FakeHttpHandler.Json(Fixture("search-avatar.json")), out var handler);

        var page = client.SearchFirst("Avatar Frontiers of Pandora", CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal("Avatar: Frontiers of Pandora", page.Title);
        Assert.Equal(HdrSupportStatus.Native, page.Status);
        Assert.Equal(["2840770"], page.SteamAppIds);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("search", request["generator"]);
        Assert.Equal("Avatar Frontiers of Pandora", request["gsrsearch"]);
        Assert.Equal("1", request["gsrlimit"]);
        Assert.Equal("0", request["gsrnamespace"]);
    }

    [Fact]
    public void Search_without_hits_returns_null()
    {
        using var client = Client(_ => FakeHttpHandler.Json("""{"batchcomplete":true}"""), out _);
        Assert.Null(client.SearchFirst("Gibt es nicht", CancellationToken.None));
    }

    [Fact]
    public void Requests_are_at_least_two_seconds_apart()
    {
        using var client = Client(_ => FakeHttpHandler.Json("""{"batchcomplete":true}"""), out var handler);

        client.SearchFirst("a", CancellationToken.None);
        client.SearchFirst("b", CancellationToken.None);
        client.SearchFirst("c", CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)], _clock.Delays);
        Assert.Equal(3, client.RequestCount);
    }

    [Fact]
    public void Shared_pacer_spaces_requests_of_different_clients()
    {
        var pacer = new RequestPacer(_clock, PcgwClient.PauseBetweenRequests);
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"batchcomplete":true}"""));
        using (var first = new PcgwClient(handler, _clock, UserAgent, TimeSpan.FromSeconds(30), pacer))
        {
            first.SearchFirst("a", CancellationToken.None);
        }

        _clock.Advance(TimeSpan.FromMilliseconds(500));
        using var second = new PcgwClient(new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"batchcomplete":true}""")), _clock, UserAgent, TimeSpan.FromSeconds(30), pacer);
        second.SearchFirst("b", CancellationToken.None);

        Assert.Equal([TimeSpan.FromMilliseconds(1500)], _clock.Delays);
        Assert.True(handler.Disposed, "Handler gehört dem Client und wird mit ihm freigegeben");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, PcgwErrorKind.RateLimited, "http-429")]
    [InlineData(HttpStatusCode.Forbidden, PcgwErrorKind.HttpError, "http-403")]
    [InlineData(HttpStatusCode.ServiceUnavailable, PcgwErrorKind.HttpError, "http-503")]
    public void Http_errors_are_reported_by_kind(HttpStatusCode status, PcgwErrorKind kind, string code)
    {
        using var client = Client(_ => FakeHttpHandler.Status(status), out _);

        var ex = Assert.Throws<PcgwException>(() => client.QueryTitles(["A"], CancellationToken.None));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(code, ex.Code);
    }

    [Theory]
    [InlineData("ratelimited", PcgwErrorKind.RateLimited)]
    [InlineData("permissiondenied", PcgwErrorKind.PermissionDenied)]
    [InlineData("readapidenied", PcgwErrorKind.PermissionDenied)]
    [InlineData("maxlag", PcgwErrorKind.ApiError)]
    public void Api_error_codes_are_reported_by_kind(string code, PcgwErrorKind kind)
    {
        using var client = Client(_ => FakeHttpHandler.Json("{\"error\":{\"code\":\"" + code + "\",\"info\":\"x\"}}"), out _);

        var ex = Assert.Throws<PcgwException>(() => client.SearchFirst("A", CancellationToken.None));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Network_failures_and_broken_answers_are_reported()
    {
        using (var client = Client(_ => throw new HttpRequestException("Host nicht gefunden"), out _))
        {
            Assert.Equal(PcgwErrorKind.Network, Assert.Throws<PcgwException>(() => client.SearchFirst("A", CancellationToken.None)).Kind);
        }

        using (var client = Client(_ => throw new TaskCanceledException("Zeitüberschreitung"), out _))
        {
            Assert.Equal(PcgwErrorKind.Network, Assert.Throws<PcgwException>(() => client.SearchFirst("A", CancellationToken.None)).Kind);
        }

        using (var client = Client(_ => FakeHttpHandler.Json("<html>Wartung</html>"), out _))
        {
            Assert.Equal(PcgwErrorKind.InvalidResponse, Assert.Throws<PcgwException>(() => client.SearchFirst("A", CancellationToken.None)).Kind);
        }
    }

    [Fact]
    public void Own_cancellation_is_not_reported_as_network_error()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = Client(_ => FakeHttpHandler.Json("{}"), out _);

        Assert.ThrowsAny<OperationCanceledException>(() => client.SearchFirst("A", cts.Token));
    }

    [Fact]
    public void Anonymous_cargo_query_is_permission_denied_like_the_recorded_answer()
    {
        using var client = Client(_ => FakeHttpHandler.Json(Fixture("cargo-permissiondenied.json")), out var handler);

        var ex = Assert.Throws<PcgwException>(() => client.QueryCatalogPage(0, 500, CancellationToken.None));

        Assert.Equal(PcgwErrorKind.PermissionDenied, ex.Kind);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("cargoquery", request.Action);
        Assert.Equal("Game,Video", request["tables"]);
        Assert.Equal("Game._pageID=Video._pageID", request["join_on"]);
        Assert.Equal("Game._pageName=Page,Game.Steam_AppID=Steam,Game.GOGcom_ID=GOG,Video.HDR=HDR", request["fields"]);
        Assert.Equal("Video.HDR IN (\"true\",\"limited\",\"always on\",\"hackable\")", request["where"]);
        Assert.Equal("500", request["limit"]);
        Assert.Equal("0", request["offset"]);
    }

    [Fact]
    public void Cargo_rows_are_decoded()
    {
        const string json = """
            {"limits":{"cargoquery":500},"cargoquery":[
              {"title":{"Page":"Ratchet &amp; Clank: Rift Apart","Steam":"1895880","GOG":"","HDR":"true"}},
              {"title":{"Page":"Baldur&#039;s Gate 3","Steam":"1086940","GOG":"1456460669,1207658924","HDR":"limited"}},
              {"title":{"Page":"Mod-Spiel","Steam":null,"GOG":null,"HDR":"hackable"}},
              {"title":{"Page":"","Steam":"1","GOG":"","HDR":"true"}}]}
            """;
        using var client = Client(_ => FakeHttpHandler.Json(json), out _);

        var rows = client.QueryCatalogPage(500, 500, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal("Ratchet & Clank: Rift Apart", rows[0].Page);
        Assert.Equal(HdrSupportStatus.Native, rows[0].Status);
        Assert.Equal(["1895880"], rows[0].Steam);
        Assert.Null(rows[0].Gog);
        Assert.Equal("Baldur's Gate 3", rows[1].Page);
        Assert.Equal(["1456460669", "1207658924"], rows[1].Gog);
        Assert.Equal(HdrSupportStatus.ModOnly, rows[2].Status);
    }

    [Fact]
    public void Login_fetches_a_token_and_posts_the_bot_password_in_the_body_only()
    {
        var wiki = new FakeWiki();
        wiki.Accounts["Dante_GE@HdrPilot"] = "abc123geheim";
        var handler = wiki.CreateHandler();
        using var client = new PcgwClient(handler, _clock, UserAgent, TimeSpan.FromSeconds(30));

        client.Login("Dante_GE@HdrPilot", "abc123geheim", CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var token = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, token.Method);
        Assert.Equal("tokens", token["meta"]);
        Assert.Equal("login", token["type"]);
        var login = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, login.Method);
        Assert.Equal("login", login.Action);
        Assert.Equal("Dante_GE@HdrPilot", login["lgname"]);
        Assert.Equal("abc123geheim", login["lgpassword"]);
        Assert.Equal("abc123+\\", login["lgtoken"]);
        Assert.DoesNotContain("abc123geheim", login.Uri.ToString(), StringComparison.Ordinal); // nie in der Adresse
    }

    [Fact]
    public void Recorded_login_token_is_read()
    {
        var responses = new Queue<HttpResponseMessage>([
            FakeHttpHandler.Json(Fixture("login-token.json")),
            FakeHttpHandler.Json("""{"login":{"result":"Success","lguserid":1,"lgusername":"Dante_GE"}}"""),
        ]);
        using var client = Client(_ => responses.Dequeue(), out var handler);

        client.Login("Dante_GE@HdrPilot", "x", CancellationToken.None);

        Assert.Equal("353f926d0db280d4e954aa67e30fdab76ac66e26+\\", handler.Requests[1]["lgtoken"]);
    }

    [Theory]
    [InlineData("Failed", PcgwErrorKind.LoginFailed)]
    [InlineData("WrongToken", PcgwErrorKind.LoginFailed)]
    [InlineData("Aborted", PcgwErrorKind.LoginFailed)]
    [InlineData("Throttled", PcgwErrorKind.LoginThrottled)]
    public void Rejected_logins_are_reported_without_the_password(string result, PcgwErrorKind kind)
    {
        var responses = new Queue<HttpResponseMessage>([
            FakeHttpHandler.Json("""{"batchcomplete":true,"query":{"tokens":{"logintoken":"t+\\"}}}"""),
            FakeHttpHandler.Json("{\"login\":{\"result\":\"" + result + "\",\"reason\":\"Incorrect username or password entered.\"}}"),
        ]);
        using var client = Client(_ => responses.Dequeue(), out _);

        var ex = Assert.Throws<PcgwException>(() => client.Login("Dante_GE@HdrPilot", "sehr-geheim", CancellationToken.None));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(result, ex.Code);
        Assert.DoesNotContain("sehr-geheim", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_without_token_is_an_invalid_answer()
    {
        using var client = Client(_ => FakeHttpHandler.Json("""{"batchcomplete":true}"""), out var handler);

        Assert.Equal(PcgwErrorKind.InvalidResponse, Assert.Throws<PcgwException>(() => client.Login("a@b", "c", CancellationToken.None)).Kind);
        Assert.Single(handler.Requests); // kein Passwort gesendet
    }
}
