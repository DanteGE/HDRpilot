using HdrPilot.Core.Hdr;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Hdr;

public sealed class HdrControllerTests
{
    private readonly FakeClock _clock = new();
    private readonly FakeEnvironment _env = new();
    private readonly ListLogger<DisplayConfigHdrController> _log = new();

    private DisplayConfigHdrController Create(FakeDisplayConfigApi api, HdrControllerOptions? options = null, IHdrHotkeySender? hotkey = null) =>
        new(api, _env, _clock, options, hotkey, _log);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string PathOf(FakeDisplayConfigApi api) => api.Monitors[0].DevicePath;

    // ---- Build-Weiche ----

    [Theory]
    [InlineData(26100, true)]
    [InlineData(26200, true)]
    [InlineData(27000, true)]
    [InlineData(26099, false)]
    [InlineData(22631, false)] // Windows 11 23H2
    [InlineData(19044, false)] // Windows 10 21H2
    public void Build_switch_selects_api(int build, bool modern)
    {
        _env.WindowsBuild = build;
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor();
        var controller = Create(api);

        Assert.Equal(modern, controller.PrefersModernApi);
        var display = Assert.Single(controller.GetDisplays());
        Assert.Equal(modern ? HdrApi.Modern : HdrApi.Legacy, display.Api);
        Assert.Equal(modern ? 1 : 0, api.CallCount("Get15"));
        Assert.Equal(modern ? 0 : 1, api.CallCount("Get9"));
    }

    [Fact]
    public void Force_api_overrides_build()
    {
        _env.WindowsBuild = 26200;
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor();
        Assert.False(Create(api, new HdrControllerOptions { ForceApi = HdrApi.Legacy }).PrefersModernApi);

        _env.WindowsBuild = 19044;
        Assert.True(Create(api, new HdrControllerOptions { ForceApi = HdrApi.Modern }).PrefersModernApi);
    }

