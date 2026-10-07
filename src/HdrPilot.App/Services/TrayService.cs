using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
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

    void SetPaused(bool paused);

    void ShowMainWindow();

    void ShowSettingsWindow();

    void OpenLogFolder();

    Task ExitAsync();
}

/// <summary>
/// Tray-Symbol (WinForms-NotifyIcon) mit WPF-Kontextmenü im HdrPilot-Stil. Das Symbol wird zur Laufzeit passend zur
/// DPI gezeichnet und nur bei Zustandswechsel neu erzeugt; alte HICONs werden freigegeben. Im Hintergrund läuft nur
/// ein Timer (alle 30 s), der HDR-Wechsel außerhalb von HdrPilot nachträgt. Das Menü wird erst beim Öffnen erzeugt
/// und nach dem Schließen verworfen.
/// </summary>
public sealed partial class TrayService : ITrayService
{
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(30);

    private const int BalloonTimeoutMs = 4000;
    private const int MaxBalloonTitle = 63;
    private const int MaxBalloonText = 255;

    private readonly ITrayHost _host;
    private readonly NotificationGate _gate;
    private readonly ILogger _logger;
    private Forms.NotifyIcon? _notifyIcon;
    private TrayIcon? _icon;
    private TrayState _state = TrayState.Initial;
    private DispatcherTimer? _recheck;
    private ForegroundSink? _sink;
    private TrayMenu? _menu;
    private TrayMenuViewModel? _menuModel;
    private bool _refreshRunning;
    private bool _refreshPending;
    private bool _disposed;

    public TrayService(ITrayHost host, NotificationGate gate, ILogger? logger = null)
    {
        _host = host;
        _gate = gate;
        _logger = logger ?? NullLogger.Instance;
    }

    public void Initialize()
    {
        _host.Dispatcher.VerifyAccess();
        if (_notifyIcon is not null || _disposed)
        {
            return;
        }

        _notifyIcon = new Forms.NotifyIcon();
        _notifyIcon.MouseUp += OnMouseUp;
        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
        _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;

        try
        {
            _state = _host.QueryTrayState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tray: Anfangszustand nicht lesbar.");
        }

        Apply(_state);
        _notifyIcon.Visible = true;

        _recheck = new DispatcherTimer(RecheckInterval, DispatcherPriority.Background, (_, _) => Refresh(), _host.Dispatcher);
        _recheck.Start();
    }

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

        // Windows zeigt das als Benachrichtigung an; sie nimmt nie den Fokus.
        _notifyIcon.ShowBalloonTip(
            BalloonTimeoutMs,
            TrayPresentation.Fit(title, MaxBalloonTitle),
            TrayPresentation.Fit(text, MaxBalloonText),
            isWarning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recheck?.Stop();
        _recheck = null;
        if (_menu is not null)
        {
            _menu.IsOpen = false;
            _menu = null;
            _menuModel = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.MouseUp -= OnMouseUp;
            _notifyIcon.MouseDoubleClick -= OnMouseDoubleClick;
            _notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;
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
        var spec = TrayPresentation.IconFor(state);
        var size = TrayIconRenderer.SystemSmallIconSize();
        if (_icon is null || _icon.Spec != spec || _icon.Size != size)
        {
            var previous = _icon;
            _icon = TrayIconRenderer.CreateIcon(spec, size);
            _notifyIcon.Icon = _icon.Icon;
            previous?.Dispose();
        }

        var tooltip = TrayPresentation.TooltipFor(state);
        if (!string.Equals(_notifyIcon.Text, tooltip, StringComparison.Ordinal))
        {
            _notifyIcon.Text = tooltip;
        }

        _menuModel?.Update(state);
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right)
        {
            ShowMenu();
        }
    }

    private void OnMouseDoubleClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            Run(_host.ShowMainWindow);
        }
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e) => Run(_host.ShowMainWindow);

    private void ShowMenu()
    {
        if (_disposed || _menu is { IsOpen: true })
        {
            return;
        }

        var model = new TrayMenuViewModel(_host, _state, ex => _logger.LogError(ex, "Tray-Befehl fehlgeschlagen."));
        var menu = new TrayMenu { DataContext = model, Placement = PlacementMode.MousePoint };
        menu.Closed += OnMenuClosed;
        _menu = menu;
        _menuModel = model;

        // Ohne Vordergrund schließt sich ein Tray-Menü beim Klick daneben nicht (bekanntes Shell-Verhalten).
        // Das Menü öffnet nur auf ausdrücklichen Rechtsklick des Nutzers.
        _sink ??= new ForegroundSink();
        _ = SetForegroundWindow(_sink.Handle);
        menu.IsOpen = true;
        if (PresentationSource.FromVisual(menu) is HwndSource popup && popup.Handle != IntPtr.Zero)
        {
            _ = SetForegroundWindow(popup.Handle);
        }

        // Zustand beim Öffnen frisch lesen (z. B. HDR von Hand umgeschaltet).
        Refresh();
    }

    private void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        var menu = (TrayMenu)sender;
        menu.Closed -= OnMenuClosed;
        menu.DataContext = null;
        if (ReferenceEquals(_menu, menu))
        {
            _menu = null;
            _menuModel = null;
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

    /// <summary>Unsichtbares Hilfsfenster, das vor dem Öffnen des Menüs in den Vordergrund kommt.</summary>
    private sealed class ForegroundSink : IDisposable
    {
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_TOOLWINDOW = 0x00000080;
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
        }

        public IntPtr Handle => _source.Handle;

        public void Dispose() => _source.Dispose();
    }
}
