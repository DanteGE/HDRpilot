using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// Testdaten-Lieferant für SteamGridDB (E93, G14.2): nur mit --config-dir und der Variablen, ohne inneren Handler und ohne
/// Socket, dokumentierte Antworten mit erzeugten Bildern und erfundenen Autoren, Protokoll ohne Schlüssel.
/// </summary>
public sealed class SteamGridDbFixtureTests
{
    private const string Key = "HDRPILOT-FIXTURE-0001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(@"C:\Pruefung", "fixture", true)]
    [InlineData(@"C:\Pruefung", null, false)]
    [InlineData(null, "fixture", false)]
    [InlineData(null, null, false)]
    [InlineData("", "fixture", false)]
    [InlineData(@"C:\Pruefung", "Fixture", false)]
    [InlineData(@"C:\Pruefung", " fixture", false)]
    [InlineData(@"C:\Pruefung", "1", false)]
    public void Fixture_is_used_only_with_config_dir_and_the_variable(string? configDir, string? value, bool used)
    {
        var asked = new List<string>();
        var factory = SteamGridDbFixture.HandlerFactoryFor(configDir, name =>
        {
            asked.Add(name);
            return value;
        }, null);

        Assert.Equal(used, factory is not null);
        if (factory is not null)
        {
            using var handler = factory();
            Assert.IsType<SteamGridDbFixtureHandler>(handler);
        }

        Assert.All(asked, name => Assert.Equal(AppServices.TestSteamGridDbVariable, name));
        Assert.Equal("HDRPILOT_TEST_STEAMGRIDDB", AppServices.TestSteamGridDbVariable);
    }

    [Fact]
    public void Fixture_answers_the_documented_endpoints_and_404_for_others()
    {
        using var client = Client();

        var hits = client.SearchGames("Nebelfahrt", Ct);
        Assert.Equal([9101L, 9102L, 9103L], hits.Select(h => h.Id));
        Assert.Equal(new SteamGridDbGameHit(9101, "Nebelfahrt", 2019, true), hits[0]);
        Assert.Equal(("Nebelfahrt: Neuauflage", 2023), (hits[1].Name, hits[1].ReleaseYear));
        Assert.Equal(("Nebelfahrt II", (int?)null), (hits[2].Name, hits[2].ReleaseYear));
        Assert.Equal(4, client.SearchGames("Doppelt", Ct).Count);
        Assert.Equal(9101, client.GameById(9101, Ct)!.Id);
        Assert.Null(client.GameById(9105, Ct));
        Assert.True(client.Assets(SteamGridDbAssetKind.Hero, SteamGridDbTarget.Steam("9990150"), 0, Ct).HasMore);
        Assert.True(client.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Steam(SteamGridDbFixture.UnknownSteamAppId), 0, Ct).GameUnknown);
        Assert.True(client.Assets(SteamGridDbAssetKind.Logo, SteamGridDbTarget.Game(9105), 0, Ct).GameUnknown);
        var none = client.Assets(SteamGridDbAssetKind.Logo, SteamGridDbTarget.Game(9103), 0, Ct);
        Assert.False(none.GameUnknown);
        Assert.Empty(none.Items);

        using var invoker = new HttpMessageInvoker(new SteamGridDbFixtureHandler(null));
        foreach (var url in new[] { "https://example.org/", "https://www.steamgriddb.com/", "https://www.steamgriddb.com/api/v2/unbekannt", "https://cdn2.steamgriddb.com/cover/fixture-99.png", "https://cdn2.steamgriddb.com/thumb/x.png", "http://cdn2.steamgriddb.com/cover/fixture-1.png" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Key);
            using var response = invoker.Send(request, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Headers.Location); // nie eine Weiterleitung
        }

