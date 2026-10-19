using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Setup;
using HdrPilot.App.Wrapper;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hosting;
using HdrPilot.Core.Launch;

namespace HdrPilot.App;

/// <summary>
/// Start: Steam-Startoption erkennen (dann nur Wrapper, siehe <see cref="SteamWrapperMode"/>), Setup-Datei oder
/// Entfernen erkennen (dann nur deren Fenster, siehe <see cref="SetupMode"/>), Einzelinstanz prüfen
/// (sonst Befehle weiterreichen), Automatik starten (inklusive Wiederanlauf nach Absturz), Tray anzeigen. Das Fenster
/// öffnet sich nur auf Wunsch; HdrPilot startet minimiert.
/// </summary>
public partial class App : Application
{
    private AppServices? _services;
    private AutomationHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Steam-Startoption (--wrap -- %command%): vor allem anderen, ohne Fenster, Tray und Design. HDR vorbereiten
        // lassen, das Spiel starten, sofort enden. Die Tray-App startet hier nie: Steam zählt alle Nachkommen zum Spiel.
        if (WrapCommandLine.TryParse(RawCommandLine.Get()) is { } wrap)
        {
            try
            {
                await Task.Run(() => SteamWrapperMode.Run(wrap));
            }
            catch (Exception)
            {
                // Der Wrapper fängt selbst alles ab; hier nur, damit nie ein Fehlerdialog im Spielstart erscheint.
            }

            Shutdown(0);
            return;
        }

        // Setup-Datei oder Entfernen (E83): eigene Fenster, nie Einzelinstanz, Automatik, Tray oder Konfiguration.
        if (SetupMode.TryDetect(Environment.ProcessPath, e.Args, out var setupMode))
        {
            var code = await SetupMode.RunAsync(this, setupMode);
            Environment.ExitCode = code;
            Shutdown(code);
            return;
        }

        var args = AppArguments.Parse(e.Args);
        if (args.Wrap)
        {
            // Nur zur Sicherheit (die rohe Befehlszeile ergab keinen Spielbefehl): auch dann nie die Tray-App starten.
            Shutdown(0);
            return;
        }

        if (args.SnapshotDir is { } snapshotDir)
        {
            await RunSnapshotModeAsync(args, snapshotDir);
            return;
        }

        if (args.ArtworkReport is { } artworkReport)
        {
            await RunArtworkReportModeAsync(args, artworkReport);
            return;
        }

        if (args.SettingsProbe is { } settingsProbe)
        {
            await RunSettingsProbeModeAsync(args, settingsProbe);
            return;
        }

