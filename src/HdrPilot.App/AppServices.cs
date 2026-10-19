using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Theming;
using HdrPilot.App.Wrapper;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.AutoHdr;
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
/// Teilklassen: AppServices.Library.cs (Spieleliste, Symbole, Verknüpfungen), AppServices.Shell.cs
/// (Tray, Einstellungen, Autostart, Bildschirmereignisse), AppServices.HdrList.cs (HDR-Liste von PCGamingWiki),
/// AppServices.Appearance.cs (Darstellung: Farben, Thema, Effekte) und AppServices.Artwork.cs (Spielbilder, SteamGridDB,
/// Helligkeit der Bildschirme), E89.
/// </summary>
public sealed partial class AppServices : IAsyncDisposable
{
    private readonly ILogger _logger;
    private Window? _mainWindow;
    private Window? _settingsWindow;
    private int _scanRunning;
    private UiMemoryRelease? _memoryRelease;

    public AppServices(AutomationHost host, Dispatcher dispatcher, SingleInstance? instance)
    {
        Host = host;
        Dispatcher = dispatcher;
        Instance = instance;
        _logger = host.LoggerFactory.CreateLogger("App");
        FileSystem = PhysicalFileSystem.Instance;
        Registry = WindowsRegistry.Instance;
        Environment = SystemEnvironment.Instance;

        CreateHdrList();
        Appearance = CreateAppearanceService();
        Icons = CreateIconService();
        Artwork = CreateArtworkService();
        SteamGridDb = CreateSteamGridDbService();
        Luminance = CreateDisplayLuminanceService();
        Shortcuts = CreateShortcutService();
        GameCleanup = new UninstalledGameCleanup(FileSystem, Paths.IconDirectory, Paths.CustomArtDirectory, Icons, Artwork, Shortcuts, LoggerFactory.CreateLogger("Bibliothek"));
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

    /// <summary>Windows Auto HDR (E88): globalen Schalter lesen und durch den Nutzer schalten (Tray-Eintrag „Auto HDR“, G9.9).</summary>
    public IAutoHdrService AutoHdr => Host.AutoHdr;

    public AppPaths Paths => Host.Paths;

    public IFileSystem FileSystem { get; }

    public IRegistry Registry { get; }

    public IAppEnvironment Environment { get; }

    public ITrayService Tray { get; }

    public IGameIconService Icons { get; }

    /// <summary>Darstellung: Farbschema, Thema, Effektstufe (E89).</summary>
    public IAppearanceService Appearance { get; }

    /// <summary>Spielbilder aus lokalen Launcher-Caches samt Ersatzbildern und eigenen Bildern (E89).</summary>
    public IArtworkService Artwork { get; }

    /// <summary>SteamGridDB (optional, Standard aus) für lokal fehlende Bilder (E89).</summary>
    public ISteamGridDbService SteamGridDb { get; }

    /// <summary>Echte Helligkeitswerte der Bildschirme über DXGI (E89); nur Bildschirmfotos setzen feste Werte ein.</summary>
    public IDisplayLuminanceService Luminance { get; private set; }

    public IShortcutService Shortcuts { get; }

    /// <summary>Aufräumen nach „Einstellungen entfernen…“ nicht installierter Spiele (E90), für Bibliothek und Einstellungen.</summary>
    public UninstalledGameCleanup GameCleanup { get; }

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

        // HDR-Liste von PCGamingWiki: einmaliger Zeitgeber, später im Hintergrund (AppServices.HdrList.cs).
        StartHdrList();

        // SteamGridDB (E89): nur eingeschaltet, mit Schlüssel und normaler Ablage; mit --config-dir nie von selbst ins Netz.
        SteamGridDb.ScheduleStartup(UsesDefaultPaths);

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

            // Steam-Startoption: der Wrapper wartet, also zuerst. Er bekommt immer eine Antwort.
            if (args.PrepareGameKey is { } prepareKey)
            {
                await PrepareRequestHandler.HandleAsync(
                    prepareKey, args.ReadyEvent, key => Sessions.PrepareAsync(key, SessionTrigger.SteamLaunchOption), _logger);
                RaiseHdrStateChanged();
            }
            else if (args.ReadyEvent is { } readyEvent)
            {
                PrepareRequestHandler.Answer(readyEvent, prepared: false, _logger);
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

            // Prüfung (G10.9): HDR-Moment ohne echte HDR-Schaltung, nur in Testinstanzen mit eigener Ablage.
            if (args.TestHdrMoment && !UsesDefaultPaths)
            {
                PlayTestHdrMoment();
            }

            // Prüfung (G10.5): Strg+F in der Bibliothek ohne Tastatureingabe, ebenso nur in Testinstanzen.
            if (args.TestToolbarSearch && !UsesDefaultPaths)
            {
                RunTestToolbarSearch();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Befehl konnte nicht ausgeführt werden.");
        }
    }

    public async Task<LaunchOutcome> LaunchAsync(string gameKey)
    {
        _logger.LogInformation("Start über HDRpilot angefordert: {Key}", gameKey);
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

        // Auch von Hand (Infobereich, Bibliothek, --toggle-hdr per Tastenkürzel): die Notlösung nie, solange ein Spiel läuft (E66).
        var allowHotkey = Sessions.HotkeyFallbackAllowed;
        foreach (var d in targets)
        {
            var result = await Hdr.SetHdrAsync(d.MonitorDevicePath, enable, allowHotkey);
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
                MemoryRelease.WindowClosed();
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
                MemoryRelease.WindowClosed();
            };
            _settingsWindow.Show();
        }

        BringToFront(_settingsWindow);
    }

