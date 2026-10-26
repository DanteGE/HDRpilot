using System.Net;
using System.Net.Http;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// Suche in „Bild wählen…“ als Dienst und Sitzung (E93, G14.2), nur mit Attrappen: ohne Schlüssel keine Sitzung und kein
/// Handler, mit Schlüssel kein Handler vor der ersten Anfrage, Suche auch bei ausgeschaltetem Schalter, Zuordnung, geteilte
/// Auflösung, ein Abstand für alles, höchstens drei Vorschauen, Grenzen, 429-Pause, Abbruch, Status, Protokoll ohne Fremdtext.
/// </summary>
public sealed class SteamGridDbSearchSessionTests
{
    private readonly SgdbRig _rig = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Without_a_key_there_is_no_session_and_no_handler()
    {
        using var service = _rig.Service();

        Assert.Null(service.OpenSearch(SgdbRig.Steam("553850")));
        Assert.Null(service.OpenSearch(SgdbRig.Manual()));
        Assert.Equal(SteamGridDbKeyState.Missing, service.KeyState);
        Assert.Equal(0, service.HandlersCreated);
        Assert.Null(((ISteamGridDbService)NullSteamGridDbService.Instance).OpenSearch(SgdbRig.Manual()));
        Assert.Equal(SteamGridDbKeyState.Missing, ((ISteamGridDbService)NullSteamGridDbService.Instance).KeyState);
    }

    [Fact]
    public async Task Opening_a_session_creates_no_handler_until_the_first_request()
    {
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);

        using var session = service.OpenSearch(SgdbRig.Steam("553850"))!;
        Assert.Equal(0, service.HandlersCreated);
        Assert.Equal(SteamGridDbMatchKind.SteamAppId, session.InitialMatch!.Kind);
        await session.ResolveAsync(Ct); // Steam: ohne Anfrage
        Assert.Equal(0, service.HandlersCreated);

        await session.GetAssetsAsync(session.InitialMatch, ArtworkKind.Cover, 0, Ct);

