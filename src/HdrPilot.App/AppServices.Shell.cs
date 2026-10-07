using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App;

/// <summary>Shell: Tray, Einstellungen, Autostart und Bildschirmereignisse.</summary>
public sealed partial class AppServices
{
    private ShellHost? _shellHost;

    private ShellHost ShellAdapter => _shellHost ??= new ShellHost(this);

    /// <summary>
    /// True, wenn HdrPilot mit der normalen Ablage läuft. Mit <c>--config-dir</c> (Tests, Prüfskripte, Bildschirmfotos)
    /// wird die echte Registry nie verändert.
    /// </summary>
    private bool UsesDefaultPaths => Paths == AppPaths.Default(Environment);

    private partial ITrayService CreateTrayService()
    {
        var logger = LoggerFactory.CreateLogger("Tray");
        var gate = new NotificationGate(() => Config.Current.Settings.Notifications, new ShellUserNotificationState(), logger);
        return new TrayService(ShellAdapter, gate, logger);
    }

    private partial IAutostartService CreateAutostartService()
    {
        var logger = LoggerFactory.CreateLogger("Autostart");
        if (!UsesDefaultPaths)
        {
            return new ConfigOnlyAutostartService(Config, logger);
        }

        var service = new AutostartService(Registry, AutostartService.ResolveExecutablePath(System.Environment.ProcessPath), logger);

        // EXE verschoben: Run-Eintrag zeigt noch auf den alten Pfad. Nur wenn Autostart in der Konfiguration an ist.
        service.Reconcile(Config.Current.Settings.StartWithWindows);
        return service;
    }

    private partial IDisplayEventsService CreateDisplayEventsService() => new DisplayEventsService(
        new SystemDisplayEventSource(),
        new DispatcherDebounceTimer(Dispatcher, DisplayEventsService.DebounceDelay),
        RaiseOnUi,
        LoggerFactory.CreateLogger("Displays"));

    private partial Window CreateSettingsWindow()
    {
        var window = new SettingsWindow();
        window.Attach(new SettingsViewModel(ShellAdapter, new DialogFolderPicker(window)));
        return window;
    }

    /// <summary>Adapter: was Tray und Einstellungen von AppServices brauchen. Hält keine Fenster.</summary>
    private sealed class ShellHost(AppServices app) : ITrayHost, IShellContext
    {
        public event EventHandler? HdrStateChanged
        {
            add => app.HdrStateChanged += value;
            remove => app.HdrStateChanged -= value;
        }

        public Dispatcher Dispatcher => app.Dispatcher;

        public ConfigService Config => app.Config;

        public IAutostartService Autostart => app.Autostart;

        public TrayState QueryTrayState()
        {
            var targets = app.TargetDisplays();
            var games = app.Sessions.Sessions.Select(s => s.GameName).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            return new TrayState(
                HdrOn: targets.Any(d => d.HdrEnabled),
                HdrSupported: targets.Any(d => d.HdrSupported),
                Paused: app.Sessions.Paused,
                ActiveGames: games);
        }

        public IReadOnlyList<DisplayInfo> GetDisplays() => app.Hdr.GetDisplays();

        public Task ToggleHdrNowAsync() => app.ToggleHdrNowAsync();

        public void SetPaused(bool paused) => app.SetPaused(paused);

        public void ShowMainWindow() => app.ShowMainWindow();

        public void ShowSettingsWindow() => app.ShowSettingsWindow();

        public void OpenLogFolder() => app.OpenLogFolder();

        public Task ExitAsync() => app.ExitAsync();

        public void OnSettingsSaved(bool searchFoldersChanged)
        {
            app.RaiseHdrStateChanged();
            if (searchFoldersChanged)
            {
                _ = app.RescanAsync();
            }
        }
    }
}
