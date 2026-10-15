using System.Windows;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
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

    /// <summary>Wie <see cref="TestIconFileVariable"/>, für „Bild wählen…“ → „Eigene Datei…“ (E89).</summary>
    public const string TestArtFileVariable = "HDRPILOT_TEST_ART_FILE";

    /// <summary>Wartezeit vor dem Bildschirmfoto: Bilder laden im Hintergrund, Überblendungen (bis 1,4 s) sollen fertig sein.</summary>
    private static readonly TimeSpan LibrarySettle = TimeSpan.FromSeconds(3);

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
        var testing = ConfigDirArgument() is not null;
        var testIconFile = testing ? System.Environment.GetEnvironmentVariable(TestIconFileVariable) : null;
        var testArtFile = testing ? System.Environment.GetEnvironmentVariable(TestArtFileVariable) : null;
        window.Attach(new LibraryViewModel(
            new AppLibraryContext(this),
            new LibraryDialogs(window, string.IsNullOrWhiteSpace(testIconFile) ? null : testIconFile, string.IsNullOrWhiteSpace(testArtFile) ? null : testArtFile)));
        return window;
    }

    /// <summary>
    /// Zustände der Bibliothek für Bildschirmfotos (E89, G10.5): die drei Ansichten, das Regal mit offener Schublade, der
    /// HDR-Moment bei 35 % (ohne HDR-Schaltung), ein Spiel mit erzeugtem Titelbild und „Bild wählen…“. Ansicht und Auswahl
    /// werden nur im ViewModel gesetzt, ohne Schreiben in die Konfiguration.
    /// </summary>
    partial void AddLibrarySnapshotScenes(List<SnapshotScene> scenes)
    {
        scenes.Add(new SnapshotScene("hauptfenster", "liste", () => LibraryScene(LibraryViewMode.List)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "raster", () => LibraryScene(LibraryViewMode.Grid)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal", () => LibraryScene(LibraryViewMode.Shelf)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-details", () => LibraryScene(LibraryViewMode.Shelf), OpenDrawerAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-hdr", () => LibraryScene(LibraryViewMode.List), FreezeMomentAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-hdr", () => LibraryScene(LibraryViewMode.Shelf), FreezeMomentAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "ersatzbild", () => LibraryScene(LibraryViewMode.List), SelectGeneratedHeroAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "bildwahl", CreatePickerScene) { Settle = TimeSpan.FromSeconds(2) });
    }

    /// <summary>Prüfung (G10.9): HDR-Moment im offenen Hauptfenster abspielen, ohne HDR zu schalten.</summary>
    partial void PlayTestHdrMoment()
    {
        if (_mainWindow is MainWindow window)
        {
            var kind = window.PlayTestHdrMoment();
            _logger.LogInformation("Prüfung: HDR-Moment gespielt ({Kind}).", kind);
        }
        else
        {
            _logger.LogInformation("Prüfung: HDR-Moment angefordert, aber kein Hauptfenster offen.");
        }
    }

    /// <summary>Mit eigener Ablage (--config-dir) gestartet: Verknüpfungen sollen dieselbe Ablage benutzen.</summary>
    private string? ConfigDirArgument()
    {
        var standard = AppPaths.Default(Environment);
        return PathUtil.PathEquals(standard.ConfigDirectory, Paths.ConfigDirectory)
            ? null
            : "--config-dir \"" + System.IO.Path.TrimEndingDirectorySeparator(Paths.ConfigDirectory) + "\"";
    }

    private Window LibraryScene(LibraryViewMode view)
    {
        var window = (MainWindow)CreateMainWindow();
        window.ViewModel?.SetViewMode(view, persist: false);
        return window;
    }

    private static async Task OpenDrawerAsync(Window window)
    {
        if (window is MainWindow { ViewModel: { } vm })
        {
            vm.IsDetailsDrawerOpen = true;
            await Task.Delay(900);
        }
    }

    private static Task FreezeMomentAsync(Window window)
    {
        (window as MainWindow)?.FreezeHdrMoment(0.35);
        return Task.Delay(200);
    }

    /// <summary>Erstes Spiel der Liste ohne Titelbild aus einer Quelle (Ersatzbild); Spiele ohne Steam zuerst.</summary>
    private async Task SelectGeneratedHeroAsync(Window window)
    {
        if (window is not MainWindow { ViewModel: { } vm })
        {
            return;
        }

        var candidates = vm.GamesView.Cast<GameItemViewModel>().OrderBy(g => g.Source == GameSource.Steam ? 1 : 0).Take(80).ToList();
        foreach (var item in candidates)
        {
            var artwork = await Artwork.GetArtworkAsync(item.Config, vm.HeroDecodeWidth);
            if (artwork.Hero is null || artwork.Hero.Origin == ArtworkOrigin.Generated)
            {
                vm.SelectQuietly(item);
                await Task.Delay(2200);
                return;
            }
        }
    }

    /// <summary>„Bild wählen…“ als eigenes Fenster für ein Spiel mit Titelbild (Bibliothek ohne Fenster, nach dem Schließen freigegeben).</summary>
    private Window CreatePickerScene()
    {
        var library = new LibraryViewModel(new AppLibraryContext(this), new LibraryDialogs(new Window()));
        library.SelectInitial();
        var item = library.SelectedGame ?? library.Games.FirstOrDefault();
        if (item is null)
        {
            library.Dispose();
            return new Window();
        }

        var picker = new ArtworkPickerViewModel(library, item, ArtworkKind.Hero);
        _ = picker.LoadAsync();
        var dialog = new ArtworkPickerDialog(picker);
        dialog.Closed += (_, _) => library.Dispose();
        return dialog;
    }
}
