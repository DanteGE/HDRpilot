using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hosting;

namespace HdrPilot.App;

/// <summary>
/// Start: Einzelinstanz prüfen (sonst Befehle weiterreichen), Automatik starten (inklusive Wiederanlauf nach
/// Absturz), Tray anzeigen. Das Fenster öffnet sich nur auf Wunsch; HdrPilot startet minimiert.
/// </summary>
public partial class App : Application
{
    private AppServices? _services;
    private AutomationHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = AppArguments.Parse(e.Args);
        var instanceId = SingleInstance.IdFor(args.ConfigDir);
        var instance = SingleInstance.TryAcquire(instanceId);
        if (instance is null)
        {
            // Zweiter Aufruf: an die laufende Instanz weiterreichen (ohne Befehl: Fenster öffnen).
            var forward = args.HasCommand ? args.ToForwardedArgs() : ["--show"];
            if (!SingleInstance.TrySend(instanceId, forward, TimeSpan.FromSeconds(5)))
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
        _host = new AutomationHost(paths);
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
