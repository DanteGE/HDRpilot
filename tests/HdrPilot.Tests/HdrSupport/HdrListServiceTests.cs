using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.HdrSupport;

/// <summary>Dienst der App: eigener Thread, ein Lauf zugleich, Start-Zeitgeber, Konto. Netz nur über die Attrappe.</summary>
public sealed class HdrListServiceTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private readonly InMemoryFileSystem _fs = new();
    private readonly AppPaths _paths = AppPaths.InDirectory(@"C:\Daten\HdrPilot");
    private readonly FakeClock _clock = new();
    private readonly FakeWiki _wiki = new();
    private readonly List<FakeHttpHandler> _handlers = [];
    private readonly AppConfig _config = new();
    private readonly HdrListService _service;

    public HdrListServiceTests()
    {
        _config.Games.Add(new GameConfig { Source = GameSource.Steam, SourceId = "934700", Name = "Dead Island 2" });
        _config.Games.Add(new GameConfig { Source = GameSource.Epic, SourceId = "Crow", Name = "Dead Island 2" });
        _config.Games.Add(new GameConfig { Source = GameSource.Gog, SourceId = "1", Name = "Palworld" });
        _wiki.Page("Dead Island 2", "934700", "true").Page("Palworld", "1623730", "false");
        _service = new HdrListService(_paths, _fs, _clock, () => _config, handlerFactory: CreateHandler, protector: new XorProtector());
    }

    private List<PcgwRequest> Requests
    {
        get
        {
            lock (_handlers)
            {
                return _handlers.SelectMany(h => h.Requests).ToList();
            }
        }
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Sync_runs_on_its_own_low_priority_background_thread()
    {
        var changes = 0;
        _service.Changed += (_, _) => Interlocked.Increment(ref changes);

        var result = await _service.SyncNowAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(HdrListRunOutcome.Completed, result.Outcome);
        var request = Assert.Single(Requests);
        Assert.Equal(ThreadPriority.BelowNormal, request.Priority);
        Assert.NotEqual(Environment.CurrentManagedThreadId, request.ManagedThreadId);
        Assert.Equal(HdrListService.UserAgent, request.UserAgent);
        Assert.True(_handlers.All(h => h.Disposed), "HttpClient und Handler leben nur für einen Lauf");
        await WaitUntil(() => Volatile.Read(ref changes) >= 2); // Beginn und Ende
        Assert.False(_service.IsRunning);
        Assert.Equal(HdrSupportStatus.Native, _service.LoadStatuses()["Steam:934700"]);
        Assert.Equal(HdrSupportStatus.Native, _service.LoadStatuses()["Epic:Crow"]);
        Assert.Equal(HdrSupportStatus.None, _service.LoadStatuses()["Gog:1"]);
    }

    [Fact]
    public async Task Unknown_games_are_left_out_of_the_statuses()
    {
        _config.Games.Add(new GameConfig { Source = GameSource.Epic, SourceId = "x", Name = "Gibt es nicht" });

        await _service.SyncNowAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.False(_service.LoadStatuses().ContainsKey("Epic:x"));
        Assert.Equal(4, _service.GetStatus().CheckedGames);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_run()
    {
        using var gate = new ManualResetEventSlim();
        _wiki.Failures[1] = () =>
        {
            gate.Wait(Wait);
            return FakeHttpHandler.Json("""{"batchcomplete":true,"query":{"pages":[]}}""");
        };

        var first = _service.SyncNowAsync();
        var second = _service.SyncNowAsync(force: false);
        Assert.Same(first, second);
        Assert.True(_service.IsRunning);
        gate.Set();
        await first.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Single(Requests, r => r["titles"] is not null); // eine Titelabfrage (danach höchstens die Suchen)
        Assert.False(_service.IsRunning);
    }

    [Fact]
    public async Task Startup_sync_waits_for_the_timer_and_respects_the_setting()
    {
        _config.Settings.HdrListSync = false;
        _service.ScheduleStartupSync(TimeSpan.FromMilliseconds(1));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Empty(Requests);
        Assert.False(_fs.FileExists(_paths.HdrSupportFile));

        _config.Settings.HdrListSync = true;
        var finished = new TaskCompletionSource();
        _service.Changed += (_, _) =>
        {
            if (!_service.IsRunning && Requests.Count > 0)
            {
                finished.TrySetResult();
            }
        };
        _service.ScheduleStartupSync(TimeSpan.FromMilliseconds(50));
        Assert.Empty(Requests); // noch nicht: einmaliger Zeitgeber
        await finished.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Single(Requests);
        Assert.Equal(_clock.UtcNow, _service.GetStatus().LastSuccessAt);
    }

    [Fact]
    public async Task Startup_sync_obeys_the_one_hour_rule()
    {
        await _service.SyncNowAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
        _config.Games.Add(new GameConfig { Source = GameSource.Steam, SourceId = "2", Name = "Neu" });
        _clock.Advance(TimeSpan.FromMinutes(30));

        var result = await _service.SyncNowAsync(force: false).WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(HdrListRunOutcome.Skipped, result.Outcome);
        Assert.Single(Requests);
    }

    [Fact]
    public async Task Disposed_service_does_not_start_the_scheduled_sync()
    {
        _service.ScheduleStartupSync(TimeSpan.FromMilliseconds(100));
        _service.Dispose();
        await Task.Delay(400, TestContext.Current.CancellationToken);

        Assert.Empty(Requests);
        Assert.Equal(HdrListRunOutcome.Failed, (await _service.SyncNowAsync().WaitAsync(Wait, TestContext.Current.CancellationToken)).Outcome);
        Assert.Empty(Requests);
    }

    [Fact]
    public async Task Account_is_stored_only_after_a_successful_test_then_the_full_list_is_loaded()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "abc123geheim";
        _wiki.CargoRows.Add(("Dead Island 2", "934700", null, "true"));

        Assert.Equal(PcgwErrorKind.LoginFailed, await _service.SaveAccountAsync("Dante_GE@HdrPilot", "falsch").WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.False(_fs.FileExists(_paths.PcgwAccountFile));
        Assert.Null(_service.GetStatus().AccountUser);

        _wiki.CargoGranted = false;
        Assert.Equal(PcgwErrorKind.PermissionDenied, await _service.SaveAccountAsync("Dante_GE@HdrPilot", "abc123geheim").WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.False(_fs.FileExists(_paths.PcgwAccountFile));

        _wiki.CargoGranted = true;
        Assert.Null(await _service.SaveAccountAsync(" Dante_GE@HdrPilot ", "abc123geheim").WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.True(_fs.FileExists(_paths.PcgwAccountFile));
        Assert.DoesNotContain("abc123geheim", _fs.ReadAllText(_paths.PcgwAccountFile), StringComparison.Ordinal);
        Assert.Equal("Dante_GE@HdrPilot", _service.GetStatus().AccountUser);

        // Danach lädt ein Abgleich die vollständige Liste (mit Konto).
        await WaitUntil(() => !_service.IsRunning && _service.GetStatus().AccountMode);
        var status = _service.GetStatus();
        Assert.Equal(3, status.CheckedGames);
        Assert.Equal(3, status.TotalGames);
        Assert.Null(status.LastError);
        Assert.True(_fs.FileExists(_paths.HdrCatalogFile));
        Assert.DoesNotContain(Requests, r => r.Raw.Contains("Palworld", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Empty_password_reuses_the_stored_one_for_the_same_user_only()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "abc123geheim";
        await _service.SaveAccountAsync("Dante_GE@HdrPilot", "abc123geheim").WaitAsync(Wait, TestContext.Current.CancellationToken);
        await WaitUntil(() => !_service.IsRunning);
        var before = Requests.Count;

        Assert.Null(await _service.SaveAccountAsync("Dante_GE@HdrPilot", null).WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.Equal("abc123geheim", Requests.Skip(before).Single(r => r.Action == "login")["lgpassword"]);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAccountAsync("Anderer@HdrPilot", null).WaitAsync(Wait, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAccountAsync("OhneBot", "x"));
    }

    [Fact]
    public async Task Removing_the_account_deletes_credentials_and_full_list_but_keeps_results()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "abc123geheim";
        _wiki.CargoRows.Add(("Dead Island 2", "934700", null, "true"));
        await _service.SaveAccountAsync("Dante_GE@HdrPilot", "abc123geheim").WaitAsync(Wait, TestContext.Current.CancellationToken);
        await WaitUntil(() => !_service.IsRunning && _fs.FileExists(_paths.HdrCatalogFile));
        var changes = 0;
        _service.Changed += (_, _) => changes++;

        _service.RemoveAccount();

        Assert.False(_fs.FileExists(_paths.PcgwAccountFile));
        Assert.False(_fs.FileExists(_paths.HdrCatalogFile));
        Assert.Null(_service.GetStatus().AccountUser);
        Assert.Equal(HdrSupportStatus.Native, _service.LoadStatuses()["Steam:934700"]);
        Assert.True(changes >= 1, "Einstellungen erfahren vom entfernten Konto");
    }

    [Fact]
    public async Task Unreadable_account_is_reported_and_nothing_is_sent()
    {
        _fs.AddFile(_paths.PcgwAccountFile, "kaputt!");

        var result = await _service.SyncNowAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(PcgwErrorKind.AccountUnreadable, result.Error);
        Assert.Empty(Requests); // nicht stillschweigend ohne Konto (mit Spielnamen)
        Assert.Equal(PcgwErrorKind.AccountUnreadable, _service.GetStatus().LastError);
    }

    [Fact]
    public void User_agent_names_app_version_and_purpose()
    {
        Assert.Matches(@"^HdrPilot/\d+\.\d+\.\d+ \(Windows desktop app; HDR list\)$", HdrListService.UserAgent);
    }

    [Fact]
    public async Task Status_counts_games_checked_under_their_current_name()
    {
        await _service.SyncNowAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
        _config.Games[2].Name = "Umbenannt";

        var status = _service.GetStatus();

        Assert.Equal(2, status.CheckedGames);
        Assert.Equal(3, status.TotalGames);
        Assert.False(status.IsRunning);
        Assert.False(status.AccountMode);
    }

    private FakeHttpHandler CreateHandler()
    {
        var handler = _wiki.CreateHandler();
        lock (_handlers)
        {
            _handlers.Add(handler);
        }

        return handler;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("Bedingung nicht erfüllt.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();

        public byte[] Unprotect(byte[] data) => Protect(data);
    }
}