        Assert.Equal(1, service.HandlersCreated);
        Assert.Single(_rig.Calls);
    }

    [Fact]
    public async Task Search_works_with_the_switch_off_and_the_automatic_run_still_does_not()
    {
        var game = SgdbRig.Steam("553850");
        _rig.Config.Games.Add(game);
        _rig.Artwork.Missing[game.Key] = [ArtworkKind.Hero];
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        Assert.False(_rig.Config.Settings.Artwork.SteamGridDb);

        await service.RunNowAsync(Ct);
        Assert.Equal(0, service.HandlersCreated); // aus: kein automatischer Netzwerkzugriff

        using var session = service.OpenSearch(game)!;
        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Hero, 0, Ct);

        Assert.Equal(30, page.Items.Count);
        Assert.Empty(_rig.Artwork.Stored);
        Assert.All(_rig.Calls, c => Assert.Contains("/heroes/steam/553850?", c.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Key_state_reads_no_status_and_counts_nothing()
    {
        _rig.Config.Games.Add(SgdbRig.Steam("1"));
        _rig.Artwork.Missing["Steam:1"] = [ArtworkKind.Hero];
        using (var writer = _rig.Service())
        {
            writer.SaveKey(SgdbRig.Key);
        }

        using var service = _rig.Service();
        Assert.Equal(SteamGridDbKeyState.Stored, service.KeyState);
        await Task.Delay(150, Ct);

        Assert.Equal(0, _rig.Artwork.MissingCalls); // kein Zählen fehlender Bilder im Hintergrund
        Assert.Equal(0, service.HandlersCreated);
    }

    [Fact]
    public async Task Steam_games_match_by_app_id_without_a_search()
    {
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Steam("553850", "Kupferwald"))!;

        var resolved = await session.ResolveAsync(Ct);
        await session.GetAssetsAsync(resolved.Match!, ArtworkKind.Logo, 0, Ct);

        Assert.True(session.IsSteamGame);
        Assert.Equal(SteamGridDbMatchKind.SteamAppId, resolved.Match!.Kind);
        Assert.Equal("Kupferwald", resolved.Match.DisplayName);
        Assert.Equal(SteamGridDbTarget.Steam("553850"), resolved.Match.Target);
        Assert.DoesNotContain(_rig.Calls, c => c.Uri.Contains("/search/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remembered_ids_ask_once_for_name_and_year_after_the_first_page()
    {
        var game = SgdbRig.Manual("Kupferwald");
        _rig.Artwork.SetSteamGridDbGameId(game, 9101);
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(game)!;

        Assert.Equal(SteamGridDbMatchKind.Remembered, session.InitialMatch!.Kind);
        Assert.Equal("Kupferwald", session.InitialMatch.DisplayName); // vor dem Netz: Name in der Bibliothek
        Assert.Empty(_rig.Calls);

        await session.GetAssetsAsync(session.InitialMatch, ArtworkKind.Cover, 0, Ct);
        var described = await session.DescribeAsync(9101, Ct);

        Assert.Equal(new SteamGridDbGameHit(9101, "Kupferwald (SteamGridDB)", 2019, false), described);
        Assert.Equal(2, _rig.Calls.Count);
        Assert.Contains("/grids/game/9101?", _rig.Calls[0].Uri, StringComparison.Ordinal);
        Assert.EndsWith("/games/id/9101", _rig.Calls[1].Uri, StringComparison.Ordinal);
        Assert.False(session.IsSteamGame);
    }

    [Fact]
    public async Task Other_games_use_an_exact_hit_or_offer_the_hits_and_flag_several_exact_hits()
    {
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);

        using (var exact = service.OpenSearch(SgdbRig.Manual("Nebelfahrt"))!)
        {
            Assert.Null(exact.InitialMatch);
            var result = await exact.ResolveAsync(Ct);
            Assert.Equal(new SteamGridDbMatch(SteamGridDbMatchKind.ExactName, SteamGridDbTarget.Game(9101), "Nebelfahrt", 2019, true), result.Match);
            Assert.Equal(2, result.Hits.Count);
        }

        _rig.SearchHits = "[{\"id\":9101,\"name\":\"Nebelfahrt II\"},{\"id\":9102,\"name\":\"Nebelfahrt: Neuauflage\",\"release_date\":1672531200}]";
        using (var none = service.OpenSearch(SgdbRig.Manual("Nebelfahrt"))!)
        {
            var result = await none.ResolveAsync(Ct);
            Assert.Null(result.Match);
            Assert.Equal([9101L, 9102L], result.Hits.Select(h => h.Id));
            Assert.Equal(2023, result.Hits[1].ReleaseYear);
        }

        _rig.SearchHits = "[{\"id\":9104,\"name\":\"Nebelfahrt\",\"release_date\":1104537600},{\"id\":9101,\"name\":\"Nebelfahrt\",\"release_date\":1561939200}]";
        using (var twice = service.OpenSearch(SgdbRig.Manual("Nebelfahrt"))!)
        {
            var result = await twice.ResolveAsync(Ct);
            Assert.Equal(9104, result.Match!.Target.GameId); // der erste
            Assert.True(result.Match.Ambiguous);
            Assert.Equal(2, result.Hits.Count); // für die Spielwahl behalten
        }

        _rig.SearchHits = "[]";
        using var empty = service.OpenSearch(SgdbRig.Manual("Nebelfahrt"))!;
        var nothing = await empty.ResolveAsync(Ct);
        Assert.Null(nothing.Match);
        Assert.Empty(nothing.Hits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_steam_app_or_a_stale_id_falls_back_to_one_name_search(bool steam)
    {
        var game = steam ? SgdbRig.Steam("9990404", "Nebelfahrt") : SgdbRig.Manual("Nebelfahrt");
        if (!steam)
        {
            _rig.Artwork.SetSteamGridDbGameId(game, 9999);
        }

        _rig.Status = r => r.RequestUri!.AbsolutePath.Contains("/steam/9990404", StringComparison.Ordinal) || r.RequestUri.AbsolutePath.Contains("/game/9999", StringComparison.Ordinal)
            ? HttpStatusCode.NotFound
            : null;
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(game)!;

        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Hero, 0, Ct);
        Assert.True(page.GameUnknown);
        var fallback = await session.ResolveByNameAsync(Ct);

        Assert.Equal(SteamGridDbMatchKind.ExactName, fallback.Match!.Kind);
        Assert.Equal(9101, fallback.Match.Target.GameId);
        Assert.Equal(steam, fallback.SteamAppUnknown);
        Assert.Equal(2, _rig.Calls.Count);
        Assert.Contains("/search/autocomplete/Nebelfahrt", _rig.Calls[1].Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_tabs_resolving_at_once_share_one_request_and_a_failed_resolve_is_not_kept()
    {
        var fail = true;
        _rig.Status = r => fail && r.RequestUri!.AbsolutePath.Contains("/search/", StringComparison.Ordinal) ? HttpStatusCode.InternalServerError : null;
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Manual("Nebelfahrt"))!;

        await Assert.ThrowsAsync<SteamGridDbException>(() => session.ResolveAsync(Ct));
        fail = false;
        _rig.LastHandler!.Hold = r => r.RequestUri!.AbsolutePath.Contains("/search/", StringComparison.Ordinal);

        // Zweiter Versuch: Die gescheiterte Auflösung gilt nicht mehr, und zwei Reiter teilen eine Anfrage.
        using var otherTab = new CancellationTokenSource();
        var first = session.ResolveAsync(Ct);
        var second = session.ResolveAsync(otherTab.Token);
        Assert.True(await _rig.LastHandler!.Held.WaitAsync(5000, Ct));
        otherTab.Cancel(); // beendet nur das Warten dieses Reiters
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        _rig.LastHandler.Release.Set();

        var result = await first;
        Assert.Equal(9101, result.Match!.Target.GameId);
        Assert.Equal(2, _rig.Calls.Count(c => c.Uri.Contains("/search/", StringComparison.Ordinal)));
        Assert.Same(result, await session.ResolveAsync(Ct)); // gemerkt, keine dritte Anfrage
        Assert.Equal(2, _rig.Calls.Count);
    }

    [Fact]
    public async Task Api_requests_keep_one_second_across_the_automatic_run_the_key_check_and_sessions()
    {
        for (var i = 1; i <= 3; i++)
        {
            var game = SgdbRig.Steam(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _rig.Config.Games.Add(game);
            _rig.Artwork.Missing[game.Key] = [ArtworkKind.Hero];
        }

        _rig.PageSizes[0] = 0; // keine Bilder: der Lauf fragt nur die Schnittstelle
        using var service = _rig.Service(pause: TimeSpan.FromSeconds(1));
        service.SaveKey(SgdbRig.Key);
        using var a = service.OpenSearch(SgdbRig.Steam("10"))!;
        using var b = service.OpenSearch(SgdbRig.Steam("11"))!;

        _rig.Config.Settings.Artwork.SteamGridDb = true;
        await Task.WhenAll(
            Task.Run(() => service.RunNowAsync(Ct), Ct),
            Task.Run(() => service.CheckKeyAsync(SgdbRig.Key, Ct), Ct),
            Task.Run(() => a.GetAssetsAsync(a.InitialMatch!, ArtworkKind.Cover, 0, Ct), Ct),
            Task.Run(() => b.GetAssetsAsync(b.InitialMatch!, ArtworkKind.Logo, 0, Ct), Ct),
            Task.Run(() => a.GetAssetsAsync(a.InitialMatch!, ArtworkKind.Hero, 0, Ct), Ct));

        List<DateTimeOffset> times;
        lock (_rig.ApiTimes)
        {
            times = [.. _rig.ApiTimes.Order()];
        }

        Assert.Equal(7, times.Count); // 3 aus dem Lauf, 1 Prüfung, 3 aus zwei Sitzungen
        for (var i = 1; i < times.Count; i++)
        {
            Assert.True(times[i] - times[i - 1] >= TimeSpan.FromSeconds(1), "Abstand " + (times[i] - times[i - 1]));
        }
    }

    [Fact]
    public async Task At_most_three_previews_load_at_once_without_the_one_second_gap()
    {
        using var service = _rig.Service(pause: TimeSpan.FromSeconds(1));
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Steam("553850"))!;
        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);
        _rig.LastHandler!.Hold = r => r.RequestUri!.Host != "www.steamgriddb.com";

        var previews = page.Items.Take(10).Select(asset => session.LoadPreviewAsync(asset, 130, Ct)).ToList();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await _rig.LastHandler.Held.WaitAsync(5000, Ct));
        }

        await Task.Delay(200, Ct);
        Assert.Equal(3, _rig.LastHandler.MaxParallelImages);
        Assert.True(((SteamGridDbSearchSession)session).ActiveWorkers <= 3);
        _rig.LastHandler.Release.Set();
        var loaded = await Task.WhenAll(previews);

        Assert.All(loaded, p => Assert.True(p.Image.IsFrozen));
        Assert.All(loaded, p => Assert.Equal(4, p.DecodedBytes)); // 1 × 1 × 4
        Assert.Empty(_rig.Clock.Delays); // Vorschauen ohne Abstand
        Assert.All(_rig.Calls.Where(c => !c.IsApi), c => Assert.Null(c.Authorization));
        Assert.Equal(3, _rig.LastHandler.MaxParallelImages);
    }

    [Fact]
    public async Task A_cancelled_queue_token_drops_waiting_previews_but_running_ones_finish()
    {
        using var service = _rig.Service(SteamGridDbSearchLimits.Default with { MaxParallelPreviews = 1, PreviewIdleExit = TimeSpan.FromMilliseconds(200) });
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Steam("553850"))!;
        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);
        _rig.LastHandler!.Hold = r => r.RequestUri!.Host != "www.steamgriddb.com";
        using var tab = new CancellationTokenSource();

        var running = session.LoadPreviewAsync(page.Items[0], 130, tab.Token);
        var waiting1 = session.LoadPreviewAsync(page.Items[1], 130, tab.Token);
        var waiting2 = session.LoadPreviewAsync(page.Items[2], 130, tab.Token);
        Assert.True(await _rig.LastHandler.Held.WaitAsync(5000, Ct));
        tab.Cancel(); // Reiterwechsel
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting2);
        _rig.LastHandler.Release.Set();

        Assert.NotNull((await running).Image); // die laufende endet mit ihrem Ergebnis
        Assert.Single(_rig.Calls, c => !c.IsApi);
    }

    [Fact]
    public async Task Session_limits_stop_api_requests_images_bytes_and_downloads()
    {
        var limits = SteamGridDbSearchLimits.Default with { MaxApiRequests = 2, MaxImageRequests = 2, MaxFullDownloads = 1, PreviewIdleExit = TimeSpan.FromMilliseconds(200) };
        using var service = _rig.Service(limits);
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Steam("553850"))!;
        var match = session.InitialMatch!;

        var page = await session.GetAssetsAsync(match, ArtworkKind.Cover, 0, Ct);
        await session.GetAssetsAsync(match, ArtworkKind.Cover, 1, Ct);
        var api = await Assert.ThrowsAsync<SteamGridDbException>(() => session.GetAssetsAsync(match, ArtworkKind.Cover, 2, Ct));
        await session.LoadPreviewAsync(page.Items[0], 130, Ct);
        await session.DownloadAsync(page.Items[1], ArtworkKind.Cover, Ct);
        var images = await Assert.ThrowsAsync<SteamGridDbException>(() => session.LoadPreviewAsync(page.Items[2], 130, Ct));
        var downloads = await Assert.ThrowsAsync<SteamGridDbException>(() => session.DownloadAsync(page.Items[3], ArtworkKind.Cover, Ct));

        Assert.Equal(SteamGridDbErrorKind.RequestLimit, api.Kind);
        Assert.Equal(SteamGridDbErrorKind.RequestLimit, images.Kind);
        Assert.Equal(SteamGridDbErrorKind.RequestLimit, downloads.Kind);
        Assert.Equal(new SteamGridDbSessionUsage(2, 2, SgdbRig.Png.Length, 1), session.Usage);
        Assert.Equal(4, _rig.Calls.Count);

        // Bytes der Vorschauen: Die zweite überschritte die Menge.
        using var bytes = _rig.Service(SteamGridDbSearchLimits.Default with { MaxPreviewBytes = SgdbRig.Png.Length + 10 });
        bytes.SaveKey(SgdbRig.Key);
        using var small = bytes.OpenSearch(SgdbRig.Steam("553850"))!;
        var items = (await small.GetAssetsAsync(small.InitialMatch!, ArtworkKind.Cover, 0, Ct)).Items;
        await small.LoadPreviewAsync(items[0], 130, Ct);
        Assert.Equal(SteamGridDbErrorKind.RequestLimit, (await Assert.ThrowsAsync<SteamGridDbException>(() => small.LoadPreviewAsync(items[1], 130, Ct))).Kind);
    }

    [Fact]
    public async Task Rate_limit_blocks_the_session_for_a_minute_without_network()
    {
        var limited = true;
        _rig.Status = r => limited ? HttpStatusCode.TooManyRequests : null;
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Steam("553850"))!;
        var match = session.InitialMatch!;

        Assert.Equal(SteamGridDbErrorKind.RateLimited, (await Assert.ThrowsAsync<SteamGridDbException>(() => session.GetAssetsAsync(match, ArtworkKind.Hero, 0, Ct))).Kind);
        Assert.Equal(_rig.Clock.UtcNow + TimeSpan.FromSeconds(60), session.BlockedUntil);
        limited = false;

        Assert.Equal(SteamGridDbErrorKind.RateLimited, (await Assert.ThrowsAsync<SteamGridDbException>(() => session.GetAssetsAsync(match, ArtworkKind.Hero, 0, Ct))).Kind);
        var asset = new SteamGridDbAsset(1, "https://cdn2.steamgriddb.com/grid/1.png", 1, 1, null, "https://cdn2.steamgriddb.com/thumb/1.png");
        Assert.Equal(SteamGridDbErrorKind.RateLimited, (await Assert.ThrowsAsync<SteamGridDbException>(() => session.LoadPreviewAsync(asset, 100, Ct))).Kind);
        Assert.Single(_rig.Calls); // während der Pause kein Netz

        _rig.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(session.BlockedUntil);
        Assert.Equal(30, (await session.GetAssetsAsync(match, ArtworkKind.Hero, 0, Ct)).Items.Count);
    }

    [Fact]
    public async Task Removing_the_key_cancels_open_sessions_at_once()
    {
        _rig.Hold = r => r.RequestUri!.Host == "www.steamgriddb.com";
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        var session = service.OpenSearch(SgdbRig.Steam("553850"))!;

        var pending = session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);
        Assert.True(await _rig.LastHandler!.Held.WaitAsync(5000, Ct));
        service.RemoveKey();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        Assert.True(_rig.LastHandler.Disposed);
        Assert.Equal(SteamGridDbKeyState.Missing, service.KeyState);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct));
    }

    [Fact]
    public async Task Disposing_a_session_aborts_a_stalled_transfer_and_releases_the_client_at_once()
    {
        // Die Vorschau hängt nach den Kopfzeilen und beachtet weder Token noch Zeit (wie ein hängender Socket).
        var stalled = new HangingStream();
        _rig.Override = r => r.RequestUri!.AbsolutePath.EndsWith("/thumb/1.png", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stalled) }
            : null;
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        var session = service.OpenSearch(SgdbRig.Steam("553850"))!;
        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);
        var handler = _rig.LastHandler!;

        var preview = session.LoadPreviewAsync(page.Items[0], 130, Ct);
        Assert.True(stalled.Reading.Wait(TimeSpan.FromSeconds(5), Ct));
        session.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        Assert.True(stalled.IsDisposed);
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task Disposing_the_service_cancels_open_sessions()
    {
        _rig.Hold = r => r.RequestUri!.Host == "www.steamgriddb.com";
        var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        var session = service.OpenSearch(SgdbRig.Manual())!;

        var pending = session.ResolveAsync(Ct);
        Assert.True(await _rig.LastHandler!.Held.WaitAsync(5000, Ct));
        service.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        Assert.Null(service.OpenSearch(SgdbRig.Manual()));
    }

    [Fact]
    public async Task Switching_the_automatic_switch_off_leaves_an_open_search_running()
    {
        _rig.Config.Settings.Artwork.SteamGridDb = true;
        _rig.Hold = r => r.RequestUri!.Host == "www.steamgriddb.com";
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Steam("553850"))!;

        var pending = session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);
        Assert.True(await _rig.LastHandler!.Held.WaitAsync(5000, Ct));
        service.SetEnabled(false);
        _rig.LastHandler.Release.Set();

        Assert.Equal(30, (await pending).Items.Count);
        Assert.False(_rig.LastHandler.Disposed);
    }

    [Fact]
    public async Task A_rejected_key_in_a_search_shows_in_the_status_without_deleting_the_key_and_a_cdn_403_does_not()
    {
        var rejected = true;
        _rig.Status = r => r.RequestUri!.Host != "www.steamgriddb.com" ? HttpStatusCode.Forbidden : rejected ? HttpStatusCode.Unauthorized : null;
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Steam("553850"))!;

        var error = await Assert.ThrowsAsync<SteamGridDbException>(() => session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct));
        Assert.Equal(SteamGridDbErrorKind.Unauthorized, error.Kind);
        Assert.Equal(ArtworkStrings.SteamGridDb_Error_Unauthorized, service.Status.LastSearchError);
        Assert.Equal(SteamGridDbKeyState.Stored, service.KeyState); // nicht gelöscht

        rejected = false;
        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);
        Assert.Null(service.Status.LastSearchError); // eine erfolgreiche Antwort löscht die Meldung

        var cdn = await Assert.ThrowsAsync<SteamGridDbException>(() => session.LoadPreviewAsync(page.Items[0], 130, Ct));
        Assert.Equal(SteamGridDbErrorKind.HttpError, cdn.Kind);
        Assert.Null(service.Status.LastSearchError);
    }

    [Fact]
    public async Task The_last_search_shows_in_the_status_after_the_session_ends()
    {
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        Assert.Null(service.Status.LastSearch);

        using (var idle = service.OpenSearch(SgdbRig.Steam("553850"))!)
        {
            // ohne Anfrage: nichts zu melden
        }

        Assert.Null(service.Status.LastSearch);
        var session = service.OpenSearch(SgdbRig.Steam("553850"))!;
        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);
        _rig.Clock.Advance(TimeSpan.FromSeconds(5));
        await session.LoadPreviewAsync(page.Items[0], 130, Ct);
        var last = _rig.Clock.UtcNow;
        _rig.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Null(service.Status.LastSearch); // erst, wenn die Suche endet
        session.Dispose();

        var status = service.Status;
        Assert.Equal(last, status.LastSearch);
        Assert.Equal(2, status.LastSearchRequests);
        Assert.Contains(_rig.Log.Entries, e => e.Level == LogLevel.Information && e.Message == "SteamGridDB-Suche beendet: 1 Anfragen, 1 Bilder.");
    }

    [Fact]
    public async Task Remember_match_replaces_the_id_drops_old_images_and_fills_again_when_on()
    {
        var manual = SgdbRig.Manual("Nebelfahrt");
        _rig.Config.Games.Add(manual);
        _rig.Artwork.SetSteamGridDbGameId(manual, 9101);
        _rig.Artwork.Missing[manual.Key] = [ArtworkKind.Hero];
        _rig.Config.Settings.Artwork.SteamGridDb = true;
        _rig.PageSizes[0] = 1;
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        await SgdbRig.WaitUntil(() => !service.Status.IsRunning);
        _rig.Artwork.Missing[manual.Key] = [ArtworkKind.Hero];
        lock (_rig.Handlers)
        {
            _rig.Handlers.Clear();
        }

        service.RememberMatch(manual, 9102);

        Assert.Equal([(manual.Key, 9102L)], _rig.Artwork.Replaced);
        Assert.True(_rig.Artwork.Flushes > 0);
        await SgdbRig.WaitUntil(() => _rig.Calls.Any(c => c.Uri.Contains("/heroes/game/9102?", StringComparison.Ordinal)));
        await SgdbRig.WaitUntil(() => !service.Status.IsRunning);

        // Steam-Spiele und ungültige IDs: nichts.
        service.RememberMatch(SgdbRig.Steam("553850"), 9103);
        service.RememberMatch(manual, 0);
        service.RememberMatch(manual, -5);
        Assert.Single(_rig.Artwork.Replaced);

        // Ausgeschaltet: gemerkt, aber kein Lauf.
        _rig.Config.Settings.Artwork.SteamGridDb = false;
        var before = _rig.Calls.Count;
        service.RememberMatch(manual, 9104);
        await Task.Delay(150, Ct);
        Assert.Equal(2, _rig.Artwork.Replaced.Count);
        Assert.Equal(before, _rig.Calls.Count);
    }

    [Fact]
    public async Task A_running_fill_never_stores_an_image_of_the_old_match()
    {
        var manual = SgdbRig.Manual("Nebelfahrt");
        _rig.Config.Games.Add(manual);
        _rig.Artwork.SetSteamGridDbGameId(manual, 9101);
        _rig.Artwork.Missing[manual.Key] = [ArtworkKind.Hero, ArtworkKind.Logo];
        _rig.PageSizes[0] = 1;
        _rig.Hold = r => r.RequestUri!.Host != "www.steamgriddb.com";
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        _rig.Config.Settings.Artwork.SteamGridDb = true;

        // Während das Bild der alten Zuordnung lädt, ändert sich die gemerkte ID (ohne Abbruch des Laufs).
        var run = service.RunNowAsync(Ct);
        Assert.True(await _rig.LastHandler!.Held.WaitAsync(5000, Ct));
        _rig.Artwork.SetSteamGridDbGameId(manual, 9102);
        _rig.LastHandler.Release.Set();
        await run;
        Assert.Empty(_rig.Artwork.Stored);

        // Über RememberMatch: Der Lauf endet, bevor er speichert.
        _rig.Artwork.SetSteamGridDbGameId(manual, 9101);
        _rig.Artwork.Checked.Clear();
        _rig.Artwork.Missing[manual.Key] = [ArtworkKind.Hero];
        var second = service.RunNowAsync(Ct);
        await SgdbRig.WaitUntil(() => _rig.Handlers.Count == 2);
        Assert.True(await _rig.LastHandler!.Held.WaitAsync(5000, Ct));
        _rig.Config.Settings.Artwork.SteamGridDb = false; // kein neuer Lauf danach
        service.RememberMatch(manual, 9103);
        _rig.LastHandler.Release.Set();
        try
        {
            await second;
        }
        catch (OperationCanceledException)
        {
            // angehalten
        }

        Assert.Empty(_rig.Artwork.Stored);
    }

    [Fact]
    public async Task Preview_threads_end_and_shut_down_their_dispatcher()
    {
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        var session = (SteamGridDbSearchSession)service.OpenSearch(SgdbRig.Steam("553850"))!;
        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);

        await Task.WhenAll(page.Items.Take(6).Select(a => session.LoadPreviewAsync(a, 130, Ct)));
        Assert.InRange(session.ActiveWorkers, 0, 3);
        await SgdbRig.WaitUntil(() => session.ActiveWorkers == 0); // nach kurzer Ruhe beendet

        session.Dispose();
        Assert.Contains("Prüfung: SteamGridDB-Suche beendet, Vorschau-Threads 0", _rig.TestLog);

        // Auch wenn beim Schließen noch Threads laufen, meldet der letzte das Ende.
        _rig.TestLog.Clear();
        var busy = (SteamGridDbSearchSession)service.OpenSearch(SgdbRig.Steam("553850"))!;
        var items = (await busy.GetAssetsAsync(busy.InitialMatch!, ArtworkKind.Cover, 0, Ct)).Items;
        _rig.LastHandler!.Hold = r => r.RequestUri!.Host != "www.steamgriddb.com";
        _ = busy.LoadPreviewAsync(items[0], 130, Ct);
        Assert.True(await _rig.LastHandler.Held.WaitAsync(5000, Ct));
        busy.Dispose();
        await SgdbRig.WaitUntil(() => busy.ActiveWorkers == 0);
        await SgdbRig.WaitUntil(() =>
        {
            lock (_rig.TestLog)
            {
                return _rig.TestLog.Contains("Prüfung: SteamGridDB-Suche beendet, Vorschau-Threads 0");
            }
        });
    }

    [Fact]
    public async Task Routine_search_errors_are_logged_as_information_without_authors_terms_names_urls_or_key()
    {
        var kind = 0;
        _rig.Status = r => (kind, r.RequestUri!.Host == "www.steamgriddb.com") switch
        {
            (1, true) => HttpStatusCode.InternalServerError,
            (2, true) => HttpStatusCode.TooManyRequests,
            (3, false) => HttpStatusCode.Forbidden,
            _ => null,
        };
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Manual("Nebelfahrt Geheimname"))!;
        var resolved = await session.ResolveAsync(Ct);
        var page = await session.GetAssetsAsync(resolved.Match!, ArtworkKind.Cover, 0, Ct);
        await session.LoadPreviewAsync(page.Items[0], 130, Ct);
        await session.SearchGamesAsync("Suchwort Kupferwald", Ct);

        kind = 1;
        await Assert.ThrowsAsync<SteamGridDbException>(() => session.GetAssetsAsync(resolved.Match!, ArtworkKind.Cover, 1, Ct));
        kind = 3;
        await Assert.ThrowsAsync<SteamGridDbException>(() => session.LoadPreviewAsync(page.Items[1], 130, Ct));
        kind = 2;
        await Assert.ThrowsAsync<SteamGridDbException>(() => session.SearchGamesAsync("Suchwort Zwei", Ct));

        var entries = _rig.Log.Entries.ToList();
        Assert.DoesNotContain(entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains(entries, e => e.Level == LogLevel.Information && e.Message == "SteamGridDB-Suche: HttpError");
        Assert.Contains(entries, e => e.Level == LogLevel.Information && e.Message == "SteamGridDB-Suche: RateLimited");
        foreach (var secret in new[] { SgdbRig.Key, "Testautor", "Nebelfahrt", "Geheimname", "Suchwort", "Kupferwald", "https://", "/grid/", "/thumb/" })
        {
            Assert.DoesNotContain(entries, e => e.Message.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Downloads_reject_images_the_save_would_refuse()
    {
        using var service = _rig.Service();
        service.SaveKey(SgdbRig.Key);
        using var session = service.OpenSearch(SgdbRig.Steam("553850"))!;
        var page = await session.GetAssetsAsync(session.InitialMatch!, ArtworkKind.Cover, 0, Ct);
        var data = await session.DownloadAsync(page.Items[0], ArtworkKind.Cover, Ct);
        Assert.Equal((1, 1), (data.Width, data.Height));
        Assert.Equal(".png", data.Extension);

        // 2000 × 19000 (38 MP) passt als Cover (in 1800 Breite unter 36 MP), als Titelbild (ganz) nicht: Die Prüfung im
        // Dialog lehnt ab, was das Speichern ablehnte.
        var tall = TestPng.Gray(2000, 19000);
        using var tallService = new SgdbRigWithImage(tall).Service();
        tallService.SaveKey(SgdbRig.Key);
        using var tallSession = tallService.OpenSearch(SgdbRig.Steam("553850"))!;
        var asset = new SteamGridDbAsset(1, "https://cdn2.steamgriddb.com/grid/1.png", 2000, 19000, "image/png");
        await Assert.ThrowsAsync<ImageTooLargeException>(() => tallSession.DownloadAsync(asset, ArtworkKind.Hero, Ct));
        var cover = await tallSession.DownloadAsync(asset, ArtworkKind.Cover, Ct);
        Assert.Equal((2000, 19000), (cover.Width, cover.Height));
    }

    [Fact]
    public void Picked_image_cap_equals_the_decoder_limit()
    {
        Assert.Equal(ArtworkImaging.MaxFileBytes, SteamGridDbClient.MaxPickedImageBytes);
    }

    /// <summary>Wie <see cref="SgdbRig"/>, aber der CDN liefert immer dieses Bild.</summary>
    private sealed class SgdbRigWithImage(byte[] image)
    {
        private readonly SgdbRig _rig = new();

        public SteamGridDbService Service() => new(new SteamGridDbServiceOptions
        {
            Artwork = _rig.Artwork,
            Current = () => _rig.Config,
            Update = mutate => mutate(_rig.Config),
            FileSystem = _rig.FileSystem,
            KeyFile = @"C:\Data\steamgriddb-key.dat",
            Protector = new XorSecretProtector(),
            Clock = _rig.Clock,
            HandlerFactory = () => new ScriptedSgdbHandler(_ =>
            {
                var content = new ByteArrayContent(image);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }),
            Pause = TimeSpan.Zero,
        });
    }
}
