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
            context.ConfigService.Update(c => c.Settings.LibraryDetailsRatio = ratio);
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
            var limited = badges["Steam:1091500"];
            Assert.Equal(Visibility.Visible, limited.Visibility);
            Assert.Equal("HDR*", ((TextBlock)limited.Child).Text);
            Assert.Equal(HdrPilot.App.Resources.LibraryStrings.Badge_HdrLimitedTooltip, limited.ToolTip);
            Assert.Equal(HdrPilot.App.Resources.LibraryStrings.Badge_HdrLimitedTooltip, AutomationProperties.GetName(limited));
            Assert.Equal(Visibility.Visible, badges["Epic:Crow"].Visibility);
            Assert.Equal("HDR", ((TextBlock)badges["Epic:Crow"].Child).Text);
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
            var (window, _, _) = Open(SteamGame(), ratio: 0.45);
            var list = (ColumnDefinition)window.FindName("ListColumn");
            var details = (ColumnDefinition)window.FindName("DetailsColumn");
            Assert.True(list.Width.IsStar && details.Width.IsStar);
            Assert.Equal(0.45, details.Width.Value / (list.Width.Value + details.Width.Value), 3);
            Assert.Equal(LibraryLayout.ListMinWidth, list.MinWidth);
            Assert.Equal(LibraryLayout.DetailsMinWidth, details.MinWidth);

            WpfTestHost.Layout(window, new Size(1280, 800));
            var (list1, details1) = (list.ActualWidth, details.ActualWidth);
            WpfTestHost.Layout(window, new Size(1700, 800));
            Assert.True(list.ActualWidth > list1 && details.ActualWidth > details1, "beide Spalten wachsen mit");
            Assert.Equal(0.45, details.ActualWidth / (list.ActualWidth + details.ActualWidth), 2);
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
            Assert.Equal(0.75, context.Config.Settings.LibraryDetailsRatio);
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
            Assert.Null(context.Config.Settings.LibraryDetailsRatio); // gesammelt, noch nicht gespeichert
            context.Dispatcher.RunDelayed();
            var saved = context.Config.Settings.LibraryDetailsRatio;
            Assert.NotNull(saved);
            Assert.True(saved > LibraryLayout.DefaultDetailsRatio, "Detailbereich breiter gemerkt: " + saved);

            // Rechts: schmaler, aber nie unter die Mindestbreite.
            for (var i = 0; i < 40; i++)
            {
                Press(window, splitter, Key.Right, size);
            }

            Assert.InRange(details.ActualWidth, LibraryLayout.DetailsMinWidth - 1, LibraryLayout.DetailsMinWidth + 1);
            window.Close();
            Assert.True(context.Config.Settings.LibraryDetailsRatio < saved, "beim Schließen gespeichert");
        });
    }

    [Fact]
    public void Toolbar_never_overflows_and_moves_filters_to_a_second_row_when_narrow()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open(SteamGame());
            var root = (FrameworkElement)window.FindName("Root");
            var groups = new[] { "SearchBox", "FilterGroup", "ActionGroup" }.Select(n => (FrameworkElement)window.FindName(n)).ToList();
            var modes = new Dictionary<double, ToolbarMode>();

            var searchMin = ((Grid)window.FindName("ToolbarArea")).ColumnDefinitions[0].MinWidth;
            foreach (var width in new[] { 1700.0, 1460, 1320, 1250, 1150, 1000, 1700 })
            {
                WpfTestHost.Layout(window, new Size(width, 800));
                var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(root);
                var available = slot.Width - root.Margin.Left - root.Margin.Right;
                window.UpdateToolbarLayout(available);
                WpfTestHost.Layout(window, new Size(width, 800));
                modes[width] = window.ToolbarMode;

                foreach (var group in groups)
                {
                    var right = group.TransformToAncestor(root).Transform(new Point(group.ActualWidth, 0)).X;
                    Assert.True(right <= available + 0.5, $"{group.Name} ragt bei {width} px hinaus: {right:0} > {available:0} ({window.ToolbarMode})");
                }

                Assert.True(((FrameworkElement)window.FindName("SearchBox")).ActualWidth >= searchMin - 0.5, $"Suche behält bei {width} px ihre Mindestbreite");
            }

            Assert.Equal(ToolbarMode.Wide, modes[1700]);
            Assert.Equal(ToolbarMode.Wide, modes[window.Width]); // Standardgröße: alles in einer Zeile
            Assert.Equal(ToolbarMode.Stacked, modes[1000]);
            var filters = (FrameworkElement)window.FindName("FilterGroup");
            Assert.Equal(0, Grid.GetRow(filters)); // wieder breit: eine Zeile
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
                        var root = (FrameworkElement)window.FindName("Root");
                        var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(root);
                        window.UpdateToolbarLayout(slot.Width - root.Margin.Left - root.Margin.Right);
                        window.FitDetailsColumn();
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