        var instanceId = SingleInstance.IdFor(args.ConfigDir);
        var instance = SingleInstance.TryAcquire(instanceId);
        if (instance is null)
        {
            // Zweiter Aufruf: an die laufende Instanz weiterreichen (ohne Befehl: Fenster öffnen, Autostart: nichts).
            var forward = args.ToRunningInstance();
            if (forward.Count > 0 && !SingleInstance.TrySend(instanceId, forward, TimeSpan.FromSeconds(5)))
            {
                MessageBox.Show(HdrPilot.App.Resources.CommonStrings.ForwardFailed, HdrPilot.App.Resources.CommonStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Shutdown(0);
            return;
        }

        ThemeManager.Initialize(this);
        AsyncRelayCommand.ErrorHandler = ex => ReportError(ex);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => LogFatal(ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            LogFatal(ev.Exception);
            ev.SetObserved();
        };

        var paths = args.ConfigDir is { } dir ? AppPaths.InDirectory(System.IO.Path.GetFullPath(dir)) : AppPaths.Default(SystemEnvironment.Instance);

        // Auto HDR (E88): mit eigener Ablage immer eine Testwurzel, nie der echte Schlüssel von Windows.
        _host = new AutomationHost(paths, autoHdrLocation: AutoHdrRegistryLocation.Resolve(paths, AppPaths.Default(SystemEnvironment.Instance), args.AutoHdrTestRoot));
        _services = new AppServices(_host, Dispatcher, instance);
        try
        {
            await _services.StartAsync();
            await _services.HandleCommandAsync(args);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }

    /// <summary>
    /// Entwicklung: Bildschirmfotos der Fenster rendern (eigene Ablage nötig), dann beenden. Mit --snapshot-sizes,
    /// --snapshot-scenes und --snapshot-themes in festen Größen und Zuständen (E89); ungültige Angaben: Exit-Code 2.
    /// </summary>
    private async Task RunSnapshotModeAsync(AppArguments args, string snapshotDir)
    {
        if (!args.HasOwnConfigDir(AppPaths.Default(SystemEnvironment.Instance)))
        {
            Shutdown(2); // nie gegen die echte Konfiguration oder den echten Datenordner
            return;
        }

        SnapshotPlan plan;
        try
        {
            plan = SnapshotPlan.Parse(args.SnapshotSizes, args.SnapshotScenes, args.SnapshotThemes, args.SnapshotDisplay);
        }
        catch (FormatException)
        {
            Shutdown(2);
            return;
        }

        ThemeManager.Initialize(this);
        var paths = AppPaths.InDirectory(System.IO.Path.GetFullPath(args.ConfigDir));
        _host = new AutomationHost(paths, autoHdrLocation: AutoHdrRegistryLocation.Resolve(paths, AppPaths.Default(SystemEnvironment.Instance), args.AutoHdrTestRoot));
        _services = new AppServices(_host, Dispatcher, instance: null);
        try
        {
            await _services.RenderSnapshotsAsync(System.IO.Path.GetFullPath(snapshotDir), plan);
        }
        catch (Exception ex)
        {
            LogFatal(ex);
            await _services.DisposeAsync();
            Shutdown(1);
            return;
        }

        await _services.DisposeAsync();
        Shutdown(0);
    }

    /// <summary>
    /// Entwicklung (G10.3): Abdeckung der Bibliothek mit lokalen Bildern als JSON schreiben (eigene Ablage nötig, Launcher-Caches
    /// nur lesend), dann beenden.
    /// </summary>
    private async Task RunArtworkReportModeAsync(AppArguments args, string reportFile)
    {
        if (!args.HasOwnConfigDir(AppPaths.Default(SystemEnvironment.Instance)))
        {
            Shutdown(2); // nie gegen die echte Konfiguration oder den echten Datenordner
            return;
        }

        ThemeManager.Initialize(this);
        var paths = AppPaths.InDirectory(System.IO.Path.GetFullPath(args.ConfigDir));
        _host = new AutomationHost(paths, autoHdrLocation: AutoHdrRegistryLocation.Resolve(paths, AppPaths.Default(SystemEnvironment.Instance), args.AutoHdrTestRoot));
        _services = new AppServices(_host, Dispatcher, instance: null);
        try
        {
            await _services.WriteArtworkReportAsync(System.IO.Path.GetFullPath(reportFile));
        }
        catch (Exception ex)
        {
            LogFatal(ex);
            await _services.DisposeAsync();
            Shutdown(1);
            return;
        }

        await _services.DisposeAsync();
        Shutdown(0);
    }

    /// <summary>
    /// Prüfung (E90, G11.3): Konfiguration der eigenen Ablage laden, wie die Einstellungen speichern (unverändert, dann mit
    /// umgeschaltetem „Nicht installierte Spiele ausblenden“), Bericht schreiben, beenden. Nie gegen die echte Konfiguration;
    /// ohne Automatik, Einlesen und Fenster (wie die Bildschirmfotos).
    /// </summary>
    private async Task RunSettingsProbeModeAsync(AppArguments args, string reportFile)
    {
        if (!args.HasOwnConfigDir(AppPaths.Default(SystemEnvironment.Instance)))
        {
            Shutdown(2); // nie gegen die echte Konfiguration oder den echten Datenordner
            return;
        }

        ThemeManager.Initialize(this);
        var paths = AppPaths.InDirectory(System.IO.Path.GetFullPath(args.ConfigDir));
        _host = new AutomationHost(paths, autoHdrLocation: AutoHdrRegistryLocation.Resolve(paths, AppPaths.Default(SystemEnvironment.Instance), args.AutoHdrTestRoot));
        _services = new AppServices(_host, Dispatcher, instance: null);
        int code;
        try
        {
            code = _services.WriteSettingsProbe(System.IO.Path.GetFullPath(reportFile)) ? 0 : 1;
        }
        catch (Exception ex)
        {
            LogFatal(ex);
            code = 1;
        }

        await _services.DisposeAsync();
        Shutdown(code);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportError(e.Exception);
        e.Handled = true; // HdrPilot läuft weiter; Spiele dürfen nie blockiert werden.
    }

    private void ReportError(Exception ex)
    {
        LogFatal(ex);
        _services?.Tray.Notify(HdrPilot.App.Resources.CommonStrings.UnexpectedErrorTitle, HdrPilot.App.Resources.CommonStrings.UnexpectedErrorMessage, isWarning: true);
    }

    private void LogFatal(Exception? ex)
    {
        if (ex is null || _host is null)
        {
            return;
        }

        var logger = _host.LoggerFactory.CreateLogger("App");
        Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, ex, "Unerwarteter Fehler.");
    }
}
