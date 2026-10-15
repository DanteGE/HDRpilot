using System.Windows;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App;

/// <summary>Spielebibliothek: Hauptfenster, Symbole der Spiele (E86) und Verknüpfungen „Mit HDR starten“.</summary>
public sealed partial class AppServices
{
    /// <summary>
    /// Nur für Prüfläufe mit eigener Ablage (--config-dir): Diese Datei nimmt „Symbol ändern…“ statt des Dateidialogs
    /// (UI-Automation kann keinen Dateidialog bedienen, phase8-icons.ps1). Ohne --config-dir wirkt sie nie.
    /// </summary>
    public const string TestIconFileVariable = "HDRPILOT_TEST_ICON_FILE";

    private partial IGameIconService CreateIconService() => new GameIconService(
        FileSystem,
        () => Config.Current.Settings.HelperExecutablePatterns,
        iconDirectory: Paths.IconDirectory,
        choiceCacheFile: Paths.IconCacheFile,
        logger: LoggerFactory.CreateLogger("Symbole"));

    /// <summary>
    /// Verknüpfungen zeigen das Symbol der Bibliothek, soweit sie es können (eigenes Symbol als .ico, gemerkte Wahl als .ico
    /// oder .exe, etwa das Steam-Symbol statt eines Starters mit EasyAntiCheat-Symbol); sonst die EXE wie in der Liste ohne
    /// tiefere Suche (<see cref="IGameIconService.ShortcutIconFile"/>).
    /// </summary>
    private partial IShortcutService CreateShortcutService() => new ShortcutService(
        iconPath: Icons.ShortcutIconFile,
        extraArguments: ConfigDirArgument());

    /// <summary>Das Fenster bekommt einen eigenen Kontext; beides wird beim Schließen vollständig freigegeben.</summary>
    private partial Window CreateMainWindow()
    {
        var window = new MainWindow();
        var testIconFile = ConfigDirArgument() is null ? null : System.Environment.GetEnvironmentVariable(TestIconFileVariable);
        window.Attach(new LibraryViewModel(new AppLibraryContext(this), new LibraryDialogs(window, string.IsNullOrWhiteSpace(testIconFile) ? null : testIconFile)));
        return window;
    }

    /// <summary>Zustände der Bibliothek für Bildschirmfotos (E89). Gerüst: das Fenster, wie es öffnet.</summary>
    partial void AddLibrarySnapshotScenes(List<SnapshotScene> scenes) =>
        scenes.Add(new SnapshotScene("hauptfenster", "liste", CreateMainWindow));

    /// <summary>Prüfung (G10.9): HDR-Moment ohne HDR-Schaltung. Gerüst: nur protokolliert, die Bibliothek spielt ihn noch nicht.</summary>
    partial void PlayTestHdrMoment() => _logger.LogInformation("Prüfung: HDR-Moment angefordert (Fenster offen: {Open}).", _mainWindow is not null);

    /// <summary>Mit eigener Ablage (--config-dir) gestartet: Verknüpfungen sollen dieselbe Ablage benutzen.</summary>
    private string? ConfigDirArgument()
    {
        var standard = AppPaths.Default(Environment);
        return PathUtil.PathEquals(standard.ConfigDirectory, Paths.ConfigDirectory)
            ? null
            : "--config-dir \"" + System.IO.Path.TrimEndingDirectorySeparator(Paths.ConfigDirectory) + "\"";
    }
}
