using System.Windows;
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

    private partial IShortcutService CreateShortcutService() => new ShortcutService(
        iconPath: game => new IconExecutableResolver(FileSystem).Resolve(game, Config.Current.Settings.HelperExecutablePatterns),
        extraArguments: ConfigDirArgument());

    /// <summary>Das Fenster bekommt einen eigenen Kontext; beides wird beim Schließen vollständig freigegeben.</summary>
    private partial Window CreateMainWindow()
    {
        var window = new MainWindow();
        var testIconFile = ConfigDirArgument() is null ? null : System.Environment.GetEnvironmentVariable(TestIconFileVariable);
        window.Attach(new LibraryViewModel(new AppLibraryContext(this), new LibraryDialogs(window, string.IsNullOrWhiteSpace(testIconFile) ? null : testIconFile)));
        return window;
    }

    /// <summary>Mit eigener Ablage (--config-dir) gestartet: Verknüpfungen sollen dieselbe Ablage benutzen.</summary>
    private string? ConfigDirArgument()
    {
        var standard = AppPaths.Default(Environment);
        return PathUtil.PathEquals(standard.ConfigDirectory, Paths.ConfigDirectory)
            ? null
            : "--config-dir \"" + System.IO.Path.TrimEndingDirectorySeparator(Paths.ConfigDirectory) + "\"";
    }
}
