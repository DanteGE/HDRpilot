using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.AutoHdr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Forms = System.Windows.Forms;

namespace HdrPilot.App.Services;

/// <summary>Was das Tray von der App braucht (Adapter auf AppServices, siehe AppServices.Shell.cs).</summary>
public interface ITrayHost
{
    Dispatcher Dispatcher { get; }

    /// <summary>Liest HDR-Zustand, Pause und laufende Spiele. Darf auf einem Hintergrund-Thread laufen.</summary>
    TrayState QueryTrayState();

    Task ToggleHdrNowAsync();

    /// <summary>Globaler Auto-HDR-Schalter von Windows, gelesen beim Öffnen des Menüs (<see cref="IAutoHdrService.ReadGlobalSwitch"/>).</summary>
    AutoHdrSwitchState ReadAutoHdrSwitch();

    /// <summary>HDRpilot hält den Schalter gerade für einen Spielstart (<see cref="IAutoHdrService.HoldsGlobalSwitch"/>): danach wieder aus.</summary>
    bool AutoHdrSwitchHeld { get; }

    /// <summary>
    /// Tray-Eintrag „Auto HDR“ (G9.9): schaltet den Windows-Schalter als Wahl des Nutzers
    /// (<see cref="IAutoHdrService.SetGlobalSwitchByUser"/>); liefert den Stand danach.
    /// </summary>
    AutoHdrSwitchState SetAutoHdrSwitch(bool on);

    void SetPaused(bool paused);

    void ShowMainWindow();

    void ShowSettingsWindow();

    void OpenLogFolder();

    Task ExitAsync();
}

/// <summary>Das sichtbare Symbol im Infobereich. Für Tests austauschbar, damit dort kein echtes Symbol erscheint.</summary>
public interface ITrayIconSurface : IDisposable
{
    /// <summary>Rechtsklick (Maustaste losgelassen): Menü öffnen.</summary>
    event EventHandler? RightClick;

    /// <summary>Doppelklick mit der linken Taste.</summary>
    event EventHandler? DoubleClick;

    event EventHandler? BalloonClicked;

    System.Drawing.Icon? Icon { get; set; }

    /// <summary>Tooltip, höchstens <see cref="TrayPresentation.MaxTooltipLength"/> Zeichen.</summary>
    string Text { get; set; }

    bool Visible { get; set; }

    void ShowBalloon(int timeoutMs, string title, string text, bool isWarning);
}

/// <summary>WinForms-NotifyIcon als <see cref="ITrayIconSurface"/>.</summary>
public sealed class NotifyIconSurface : ITrayIconSurface
{
    private readonly Forms.NotifyIcon _icon = new();

    public NotifyIconSurface()
    {
        _icon.MouseUp += OnMouseUp;
        _icon.MouseDoubleClick += OnMouseDoubleClick;
        _icon.BalloonTipClicked += OnBalloonTipClicked;
    }

    public event EventHandler? RightClick;

    public event EventHandler? DoubleClick;

    public event EventHandler? BalloonClicked;

    public System.Drawing.Icon? Icon
    {
        get => _icon.Icon;
        set => _icon.Icon = value;
    }

    public string Text
    {
        get => _icon.Text;
        set => _icon.Text = value;
    }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    // Windows zeigt das als Benachrichtigung an; sie nimmt nie den Fokus.
    public void ShowBalloon(int timeoutMs, string title, string text, bool isWarning) =>
        _icon.ShowBalloonTip(timeoutMs, title, text, isWarning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.MouseUp -= OnMouseUp;
        _icon.MouseDoubleClick -= OnMouseDoubleClick;
        _icon.BalloonTipClicked -= OnBalloonTipClicked;
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right)
        {
            RightClick?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMouseDoubleClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            DoubleClick?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e) => BalloonClicked?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Tray-Symbol (WinForms-NotifyIcon) mit WPF-Kontextmenü im HdrPilot-Stil. Das Symbol wird zur Laufzeit passend zur
/// DPI gezeichnet und nur bei Zustandswechsel neu erzeugt; alte HICONs werden freigegeben. Im Hintergrund läuft nur
/// ein Timer (alle 30 s), der HDR-Wechsel außerhalb von HdrPilot nachträgt. Nur solange ein Neustart-Puffer läuft
/// (höchstens wenige Minuten), führt ein zweiter Timer den Countdown im Tooltip jede Sekunde nach. Das Menü wird erst
/// beim Öffnen erzeugt und nach dem Schließen verworfen.
/// </summary>
public sealed partial class TrayService : ITrayService
{
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(30);

