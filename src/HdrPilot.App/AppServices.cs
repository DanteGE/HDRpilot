using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Hosting;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App;

/// <summary>
/// Zentrale der App: hält die Automatik (<see cref="AutomationHost"/>), die Dienste und die Fenster.
/// Fenster werden beim Schließen vollständig freigegeben, damit im Hintergrund nur Tray und Überwachung laufen.
/// Teilklassen: AppServices.Library.cs (Spieleliste, Symbole, Verknüpfungen) und AppServices.Shell.cs
/// (Tray, Einstellungen, Autostart, Bildschirmereignisse).
/// </summary>
public sealed partial class AppServices : IAsyncDisposable
{
    private readonly ILogger _logger;
    private Window? _mainWindow;
    private Window? _settingsWindow;
    private int _scanRunning;

    public AppServices(AutomationHost host, Dispatcher dispatcher, SingleInstance? instance)
    {
        Host = host;
        Dispatcher = dispatcher;
        Instance = instance;
        _logger = host.LoggerFactory.CreateLogger("App");
        FileSystem = PhysicalFileSystem.Instance;
        Registry = WindowsRegistry.Instance;
        Environment = SystemEnvironment.Instance;

        Icons = CreateIconService();
        Shortcuts = CreateShortcutService();
        Autostart = CreateAutostartService();
        DisplayEvents = CreateDisplayEventsService();
        Tray = CreateTrayService();

        host.Sessions.Notification += OnSessionNotification;
        host.Sessions.StateChanged += (_, _) => RaiseHdrStateChanged();
        DisplayEvents.DisplaysChanged += OnDisplaysChanged;
    }

    public AutomationHost Host { get; }

    public Dispatcher Dispatcher { get; }

    public SingleInstance? Instance { get; }

    public ConfigService Config => Host.Config;

    public SessionManager Sessions => Host.Sessions;

    public IHdrController Hdr => Host.Hdr;

    public AppPaths Paths => Host.Paths;

    public IFileSystem FileSystem { get; }

    public IRegistry Registry { get; }

    public IAppEnvironment Environment { get; }

    public ITrayService Tray { get; }

    public IGameIconService Icons { get; }

    public IShortcutService Shortcuts { get; }

    public IAutostartService Autostart { get; }

    public IDisplayEventsService DisplayEvents { get; }

    public ILoggerFactory LoggerFactory => Host.LoggerFactory;

    /// <summary>Ergebnis des letzten Einlesens (Fehler je Scanner für die Oberfläche).</summary>
    public LibraryScanReport? LastScan { get; private set; }

    public bool IsScanning => Volatile.Read(ref _scanRunning) == 1;

    /// <summary>Einlesen begonnen oder beendet (auf dem UI-Thread).</summary>
    public event EventHandler? ScanStateChanged;

    /// <summary>HDR-Zustand könnte sich geändert haben (auf dem UI-Thread).</summary>
    public event EventHandler? HdrStateChanged;

    public bool IsMainWindowOpen => _mainWindow is not null;

