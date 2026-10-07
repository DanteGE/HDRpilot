using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Launchers;
using HdrPilot.Core.Logging;
using HdrPilot.Core.Native;
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
    private readonly ILogger _logger;
    private readonly LogLevelSwitch _levelSwitch;
    private bool _started;
    private readonly FileSystemWatcher? _configWatcher;
    private Timer? _reloadTimer;

    public AutomationHost(AppPaths paths, IReadOnlyList<ILoggerProvider>? extraLogProviders = null)
        : this(paths, PhysicalFileSystem.Instance, WindowsRegistry.Instance, SystemEnvironment.Instance, SystemClock.Instance, extraLogProviders)
    {
    }

    public AutomationHost(
        AppPaths paths, IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, IClock clock, IReadOnlyList<ILoggerProvider>? extraLogProviders = null)
    {
        Paths = paths;
        _fileSystem = fileSystem;
        _registry = registry;
        _environment = environment;
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
            LoggerFactory.CreateLogger("Sessions"));

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

    public CompositeLoggerFactory LoggerFactory { get; }

    public RollingFileLoggerProvider FileLog { get; }

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
        _logger.LogInformation("HDRpilot beendet. Gemerkte Zustände bleiben gespeichert und werden beim nächsten Start geprüft.");
        LoggerFactory.Dispose();
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
