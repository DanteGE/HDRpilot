using HdrPilot.Core.Abstractions;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Launchers;
using HdrPilot.Core.Logging;
using HdrPilot.Core.Native;
using HdrPilot.Core.Platform;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.Hosting;

/// <summary>
/// Setzt die Automatik zusammen (Konfiguration, HDR-Steuerung, Überwachung, Sitzungslogik, Protokoll) und wird von
/// Kommandozeile (<c>hdrpilot watch</c>) und App gleichermaßen benutzt.
/// </summary>
public sealed class AutomationHost : IAsyncDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly IAppEnvironment _environment;
    private readonly IContainerDetector _containerDetector;
    private readonly ILogger _logger;
    private readonly LogLevelSwitch _levelSwitch;
    private readonly AutoHdrService _autoHdr;
    private bool _started;
    private readonly FileSystemWatcher? _configWatcher;
    private Timer? _reloadTimer;

    /// <param name="autoHdrLocation">Wohin Auto HDR schreibt (E88); null: siehe <see cref="AutoHdrRegistryLocation.Resolve"/>.</param>
    public AutomationHost(AppPaths paths, IReadOnlyList<ILoggerProvider>? extraLogProviders = null, AutoHdrRegistryLocation? autoHdrLocation = null)
        : this(paths, PhysicalFileSystem.Instance, WindowsRegistry.Instance, SystemEnvironment.Instance, SystemClock.Instance, extraLogProviders, autoHdrLocation: autoHdrLocation)
    {
    }

    /// <param name="containerDetector">Prüft beim Start, ob HDRpilot im Container einer anderen App läuft (Standard: <see cref="ContainerDetector"/>, E84).</param>
    /// <param name="autoHdrLocation">
    /// Wohin Auto HDR schreibt (E88). Null: echter Schlüssel nur mit der Standardablage, mit jeder anderen Ablage deren
    /// Testwurzel (<see cref="AutoHdrRegistryLocation.Resolve"/>).
    /// </param>
    /// <param name="autoHdrStore">Schlüssel UserGpuPreferences (Tests: Attrappe). Null: echte Registry am Ort <paramref name="autoHdrLocation"/>.</param>
    public AutomationHost(
        AppPaths paths,
        IFileSystem fileSystem,
        IRegistry registry,
        IAppEnvironment environment,
        IClock clock,
        IReadOnlyList<ILoggerProvider>? extraLogProviders = null,
        IContainerDetector? containerDetector = null,
        AutoHdrRegistryLocation? autoHdrLocation = null,
        IGpuPreferenceStore? autoHdrStore = null)
    {
        Paths = paths;
        _fileSystem = fileSystem;
        _registry = registry;
        _environment = environment;
        _containerDetector = containerDetector ?? new ContainerDetector(environment);
        Clock = clock;

        _levelSwitch = new LogLevelSwitch(LogLevel.Information);
        FileLog = new RollingFileLoggerProvider(paths.LogDirectory, () => _levelSwitch.Level, clock);
        LoggerFactory = new CompositeLoggerFactory([FileLog, .. extraLogProviders ?? []]);
        _logger = LoggerFactory.CreateLogger("HDRpilot");

        Config = new ConfigService(new ConfigStore(fileSystem, paths.ConfigFile, clock, LoggerFactory.CreateLogger("Config")), clock, LoggerFactory.CreateLogger("Config"));
        _levelSwitch.Level = Config.Current.Settings.LogLevel;
        if (Config.LoadWarning is not null)
        {
            _logger.LogWarning("{Warning}", Config.LoadWarning);
        }

        Hdr = new DisplayConfigHdrController(
            Win32DisplayConfigApi.Instance,
            environment,
            clock,
            new HdrControllerOptions
            {
                HotkeyFallbackProvider = () => Config.Current.Settings.HotkeyFallback,
                FullscreenAppRunning = ShellNative.IsFullscreenAppRunning,
            },
            new Win32HdrHotkeySender(),
            new TypedLogger<DisplayConfigHdrController>(LoggerFactory));

        // Auto HDR je Spiel (E88): Testinstanzen (eigene Ablage) erreichen den echten Schlüssel nie.
        var location = autoHdrLocation ?? AutoHdrRegistryLocation.Resolve(paths, AppPaths.Default(environment));
        _autoHdr = new AutoHdrService(
            Config,
            autoHdrStore ?? new RegistryGpuPreferenceStore(location),
            location,
            fileSystem,
            paths,
            environment,
            clock,
            LoggerFactory.CreateLogger("AutoHdr"));

        Watcher = new PollingProcessWatcher(
            new NtProcessSnapshotProvider(LoggerFactory.CreateLogger("Processes")),
            TimeSpan.FromMilliseconds(Config.Current.Settings.PollIntervalMs),
            LoggerFactory.CreateLogger("Watcher"));

        Sessions = new SessionManager(
            Hdr,
            Config,
            new FileSessionStateStore(fileSystem, paths.SessionStateFile, LoggerFactory.CreateLogger("SessionState")),
            new ShellGameLauncher(),
            clock,
            (name, patterns) => HelperExecutables.IsHelper(name, patterns),
            ForbiddenRoots(environment),
            LoggerFactory.CreateLogger("Sessions"),
            new SessionManagerOptions { AutoHdr = _autoHdr });

        // Frühwarnung: Spielstarts aus Epic und Steam an deren Protokollen erkennen, HDR vor dem Spielprozess.
        LauncherSignals = LauncherSignalService.Create(
            Config, Sessions, fileSystem, registry, environment, clock, LoggerFactory.CreateLogger("LauncherSignals"));

        Config.Changed += OnConfigChanged;

        // Änderungen anderer Prozesse (etwa "hdrpilot games set") ohne Abfrageschleife bemerken.
        try
        {
            _configWatcher = new FileSystemWatcher(paths.ConfigDirectory, Path.GetFileName(paths.ConfigFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _configWatcher.Changed += (_, _) => ScheduleConfigReload();
            _configWatcher.Created += (_, _) => ScheduleConfigReload();
            _configWatcher.Renamed += (_, _) => ScheduleConfigReload();
            _configWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "Konfigurationsordner wird nicht auf fremde Änderungen überwacht.");
        }
    }

    public AppPaths Paths { get; }

    public IClock Clock { get; }

    public ConfigService Config { get; }

    public IHdrController Hdr { get; }

    public PollingProcessWatcher Watcher { get; }

    public SessionManager Sessions { get; }

    /// <summary>Frühwarnung aus den Protokollen von Epic Games Launcher und Steam.</summary>
    public LauncherSignalService LauncherSignals { get; }

    /// <summary>Windows Auto HDR je Spiel und globale Notlösung (E88).</summary>
    public IAutoHdrService AutoHdr => _autoHdr;

    public CompositeLoggerFactory LoggerFactory { get; }

    public RollingFileLoggerProvider FileLog { get; }

    /// <summary>Container-Prüfung dieses Starts (E84); wirft nie. Vor dem Start bereits erledigt.</summary>
    internal Task ContainerCheck { get; private set; } = Task.CompletedTask;

    /// <summary>Erster Zyklus (Wiederanlauf nach Absturz), dann Hintergrundüberwachung.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var s = Config.Current.Settings;
        _logger.LogInformation(
            "HDRpilot {Version} startet. Konfiguration {Config}, Abfrageintervall {Interval} ms, Nachlaufzeit {Grace} s, Windows-Build {Build}.",
            typeof(AutomationHost).Assembly.GetName().Version, Paths.ConfigFile, s.PollIntervalMs, s.GracePeriodSeconds, _environment.WindowsBuild);

        // E84: Im Container einer anderen App (etwa aus der Claude-App gestartet) landen der Autostart und je nach Ordner
        // auch Einstellungen und Protokolle in deren privatem Speicher. Einmal je Start im Hintergrund prüfen, ohne den
        // Start aufzuhalten.
        ContainerCheck = Task.Run(CheckContainer, CancellationToken.None);
        _autoHdr.Start(); // Werte je Spiel vor dem ersten Zyklus abgleichen
        await Sessions.InitializeAsync(Watcher.Poll(), cancellationToken).ConfigureAwait(false);
        Watcher.Start(OnPollAsync);
        LauncherSignals.Start(); // erst nach dem ersten Zyklus: vorher lehnt die Sitzungslogik Vorbereitungen ab
    }

    public Task<LaunchOutcome> LaunchAsync(string gameKey, CancellationToken cancellationToken = default) =>
        Sessions.LaunchAsync(gameKey, cancellationToken);

    public Task OnDisplaysChangedAsync(CancellationToken cancellationToken = default) =>
        Sessions.OnDisplaysChangedAsync(cancellationToken);

    /// <summary>Liest alle Launcher neu ein und überträgt das Ergebnis in die Konfiguration.</summary>
    public async Task<(LibraryScanReport Report, MergeSummary Summary)> RescanAsync(CancellationToken cancellationToken = default)
    {
        var scanners = ScannerCatalog.CreateAll(_fileSystem, _registry, _environment, LoggerFactory);
        var report = await new LibraryScanner(scanners, LoggerFactory.CreateLogger("Scan"))
            .ScanAsync(new ScanOptions { SearchFolders = Config.Current.Settings.SearchFolders }, cancellationToken)
            .ConfigureAwait(false);
        var summary = Config.ApplyScan(report, _fileSystem);
        _logger.LogInformation(
            "Einlesen: {Games} Spiele, {Added} neu, {Missing} nicht mehr installiert, {Errors} Fehler.",
            report.Games.Count, summary.Added, summary.NoLongerInstalled, report.AllErrors.Count);
        return (report, summary);
    }

    public async ValueTask DisposeAsync()
    {
        Config.Changed -= OnConfigChanged;
        _configWatcher?.Dispose();
        _reloadTimer?.Dispose();
        LauncherSignals.Dispose();
        await Watcher.StopAsync().ConfigureAwait(false);
        _autoHdr.Shutdown(); // nach dem letzten Zyklus
        await ContainerCheck.ConfigureAwait(false); // Probe nie liegen lassen (dauert höchstens Sekundenbruchteile)
        _logger.LogInformation("HDRpilot beendet. Gemerkte Zustände bleiben gespeichert und werden beim nächsten Start geprüft.");
        LoggerFactory.Dispose();
    }

    /// <summary>
    /// Prüft, ob HDRpilot im Container einer anderen App läuft (E84), unabhängig davon, wo die Ablage liegt, und warnt dann
    /// im Protokoll (nur dort, Entscheidung des Nutzers). Läuft einmal je Start. Wirft nie.
    /// </summary>
    private void CheckContainer()
    {
        try
        {
            var result = _containerDetector.Detect();
            if (result.Redirected)
            {
                _logger.LogWarning(
                    "HDRpilot läuft im Container der App {Package}: Windows leitet Registry-Einträge wie den Autostart und je nach Ordner auch Einstellungen und Protokolle in deren privaten Speicher um. Bitte HDRpilot über das Startmenü oder die Verknüpfung starten.",
                    result.PackageFolder);
            }

            if (result.Problem is not null)
            {
                _logger.LogInformation("Container-Prüfung mit Probe in {Folder}: {Problem}", result.Folder, result.Problem);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Container-Prüfung fehlgeschlagen.");
        }
    }

    /// <summary>Ein Zyklus der Prozessüberwachung; die Frühwarnung prüft im selben Takt mit (kein eigener Zeitgeber).</summary>
    private Task OnPollAsync(ProcessChanges changes, CancellationToken cancellationToken)
    {
        LauncherSignals.Tick();
        return Sessions.UpdateAsync(changes, cancellationToken);
    }

    private void ScheduleConfigReload()
    {
        // Entprellen: atomares Schreiben löst mehrere Ereignisse aus.
        var timer = _reloadTimer ??= new Timer(_ =>
        {
            try
            {
                Config.ReloadIfChangedOnDisk();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Konfiguration ließ sich nicht neu laden.");
            }
        });
        timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        var s = Config.Current.Settings;
        Watcher.Interval = TimeSpan.FromMilliseconds(s.PollIntervalMs);
        _levelSwitch.Level = s.LogLevel;
        LauncherSignals.ApplySettings();
        _autoHdr.OnConfigChanged();
    }

    /// <summary>Ordner, die nie als Installationsordner gelten dürfen.</summary>
    private static IReadOnlyList<string> ForbiddenRoots(IAppEnvironment env) =>
    [
        env.GetFolderPath(Environment.SpecialFolder.Windows),
        env.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        env.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        env.GetFolderPath(Environment.SpecialFolder.UserProfile),
        env.GetFolderPath(Environment.SpecialFolder.Desktop),
        env.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        env.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        env.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        env.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    ];
}

/// <summary>Veränderbare Protokollstufe (Einstellung wirkt sofort).</summary>
public sealed class LogLevelSwitch(LogLevel level)
{
    public LogLevel Level { get; set; } = level;
}

/// <summary>ILogger&lt;T&gt; über die eigene Factory, ohne Microsoft.Extensions.Logging einzubinden.</summary>
public sealed class TypedLogger<T>(ILoggerFactory factory) : ILogger<T>
{
    private readonly ILogger _inner = factory.CreateLogger(typeof(T).Name);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _inner.Log(logLevel, eventId, state, exception, formatter);
}
