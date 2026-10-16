using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Hauptfenster auf dem WPF-Testhost: erzeugt und angeordnet, nie angezeigt. Mit der Umgebungsvariable
/// HDRPILOT_LIBRARY_RENDER_DIR wird es zusätzlich hell und dunkel als PNG zur Sichtprüfung abgelegt.
/// </summary>
public sealed class MainWindowLayoutTests
{
    private const string GameDir = @"D:\Steam-Bibliothek\steamapps\common\Cyberpunk 2077";

    private static GameConfig SteamGame()
    {
        var game = Game(GameSource.Steam, "1091500", "Cyberpunk 2077: Ultimate Edition mit sehr langem Namen", hdrAuto: true, isNew: false, installDir: GameDir);
        game.KnownExecutables = [GameDir + @"\bin\x64\Cyberpunk2077.exe", GameDir + @"\REDprelauncher.exe"];
        game.DetectedExecutables = [GameDir + @"\bin\x64\Cyberpunk2077.exe"];
        game.LastPlayed = new DateTimeOffset(2026, 10, 6, 21, 15, 0, TimeSpan.FromHours(2));
        return game;
    }

    private static (MainWindow Window, TestLibraryContext Context, LibraryViewModel Model) Open(GameConfig selected, double? ratio = null, bool hdrSupport = false)
    {
        EnsureLibraryResources();
        var context = new TestLibraryContext(
            selected,
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: true),
            Game(GameSource.Gog, "1", "Baldur's Gate 3", hdrAuto: true, isNew: false),
            Game(GameSource.Manual, "m1", "Eigenes Spiel", isNew: false, installed: false));
        if (ratio is not null)
        {
            context.ConfigService.Update(c => c.Settings.LibraryPaneRatio = ratio);
        }

