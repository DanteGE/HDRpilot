using System.Windows;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
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
        var window = new MainWindow { EffectsLog = LoggerFactory.CreateLogger("Bibliothek") };
        var testing = ConfigDirArgument() is not null;
        var testIconFile = testing ? System.Environment.GetEnvironmentVariable(TestIconFileVariable) : null;
        var testArtFile = testing ? System.Environment.GetEnvironmentVariable(TestArtFileVariable) : null;
        window.Attach(new LibraryViewModel(
            new AppLibraryContext(this),
            new LibraryDialogs(window, string.IsNullOrWhiteSpace(testIconFile) ? null : testIconFile, string.IsNullOrWhiteSpace(testArtFile) ? null : testArtFile, TestLog())));
        return window;
    }

    /// <summary>
    /// Nur in Prüfläufen mit eigener Ablage (--config-dir): Zeilen „Prüfung: …“ ins Protokoll, etwa wohin der Fokus einer
    /// offenen Rückfrage zeigt (phase11-uninstalled-ui.ps1, E90). Sonst null.
    /// </summary>
    private Action<string>? TestLog()
    {
        if (ConfigDirArgument() is null)
        {
            return null;
        }

        var logger = LoggerFactory.CreateLogger("Prüfung");
        return message => logger.LogInformation("{Message}", message);
    }

    /// <summary>
    /// Zustände der Bibliothek für Bildschirmfotos (E89, G10.5): die drei Ansichten, die Liste weit nach unten gescrollt
    /// (der Titelbereich behält seine Höhe), das Regal mit offener Schublade, der HDR-Moment bei 35 % (ohne HDR-Schaltung),
    /// ein Spiel mit erzeugtem Titelbild, ein Spiel mit eigenem HDR, das Kontextmenü eines Spiels und „Bild wählen…“. Ansicht
    /// und Auswahl werden nur im ViewModel gesetzt, ohne Schreiben in die Konfiguration; eine gemerkte Höhe des Titelbereichs
    /// (settings.libraryHeroRatio der Ablage) gilt wie beim Öffnen.
    /// </summary>
    partial void AddLibrarySnapshotScenes(List<SnapshotScene> scenes)
    {
        scenes.Add(new SnapshotScene("hauptfenster", "liste", () => LibraryScene(LibraryViewMode.List)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-gescrollt", () => LibraryScene(LibraryViewMode.List), ScrollListAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "raster", () => LibraryScene(LibraryViewMode.Grid)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal", () => LibraryScene(LibraryViewMode.Shelf)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-details", () => LibraryScene(LibraryViewMode.Shelf), OpenDrawerAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-hdr", () => LibraryScene(LibraryViewMode.List), FreezeMomentAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-hdr", () => LibraryScene(LibraryViewMode.Shelf), FreezeMomentAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "ersatzbild", () => LibraryScene(LibraryViewMode.List), SelectGeneratedHeroAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "nativ-hdr", () => LibraryScene(LibraryViewMode.Grid), SelectNativeHdrAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "kontextmenue", () => LibraryScene(LibraryViewMode.List))
        {
            Settle = LibrarySettle,
            Overlays = window => window is MainWindow main && main.CreateContextMenuPreview() is { } menu ? [menu] : [],
        });
        scenes.Add(new SnapshotScene("hauptfenster", "bildwahl", CreatePickerScene) { Settle = TimeSpan.FromSeconds(2), Resizable = false });

        // Werkzeugleiste „Fließleiste mit Lichtnaht“ (Wunsch des Nutzers 2026-10-17): Filter an, eingeklappt (Kapsel), eingeklappt
        // und gefiltert (Lichtnaht), in allen Ansichten; Tooltip eines Filters, Lasche beim Überfahren, Menü der Leiste. Zwei
        // Zeilen zeigen die schmalen Größen (etwa 1000x620, 1280x800 bei 110 %).
        scenes.Add(new SnapshotScene("hauptfenster", "liste-gefiltert", () => LibraryScene(LibraryViewMode.List), w => ToolbarStateAsync(w, collapsed: false, filtered: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-eingeklappt", () => LibraryScene(LibraryViewMode.List), w => ToolbarStateAsync(w, collapsed: true, filtered: false)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-eingeklappt-gefiltert", () => LibraryScene(LibraryViewMode.List), w => ToolbarStateAsync(w, collapsed: true, filtered: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "raster-eingeklappt", () => LibraryScene(LibraryViewMode.Grid), w => ToolbarStateAsync(w, collapsed: true, filtered: false)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "raster-eingeklappt-gefiltert", () => LibraryScene(LibraryViewMode.Grid), w => ToolbarStateAsync(w, collapsed: true, filtered: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-eingeklappt", () => LibraryScene(LibraryViewMode.Shelf), w => ToolbarStateAsync(w, collapsed: true, filtered: false)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-eingeklappt-gefiltert", () => LibraryScene(LibraryViewMode.Shelf), w => ToolbarStateAsync(w, collapsed: true, filtered: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-tooltip", () => LibraryScene(LibraryViewMode.List))
        {
            Settle = LibrarySettle,
            Overlays = window => window is MainWindow main && main.CreateToolTipPreview("OnlyHdrCapableToggle") is { } tip ? [tip] : [],
        });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-lasche", () => LibraryScene(LibraryViewMode.List), HoverTabAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-einklappen-t120", () => LibraryScene(LibraryViewMode.List), w => FreezeToolbarAsync(w, collapse: true, 120)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-einklappen-t600", () => LibraryScene(LibraryViewMode.List), w => FreezeToolbarAsync(w, collapse: true, 600)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-ausklappen-t140", () => LibraryScene(LibraryViewMode.List), w => FreezeToolbarAsync(w, collapse: false, 140)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-menue-leiste", () => LibraryScene(LibraryViewMode.List))
        {
            Settle = LibrarySettle,
            Overlays = window => window is MainWindow main && main.CreateToolbarMenuPreview() is { } menu ? [menu] : [],
        });

        // Auswahl beim Filtern (Entscheidung des Nutzers 2026-10-19): Gefiltert zeigen Titelbereich und Details den ersten
        // Treffer (siehe die Zustände „-gefiltert“), ohne Treffer kein Spiel und den Hinweis in der Liste; im Regal dann auch
        // keine Automatik-Karte.
        scenes.Add(new SnapshotScene("hauptfenster", "liste-ohne-treffer", () => LibraryScene(LibraryViewMode.List), NoMatchAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-ohne-treffer", () => LibraryScene(LibraryViewMode.Shelf), NoMatchAsync) { Settle = LibrarySettle });

        // Nicht installierte Spiele (E90): Knopf „Einstellungen entfernen…“ ganz unten im Detailbereich, das Kontextmenü mit dem
        // Eintrag und die Rückfrage für das erste entfernbare Spiel der Ablage, einmal mit den Verknüpfungen, die es wirklich
        // gibt, und einmal als Vorschau mit zwei (Häkchen), ohne etwas zu entfernen.
        scenes.Add(new SnapshotScene("hauptfenster", "nicht-installiert", () => LibraryScene(LibraryViewMode.List), w => SelectRemovableAsync(w, scrollDetails: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "kontextmenue-nicht-installiert", () => LibraryScene(LibraryViewMode.List), w => SelectRemovableAsync(w, scrollDetails: false))
        {
            Settle = LibrarySettle,
            Overlays = window => window is MainWindow main && main.CreateContextMenuPreview() is { } menu ? [menu] : [],
        });
        scenes.Add(new SnapshotScene("hauptfenster", "rueckfrage-entfernen", () => RemoveSettingsDialogScene(previewShortcuts: 0)) { Settle = TimeSpan.FromSeconds(1), Resizable = false });
        scenes.Add(new SnapshotScene("hauptfenster", "rueckfrage-entfernen-verknuepfungen", () => RemoveSettingsDialogScene(previewShortcuts: 2)) { Settle = TimeSpan.FromSeconds(1), Resizable = false });

        // Quiver Launcher (E91): das erste Quiver-Spiel der Ablage gewählt in Liste, Raster und Regal (auch mit Schublade), dazu der
        // Filter „Quiver“ in der Leiste und in der eingeklappten Kapsel. Ohne Quiver-Spiel bleibt die Auswahl wie beim Öffnen.
        scenes.Add(new SnapshotScene("hauptfenster", "quiver-liste", () => LibraryScene(LibraryViewMode.List), w => SelectQuiverAsync(w)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "quiver-raster", () => LibraryScene(LibraryViewMode.Grid), w => SelectQuiverAsync(w)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "quiver-regal", () => LibraryScene(LibraryViewMode.Shelf), w => SelectQuiverAsync(w)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "quiver-regal-details", () => LibraryScene(LibraryViewMode.Shelf), w => SelectQuiverAsync(w, drawer: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "quiver-gefiltert", () => LibraryScene(LibraryViewMode.List), w => SelectQuiverAsync(w, filter: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "quiver-kapsel", () => LibraryScene(LibraryViewMode.Grid), w => SelectQuiverAsync(w, filter: true, collapse: true)) { Settle = LibrarySettle });

        // Rückmeldung des Nutzers 2026-10-22: Lasche beim Überfahren auch in Raster und Regal (mittig auf der Bildkante), Kacheln
        // wie überfahren (die gewählte erste und eine zweite; im Raster wächst die erste Reihe nach unten, im Regal steigen sie).
        scenes.Add(new SnapshotScene("hauptfenster", "raster-lasche", () => LibraryScene(LibraryViewMode.Grid), HoverTabAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-lasche", () => LibraryScene(LibraryViewMode.Shelf), HoverTabAsync) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "raster-ueberfahren", () => LibraryScene(LibraryViewMode.Grid), w => HoverTilesAsync(w, secondRow: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "regal-ueberfahren", () => LibraryScene(LibraryViewMode.Shelf), w => HoverTilesAsync(w, secondRow: false)) { Settle = LibrarySettle });

        // Hoher Kontrast (ohne Umstellung des Systems): Kapsel ungefiltert und gefiltert in Systemfarben.
        scenes.Add(new SnapshotScene("hauptfenster", "liste-eingeklappt-kontrast", () => LibraryScene(LibraryViewMode.List), w => ToolbarStateAsync(w, collapsed: true, filtered: false, highContrast: true)) { Settle = LibrarySettle });
        scenes.Add(new SnapshotScene("hauptfenster", "liste-eingeklappt-gefiltert-kontrast", () => LibraryScene(LibraryViewMode.List), w => ToolbarStateAsync(w, collapsed: true, filtered: true, highContrast: true)) { Settle = LibrarySettle });
    }

    /// <summary>
    /// Werkzeugleiste für ein Bildschirmfoto: gefiltert wie in der Vorschau (Suche „gears“, „Neu“, „HDR-fähig“) und ein- bzw.
    /// ausgeklappt, nur im ViewModel (nichts wird gespeichert), auf Wunsch in hohem Kontrast; danach laufen die Übergänge zu
    /// Ende.
    /// </summary>
    private static async Task ToolbarStateAsync(Window window, bool collapsed, bool filtered, bool highContrast = false)
    {
        if (window is not MainWindow { ViewModel: { } vm } main)
        {
            return;
        }

        if (highContrast)
        {
            main.SetToolbarHighContrast(true);
        }

        if (filtered)
        {
            // „HDR-fähig“ nur, wenn die Ablage HDR-Angaben hat (sonst bliebe die Liste leer).
            vm.SearchText = collapsed ? "gears" : string.Empty;
            vm.ApplySearch();
            vm.OnlyNew = true;
            vm.OnlyHdrCapable = vm.HdrCapableCount > 0;
        }

        vm.SetToolbarCollapsed(collapsed, persist: false);
        await Task.Delay(1400);
    }

    /// <summary>Suche ohne Treffer, nur im ViewModel (nichts wird gespeichert); danach laufen die Überblendungen zu Ende.</summary>
    private static async Task NoMatchAsync(Window window)
    {
        if (window is MainWindow { ViewModel: { } vm })
        {
            vm.SearchText = "xqz";
            vm.ApplySearch();
            await Task.Delay(1400);
        }
    }

    /// <summary>
    /// Erstes nicht installiertes Launcher-Spiel der Liste wählen, ohne es als gesehen zu speichern (E90), und auf Wunsch den
    /// Detailbereich bis ganz nach unten scrollen, wo „Einstellungen entfernen…“ steht.
    /// </summary>
    private static async Task SelectRemovableAsync(Window window, bool scrollDetails)
    {
        if (window is not MainWindow { ViewModel: { } vm } main
            || vm.GamesView.Cast<GameItemViewModel>().FirstOrDefault(g => g.IsRemovable) is not { } item)
        {
            return;
        }

        vm.SelectQuietly(item);
        await Task.Delay(1800);
        if (scrollDetails)
        {
            main.ScrollDetailsToEnd();
            await Task.Delay(300);
        }
    }

    /// <summary>
    /// Erstes Quiver-Spiel der Bibliothek wählen, ohne es als gesehen zu speichern (E91); auf Wunsch vorher den Quellenfilter
    /// „Quiver“ setzen und die Leiste zur Kapsel einklappen, im Regal die Schublade öffnen. Nur im ViewModel, nichts wird
    /// gespeichert; danach laufen Überblendungen und das Laden der Bilder zu Ende.
    /// </summary>
    private static async Task SelectQuiverAsync(Window window, bool filter = false, bool collapse = false, bool drawer = false)
    {
        if (window is not MainWindow { ViewModel: { } vm } || vm.Games.FirstOrDefault(g => g.Source == GameSource.Quiver) is not { } item)
        {
            return;
        }

        if (filter && vm.SourceOptions.FirstOrDefault(o => o.Source == GameSource.Quiver) is { } option)
        {
            vm.SelectedSource = option;
        }

        vm.SelectQuietly(item);
        if (collapse)
        {
            vm.SetToolbarCollapsed(true, persist: false);
        }

        await Task.Delay(1800);
        if (drawer)
        {
            vm.IsDetailsDrawerOpen = true;
            await Task.Delay(900);
        }
    }

    /// <summary>
    /// Rückfrage „Einstellungen entfernen…“ für das erste entfernbare Spiel der Ablage, wie die Bibliothek sie zusammensetzt
    /// (Laufwerk geprüft, Verknüpfungen gesucht); mit <paramref name="previewShortcuts"/> über 0 als Vorschau mit so vielen
    /// Verknüpfungen (Häkchen). Ohne entfernbares Spiel ein leeres Fenster.
    /// </summary>
    private Window RemoveSettingsDialogScene(int previewShortcuts)
    {
        if (LibraryCleanup.Removable(Config.Current).FirstOrDefault() is not { } game)
        {
            return new Window();
        }

        var trace = GameCleanup.CheckInstallAsync(game).GetAwaiter().GetResult();
        var shortcuts = previewShortcuts > 0 ? previewShortcuts : GameCleanup.FindShortcuts(game.Key).Count;
        return new ConfirmDialog(RemoveSettingsText.ForGame(game, trace, shortcuts));
    }

    /// <summary>Ein- bzw. Ausklappen der Werkzeugleiste, angehalten nach einigen Millisekunden (wie #t= der Vorschau).</summary>
    private static async Task FreezeToolbarAsync(Window window, bool collapse, double milliseconds)
    {
        if (window is MainWindow main)
        {
            main.FreezeToolbarMotion(collapse, milliseconds);
        }

        await Task.Delay(400);
    }

    /// <summary>
    /// Kacheln wie beim Überfahren (ohne Maus): die erste (gewählt) und im Raster die zweite der zweiten Reihe, im Regal die vierte.
    /// </summary>
    private static async Task HoverTilesAsync(Window window, bool secondRow)
    {
        if (window is MainWindow main)
        {
            var columns = secondRow && MainWindow.FindTilePanel((System.Windows.Controls.ListBox)main.FindName("GameGrid")) is { } grid ? grid.Columns : 2;
            main.PreviewTileHover(0, columns + 1);
        }

        await Task.Delay(400);
    }

    /// <summary>Lasche, wie sie beim Überfahren nach links zu „Leiste ausblenden · Strg+Umschalt+F“ gewachsen ist.</summary>
    private static async Task HoverTabAsync(Window window)
    {
        if (window is MainWindow main)
        {
            main.PreviewTabHover();
        }

        await Task.Delay(400);
    }

    /// <summary>Prüfung (G10.9): HDR-Moment im offenen Hauptfenster abspielen (mit heller Fassung), ohne HDR zu schalten.</summary>
    partial void PlayTestHdrMoment()
    {
        if (_mainWindow is MainWindow window)
        {
            _ = PlayTestHdrMomentAsync(window);
        }
        else
        {
            _logger.LogInformation("Prüfung: HDR-Moment angefordert, aber kein Hauptfenster offen.");
        }
    }

    private async Task PlayTestHdrMomentAsync(MainWindow window)
    {
        var kind = await window.PlayTestHdrMomentAsync();
        _logger.LogInformation("Prüfung: HDR-Moment gespielt ({Kind}, helle Fassung {Boost}).", kind, window.HeroStage.ShownBoost is null ? "fehlt" : "da");
    }

    /// <summary>
    /// Prüfung (G10.5): Strg+F im offenen Hauptfenster wie gedrückt (MainWindow.HandleKey, ohne Tastatureingabe). Im
    /// Protokoll stehen danach der Zustand der Werkzeugleiste und wohin der Fokus ging (in einem nicht aktiven Fenster der
    /// Fokus, den es beim Aktivieren wiederherstellt; er wird niemandem genommen).
    /// </summary>
    partial void RunTestToolbarSearch()
    {
        if (_mainWindow is MainWindow { ViewModel: { } vm } window)
        {
            var handled = window.HandleKey(System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.Control, null);
            var focused = System.Windows.Input.FocusManager.GetFocusedElement(window) is DependencyObject element
                ? System.Windows.Automation.AutomationProperties.GetAutomationId(element)
                : "keiner";
            _logger.LogInformation(
                "Prüfung: Strg+F ausgeführt ({Handled}), Werkzeugleiste {State}, Fokus {Focus}.",
                handled ? "erledigt" : "nicht erledigt",
                vm.IsToolbarCollapsed ? "eingeklappt" : "ausgeklappt",
                string.IsNullOrEmpty(focused) ? "ohne Kennung" : focused);
        }
        else
        {
            _logger.LogInformation("Prüfung: Strg+F angefordert, aber kein Hauptfenster offen.");
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

    /// <summary>Liste um 14 Zeilen nach unten scrollen und die Vorschaubilder der neuen Zeilen laden lassen.</summary>
    private static async Task ScrollListAsync(Window window)
    {
        if (window is MainWindow main)
        {
            main.ScrollListBy(14);
            await Task.Delay(1500);
        }
    }

    private static async Task OpenDrawerAsync(Window window)
    {
        if (window is MainWindow { ViewModel: { } vm })
        {
            vm.IsDetailsDrawerOpen = true;
            await Task.Delay(900);
        }
    }

    private static async Task FreezeMomentAsync(Window window)
    {
        if (window is MainWindow main)
        {
            await main.FreezeHdrMomentAsync(0.35);
        }

        await Task.Delay(200);
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

    /// <summary>Ein weiteres Spiel mit eigenem HDR laut PCGamingWiki, bevorzugt eines mit „HDR automatisch“.</summary>
    private static async Task SelectNativeHdrAsync(Window window)
    {
        if (window is not MainWindow { ViewModel: { } vm })
        {
            return;
        }

        var native = vm.GamesView.Cast<GameItemViewModel>()
            .Where(g => g.HdrSupport == HdrSupportStatus.Native && !ReferenceEquals(g, vm.SelectedGame))
            .ToList();
        if ((native.FirstOrDefault(g => g.HdrAuto) ?? native.FirstOrDefault()) is { } item)
        {
            vm.SelectQuietly(item);
            await Task.Delay(2200);
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
