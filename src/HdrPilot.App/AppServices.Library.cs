using System.Windows;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Util;

namespace HdrPilot.App;

/// <summary>Spielebibliothek: Hauptfenster, Symbole aus den Spiel-EXEs und Verknüpfungen „Mit HDR starten“.</summary>
public sealed partial class AppServices
{
    private partial IGameIconService CreateIconService() =>
        new GameIconService(FileSystem, () => Config.Current.Settings.HelperExecutablePatterns);

    private partial IShortcutService CreateShortcutService() => new ShortcutService(
        iconPath: game => new IconExecutableResolver(FileSystem).Resolve(game, Config.Current.Settings.HelperExecutablePatterns),
        extraArguments: ConfigDirArgument());

    /// <summary>Das Fenster bekommt einen eigenen Kontext; beides wird beim Schließen vollständig freigegeben.</summary>
    private partial Window CreateMainWindow()
    {
        var window = new MainWindow();
        window.Attach(new LibraryViewModel(new AppLibraryContext(this), new LibraryDialogs(window)));
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