        if (hdrSupport)
        {
            // Kennzeichen laut PCGamingWiki: „HDR*“ beim gewählten Spiel, „HDR“ bei Dead Island 2 (neben „NEU“).
            context.HdrSupport[selected.Key] = HdrSupportStatus.Limited;
            context.HdrSupport["Epic:Crow"] = HdrSupportStatus.Native;
            context.HdrSupport["Gog:1"] = HdrSupportStatus.None;
        }

        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games.Single(g => g.Key == selected.Key); // vor Attach: ohne Einblend-Animation
        var window = new MainWindow();
        window.Attach(model);
        return (window, context, model);
    }

    [Fact]
    public void Launch_section_comes_right_after_the_game_header_before_automation()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(SteamGame());
            WpfTestHost.Layout(window, new Size(1280, 800));
            WpfTestHost.DoEvents();

            var content = (Panel)window.FindName("DetailsContent");
            var order = content.Children.Cast<UIElement>().ToList();
            var launch = order.IndexOf((UIElement)window.FindName("LaunchSection"));
            var automation = order.IndexOf((UIElement)window.FindName("AutomationSection"));
            var executables = order.IndexOf((UIElement)window.FindName("ExecutablesSection"));

            // Kopf (0), Hinweis „nicht installiert“ (1), dann Starten (2) vor der Automatik.
            Assert.Equal(2, launch);
            Assert.True(launch < automation && automation < executables, $"Reihenfolge {launch}/{automation}/{executables}");

            var launchIds = Ids((DependencyObject)window.FindName("LaunchSection"));
            foreach (var id in new[] { "LaunchButton", "LaunchNoCommandHint", "CreateDesktopShortcutButton", "CreateStartMenuShortcutButton", "CopySteamLaunchOptionButton" })
            {
                Assert.True(launchIds.ContainsKey(id), "fehlt im Abschnitt Starten: " + id);
            }

            Assert.True(Ids((DependencyObject)window.FindName("AutomationSection")).ContainsKey("DetailHdrAuto"));
            window.Close();
        });
    }

    [Fact]
    public void Window_exposes_new_controls_with_names_and_keeps_existing_ids()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, model) = Open(SteamGame());
            WpfTestHost.Layout(window, new Size(1280, 800));
            WpfTestHost.DoEvents();
            var ids = Ids(window);

            foreach (var id in new[]
            {
                "HdrStatus", "ToggleHdrButton", "PauseToggle", "SettingsButton", "SearchBox", "SourceFilter", "SortOrder", "OnlySelectedToggle",
                "OnlyNewToggle", "OnlyHdrCapableToggle", "RescanButton", "AddGameButton", "MarkAllSeenButton", "GameList", "LibrarySplitter", "DetailsPanel", "DetailName",
                "DetailHdrAuto", "DetailRestore", "DetailDisplay", "DetailExecutables", "AddExeButton", "RemoveExeButton", "RelearnExeButton",
                "LaunchButton", "CreateDesktopShortcutButton", "CreateStartMenuShortcutButton", "CopySteamLaunchOptionButton", "DetailMessage",
                "DetailExeMessage", "RemoveGameButton",
            })
            {
                Assert.True(ids.ContainsKey(id), "AutomationId fehlt: " + id);
            }

            foreach (var id in new[] { "SortOrder", "LibrarySplitter", "CopySteamLaunchOptionButton", "OnlyHdrCapableToggle" })
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(ids[id])), "Name fehlt: " + id);
            }

            var hdrChip = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(ids["OnlyHdrCapableToggle"]);
            hdrChip.IsChecked = true;
            Assert.True(model.OnlyHdrCapable);
            hdrChip.IsChecked = false;

            var sort = Assert.IsType<ComboBox>(ids["SortOrder"]);
            Assert.Same(model.SortOptions, sort.ItemsSource);
            Assert.Same(model.SelectedSortOption, sort.SelectedItem);
            sort.SelectedItem = model.SortOptions[1];
            Assert.Equal(GameSortOrder.LastPlayed, model.SortOrder);

            var splitter = Assert.IsType<GridSplitter>(ids["LibrarySplitter"]);
            Assert.True(splitter.Focusable && splitter.IsTabStop, "Trenner muss per Tastatur erreichbar sein");
            Assert.Equal(GridResizeDirection.Columns, splitter.ResizeDirection);

            // Tab-Reihenfolge: Liste, dann Trenner, dann Details.
            var columns = ((Panel)window.FindName("ColumnsArea")).Children.Cast<UIElement>().ToList();
            Assert.True(
                columns.IndexOf((UIElement)window.FindName("ListArea")) < columns.IndexOf(splitter)
                && columns.IndexOf(splitter) < columns.IndexOf((UIElement)window.FindName("DetailsArea")),
                "Trenner steht zwischen Liste und Details");
            window.Close();
        });
    }

    [Fact]
    public void Hdr_badge_marks_native_and_limited_support_in_the_list()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(SteamGame(), hdrSupport: true);
            WpfTestHost.Layout(window, new Size(1460, 800));
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(1460, 800));

            var badges = WpfTestHost.Visuals(window).OfType<Border>()
                .Where(b => AutomationProperties.GetAutomationId(b) == "HdrSupportBadge")
                .ToDictionary(b => ((GameItemViewModel)b.DataContext).Key);
            Assert.Equal(4, badges.Count); // jede Zeile hat den Platz, sichtbar nur bei HDR
            static string Text(Border badge) => WpfTestHost.Visuals(badge).OfType<TextBlock>().Single().Text;

            // Spalte „HDR“ (Showcase Hero): „TEILWEISE“ bzw. „NATIV“ mit Punkt, Tooltip und Name wie bisher.
            var limited = badges["Steam:1091500"];
            Assert.Equal(Visibility.Visible, limited.Visibility);
            Assert.Equal(HdrPilot.App.Resources.LibraryStrings.HdrPill_Limited, Text(limited));
            Assert.Equal(HdrPilot.App.Resources.LibraryStrings.Badge_HdrLimitedTooltip, limited.ToolTip);
            Assert.Equal(HdrPilot.App.Resources.LibraryStrings.Badge_HdrLimitedTooltip, AutomationProperties.GetName(limited));
            Assert.Equal(Visibility.Visible, badges["Epic:Crow"].Visibility);
            Assert.Equal(HdrPilot.App.Resources.LibraryStrings.HdrPill_Native, Text(badges["Epic:Crow"]));
            Assert.Equal(HdrPilot.App.Resources.LibraryStrings.Badge_HdrTooltip, AutomationProperties.GetName(badges["Epic:Crow"]));
            Assert.Equal(Visibility.Collapsed, badges["Gog:1"].Visibility); // laut Wiki kein HDR
            Assert.Equal(Visibility.Collapsed, badges["Manual:m1"].Visibility); // unbekannt
            window.Close();
        });
    }

    [Fact]
    public void Steam_launch_option_is_only_offered_for_steam_games()
    {
        WpfTestHost.Run(() =>
        {
            var (steam, steamContext, _) = Open(SteamGame());
            WpfTestHost.Layout(steam, new Size(1280, 800));
            WpfTestHost.DoEvents();
            Assert.Equal(Visibility.Visible, ((UIElement)steam.FindName("SteamLaunchOptionArea")).Visibility);

            var button = (Button)Ids(steam)["CopySteamLaunchOptionButton"];
            Assert.True(button.IsEnabled);
            button.Command.Execute(null);
            Assert.Equal(["\"C:\\Program Files\\HdrPilot\\HdrPilot.exe\" --wrap -- %command%"], steamContext.FakeClipboard.Texts);
            steam.Close();

            var (epic, _, _) = Open(Game(GameSource.Epic, "Fortnite", "Fortnite", isNew: false));
            WpfTestHost.Layout(epic, new Size(1280, 800));
            WpfTestHost.DoEvents();
            Assert.Equal(Visibility.Collapsed, ((UIElement)epic.FindName("SteamLaunchOptionArea")).Visibility);
            epic.Close();
        });
    }

    [Fact]
    public void Both_columns_grow_with_the_window_and_the_stored_split_is_applied()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(SteamGame(), ratio: 0.3);
            var list = (ColumnDefinition)window.FindName("ListColumn");
            var details = (ColumnDefinition)window.FindName("DetailsColumn");
            Assert.True(list.Width.IsStar && details.Width.IsStar);
            Assert.Equal(0.3, details.Width.Value / (list.Width.Value + details.Width.Value), 3);
            Assert.Equal(LibraryLayout.ListMinWidth, list.MinWidth);
            Assert.Equal(LibraryLayout.DetailsMinWidth, details.MinWidth);
            Assert.Equal(ShowcaseLayout.MaxPaneWidth, details.MaxWidth);

            WpfTestHost.Layout(window, new Size(1280, 800));
            var (list1, details1) = (list.ActualWidth, details.ActualWidth);
            WpfTestHost.Layout(window, new Size(1700, 800));
            Assert.True(list.ActualWidth > list1 && details.ActualWidth > details1, "beide Spalten wachsen mit");
            Assert.Equal(0.3, details.ActualWidth / (list.ActualWidth + details.ActualWidth), 2);

            // Showcase Hero: Der Detailbereich wächst höchstens bis 560 px (Prototyp 340–396), den Rest bekommt die Liste.
            WpfTestHost.Layout(window, new Size(3440, 1440));
            Assert.Equal(ShowcaseLayout.MaxPaneWidth, details.ActualWidth, 1);
            Assert.True(list.ActualWidth > 2000, "Liste bei 3440 px: " + list.ActualWidth);
            window.Close();
        });
    }

    [Fact]
    public void Stored_split_is_clamped_to_the_current_window_size()
    {
        WpfTestHost.Run(() =>
        {
            var (window, context, model) = Open(SteamGame(), ratio: 0.75);
            var list = (ColumnDefinition)window.FindName("ListColumn");
            var details = (ColumnDefinition)window.FindName("DetailsColumn");

            WpfTestHost.Layout(window, new Size(1000, 700));
            window.FitDetailsColumn();
            WpfTestHost.Layout(window, new Size(1000, 700));

            Assert.True(list.ActualWidth >= LibraryLayout.ListMinWidth - 0.5, "Liste behält ihre Mindestbreite: " + list.ActualWidth);
            Assert.True(details.ActualWidth >= LibraryLayout.DetailsMinWidth - 0.5);
            var ratio = details.Width.Value / (list.Width.Value + details.Width.Value);
            Assert.True(ratio < 0.75, "Sternanteil begrenzt: " + ratio);
            Assert.Equal(0.75, model.DetailsRatio); // gemerkter Wert bleibt für größere Fenster
            Assert.Equal(0.75, context.Config.Settings.LibraryPaneRatio);
            window.Close();
        });
    }

    [Fact]
    public void Splitter_moves_with_the_arrow_keys_and_the_split_is_remembered()
    {
        WpfTestHost.Run(() =>
        {
            var (window, context, model) = Open(SteamGame());
            var splitter = (GridSplitter)window.FindName("LibrarySplitter");
            var details = (ColumnDefinition)window.FindName("DetailsColumn");
            var size = new Size(1280, 800);
            WpfTestHost.Layout(window, size);
            var before = details.ActualWidth;

            Press(window, splitter, Key.Left, size);
            Press(window, splitter, Key.Left, size);

            var expected = before + (2 * splitter.KeyboardIncrement);
            Assert.InRange(details.ActualWidth, expected - 1, expected + 1);
            Assert.NotNull(model.DetailsRatio);
            Assert.Null(context.Config.Settings.LibraryPaneRatio); // gesammelt, noch nicht gespeichert
            context.Dispatcher.RunDelayed();
            var saved = context.Config.Settings.LibraryPaneRatio;
            Assert.NotNull(saved);
            Assert.True(saved > LibraryLayout.DefaultDetailsRatio, "Detailbereich breiter gemerkt: " + saved);

            // Rechts: schmaler, aber nie unter die Mindestbreite.
            for (var i = 0; i < 40; i++)
            {
                Press(window, splitter, Key.Right, size);
            }

            Assert.InRange(details.ActualWidth, LibraryLayout.DetailsMinWidth - 1, LibraryLayout.DetailsMinWidth + 1);
            window.Close();
            Assert.True(context.Config.Settings.LibraryPaneRatio < saved, "beim Schließen gespeichert");
        });
    }

    [Fact]
    public void Hero_handle_moves_with_the_keyboard_in_steps_and_enter_restores_the_standard()
    {
        WpfTestHost.Run(() =>
        {
            var (window, context, model) = Open(SteamGame());
            var splitter = (HeroSplitter)window.FindName("HeroSplitter");
            var size = new Size(1745, 982);
            WpfTestHost.Layout(window, size);
            var root = ((FrameworkElement)window.FindName("Root")).ActualHeight;
            var start = window.HeroHeight;
            Assert.Equal(Math.Round(root / 3), start);

            // Pfeiltasten: Schritte von 24 px (Pfeil nach unten: Titelbereich höher), Bild ab: 96 px; sofort angewandt.
            Press(window, splitter, Key.Down, size);
            Press(window, splitter, Key.Down, size);
            Assert.Equal(start + (2 * HeroSplitter.SmallStep), window.HeroHeight);
            Press(window, splitter, Key.Up, size);
            Assert.Equal(start + HeroSplitter.SmallStep, window.HeroHeight);
            Press(window, splitter, Key.PageDown, size);
            Assert.Equal(start + HeroSplitter.SmallStep + HeroSplitter.LargeStep, window.HeroHeight);
            Assert.Equal(window.HeroHeight - ShowcaseLayout.HeroSplitterOverlap, splitter.TransformToAncestor((Visual)window.FindName("Root")).Transform(default).Y, 0.5);
            Assert.Null(context.Config.Settings.LibraryHeroRatio); // gesammelt, noch nicht gespeichert
            context.Dispatcher.RunDelayed();
            Assert.Equal(Math.Round(window.HeroHeight / root, 4), context.Config.Settings.LibraryHeroRatio);

            // Ende und Pos1: größte und kleinste Höhe; darüber hinaus geschieht nichts.
            Press(window, splitter, Key.End, size);
            Assert.Equal(root - 416, window.HeroHeight);
            Press(window, splitter, Key.PageDown, size);
            Assert.Equal(root - 416, window.HeroHeight);
            Press(window, splitter, Key.Home, size);
            Assert.Equal(ShowcaseLayout.MinListHero, window.HeroHeight);
            Press(window, splitter, Key.Up, size);
            Assert.Equal(ShowcaseLayout.MinListHero, window.HeroHeight);

            // Eingabetaste wie Doppelklick: Standardhöhe, nichts mehr gemerkt.
            Press(window, splitter, Key.Enter, size);
            Assert.Equal(start, window.HeroHeight);
            Assert.Null(model.HeroRatio);
            window.Close();
            Assert.Null(context.Config.Settings.LibraryHeroRatio);
        });
    }

    [Fact]
    public void Toolbar_never_overflows_and_folds_in_stages_when_narrow()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(SteamGame());
            var toolbar = (FrameworkElement)window.FindName("ToolbarArea");
            var filters = (FrameworkElement)window.FindName("FilterGroup");
            var actions = (FrameworkElement)window.FindName("ActionGroup");
            var stages = new Dictionary<double, int>();
            var standard = window.Width;

            foreach (var width in new[] { 2560.0, 1920, standard, 1366, 1280, 1150, 1000, 2560 })
            {
                var size = new Size(width, 900);
                WpfTestHost.Layout(window, size);
                window.FitDetailsColumn();
                WpfTestHost.Layout(window, size);
                window.UpdateToolbarLayout();
                WpfTestHost.Layout(window, size);
                stages[width] = window.ToolbarStage;

                var f = filters.TransformToAncestor(toolbar).TransformBounds(new Rect(filters.RenderSize));
                var a = actions.TransformToAncestor(toolbar).TransformBounds(new Rect(actions.RenderSize));
                foreach (var (name, bounds) in new[] { ("Filter", f), ("Aktionen", a) })
                {
                    Assert.True(bounds.Left >= -0.5 && bounds.Right <= toolbar.ActualWidth + 0.5, $"{name} ragt bei {width} px hinaus: {bounds} in {toolbar.ActualWidth:0} (Stufe {window.ToolbarStage})");
                }

                Assert.False(f.IntersectsWith(a) && f.Width > 0 && a.Width > 0 && Rect.Intersect(f, a).Width > 0.5, $"Filter und Aktionen überlappen bei {width} px (Stufe {window.ToolbarStage})");
                Assert.True(((FrameworkElement)window.FindName("SearchBox")).ActualWidth >= 140, $"Suche bleibt bei {width} px benutzbar");
            }

            // Je schmaler, desto mehr gefaltet; breit wieder alles ausgeschrieben.
            Assert.Equal(0, stages[2560]);
            Assert.True(stages[2560] <= stages[1920] && stages[1920] <= stages[standard] && stages[standard] <= stages[1366]
                && stages[1366] <= stages[1280] && stages[1280] <= stages[1150] && stages[1150] <= stages[1000], string.Join(", ", stages.Select(s => $"{s.Key}: {s.Value}")));

            // Standardgröße: die Filter-Chips bleiben sichtbar (phase4-ui.ps1 schaltet sie direkt).
            Assert.True(stages[standard] < 6, "Stufe bei Standardgröße: " + stages[standard]);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ChipGroup")).Visibility);
            Assert.Equal(Dock.Right, DockPanel.GetDock(actions)); // wieder breit: eine Zeile
            window.Close();
        });
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1280)]
    public void Details_content_wraps_instead_of_overflowing_at_the_narrowest_split(double windowWidth)
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, model) = Open(SteamGame(), ratio: LibraryLayout.MinDetailsRatio);
            model.Details!.CopySteamLaunchOptionCommand.Execute(null); // lange Erfolgsmeldung
            WpfTestHost.Layout(window, new Size(windowWidth, 800));
            window.FitDetailsColumn();
            WpfTestHost.Layout(window, new Size(windowWidth, 800));
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(windowWidth, 800));

            var column = (ColumnDefinition)window.FindName("DetailsColumn");
            Assert.InRange(column.ActualWidth, LibraryLayout.DetailsMinWidth - 1, LibraryLayout.DetailsMinWidth + 1);
            var content = (FrameworkElement)window.FindName("DetailsContent");
            Assert.True(content.ActualWidth > 200);
            foreach (var element in WpfTestHost.Visuals(content).OfType<FrameworkElement>().Where(e => Shown(e, content)))
            {
                var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
                Assert.True(bounds.Right <= content.ActualWidth + 0.5, $"{Describe(element)} ragt hinaus: {bounds.Right:0.0} > {content.ActualWidth:0.0}");
            }

            window.Close();
        });
    }

    [Fact]
    public void Main_window_renders_in_both_themes_for_review()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var (dark, suffix) in new[] { (true, "dunkel"), (false, "hell") })
            {
                try
                {
                    WpfTestHost.UseTheme(dark);
                    foreach (var (width, height, name) in new[] { (1460.0, 860.0, "standard"), (1000.0, 700.0, "schmal"), (1720.0, 900.0, "breit") })
                    {
                        var (window, _, model) = Open(SteamGame(), hdrSupport: true);
                        model.SortOrder = GameSortOrder.LastPlayed;
                        model.Details!.CopySteamLaunchOptionCommand.Execute(null);
                        WpfTestHost.Layout(window, new Size(width, height));
                        window.FitDetailsColumn();
                        WpfTestHost.Layout(window, new Size(width, height));
                        window.UpdateToolbarLayout();
                        WpfTestHost.Layout(window, new Size(width, height));

                        var bitmap = Render(window);
                        Assert.True(bitmap.PixelWidth >= width && bitmap.PixelHeight >= height);
                        SaveForReview(bitmap, $"bibliothek-{name}-{suffix}.png");
                        window.Close();
                    }
                }
                finally
                {
                    WpfTestHost.UseTheme(dark: true);
                }
            }
        });
    }

    /// <summary>Lädt die Stile der Bibliothek in die Test-Application (WpfTestHost lädt nur Tokens, Controls und Shell).</summary>
    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }

    private static Dictionary<string, FrameworkElement> Ids(DependencyObject root)
    {
        var result = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        foreach (var element in Logical(root).OfType<FrameworkElement>())
        {
            var id = AutomationProperties.GetAutomationId(element);
            if (!string.IsNullOrEmpty(id))
            {
                Assert.True(result.TryAdd(id, element), "AutomationId doppelt: " + id);
            }
        }

        return result;
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var descendant in Logical(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Sichtbar angeordnet: selbst und alle Vorfahren bis zur Wurzel sichtbar, mit Fläche.</summary>
    private static bool Shown(FrameworkElement element, Visual root)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        DependencyObject? current = element;
        while (current is not null && !ReferenceEquals(current, root))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return current is not null;
    }

    private static string Describe(FrameworkElement element) =>
        element.GetType().Name
        + (string.IsNullOrEmpty(element.Name) ? string.Empty : " " + element.Name)
        + (element is TextBlock t ? " \"" + t.Text + "\"" : string.Empty)
        + (string.IsNullOrEmpty(AutomationProperties.GetAutomationId(element)) ? string.Empty : " #" + AutomationProperties.GetAutomationId(element));

    /// <summary>
    /// Taste wie vom Eingabesystem: KeyDown (bewegt den Trenner), dann KeyUp (merkt die Breite), danach neu anordnen
    /// (in der App geschieht das vor dem nächsten Tastendruck von selbst).
    /// </summary>
    private static void Press(Window window, UIElement target, Key key, Size size)
    {
        var source = new TestPresentationSource();
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyUpEvent });
        WpfTestHost.Layout(window, size);
    }

    private static RenderTargetBitmap Render(Window window)
    {
        var root = (Visual)VisualTreeHelper.GetChild(window, 0);
        var size = ((FrameworkElement)root).RenderSize;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static void SaveForReview(BitmapSource bitmap, string name)
    {
        var dir = Environment.GetEnvironmentVariable("HDRPILOT_LIBRARY_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, name));
        encoder.Save(stream);
    }

    /// <summary>Tastenereignisse brauchen eine Quelle; das Fenster wird in Tests nie angezeigt.</summary>
    private sealed class TestPresentationSource : PresentationSource
    {
        private Visual? _root;

        public override Visual? RootVisual
        {
            get => _root;
            set => _root = value;
        }

        public override bool IsDisposed => false;

        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