    /// <summary>Takt des Countdowns „HDR bleibt noch m:ss an (Neustart-Puffer)“.</summary>
    public static readonly TimeSpan CountdownInterval = TimeSpan.FromSeconds(1);

    public const int BalloonTimeoutMs = 4000;
    public const int MaxBalloonTitle = 63;
    public const int MaxBalloonText = 255;

    private readonly ITrayHost _host;
    private readonly NotificationGate _gate;
    private readonly ILogger _logger;
    private readonly Func<ITrayIconSurface> _createSurface;
    private readonly Func<int> _iconSize;
    private readonly Func<DateTimeOffset> _now;
    private readonly IAppearanceService? _appearance;
    private readonly WindowIconSource? _windowIcon;
    private IconPalette _iconPalette = IconPalette.Default;
    private bool _lightTaskbar;
    private ITrayIconSurface? _notifyIcon;
    private TrayIcon? _icon;
    private TrayState _state = TrayState.Initial;
    private DispatcherTimer? _recheck;
    private DispatcherTimer? _countdown;
    private ForegroundSink? _sink;
    private TrayMenu? _menu;
    private TrayMenuViewModel? _menuModel;
    private bool _refreshRunning;
    private bool _refreshPending;
    private bool _disposed;

    /// <param name="surfaceFactory">Erzeugt das Symbol im Infobereich (Standard: NotifyIcon; Tests: Attrappe).</param>
    /// <param name="iconSize">Kantenlänge des Symbols in Pixeln (Standard: kleine Symbole bei System-DPI).</param>
    /// <param name="clock">Uhr für den Countdown des Neustart-Puffers (Standard: Systemzeit; Tests: steuerbar).</param>
    /// <param name="appearance">Farbschema und Taskleisten-Thema für das Symbol (E89, G10.2); null: Auslieferungsfarben.</param>
    /// <param name="windowIcon">Fenstersymbol, das den Zustand mitbekommt (HDR an/aus, Pause, Spiel läuft); null: keins.</param>
    public TrayService(
        ITrayHost host,
        NotificationGate gate,
        ILogger? logger = null,
        Func<ITrayIconSurface>? surfaceFactory = null,
        Func<int>? iconSize = null,
        Func<DateTimeOffset>? clock = null,
        IAppearanceService? appearance = null,
        WindowIconSource? windowIcon = null)
    {
        _host = host;
        _gate = gate;
        _logger = logger ?? NullLogger.Instance;
        _createSurface = surfaceFactory ?? (() => new NotifyIconSurface());
        _iconSize = iconSize ?? TrayIconRenderer.SystemSmallIconSize;
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _appearance = appearance;
        _windowIcon = windowIcon;
    }

    /// <summary>Spezifikation des gerade gezeigten Symbols (Prüfung).</summary>
    public TrayIconSpec? CurrentIconSpec => _icon?.Spec;

    public void Initialize()
    {
        _host.Dispatcher.VerifyAccess();
        if (_notifyIcon is not null || _disposed)
        {
            return;
        }

        _notifyIcon = _createSurface();
        _notifyIcon.RightClick += OnRightClick;
        _notifyIcon.DoubleClick += OnDoubleClick;
        _notifyIcon.BalloonClicked += OnBalloonClicked;

        try
        {
            _state = _host.QueryTrayState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tray: Anfangszustand nicht lesbar.");
        }

        if (_appearance is not null)
        {
            ReadAppearance();
            _appearance.Changed += OnAppearanceChanged;
        }

        Apply(_state);
        _notifyIcon.Visible = true;

        _recheck = new DispatcherTimer(RecheckInterval, DispatcherPriority.Background, (_, _) => Refresh(), _host.Dispatcher);
        _recheck.Start();
    }

    /// <summary>True, solange ein <see cref="Refresh"/> den Zustand liest (nur auf dem UI-Thread lesen).</summary>
    public bool IsRefreshing => _refreshRunning;

    /// <summary>True, solange der Countdown des Neustart-Puffers im Tooltip läuft (nur auf dem UI-Thread lesen).</summary>
    public bool IsCountdownRunning => _countdown is not null;

    /// <summary>Zustand im Hintergrund neu lesen und Symbol, Tooltip und offenes Menü anpassen.</summary>
    public void Refresh()
    {
        if (!_host.Dispatcher.CheckAccess())
        {
            _host.Dispatcher.BeginInvoke(Refresh);
            return;
        }

        if (_notifyIcon is null || _disposed)
        {
            return;
        }

        if (_refreshRunning)
        {
            _refreshPending = true;
            return;
        }

        _refreshRunning = true;
        _ = RefreshLoopAsync();
    }

