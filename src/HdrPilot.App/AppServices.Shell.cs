using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Library;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Core.Sessions;
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
        return new TrayService(ShellAdapter, gate, logger, appearance: Appearance, windowIcon: WindowIconSource.Shared);
    }

    private partial IAutostartService CreateAutostartService()
    {
        var logger = LoggerFactory.CreateLogger("Autostart");
        if (!UsesDefaultPaths)
        {
            return new ConfigOnlyAutostartService(Config, logger);
        }

        var service = new AutostartService(Registry, AutostartService.ResolveExecutablePath(System.Environment.ProcessPath), logger);

        // EXE verschoben: Run-Eintrag zeigt noch auf den alten Pfad. Nur wenn Autostart in der Konfiguration an ist; ein
        // fehlender Eintrag bleibt weg (etwa vom Setup entfernt, E83).
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
        var window = new SettingsWindow { TestLog = TestLog() };
        window.Attach(new SettingsViewModel(
            ShellAdapter,
            new DialogFolderPicker(window),
            LoggerFactory.CreateLogger("Einstellungen"),
            new WindowConfirmDialogs(() => window, TestLog())));
        return window;
    }

    /// <summary>
    /// Zustände der Einstellungen für Bildschirmfotos (E89, G10.8): wie das Fenster öffnet, die Seite „Darstellung“ oben, bei
    /// „Bilder“ und bei „Symbole und Tray“. Vor dem Foto wartet der Zustand, bis Abdeckung, Beispiele und Vorschau geladen
    /// sind (höchstens 20 s), und springt dann zum Anker.
    /// </summary>
    partial void AddSettingsSnapshotScenes(List<SnapshotScene> scenes)
    {
        scenes.Add(new SnapshotScene("einstellungen", "allgemein", CreateSettingsWindow));
        scenes.Add(SettingsScene("darstellung", "NavAppearance"));
        scenes.Add(SettingsScene("darstellung-bilder", "NavAppearanceArtwork"));
        scenes.Add(SettingsScene("darstellung-tray", "NavAppearanceTray"));

        // Karte „Bibliothek“ (E90) und die Rückfrage des Sammel-Entfernens mit allen entfernbaren Spielen der Ablage.
        scenes.Add(SettingsScene("bibliothek", "NavLibrary"));
        scenes.Add(new SnapshotScene("einstellungen", "rueckfrage-alle", RemoveAllDialogScene) { Settle = TimeSpan.FromSeconds(1), Resizable = false });
    }

    /// <summary>Rückfrage des Sammel-Entfernens, wie die Einstellungen sie zusammensetzen; ohne entfernbares Spiel ein leeres Fenster.</summary>
    private Window RemoveAllDialogScene()
    {
        var games = LibraryCleanup.Removable(Config.Current);
        if (games.Count == 0)
        {
            return new Window();
        }

        var traces = GameCleanup.CheckInstallsAsync(games).GetAwaiter().GetResult();
        var shortcuts = games.Sum(g => GameCleanup.FindShortcuts(g.Key).Count);
        return new ConfirmDialog(RemoveSettingsText.ForGames(games, traces, shortcuts));
    }

    /// <summary>
    /// Prüfung (E90, G11.3, phase11-migration.ps1, --settings-probe): die geladene Konfiguration (eine Kopie der echten Ablage)
    /// zweimal wie die Einstellungen speichern, erst unverändert (Vergleichsstand im neuen Format:
    /// <c>config.unveraendert.json</c> neben dem Bericht), dann mit umgeschaltetem „Nicht installierte Spiele ausblenden“
    /// (<c>config.umgeschaltet.json</c>). Der Bericht nennt Ladewarnung, Zahl der Spiele, die nicht installierten, was die
    /// Karte „Bibliothek“ zeigt und je entfernbares Spiel die Spur einer Installation, wie die Rückfrage des Sammel-Entfernens
    /// sie prüft (nur lesend). Nur mit eigener Ablage aufgerufen (App.xaml.cs). True, wenn beide Male gespeichert wurde.
    /// </summary>
    public bool WriteSettingsProbe(string reportFile)
    {
        var directory = System.IO.Path.GetDirectoryName(reportFile) ?? throw new ArgumentException("Bericht ohne Ordner: " + reportFile, nameof(reportFile));
        System.IO.Directory.CreateDirectory(directory);
        var loaded = Config.Current;
        var logger = LoggerFactory.CreateLogger("Prüfung");

        SettingsViewModel Open() => new(ShellAdapter, new NoFolderPicker(), logger);
        int shownCount, shownRemovable;
        bool hideBefore, savedUnchanged;
        using (var unchanged = Open())
        {
            (shownCount, shownRemovable, hideBefore) = (unchanged.UninstalledCount, unchanged.RemovableCount, unchanged.HideUninstalledGames);
            savedUnchanged = unchanged.Save();
        }

        System.IO.File.Copy(Paths.ConfigFile, System.IO.Path.Combine(directory, "config.unveraendert.json"), overwrite: true);
        bool hideAfter, savedToggled;
        using (var toggled = Open())
        {
            toggled.HideUninstalledGames = !toggled.HideUninstalledGames;
            hideAfter = toggled.HideUninstalledGames;
            savedToggled = toggled.Save();
        }

        System.IO.File.Copy(Paths.ConfigFile, System.IO.Path.Combine(directory, "config.umgeschaltet.json"), overwrite: true);
        var removable = LibraryCleanup.Removable(loaded);
        var traces = GameCleanup.CheckInstallsAsync(removable).GetAwaiter().GetResult();
        var report = new
        {
            Config.LoadWarning,
            Games = loaded.Games.Count,
            NotInstalled = LibraryCleanup.CountNotInstalled(loaded),
            NotInstalledGames = loaded.Games.Where(g => !g.IsInstalled).Select(g => new { g.Key, g.Name, g.HdrAuto, g.IsManual }).ToList(),
            Removable = removable.Count,
            RemovableTraces = removable.Zip(traces, (g, t) => new { g.Key, g.Name, Sign = t.Sign.ToString(), t.Drive }).ToList(),
            ShownCount = shownCount,
            ShownRemovable = shownRemovable,
            HideBefore = hideBefore,
            HideAfter = hideAfter,
            HideStored = Config.Current.Settings.HideUninstalledGames,
            SavedUnchanged = savedUnchanged,
            SavedToggled = savedToggled,
        };
        System.IO.File.WriteAllText(reportFile, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        logger.LogInformation(
            "Prüfung: Einstellungen gespeichert ({Unchanged}/{Toggled}), {Games} Spiele, {NotInstalled} nicht installiert, ausblenden {Before} → {After}.",
            savedUnchanged, savedToggled, report.Games, report.NotInstalled, hideBefore, hideAfter);
        return savedUnchanged && savedToggled;
    }

    /// <summary>Ordnerauswahl ohne Dialog (Prüfung: nie gebraucht).</summary>
    private sealed class NoFolderPicker : IFolderPicker
    {
        public string? PickFolder(string? initialDirectory) => null;
    }

    private SnapshotScene SettingsScene(string name, string anchor) => new("einstellungen", name, CreateSettingsWindow, async window =>
    {
        if (window is SettingsWindow settings && settings.DataContext is SettingsViewModel model)
        {
            await Task.WhenAny(model.Appearance.Loaded, Task.Delay(TimeSpan.FromSeconds(20)));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            settings.ScrollToSection(anchor);
        }
    })
    {
        Settle = TimeSpan.FromMilliseconds(1800),
    };

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

        public IHdrListService HdrList => app.HdrList;

        public IAppearanceService Appearance => app.Appearance;

        public IArtworkService Artwork => app.Artwork;

        public ISteamGridDbService SteamGridDb => app.SteamGridDb;

        public IDisplayLuminanceService Luminance => app.Luminance;

        public void OpenWebPage(string url) => AppServices.OpenWebPage(url);

        public TrayState QueryTrayState()
        {
            var targets = app.TargetDisplays();
            var sessions = app.Sessions.Sessions;
            var games = sessions.Select(s => s.GameName).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            var (bufferUntil, bufferHeld) = TrayPresentation.RestartBufferOf(sessions);
            return new TrayState(
                HdrOn: targets.Any(d => d.HdrEnabled),
                HdrSupported: targets.Any(d => d.HdrSupported),
                Paused: app.Sessions.Paused,
                ActiveGames: games,
                RestartBufferUntil: bufferUntil,
                RestartBufferHeld: bufferHeld);
        }

        public IReadOnlyList<DisplayInfo> GetDisplays() => app.Hdr.GetDisplays();

        public Task ToggleHdrNowAsync() => app.ToggleHdrNowAsync();

        // Testinstanzen (--config-dir) erreichen hier nur ihre Testwurzel (E88).
        public AutoHdrSwitchState ReadAutoHdrSwitch() => app.AutoHdr.ReadGlobalSwitch();

        public bool AutoHdrSwitchHeld => app.AutoHdr.HoldsGlobalSwitch;

        public AutoHdrSwitchState SetAutoHdrSwitch(bool on) => app.AutoHdr.SetGlobalSwitchByUser(on);

        public void SetPaused(bool paused) => app.SetPaused(paused);

        public void ShowMainWindow() => app.ShowMainWindow();

        public void ShowSettingsWindow() => app.ShowSettingsWindow();

        public void OpenLogFolder() => app.OpenLogFolder();

        public Task ExitAsync() => app.ExitAsync();

        public void OpenLibrary() => app.ShowMainWindow();

        public UninstalledGameCleanup Cleanup => app.GameCleanup;

        public IReadOnlyList<SessionInfo> Sessions => app.Sessions.Sessions;

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