    /// <summary>
    /// Entwicklung: Fenster in ihren Zuständen hell und dunkel unsichtbar als PNG rendern (siehe SnapshotRenderer). Die
    /// Zustände liefern die Teilklassen: Bibliothek (AppServices.Library.cs) und Einstellungen (AppServices.Shell.cs).
    /// </summary>
    public Task<IReadOnlyList<string>> RenderSnapshotsAsync(string directory, SnapshotPlan plan)
    {
        if (plan.Display is { } display)
        {
            // Vor dem ersten Fenster: Kapsel, Skala und Vorschau zeigen die vorgegebenen Werte (Monitor aus, DXGI ohne Werte).
            Luminance.Dispose();
            Luminance = new FixedDisplayLuminanceService(display);
        }

        var scenes = new List<SnapshotScene>();
        AddLibrarySnapshotScenes(scenes);
        AddSettingsSnapshotScenes(scenes);
        return SnapshotRenderer.RenderAsync(System.Windows.Application.Current, scenes, plan, directory);
    }

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
        StopHdrList();
        Tray.Dispose();
        SteamGridDb.Dispose();
        Artwork.Dispose();
        Luminance.Dispose();
        Appearance.Dispose();
        await Host.DisposeAsync();
        Instance?.Dispose();
    }

    public void RaiseHdrStateChanged() => RaiseOnUi(() =>
    {
        // Helligkeitswerte nur nach HDR- oder Bildschirmwechsel neu lesen (E89); ohne offenes Fenster liest der Dienst nichts.
        Luminance.Invalidate();
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

    /// <summary>
    /// Nach dem Schließen eines Fensters: sofort und noch einmal, wenn alles zur Ruhe gekommen ist (Bilddienst aufgeräumt,
    /// Grafikspeicher freigegeben, siehe <see cref="UiMemoryRelease"/>). Nichts läuft wiederholt.
    /// </summary>
    private UiMemoryRelease MemoryRelease => _memoryRelease ??= new UiMemoryRelease(new UiMemoryReleaseOptions
    {
        AnyWindowOpen = () => _mainWindow is not null || _settingsWindow is not null,
        CleanupFinished = () => Artwork.WhenReleasedAsync(),
        FlushGraphics = FlushGraphicsMemoryAsync,
        Collect = thorough =>
        {
            if (!thorough)
            {
                ReleaseUiMemory();
                return;
            }

            using var process = Process.GetCurrentProcess();
            var before = process.PrivateMemorySize64;
            ReleaseUiMemory(thorough: true);
            process.Refresh();
            _logger.LogDebug("Speicher nach dem Schließen zurückgegeben: {Before} MB → {After} MB festgeschrieben.", before / (1024 * 1024), process.PrivateMemorySize64 / (1024 * 1024));
        },
        UiIdle = async () => await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle),
    });

    /// <summary>Grafikspeicher geschlossener Fenster freigeben (Hilfsfenster hinter dem Desktop); Fehler nur ins Protokoll.</summary>
    private async Task FlushGraphicsMemoryAsync()
    {
        try
        {
            await GraphicsMemoryFlush.RunAsync();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ExternalException)
        {
            _logger.LogDebug("Grafikspeicher nicht freigegeben: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Speicher zurückgeben, damit HdrPilot im Hintergrund schlank bleibt. <paramref name="thorough"/> (einmal, wenn nach dem
    /// Schließen alles zur Ruhe gekommen ist): auch den Speicher großer Objekte verdichten und freien Speicher des GC an
    /// Windows zurückgeben.
    /// </summary>
    private static void ReleaseUiMemory(bool thorough = false)
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        if (thorough)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        else
        {
            GC.Collect();
        }

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

    // Showcase Hero (E89): Darstellung (AppServices.Appearance.cs), Spielbilder, SteamGridDB und Helligkeit
    // (AppServices.Artwork.cs).
    private partial IAppearanceService CreateAppearanceService();

    private partial IArtworkService CreateArtworkService();

    private partial ISteamGridDbService CreateSteamGridDbService();

    private partial IDisplayLuminanceService CreateDisplayLuminanceService();

    /// <summary>Entwicklung (G10.3): Abdeckung mit lokalen Bildern als JSON schreiben (AppServices.Artwork.cs).</summary>
    public partial Task WriteArtworkReportAsync(string path);

    /// <summary>Zustände der Bibliothek für Bildschirmfotos (AppServices.Library.cs).</summary>
    partial void AddLibrarySnapshotScenes(List<SnapshotScene> scenes);

    /// <summary>Zustände der Einstellungen für Bildschirmfotos (AppServices.Shell.cs).</summary>
    partial void AddSettingsSnapshotScenes(List<SnapshotScene> scenes);

    /// <summary>Prüfung: HDR-Moment im offenen Hauptfenster abspielen, ohne HDR zu schalten (AppServices.Library.cs).</summary>
    partial void PlayTestHdrMoment();

    partial void RunTestToolbarSearch();
}