    public void Notify(string title, string message, bool isWarning = false)
    {
        if (!_host.Dispatcher.CheckAccess())
        {
            _host.Dispatcher.BeginInvoke(() => Notify(title, message, isWarning));
            return;
        }

        title ??= string.Empty;
        message ??= string.Empty;
        if (_gate.Evaluate(title, message) != NotificationDecision.Show)
        {
            return;
        }

        if (_notifyIcon is null || _disposed)
        {
            _logger.LogInformation("Hinweis ohne Tray: {Title} | {Message}", title, message);
            return;
        }

        var text = message.Length == 0 ? title : message;
        if (text.Length == 0)
        {
            return;
        }

        _notifyIcon.ShowBalloon(
            BalloonTimeoutMs,
            TrayPresentation.Fit(title, MaxBalloonTitle),
            TrayPresentation.Fit(text, MaxBalloonText),
            isWarning);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_appearance is not null)
        {
            _appearance.Changed -= OnAppearanceChanged;
        }

        _recheck?.Stop();
        _recheck = null;
        StopCountdown();
        if (_menu is not null)
        {
            _menu.IsOpen = false;
            _menu = null;
            _menuModel = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.RightClick -= OnRightClick;
            _notifyIcon.DoubleClick -= OnDoubleClick;
            _notifyIcon.BalloonClicked -= OnBalloonClicked;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
        _sink?.Dispose();
        _sink = null;
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            do
            {
                _refreshPending = false;
                TrayState next;
                try
                {
                    next = await Task.Run(_host.QueryTrayState);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tray: Zustand nicht lesbar.");
                    return;
                }

                if (_disposed)
                {
                    return;
                }

                Apply(next);
            }
            while (_refreshPending && !_disposed);
        }
        finally
        {
            _refreshRunning = false;
        }
    }

    private void Apply(TrayState state)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _state = state;
        var spec = TrayPresentation.IconFor(state) with { Palette = _iconPalette, LightTaskbar = _lightTaskbar };
        _windowIcon?.SetState(spec.HdrOn, spec.Paused, spec.GameRunning);
        var size = _iconSize();
        if (_icon is null || _icon.Spec != spec || _icon.Size != size)
        {
            try
            {
                var next = TrayIconRenderer.CreateIcon(spec, size);
                var previous = _icon;
                _icon = next;
                _notifyIcon.Icon = next.Icon;
                previous?.Dispose();
            }
            catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
            {
                // GDI+ am Ende (z. B. zu viele GDI-Objekte im System): altes Symbol behalten, Tooltip trotzdem anpassen.
                _logger.LogWarning(ex, "Tray: Symbol ließ sich nicht zeichnen.");
            }
        }