    public async Task StartAsync()
    {
        await Host.StartAsync();
        Tray.Initialize();
        DisplayEvents.Start();
        Instance?.Listen(args => _ = HandleCommandAsync(AppArguments.Parse(args)));
        // Bei jedem Start im Hintergrund einlesen (rund 0,6 s): neue Spiele erscheinen markiert und aus,
        // deinstallierte werden als "nicht installiert" markiert, und die Fehler je Scanner sind sofort sichtbar.
        var firstRun = Config.Current.Games.Count == 0;
        _ = RescanAsync();
        if (firstRun)
        {
            Tray.Notify(Resources.CommonStrings.FirstRunTitle, Resources.CommonStrings.FirstRunMessage);
        }

        // Ohne offenes Fenster läuft HdrPilot nur als Tray: Anlauf-Speicher gleich zurückgeben.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (_mainWindow is null && _settingsWindow is null)
            {
                ReleaseUiMemory();
            }
        });
    }

    public async Task HandleCommandAsync(AppArguments args)
    {
        try
        {
            if (args.Exit)
            {
                await ExitAsync();
                return;
            }

            if (args.Show)
            {
                ShowMainWindow();
            }

            if (args.OpenSettings)
            {
                ShowSettingsWindow();
            }

            if (args.Pause is { } pause)
            {
                SetPaused(pause);
            }

            if (args.ToggleHdr)
            {
                await ToggleHdrNowAsync();
            }

            if (args.Rescan)
            {
                await RescanAsync();
            }

            if (args.RunGameKey is { } key)
            {
                await LaunchAsync(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Befehl konnte nicht ausgeführt werden.");
        }
    }

    public async Task<LaunchOutcome> LaunchAsync(string gameKey)
    {
        _logger.LogInformation("Start über HdrPilot angefordert: {Key}", gameKey);
        var outcome = await Host.LaunchAsync(gameKey);
        if (!outcome.Started)
        {
            Tray.Notify(Resources.CommonStrings.LaunchFailedTitle, outcome.Message, isWarning: true);
        }

        RaiseHdrStateChanged();
        return outcome;
    }

    public async Task<LibraryScanReport?> RescanAsync()
    {
        if (Interlocked.Exchange(ref _scanRunning, 1) == 1)
        {
            return LastScan;
        }

        RaiseOnUi(() => ScanStateChanged?.Invoke(this, EventArgs.Empty));
        try
        {
            var (report, _) = await Task.Run(() => Host.RescanAsync());
            LastScan = report;
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Einlesen fehlgeschlagen.");
            return LastScan;
        }
        finally
        {
            Volatile.Write(ref _scanRunning, 0);
            RaiseOnUi(() =>
            {
                ScanStateChanged?.Invoke(this, EventArgs.Empty);

                // Das Einlesen liest große Launcher-Dateien (Steam appinfo.vdf rund 15 MB): danach Speicher zurückgeben,
                // wenn kein Fenster offen ist, damit HdrPilot im Hintergrund schlank bleibt.
                if (_mainWindow is null && _settingsWindow is null)
                {
                    ReleaseUiMemory();
                }
            });
        }
    }

    /// <summary>Bildschirme, die HdrPilot schaltet: Einstellung, sonst der Hauptbildschirm.</summary>
    public IReadOnlyList<DisplayInfo> TargetDisplays()
    {
        var displays = Hdr.GetDisplays();
        var targets = Config.Current.Settings.TargetDisplays;
        if (targets.Count > 0)
        {
            return displays.Where(d => targets.Contains(d.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        return displays.Where(d => d.IsPrimary).Take(1).DefaultIfEmpty(displays.FirstOrDefault()).OfType<DisplayInfo>().ToList();
    }

    /// <summary>True, wenn HDR auf mindestens einem Zielbildschirm an ist.</summary>
    public bool IsHdrOn() => TargetDisplays().Any(d => d.HdrEnabled);

    /// <summary>Tray: HDR jetzt umschalten (ohne Spiel, wirkt auf die Zielbildschirme).</summary>
    public async Task ToggleHdrNowAsync()
    {
        var targets = TargetDisplays().Where(d => d.HdrSupported).ToList();
        if (targets.Count == 0)
        {
            Tray.Notify(Resources.CommonStrings.NoHdrDisplayTitle, Resources.CommonStrings.NoHdrDisplayMessage, isWarning: true);
            return;
        }

        var enable = !targets.Any(d => d.HdrEnabled);
        foreach (var d in targets)
        {
            // Ausdrücklich von Hand im Infobereich: die Notlösung ist erlaubt, wenn sie eingeschaltet ist.
            var result = await Hdr.SetHdrAsync(d.MonitorDevicePath, enable, allowHotkeyFallback: true);
            _logger.Log(
                result.IsSuccess ? LogLevel.Information : LogLevel.Warning,
                "HDR-Schaltung | Auslöser: von Hand (Tray) | Bildschirm: {Display} | vorher: {Before} | nachher: {After} | Ergebnis: {Status} | {Message}",
                d.FriendlyName, result.Before, result.After, result.Status, result.Message);
        }

        RaiseHdrStateChanged();
    }

    public void SetPaused(bool paused)
    {
        Sessions.Paused = paused;
        RaiseHdrStateChanged();
    }

    public void ShowMainWindow()
    {
        Dispatcher.VerifyAccess();
        if (_mainWindow is null)
        {
            _mainWindow = CreateMainWindow();
            _mainWindow.Closed += (_, _) =>
            {
                _mainWindow = null;
                ReleaseUiMemory();
            };
            _mainWindow.Show();
        }

        BringToFront(_mainWindow);
    }

    public void ShowSettingsWindow()
    {
        Dispatcher.VerifyAccess();
        if (_settingsWindow is null)
        {
            _settingsWindow = CreateSettingsWindow();
            if (_mainWindow is not null)
            {
                _settingsWindow.Owner = _mainWindow;
            }

            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                ReleaseUiMemory();
            };
            _settingsWindow.Show();
        }

        BringToFront(_settingsWindow);
    }

    /// <summary>Entwicklung: Fenster hell und dunkel unsichtbar als PNG rendern (siehe SnapshotRenderer).</summary>
    public Task RenderSnapshotsAsync(string directory) => SnapshotRenderer.RenderAsync(
        System.Windows.Application.Current,
        [("hauptfenster", CreateMainWindow), ("einstellungen", CreateSettingsWindow)],
        directory);

    public void OpenLogFolder()
    {
        System.IO.Directory.CreateDirectory(Paths.LogDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.LogDirectory}\"") { UseShellExecute = true })?.Dispose();
    }

    public async Task ExitAsync()
    {
        _settingsWindow?.Close();
        _mainWindow?.Close();
        await DisposeAsync();
        System.Windows.Application.Current.Shutdown();
    }

    public async ValueTask DisposeAsync()
    {
        DisplayEvents.DisplaysChanged -= OnDisplaysChanged;
        DisplayEvents.Dispose();
        Tray.Dispose();
        await Host.DisposeAsync();
        Instance?.Dispose();
    }

    public void RaiseHdrStateChanged() => RaiseOnUi(() =>
    {
        Tray.Refresh();
        HdrStateChanged?.Invoke(this, EventArgs.Empty);
    });

    public void RaiseOnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.BeginInvoke(action);
        }
    }

    private void OnSessionNotification(object? sender, SessionNotification n)
    {
        var warning = n.Kind is NotificationKind.SwitchFailed or NotificationKind.DisplayNotConnected
            or NotificationKind.DisplayNotHdrCapable or NotificationKind.LaunchFailed or NotificationKind.StateNotSaved;
        RaiseOnUi(() => Tray.Notify(n.Title, n.Message, warning));
    }

    private async void OnDisplaysChanged(object? sender, EventArgs e)
    {
        try
        {
            await Host.OnDisplaysChangedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei der Bildschirmänderung.");
        }

        RaiseHdrStateChanged();
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    /// <summary>Nach dem Schließen eines Fensters: Speicher zurückgeben, damit HdrPilot im Hintergrund schlank bleibt.</summary>
    private static void ReleaseUiMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        SetProcessWorkingSetSize(process.Handle, -1, -1);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(IntPtr process, nint minimumWorkingSetSize, nint maximumWorkingSetSize);

    // Werden in den Teilklassen umgesetzt.
    private partial IGameIconService CreateIconService();

    private partial IShortcutService CreateShortcutService();

    private partial Window CreateMainWindow();

    private partial ITrayService CreateTrayService();

    private partial IAutostartService CreateAutostartService();

    private partial IDisplayEventsService CreateDisplayEventsService();

    private partial Window CreateSettingsWindow();
}