        // Ohne Schlüssel antwortet die Schnittstelle nicht.
        using var anonymous = invoker.Send(new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/search/autocomplete/Nebelfahrt"), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public void Fixture_has_no_inner_handler_and_opens_no_socket()
    {
        var type = typeof(SteamGridDbFixtureHandler);

        Assert.False(typeof(DelegatingHandler).IsAssignableFrom(type));
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.DoesNotContain(fields, f => typeof(HttpMessageHandler).IsAssignableFrom(f.FieldType) || typeof(HttpClient).IsAssignableFrom(f.FieldType) || typeof(Socket).IsAssignableFrom(f.FieldType));

        // Verhalten: ein fremder Rechner bekommt 404 statt einer Verbindung.
        using var invoker = new HttpMessageInvoker(new SteamGridDbFixtureHandler(null));
        using var response = invoker.Send(new HttpRequestMessage(HttpMethod.Get, "https://192.0.2.1/"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fixture_handler_overrides_the_synchronous_send()
    {
        var send = typeof(SteamGridDbFixtureHandler).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(HttpRequestMessage), typeof(CancellationToken)]);

        Assert.Equal(typeof(SteamGridDbFixtureHandler), send!.DeclaringType);
        using var invoker = new HttpMessageInvoker(new SteamGridDbFixtureHandler(null));
        using var sync = invoker.Send(new HttpRequestMessage(HttpMethod.Get, "https://cdn2.steamgriddb.com/thumb/fixture-cover-1.png"), Ct);
        using var later = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://cdn2.steamgriddb.com/thumb/fixture-cover-1.png"), Ct);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
    }

    [Theory]
    [InlineData("cover", true, 200, 300)]
    [InlineData("cover", false, 600, 900)]
    [InlineData("hero", true, 400, 129)]
    [InlineData("hero", false, 1920, 620)]
    [InlineData("logo", true, 300, 116)]
    [InlineData("logo", false, 800, 310)]
    public async Task Fixture_images_are_generated_pngs_of_the_documented_sizes(string kind, bool thumb, int width, int height)
    {
        using var invoker = new HttpMessageInvoker(new SteamGridDbFixtureHandler(null));
        var url = thumb ? "https://cdn2.steamgriddb.com/thumb/fixture-" + kind + "-4.png" : "https://cdn2.steamgriddb.com/" + kind + "/fixture-4.png";
        using var response = invoker.Send(new HttpRequestMessage(HttpMethod.Get, url), Ct);
        var data = await response.Content.ReadAsByteArrayAsync(Ct);

        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(data.Length, response.Content.Headers.ContentLength);
        Assert.True(ImageHeader.TryGetSize(data, out var w, out var h));
        Assert.Equal((width, height), (w, h));
        var pixels = ArtworkImaging.ToBgra(ArtworkImaging.Decode(data, width));
        Assert.Equal((width, height), (pixels.Width, pixels.Height));
        var corner = pixels.Pixels[3];
        var middle = pixels.Pixels[(((height / 2) * width) + (width / 2)) * 4 + 3];
        Assert.Equal(255, middle);
        Assert.Equal(kind == "logo" ? 0 : 255, corner); // Logos sind außen durchsichtig
        Assert.Same(SteamGridDbFixture.Image(kind, thumb, 4), SteamGridDbFixture.Image(kind, thumb, 4)); // gemerkt
        Assert.NotEqual(SteamGridDbFixture.Image(kind, thumb, 4), SteamGridDbFixture.Image(kind, thumb, 5));
    }

    [Fact]
    public void Fixture_log_names_host_path_and_key_presence_but_never_the_key_and_survives_parallel_writes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hdrpilot-sgdb-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var log = Path.Combine(folder, "sgdb-fixture.log");
            using var client = new SteamGridDbClient(new SteamGridDbFixtureHandler(log), new FakeClock(), Key, "HDRpilot/Test", pacer: new HdrPilot.Core.HdrSupport.RequestPacer(new FakeClock(), TimeSpan.Zero));
            var asset = new SteamGridDbAsset(1, "https://cdn2.steamgriddb.com/cover/fixture-1.png", 600, 900, "image/png", "https://cdn2.steamgriddb.com/thumb/fixture-cover-1.png");

            client.SearchGames("Nebelfahrt", Ct);
            Parallel.For(0, 40, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ => client.DownloadPreview(asset, CancellationToken.None));
            client.Download(asset, SteamGridDbClient.MaxPickedImageBytes, paced: false, Ct);

            var lines = File.ReadAllLines(log);
            Assert.Equal(42, lines.Length);
            Assert.Equal("GET www.steamgriddb.com /api/v2/search/autocomplete/Nebelfahrt schluessel=ja", lines[0]);
            Assert.Equal(40, lines.Count(l => l == "GET cdn2.steamgriddb.com /thumb/fixture-cover-1.png schluessel=nein"));
            Assert.Equal("GET cdn2.steamgriddb.com /cover/fixture-1.png schluessel=nein", lines[^1]);
            Assert.All(lines, l => Assert.Matches(new Regex("^GET [a-z0-9.]+ /\\S* schluessel=(ja|nein)$"), l));
            Assert.DoesNotContain(Key, File.ReadAllText(log), StringComparison.Ordinal);
            Assert.DoesNotContain("Testautor", File.ReadAllText(log), StringComparison.Ordinal);

            // Ein Schreibfehler scheitert die Anfrage nie.
            using var blocked = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.NotEmpty(client.DownloadPreview(asset, Ct));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Fixture_rejects_the_rejected_keys_and_limits_stalls_or_drops_the_marker_terms()
    {
        using (var rejected = Client(SteamGridDbFixture.RejectedKey))
        {
            Assert.False(rejected.CheckKey(Ct));
        }

        using (var later = Client(SteamGridDbFixture.LaterRejectedKey))
        {
            Assert.True(later.CheckKey(Ct));
            Assert.Equal(SteamGridDbErrorKind.Unauthorized, Assert.Throws<SteamGridDbException>(() => later.Assets(SteamGridDbAssetKind.Grid, SteamGridDbTarget.Game(9101), 0, Ct)).Kind);
        }

        var timeouts = new SteamGridDbTimeouts(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        using var client = Client(timeouts: timeouts);
        Assert.Empty(client.SearchGames("Nebelfahrt OhneTreffer", Ct));
        Assert.Equal(SteamGridDbErrorKind.RateLimited, Assert.Throws<SteamGridDbException>(() => client.SearchGames("Zuviel", Ct)).Kind);
        Assert.Equal(SteamGridDbErrorKind.Network, Assert.Throws<SteamGridDbException>(() => client.SearchGames("Offline", Ct)).Kind);
        var slow = Assert.Throws<SteamGridDbException>(() => client.SearchGames("Langsam", Ct));
        Assert.Equal(SteamGridDbErrorKind.Network, slow.Kind);
        Assert.True(slow.IsTimeout);
    }

    [Fact]
    public void Fixture_pages_hold_48_then_22_valid_entries_then_an_empty_page()
    {
        using var client = Client();
        var target = SteamGridDbTarget.Game(9101);

        var first = client.Assets(SteamGridDbAssetKind.Hero, target, 0, Ct);
        var second = client.Assets(SteamGridDbAssetKind.Hero, target, 1, Ct);
        var third = client.Assets(SteamGridDbAssetKind.Hero, target, 2, Ct);

        Assert.Equal((50, 48, 2), (first.RawCount, first.Items.Count, first.Skipped));
        Assert.Equal(22, second.Items.Count);
        Assert.True(second.HasMore);
        Assert.Empty(third.Items);
        Assert.False(third.HasMore);
        Assert.Equal(1920, first.Items[0].Width);
        Assert.Equal("Testautor 01", first.Items[0].Author);
        Assert.Null(first.Items.Single(a => a.Id == 100005).Author);
        Assert.Equal(48, first.Items.Single(a => a.Id == 100003).Author!.Length);
        Assert.Null(first.Items.Single(a => a.Id == 100010).ThumbUrl);
        Assert.Equal("https://cdn2.steamgriddb.com/thumb/fixture-hero-1.png", first.Items[0].ThumbUrl);
        Assert.Equal("https://cdn2.steamgriddb.com/hero/fixture-1.png", first.Items[0].Url);
        Assert.Equal("Testautor 72", second.Items[^1].Author);
        Assert.Equal(new[] { 600, 900 }, new[] { client.Assets(SteamGridDbAssetKind.Grid, target, 0, Ct).Items[0].Width, client.Assets(SteamGridDbAssetKind.Grid, target, 0, Ct).Items[0].Height });
    }

    private static SteamGridDbClient Client(string key = Key, SteamGridDbTimeouts? timeouts = null) =>
        new(new SteamGridDbFixtureHandler(null), new FakeClock(), key, "HDRpilot/Test", pacer: new HdrPilot.Core.HdrSupport.RequestPacer(new FakeClock(), TimeSpan.Zero), timeouts: timeouts);
}