    [Fact]
    public void Modern_read_failure_falls_back_to_legacy_and_logs_once()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false, wcgOn: true);
        api.Info2Result = 50; // ERROR_NOT_SUPPORTED
        var controller = Create(api);

        var d1 = Assert.Single(controller.GetDisplays());
        var d2 = Assert.Single(controller.GetDisplays());

        Assert.Equal(HdrApi.Legacy, d1.Api);
        Assert.True(d1.HdrSupported);
        Assert.False(d1.HdrEnabled); // WCG an zählt nicht als HDR
        Assert.Equal(HdrApi.Legacy, d2.Api);
        Assert.Equal(1, _log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("Typ 15", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Modern_set_failure_falls_back_to_legacy_set_and_logs()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false, wcgOn: false);
        api.SetHdrResult = 31; // ERROR_GEN_FAILURE
        var controller = Create(api);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.Success, result.Status);
        Assert.Equal(HdrApi.Legacy, result.Api);
        Assert.Equal(1, api.CallCount("Set16"));
        Assert.Equal(1, api.CallCount("Set10:True"));
        Assert.True(_log.Contains(LogLevel.Warning, "falle auf SET_ADVANCED_COLOR_STATE"));
    }

    // ---- Schalten und Bestätigen ----

    [Fact]
    public async Task Enable_sets_type_16_and_confirms_by_reading_again()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.ApplyAfterReads = 3; // Windows braucht drei Lesevorgänge
        var controller = Create(api);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.Success, result.Status);
        Assert.False(result.Before);
        Assert.True(result.After);
        Assert.Equal(HdrApi.Modern, result.Api);
        Assert.Equal(["Set16:True"], api.Calls.Where(c => c.StartsWith("Set", StringComparison.Ordinal)));
        Assert.Equal(3, _clock.Delays.Count);
        Assert.True(api.Monitors[0].HdrUserEnabled);
        Assert.True(api.Monitors[0].WcgUserEnabled); // WCG-Einstellung bleibt erhalten
    }

    [Fact]
    public async Task Disable_returns_to_wcg_mode()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: true, wcgOn: true);
        var controller = Create(api);

        var result = await controller.SetHdrAsync(PathOf(api), enable: false, Ct);

        Assert.Equal(HdrSetStatus.Success, result.Status);
        Assert.True(result.Before);
        Assert.False(result.After);
        Assert.Equal(AdvancedColorMode.Wcg, api.Monitors[0].ActiveMode);
    }

    [Fact]
    public async Task Already_in_state_does_not_switch()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: true);
        var controller = Create(api);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.AlreadyInState, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, api.CallCount("Set"));
    }

    [Fact]
    public async Task Not_confirmed_after_all_attempts_is_failure()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.IgnoreSets = true;
        var controller = Create(api, new HdrControllerOptions { VerifyAttempts = 4, VerifyInterval = TimeSpan.FromMilliseconds(250) });

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.Failed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(4, _clock.Delays.Count);
        Assert.All(_clock.Delays, d => Assert.Equal(TimeSpan.FromMilliseconds(250), d));
        Assert.Contains("nach 4 Prüfungen", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switched_on_but_never_active_reports_policy()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.Monitors[0].HdrNeverActivates = true;
        var controller = Create(api, new HdrControllerOptions { VerifyAttempts = 3 });

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.Failed, result.Status);
        Assert.Contains("Richtlinie", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Target_ids_are_resolved_again_for_each_read()
    {
        // Nach dem Umschalten vergibt der Treiber eine neue Ziel-ID: Bestätigung muss über monitorDevicePath laufen.
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.AfterSet = m => m.Target = m.Target with { TargetId = m.Target.TargetId + 100 };
        var controller = Create(api);

        var on = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);
        var off = await controller.SetHdrAsync(PathOf(api), enable: false, Ct);

        Assert.Equal(HdrSetStatus.Success, on.Status);
        Assert.Equal(HdrSetStatus.Success, off.Status);
        Assert.Equal(4553u, api.Monitors[0].Target.TargetId);
    }

    [Fact]
    public async Task Not_hdr_capable_does_not_switch()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor();
        api.Monitors[0].HdrSupported = false;
        var controller = Create(api);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.NotHdrCapable, result.Status);
        Assert.Equal(0, api.CallCount("Set"));
        Assert.Contains("unterstützt kein HDR", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_display_is_not_connected()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor();
        var result = await Create(api).SetHdrAsync(@"\\?\DISPLAY#GONE", enable: true, Ct);
        Assert.Equal(HdrSetStatus.NotConnected, result.Status);
        Assert.Equal(0, api.CallCount("Set"));
    }

    [Fact]
    public void Query_failure_yields_empty_list_and_warning()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor();
        api.QueryResult = 5;
        Assert.Empty(Create(api).GetDisplays());
        Assert.True(_log.Contains(LogLevel.Warning, "QueryDisplayConfig"));
    }

    // ---- Tastenkürzel-Notlösung ----

    [Fact]
    public async Task Hotkey_fallback_is_off_by_default()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.SetHdrResult = 31;
        api.SetAdvancedColorResult = 31;
        var hotkey = new FakeHotkeySender(api);
        var controller = Create(api, hotkey: hotkey);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.Failed, result.Status);
        Assert.Equal(0, hotkey.Sent);
    }

    [Fact]
    public async Task Hotkey_fallback_used_only_after_api_failure_and_confirmed()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.SetHdrResult = 31;
        api.SetAdvancedColorResult = 31;
        var hotkey = new FakeHotkeySender(api);
        var controller = Create(api, new HdrControllerOptions { HotkeyFallbackEnabled = true }, hotkey);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.Success, result.Status);
        Assert.True(result.UsedHotkeyFallback);
        Assert.Equal(1, hotkey.Sent);
        Assert.True(_log.Contains(LogLevel.Warning, "Win+Alt+B"));
    }

    [Fact]
    public async Task Hotkey_fallback_checks_state_first_and_does_not_toggle_wrong_way()
    {
        // API meldet Fehler, hat aber trotzdem geschaltet: das Kürzel würde HDR wieder ausschalten.
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.FailButApply = true;
        var hotkey = new FakeHotkeySender(api);
        var controller = Create(api, new HdrControllerOptions { HotkeyFallbackEnabled = true }, hotkey);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.Success, result.Status);
        Assert.Equal(0, hotkey.Sent);
        Assert.False(result.UsedHotkeyFallback);
        Assert.True(api.Monitors[0].HdrUserEnabled);
    }

    [Fact]
    public async Task Hotkey_fallback_sends_when_state_still_wrong_after_ignored_set()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.IgnoreSets = true;
        var hotkey = new FakeHotkeySender(api);
        var controller = Create(api, new HdrControllerOptions { HotkeyFallbackEnabled = true, VerifyAttempts = 2 }, hotkey);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(1, hotkey.Sent);
        Assert.Equal(HdrSetStatus.Success, result.Status);
        Assert.True(result.UsedHotkeyFallback);
    }

    [Fact]
    public async Task Hotkey_fallback_failure_is_reported()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.SetHdrResult = 31;
        api.SetAdvancedColorResult = 31;
        var hotkey = new FakeHotkeySender(api, succeeds: false);
        var controller = Create(api, new HdrControllerOptions { HotkeyFallbackEnabled = true }, hotkey);

        var result = await controller.SetHdrAsync(PathOf(api), enable: true, Ct);

        Assert.Equal(HdrSetStatus.Failed, result.Status);
        Assert.Equal(1, hotkey.Sent);
    }

    [Fact]
    public async Task Concurrent_switches_are_serialized()
    {
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        var controller = Create(api);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(i => Task.Run(() => controller.SetHdrAsync(PathOf(api), enable: i % 2 == 0, Ct))));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Message));
    }
}
