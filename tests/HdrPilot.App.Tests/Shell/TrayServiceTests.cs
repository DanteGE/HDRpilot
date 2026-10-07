using System.Runtime.InteropServices;
using HdrPilot.App.Services;
using Drawing = System.Drawing;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// TrayService mit Attrappe statt echtem NotifyIcon (es erscheint kein Symbol im Infobereich). Läuft auf dem
/// WPF-Testhost, weil der Dienst an den UI-Thread gebunden ist.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed partial class TrayServiceTests
{
    [Fact]
    public void Initialize_shows_icon_and_tooltip_for_the_current_state()
    {
        var host = new FakeTrayHost { State = new TrayState(HdrOn: true, HdrSupported: true, Paused: false, ActiveGames: ["Hades II"]) };
        var surface = new FakeSurface();
        var service = Create(host, surface);

        WpfTestHost.Run(service.Initialize);

        Assert.True(surface.Visible);
        Assert.NotNull(surface.Icon);
        Assert.Equal(1, surface.IconSets);
        Assert.Equal(TrayPresentation.TooltipFor(host.State), surface.Text);
        WpfTestHost.Run(service.Dispose);
    }

    [Fact]
    public void Refresh_redraws_the_icon_only_when_the_state_changes_and_frees_the_old_one()
    {
        var host = new FakeTrayHost();
        var surface = new FakeSurface();
        var service = Create(host, surface);
        WpfTestHost.Run(service.Initialize);
        var first = surface.Icon!;
        var firstHandle = first.Handle;
        Assert.True(IsLiveIcon(firstHandle));

        // Gleicher Zustand: kein neues Symbol.
        RefreshAndWait(service, surface, host, host.State);
        Assert.Equal(1, surface.IconSets);
        Assert.Same(first, surface.Icon);

        // HDR an: neues Symbol, altes HICON freigegeben (DestroyIcon).
        RefreshAndWait(service, surface, host, host.State with { HdrOn = true });
        Assert.Equal(2, surface.IconSets);
        Assert.False(IsLiveIcon(firstHandle), "altes HICON nicht freigegeben");
        var secondHandle = surface.Icon!.Handle;
        Assert.True(IsLiveIcon(secondHandle));

        // Nur das Spiel ändert sich: Tooltip neu, Symbol bleibt.
        RefreshAndWait(service, surface, host, host.State with { ActiveGames = ["Cyberpunk 2077"] });
        Assert.Equal(2, surface.IconSets);

        // Pause: neues Symbol (Pause-Balken).
        RefreshAndWait(service, surface, host, host.State with { Paused = true });
        Assert.Equal(3, surface.IconSets);
        Assert.False(IsLiveIcon(secondHandle), "altes HICON nicht freigegeben");

        // Abfrage im Hintergrund, Symbol und Tooltip aber immer auf dem UI-Thread.
        Assert.Equal([WpfTestHost.Dispatcher.Thread.ManagedThreadId], surface.Threads);

        var lastHandle = surface.Icon!.Handle;
        WpfTestHost.Run(service.Dispose);
        Assert.False(IsLiveIcon(lastHandle), "HICON beim Beenden nicht freigegeben");
    }

    [Fact]
    public void Many_state_changes_do_not_leak_gdi_or_user_handles()
    {
        var host = new FakeTrayHost();
        var surface = new FakeSurface();
        var service = Create(host, surface);
        WpfTestHost.Run(service.Initialize);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        RefreshAndWait(service, surface, host, host.State with { HdrOn = true });
        var gdi = GetGuiResources(process.Handle, 0);
        var user = GetGuiResources(process.Handle, 1);

        for (var i = 0; i < 60; i++)
        {
            RefreshAndWait(service, surface, host, new TrayState(i % 2 == 0, true, i % 3 == 0, []));
        }

        Assert.True(surface.IconSets > 40, $"Symbol nur {surface.IconSets}-mal gesetzt");
        Assert.True(GetGuiResources(process.Handle, 0) - gdi < 20, "GDI-Objekte wachsen");
        Assert.True(GetGuiResources(process.Handle, 1) - user < 20, "USER-Objekte wachsen");
        WpfTestHost.Run(service.Dispose);
    }

    [Fact]
    public void Refresh_requests_during_a_running_query_are_coalesced()
    {
        var host = new BlockingTrayHost();
        var surface = new FakeSurface();
        var service = Create(host, surface);
        WpfTestHost.Run(service.Initialize);
        var initial = host.Queries;
        host.Started.Reset();
        host.Gate.Reset();

        WpfTestHost.Run(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                service.Refresh();
            }
        });
        Assert.True(host.Started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "Abfrage startete nicht");
        WpfTestHost.Run(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                service.Refresh();
            }
        });
        host.Gate.Set();

        // Erste Abfrage plus genau eine Nachholabfrage für alle Wünsche während der ersten.
        Assert.True(SpinWait.SpinUntil(() => WpfTestHost.Run(() => !service.IsRefreshing), TimeSpan.FromSeconds(5)), "Refresh endet nicht");
        Assert.Equal(2, host.Queries - initial);
        WpfTestHost.Run(service.Dispose);
    }

    [Fact]
    public void Notify_shows_a_balloon_only_when_allowed()
    {
        var enabled = true;
        var state = new SwitchableState { Current = UserNotificationState.AcceptsNotifications };
        var surface = new FakeSurface();
        var service = new TrayService(new FakeTrayHost(), new NotificationGate(() => enabled, state), surfaceFactory: () => surface, iconSize: () => 16);
        WpfTestHost.Run(service.Initialize);

        WpfTestHost.Run(() => service.Notify("HDR an", "Hades II", isWarning: false));
        Assert.Equal(("HDR an", "Hades II", false), Assert.Single(surface.Balloons));

        // Vollbild-Spiel oder Präsentation: nichts anzeigen.
        foreach (var busy in new[] { UserNotificationState.RunningDirect3DFullScreen, UserNotificationState.Busy, UserNotificationState.PresentationMode })
        {
            state.Current = busy;
            WpfTestHost.Run(() => service.Notify("HDR aus", "Hades II beendet"));
        }

        // Hinweise aus: nichts anzeigen.
        state.Current = UserNotificationState.AcceptsNotifications;
        enabled = false;
        WpfTestHost.Run(() => service.Notify("HDR aus", "Hades II beendet"));

        Assert.Single(surface.Balloons);
        WpfTestHost.Run(service.Dispose);
    }

    [Fact]
    public async Task Notify_from_a_background_thread_reaches_the_ui_thread_and_is_shortened()
    {
        var surface = new FakeSurface();
        var service = new TrayService(
            new FakeTrayHost(),
            new NotificationGate(() => true, new SwitchableState { Current = UserNotificationState.AcceptsNotifications }),
            surfaceFactory: () => surface,
            iconSize: () => 16);
        WpfTestHost.Run(service.Initialize);

        var longTitle = new string('t', 100);
        var longText = new string('x', 400);
        await Task.Run(() => service.Notify(longTitle, longText, isWarning: true), TestContext.Current.CancellationToken);
        WpfTestHost.DoEvents();

        var balloon = Assert.Single(surface.Balloons);
        Assert.True(balloon.IsWarning);
        Assert.True(balloon.Title.Length <= TrayService.MaxBalloonTitle);
        Assert.True(balloon.Text.Length <= TrayService.MaxBalloonText);
        Assert.Equal(WpfTestHost.Dispatcher.Thread.ManagedThreadId, surface.BalloonThread);
        WpfTestHost.Run(service.Dispose);
    }

    [Fact]
    public void Double_click_and_balloon_click_open_the_main_window()
    {
        var host = new FakeTrayHost();
        var surface = new FakeSurface();
        var service = Create(host, surface);
        WpfTestHost.Run(service.Initialize);

        WpfTestHost.Run(surface.RaiseDoubleClick);
        WpfTestHost.Run(surface.RaiseBalloonClicked);

        Assert.Equal(["open", "open"], host.Calls);
        WpfTestHost.Run(service.Dispose);
    }

    [Fact]
    public void Dispose_hides_and_releases_the_icon_and_ignores_later_calls()
    {
        var host = new FakeTrayHost();
        var surface = new FakeSurface();
        var service = Create(host, surface);
        WpfTestHost.Run(service.Initialize);

        WpfTestHost.Run(service.Dispose);
        WpfTestHost.Run(() =>
        {
            service.Refresh();
            service.Notify("a", "b");
            service.Dispose();
        });

        Assert.False(surface.Visible);
        Assert.True(surface.Disposed);
        Assert.Empty(surface.Balloons);
        Assert.Equal(0, surface.Subscribers);
        Assert.False(service.IsRefreshing);
    }

    private static TrayService Create(ITrayHost host, FakeSurface surface) => new(
        host,
        new NotificationGate(() => true, new SwitchableState { Current = UserNotificationState.AcceptsNotifications }),
        surfaceFactory: () => surface,
        iconSize: () => 16);

    /// <summary>Setzt den Zustand, löst Refresh aus und wartet, bis er im Tooltip angekommen ist.</summary>
    private static void RefreshAndWait(TrayService service, FakeSurface surface, FakeTrayHost host, TrayState next)
    {
        host.State = next;
        WpfTestHost.Run(service.Refresh);
        var tooltip = TrayPresentation.TooltipFor(next);
        Assert.True(
            SpinWait.SpinUntil(() => WpfTestHost.Run(() => !service.IsRefreshing && surface.Text == tooltip), TimeSpan.FromSeconds(5)),
            "Refresh kam nicht an");
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    /// <summary>True, wenn das HICON noch existiert (GetIconInfo gelingt). Gibt die dabei erzeugten Bitmaps frei.</summary>
    private static bool IsLiveIcon(IntPtr handle)
    {
        if (!GetIconInfo(handle, out var info))
        {
            return false;
        }

        DeleteObject(info.ColorBitmap);
        DeleteObject(info.MaskBitmap);
        return true;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(IntPtr hIcon, out IconInfo info);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    private sealed class FakeSurface : ITrayIconSurface
    {
        private Drawing.Icon? _icon;
        private string _text = string.Empty;

        public event EventHandler? RightClick;

        public event EventHandler? DoubleClick;

        public event EventHandler? BalloonClicked;

        public Drawing.Icon? Icon
        {
            get => _icon;
            set
            {
                // Wie der echte NotifyIcon: das Handle wird beim Setzen benutzt (wirft, wenn schon freigegeben).
                _ = value?.Handle;
                _icon = value;
                IconSets++;
                Threads.Add(Environment.CurrentManagedThreadId);
            }
        }

        public int IconSets { get; private set; }

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                Threads.Add(Environment.CurrentManagedThreadId);
            }
        }

        /// <summary>Threads, auf denen Symbol oder Tooltip gesetzt wurden (muss immer der UI-Thread sein).</summary>
        public HashSet<int> Threads { get; } = [];

        public bool Visible { get; set; }

        public bool Disposed { get; private set; }

        public List<(string Title, string Text, bool IsWarning)> Balloons { get; } = [];

        public int BalloonThread { get; private set; }

        public int Subscribers => (RightClick?.GetInvocationList().Length ?? 0) + (DoubleClick?.GetInvocationList().Length ?? 0) + (BalloonClicked?.GetInvocationList().Length ?? 0);

        public void ShowBalloon(int timeoutMs, string title, string text, bool isWarning)
        {
            BalloonThread = Environment.CurrentManagedThreadId;
            Balloons.Add((title, text, isWarning));
        }

        public void RaiseDoubleClick() => DoubleClick?.Invoke(this, EventArgs.Empty);

        public void RaiseBalloonClicked() => BalloonClicked?.Invoke(this, EventArgs.Empty);

        public void Dispose() => Disposed = true;
    }

    private sealed class SwitchableState : IUserNotificationStateProvider
    {
        public UserNotificationState Current { get; set; }

        public UserNotificationState Query() => Current;
    }

    /// <summary>Host, dessen Abfrage wartet, bis der Test sie freigibt.</summary>
    private sealed class BlockingTrayHost : ITrayHost
    {
        private int _queries;

        public System.Windows.Threading.Dispatcher Dispatcher => WpfTestHost.Dispatcher;

        public ManualResetEventSlim Gate { get; } = new(initialState: true);

        public ManualResetEventSlim Started { get; } = new();

        public int Queries => Volatile.Read(ref _queries);

        public TrayState QueryTrayState()
        {
            Interlocked.Increment(ref _queries);
            Started.Set();
            Gate.Wait(TimeSpan.FromSeconds(10));
            return TrayState.Initial;
        }

        public Task ToggleHdrNowAsync() => Task.CompletedTask;

        public void SetPaused(bool paused)
        {
        }

        public void ShowMainWindow()
        {
        }

        public void ShowSettingsWindow()
        {
        }

        public void OpenLogFolder()
        {
        }

        public Task ExitAsync() => Task.CompletedTask;
    }
}