        ApplyTooltip();
        _menuModel?.Update(state);
    }

    /// <summary>
    /// Farbschema oder Taskleisten-Thema gewechselt: Symbol mit dem letzten Zustand neu zeichnen. Gleiche Farben (etwa nur
    /// die Effektstufe geändert) ergeben dieselbe Spezifikation, dann wird nichts gezeichnet.
    /// </summary>
    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        ReadAppearance();
        Apply(_state);
    }

    private void ReadAppearance()
    {
        try
        {
            var current = _appearance!.Current;
            _iconPalette = IconPalette.From(PaletteEngine.Compute(current.Palette, dark: true, current.AutoTextOnAccent));
            _lightTaskbar = current.LightTaskbar;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Tray: Farben der Darstellung nicht lesbar, es gelten die Auslieferungsfarben.");
        }
    }

    /// <summary>Tooltip aus dem letzten Zustand; startet oder beendet den Countdown des Neustart-Puffers.</summary>
    private void ApplyTooltip()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var now = _now();
        var tooltip = TrayPresentation.TooltipFor(_state, now);
        if (!string.Equals(_notifyIcon.Text, tooltip, StringComparison.Ordinal))
        {
            _notifyIcon.Text = tooltip;
        }

        if (!TrayPresentation.HasCountdown(_state, now))
        {
            StopCountdown();
        }
        else if (_countdown is null)
        {
            // Nur der Text wird nachgeführt (kein neues Lesen des Zustands); endet der Puffer, meldet die
            // Sitzungslogik den neuen Zustand ohnehin über Refresh.
            _countdown = new DispatcherTimer(CountdownInterval, DispatcherPriority.Background, (_, _) => ApplyTooltip(), _host.Dispatcher);
            _countdown.Start();
        }
    }

    private void StopCountdown()
    {
        _countdown?.Stop();
        _countdown = null;
    }

    private void OnRightClick(object? sender, EventArgs e) => Run(ShowMenu);

    private void OnDoubleClick(object? sender, EventArgs e) => Run(_host.ShowMainWindow);

    private void OnBalloonClicked(object? sender, EventArgs e) => Run(_host.ShowMainWindow);

    private void ShowMenu()
    {
        if (_disposed)
        {
            return;
        }

        if (_menu is { IsOpen: true } open)
        {
            // Normalerweise schließt schon das Drücken der rechten Taste das offene Menü. Blieb es ohne Vordergrund offen
            // (Windows hat ihn verweigert), schließt es der nächste Rechtsklick, statt dass es hängen bleibt.
            open.IsOpen = false;
            return;
        }

        var model = new TrayMenuViewModel(_host, _state, ex => _logger.LogError(ex, "Tray-Befehl fehlgeschlagen."));
        var menu = new TrayMenu { DataContext = model, Placement = PlacementMode.MousePoint };
        menu.Closed += OnMenuClosed;
        _menu = menu;
        _menuModel = model;

        // Ohne Vordergrund schließt sich ein Tray-Menü beim Klick daneben nicht (bekanntes Shell-Verhalten). Den Vordergrund
        // bekommt nur das unsichtbare Hilfsfenster, nie das Menü-Popup selbst: das ist nicht aktivierbar (WS_EX_NOACTIVATE),
        // und in den Vordergrund gezwungen holte Windows 11 nach etwa 150 ms das vorherige Fenster zurück, worauf WPF das
        // Menü schloss (E82, geprüft mit echtem Rechtsklick: phase6-traymenu.ps1). Das Menü öffnet nur auf ausdrücklichen
        // Rechtsklick des Nutzers.
        if (_sink is null || !IsWindow(_sink.Handle))
        {
            _sink?.Dispose();
            _sink = new ForegroundSink();
        }

        if (!SetForegroundWindow(_sink.Handle))
        {
            _logger.LogWarning("Tray: Windows hat den Vordergrund für das Menü verweigert; ein Klick daneben schließt es dann nicht.");
        }

        menu.IsOpen = true;

        // Zustand beim Öffnen frisch lesen (z. B. HDR von Hand umgeschaltet).
        Refresh();
    }

    private void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        var menu = (TrayMenu)sender;
        menu.Closed -= OnMenuClosed;

        // Der Befehl des angeklickten Eintrags läuft nach dem Schließen (WPF ruft ihn mit Render-Priorität auf).
        // Die Bindung erst danach lösen, sonst fände er keinen Befehl mehr.
        _host.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            menu.DataContext = null;
            ReturnForeground();
        });
        if (ReferenceEquals(_menu, menu))
        {
            _menu = null;
            _menuModel = null;
        }
    }

    /// <summary>
    /// Nach dem Menü: Hält das unsichtbare Hilfsfenster noch den Vordergrund (Esc, Befehl ohne eigenes Fenster), geht er an
    /// die Taskleiste zurück, die der Rechtsklick aktiviert hatte (so sieht es die Shell-Doku für abgebrochene Tray-Menüs
    /// vor). Sonst bliebe ein unsichtbares Fenster aktiv und Tastendrücke gingen ins Leere.
    /// </summary>
    private void ReturnForeground()
    {
        if (_sink is null || GetForegroundWindow() != _sink.Handle)
        {
            return; // ein anderes Fenster ist schon vorn (Klick daneben, Hauptfenster geöffnet)
        }

        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            _ = SetForegroundWindow(taskbar);
        }
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray-Befehl fehlgeschlagen.");
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? className, string? windowName);

    /// <summary>Unsichtbares Hilfsfenster, das vor dem Öffnen des Menüs in den Vordergrund kommt.</summary>
    private sealed class ForegroundSink : IDisposable
    {
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WM_CLOSE = 0x0010;
        private readonly HwndSource _source;

        public ForegroundSink()
        {
            _source = new HwndSource(new HwndSourceParameters("HdrPilot.TrayMenuOwner")
            {
                WindowStyle = WS_POPUP,
                ExtendedWindowStyle = WS_EX_TOOLWINDOW,
                Width = 0,
                Height = 0,
                PositionX = -32000,
                PositionY = -32000,
            });
            _source.AddHook(IgnoreClose);
        }

        public IntPtr Handle => _source.Handle;

        public void Dispose() => _source.Dispose();

        // Alt+F4, während das Hilfsfenster vorn ist, darf es nicht zerstören (sonst öffneten weitere Menüs ohne Vordergrund).
        // Beim Beenden zerstört Dispose das Fenster direkt, ohne WM_CLOSE.
        private static IntPtr IgnoreClose(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLOSE)
            {
                handled = true;
            }

            return IntPtr.Zero;
        }
    }
}
