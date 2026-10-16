using System.Net;
using System.Net.Http;
using System.Text;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Tests.Artwork;

/// <summary>
/// SteamGridDB als Dienst (E89, G10.4), nur mit Attrappen: standardmäßig aus und dann nachweislich ohne Netzwerk (kein
/// Handler, keine Anfrage), nur mit eigenem Schlüssel, füllt nur fehlende Bilder, merkt sich Fehlanzeigen, startet von selbst
/// nur einmal am Tag mit normaler Ablage und ohne laufendes Spiel, der Schlüssel steht nie im Protokoll.
/// </summary>
public sealed class SteamGridDbServiceTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly AppConfig _config = new();
    private readonly FakeArtwork _artwork = new();
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();
    private readonly ListLogger<SteamGridDbServiceTests> _log = new();
    private readonly List<CountingHandler> _handlers = [];
    private bool _gameRunning;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Off_by_default_means_no_handler_and_no_request()
    {
        _config.Games.Add(Steam("1"));
        _artwork.Missing["Steam:1"] = [ArtworkKind.Hero];
        using var service = Service();
        service.SaveKey(Key);

        await service.RunNowAsync(Ct);
        service.ScheduleStartup(allowed: true);
        await Task.Delay(100, Ct);

        Assert.False(_config.Settings.Artwork.SteamGridDb);
        Assert.False(service.Status.Enabled);
        Assert.Empty(_handlers);
        Assert.Equal(0, service.HandlersCreated);
        Assert.Empty(_artwork.Stored);
    }

    [Fact]
    public async Task Enabled_without_key_never_touches_the_network()
    {
        _config.Games.Add(Steam("1"));
        _artwork.Missing["Steam:1"] = [ArtworkKind.Hero];
        _config.Settings.Artwork.SteamGridDb = true;
        using var service = Service();

        await service.RunNowAsync(Ct);

        Assert.Equal(SteamGridDbKeyState.Missing, service.Status.Key);
        Assert.Equal(0, service.HandlersCreated);
    }

    [Fact]
    public async Task Fills_only_missing_kinds_and_stores_images()
    {
        _config.Games.Add(Steam("553850"));
        _config.Games.Add(Steam("10"));
        _artwork.Missing["Steam:553850"] = [ArtworkKind.Logo];
        using var service = Service();
        service.SaveKey(Key);

        service.SetEnabled(true);
        await WaitIdle(service);

        var handler = Assert.Single(_handlers);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://www.steamgriddb.com/api/v2/logos/steam/553850?", handler.Requests[0], StringComparison.Ordinal);
        Assert.StartsWith("https://cdn2.steamgriddb.com/", handler.Requests[1], StringComparison.Ordinal);
        Assert.Equal([("Steam:553850", ArtworkKind.Logo, ".png")], _artwork.Stored);
        Assert.True(_config.Settings.Artwork.SteamGridDb);
        var status = service.Status;
        Assert.Equal(2, status.LastRequests);
        Assert.Equal(1, status.LastFilled);
        Assert.Equal(_clock.UtcNow, status.LastRun);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task Nothing_missing_means_no_handler_even_when_enabled()
    {
        _config.Games.Add(Steam("1"));
        using var service = Service();
        service.SaveKey(Key);
        _config.Settings.Artwork.SteamGridDb = true;

        await service.RunNowAsync(Ct);

        Assert.Equal(0, service.HandlersCreated);
        Assert.Equal(_clock.UtcNow, service.Status.LastRun);
    }

    [Fact]
    public async Task Misses_are_remembered_for_thirty_days()
    {
        _config.Games.Add(Steam("1"));
        _artwork.Missing["Steam:1"] = [ArtworkKind.Hero];
        using var service = Service(empty: true);
        service.SaveKey(Key);
        _config.Settings.Artwork.SteamGridDb = true;

        await service.RunNowAsync(Ct);
        _clock.Advance(TimeSpan.FromDays(29));
        await service.RunNowAsync(Ct);

        Assert.Single(_handlers); // zweiter Lauf: nichts fällig, kein Handler
        Assert.Equal(_clock.UtcNow - TimeSpan.FromDays(29), _artwork.Checked[("Steam:1", ArtworkKind.Hero)]);

        // Gegenprobe: nach 30 Tagen wird wieder gefragt.
        _clock.Advance(TimeSpan.FromDays(2));
        await service.RunNowAsync(Ct);
        Assert.Equal(2, _handlers.Count);
    }

    [Fact]
    public async Task Games_without_steam_id_are_searched_once_by_name()
    {
        _config.Games.Add(new GameConfig { Source = GameSource.Gog, SourceId = "1456460669", Name = "Baldur's Gate 3" });
        _artwork.Missing["Gog:1456460669"] = [ArtworkKind.Logo];
        using var service = Service();
        service.SaveKey(Key);
        _config.Settings.Artwork.SteamGridDb = true;

        await service.RunNowAsync(Ct);

        var requests = _handlers.Single().Requests;
        Assert.Contains("/search/autocomplete/", requests[0], StringComparison.Ordinal);
        Assert.StartsWith("https://www.steamgriddb.com/api/v2/logos/game/5247826?", requests[1], StringComparison.Ordinal);
        Assert.Equal(5247826, _artwork.GameIds["Gog:1456460669"]);
    }

    [Fact]
    public async Task Switching_off_or_removing_the_key_stops_a_running_run_at_once()
    {
        foreach (var stop in new[] { "aus", "schluessel" })
        {
            _config.Games.Clear();
            for (var i = 1; i <= 6; i++)
            {
                _config.Games.Add(Steam(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                _artwork.Missing["Steam:" + i] = [ArtworkKind.Hero];
            }

            _handlers.Clear();
            _config.Settings.Artwork.SteamGridDb = true;
            SteamGridDbService? running = null;
            var requests = 0;
            using var service = Service(onRequest: () =>
            {
                // Der Nutzer greift nach der ersten Anfrage ein (wie in der Prüfung: danach liefen noch 11 Anfragen).
                if (Interlocked.Increment(ref requests) == 1)
                {
                    if (stop == "aus")
                    {
                        running!.SetEnabled(false);
                    }
                    else
                    {
                        running!.RemoveKey();
                    }
                }
            });
            running = service;
            service.SaveKey(Key);
            _artwork.Stored.Clear();

            try
            {
                await service.RunNowAsync(Ct);
            }
            catch (OperationCanceledException)
            {
                // Angehalten: auch das ist ein Ende des Laufs.
            }

            await WaitIdle(service);
            Assert.Equal(1, _handlers.Sum(h => h.Requests.Count));
            Assert.Empty(_artwork.Stored);
            Assert.Null(_artwork.SteamGridDbLastRun); // kein „heute gelaufen“: der nächste Lauf macht weiter
        }
    }

    [Fact]
    public async Task Game_start_stops_a_run_and_no_run_starts_while_a_game_is_running()
    {
        for (var i = 1; i <= 6; i++)
        {
            _config.Games.Add(Steam(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            _artwork.Missing["Steam:" + i] = [ArtworkKind.Hero];
        }

        _config.Settings.Artwork.SteamGridDb = true;
        _gameRunning = true;
        using var service = Service(onRequest: () => _gameRunning = true);
        service.SaveKey(Key);

        // Läuft schon ein Spiel, beginnt kein Lauf (auch nicht nach dem Speichern des Schlüssels).
        await service.RunNowAsync(Ct);
        Assert.Equal(0, service.HandlersCreated);
        Assert.Equal("Übersprungen: Ein Spiel läuft", service.Status.LastError);

        // Spielstart mitten im Lauf: nach der laufenden Bildart ist Schluss.
        _gameRunning = false;
        await service.RunNowAsync(Ct);
        await WaitIdle(service);
        var requests = _handlers.Sum(h => h.Requests.Count);
        Assert.InRange(requests, 1, 2); // die erste Abfrage und höchstens ihr Download
        Assert.Equal("Übersprungen: Ein Spiel läuft", service.Status.LastError);
        Assert.True(_artwork.Stored.Count <= 1);
    }

    [Fact]
    public async Task Rejected_key_stops_the_run_with_a_message()
    {
        _config.Games.Add(Steam("1"));
        _config.Games.Add(Steam("2"));
        _artwork.Missing["Steam:1"] = [ArtworkKind.Hero];
        _artwork.Missing["Steam:2"] = [ArtworkKind.Hero];
        using var service = Service(status: HttpStatusCode.Unauthorized);
        service.SaveKey(Key);
        _config.Settings.Artwork.SteamGridDb = true;

        await service.RunNowAsync(Ct);

        Assert.Single(_handlers.Single().Requests);
        Assert.Equal("Schlüssel abgelehnt – bitte prüfen", service.Status.LastError);
    }

    [Fact]
    public async Task Startup_run_only_when_allowed_enabled_idle_and_not_today()
    {
        _config.Games.Add(Steam("1"));
        _artwork.Missing["Steam:1"] = [ArtworkKind.Hero];
        using var service = Service();
        service.SaveKey(Key);
        _config.Settings.Artwork.SteamGridDb = true;

        service.ScheduleStartup(allowed: false); // mit --config-dir: nie von selbst
        await Task.Delay(150, Ct);
        Assert.Equal(0, service.HandlersCreated);

        _gameRunning = true;
        service.ScheduleStartup(allowed: true);
        await Task.Delay(150, Ct);
        Assert.Equal(0, service.HandlersCreated);
        Assert.Equal("Übersprungen: Ein Spiel läuft", service.Status.LastError);

        _gameRunning = false;
        _artwork.SteamGridDbLastRun = _clock.UtcNow - TimeSpan.FromHours(3);
        service.ScheduleStartup(allowed: true);
        await Task.Delay(150, Ct);
        Assert.Equal(0, service.HandlersCreated); // heute schon gelaufen

        _artwork.SteamGridDbLastRun = _clock.UtcNow - TimeSpan.FromDays(2);
        service.ScheduleStartup(allowed: true);
        await WaitUntil(() => service.HandlersCreated == 1);
        await WaitIdle(service);
    }

    [Fact]
    public async Task Key_is_stored_encrypted_checked_on_request_and_never_logged()
    {
        using var service = Service();

        Assert.Equal(SteamGridDbKeyState.Missing, service.Status.Key);
        service.SaveKey(Key);
        var valid = await service.CheckKeyAsync(Key, Ct);
        var implausible = await service.CheckKeyAsync("kurz", Ct);

        Assert.Equal(SteamGridDbKeyState.Stored, service.Status.Key);
        Assert.Equal(SteamGridDbCheckResult.Valid, valid);
        Assert.Equal(SteamGridDbCheckResult.Invalid, implausible);
        Assert.Equal(1, service.HandlersCreated); // nur „Prüfen“ mit gültig aussehendem Schlüssel
        Assert.DoesNotContain(Key, _fs.ReadAllText(@"C:\Data\steamgriddb-key.dat"), StringComparison.Ordinal);
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains(Key, StringComparison.Ordinal));

        service.RemoveKey();
        Assert.Equal(SteamGridDbKeyState.Missing, service.Status.Key);
        Assert.False(_fs.FileExists(@"C:\Data\steamgriddb-key.dat"));
    }

    [Fact]
    public async Task Status_counts_missing_images_without_network()
    {
        _config.Games.Add(Steam("1"));
        _config.Games.Add(Steam("2"));
        _artwork.Missing["Steam:1"] = [ArtworkKind.Hero, ArtworkKind.Logo];
        _artwork.Missing["Steam:2"] = [ArtworkKind.Logo, ArtworkKind.Cover];
        using var service = Service();

        _ = service.Status;
        await WaitUntil(() => service.Status.MissingLogo == 2);

        Assert.Equal((1, 2, 1), (service.Status.MissingHero, service.Status.MissingLogo, service.Status.MissingCover));
        Assert.Equal(0, service.HandlersCreated);
    }

    private static GameConfig Steam(string id) => new() { Source = GameSource.Steam, SourceId = id, Name = "Spiel " + id };

    private SteamGridDbService Service(HttpStatusCode status = HttpStatusCode.OK, bool empty = false, Action? onRequest = null) => new(new SteamGridDbServiceOptions
    {
        Artwork = _artwork,
        Current = () => _config,
        Update = mutate => mutate(_config),
        FileSystem = _fs,
        KeyFile = @"C:\Data\steamgriddb-key.dat",
        Protector = new XorProtector(),
        Clock = _clock,
        GameRunning = () => _gameRunning,
        HandlerFactory = () =>
        {
            var handler = new CountingHandler(request =>
            {
                onRequest?.Invoke();
                return Respond(request, status, empty);
            });
            lock (_handlers)
            {
                _handlers.Add(handler);
            }

            return handler;
        },
        StartupDelay = TimeSpan.FromMilliseconds(10),
        Pause = TimeSpan.Zero,
        Logger = _log,
    });

    private static HttpResponseMessage Respond(HttpRequestMessage request, HttpStatusCode status, bool empty)
    {
        if (status != HttpStatusCode.OK)
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

        var json = uri.AbsolutePath.Contains("/search/", StringComparison.Ordinal)
            ? "{\"success\":true,\"data\":[{\"id\":5247826,\"name\":" + System.Text.Json.JsonSerializer.Serialize(Uri.UnescapeDataString(uri.AbsolutePath.Split('/')[^1])) + "}]}"
            : empty
                ? "{\"success\":true,\"data\":[]}"
                : "{\"success\":true,\"data\":[{\"id\":1,\"url\":\"https://cdn2.steamgriddb.com/logo/a.png\",\"width\":1,\"height\":1,\"mime\":\"image/png\"}]}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static async Task WaitIdle(SteamGridDbService service) => await WaitUntil(() => !service.Status.IsRunning);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25, Ct);
        }

        Assert.True(condition());
    }

    /// <summary>Bilderdienst als Attrappe: was fehlt, was gefragt und was gespeichert wurde.</summary>
    private sealed class FakeArtwork : ISteamGridDbArtwork
    {
        public Dictionary<string, List<ArtworkKind>> Missing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<(string, ArtworkKind), DateTimeOffset> Checked { get; } = [];

        public Dictionary<string, long> GameIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Key, ArtworkKind Kind, string Extension)> Stored { get; } = [];

        public DateTimeOffset? SteamGridDbLastRun { get; set; }

        public IReadOnlyList<ArtworkKind> MissingKinds(GameConfig game) => Missing.TryGetValue(game.Key, out var kinds) ? kinds : [];

        public DateTimeOffset? SteamGridDbChecked(GameConfig game, ArtworkKind kind) => Checked.TryGetValue((game.Key, kind), out var when) ? when : null;

        public void MarkSteamGridDbChecked(GameConfig game, ArtworkKind kind, DateTimeOffset when) => Checked[(game.Key, kind)] = when;

        public long? SteamGridDbGameId(GameConfig game) => GameIds.TryGetValue(game.Key, out var id) ? id : null;

        public void SetSteamGridDbGameId(GameConfig game, long id) => GameIds[game.Key] = id;

        public void StoreSteamGridDbImage(GameConfig game, ArtworkKind kind, byte[] data, string extension, DateTimeOffset when)
        {
            Stored.Add((game.Key, kind, extension));
            Missing[game.Key].Remove(kind);
            Checked[(game.Key, kind)] = when;
        }

        public void FlushIndex()
        {
        }
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x33)).ToArray();

        public byte[] Unprotect(byte[] data) => Protect(data);
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request.RequestUri!.AbsoluteUri);
            }

            return respond(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
