using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using HdrPilot.App.Resources;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Rückmeldungen des Nutzers vom 2026-10-22 im Hauptfenster, auf dem WPF-Testhost erzeugt und angeordnet, nie angezeigt:
/// Titel mit der gewählten Quelle samt Kennzeile, Kennzahlen und Quellenmenü (Punkt 7), Filterknöpfe ohne bleibenden Ring
/// nach einem Mausklick (Punkt 4) und die zugeklappten Erklärungen zu „Windows Auto HDR“ (Punkt 2). Läuft allein: Die
/// Prüfung der Überblendung stellt die Effektstufe.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class HeaderDetailsWindowTests(ITestOutputHelper output)
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static TestLibraryContext SampleContext(string effects = "Full") => new(
        Game(GameSource.Steam, "1", "Alan Wake 2", hdrAuto: true, isNew: true),
        Game(GameSource.Steam, "2", "Bernsteinhafen", hdrAuto: true, isNew: false),
        Game(GameSource.Steam, "3", "Celeste", isNew: true),
        Game(GameSource.Steam, "4", "Dredge", isNew: false),
        Game(GameSource.Gog, "6", "Frostpunk", hdrAuto: true, isNew: true),
        Game(GameSource.Gog, "7", "Gothic", isNew: true),
        Game(GameSource.Manual, "10", "Jusant", isNew: false))
    {
        Appearance = new StaticAppearanceService(new AppearanceSettings { EffectLevel = effects }),
    };

    private static (MainWindow Window, LibraryViewModel Model, TestLibraryContext Context) Open(Size size, LibraryViewMode view = LibraryViewMode.List, TestLibraryContext? context = null)
    {
        EnsureLibraryResources();
        context ??= SampleContext();
        context.ConfigService.Update(c => c.Settings.LibraryView = LibraryViewModes.ToConfig(view));
        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games.Single(g => g.Name == "Dredge"); // nicht neu: wählen ändert keine Zahl
        var window = new MainWindow();
        window.Attach(model);
        Arrange(window, size);
        return (window, model, context);
    }

    private static void Arrange(MainWindow window, Size size)
    {
        WpfTestHost.Layout(window, size);
        window.FitDetailsColumn();
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, size);
    }

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }

    private static T Named<T>(MainWindow window, string name) => (T)window.FindName(name);

    private static Rect Bounds(MainWindow window, FrameworkElement element) =>
        element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));

    private static T Part<T>(Control control, string name) where T : FrameworkElement => (T)control.Template.FindName(name, control);

    /// <summary>Angeordnet und sichtbar: selbst und alle Vorfahren sichtbar, mit Fläche (das Fenster wird nie angezeigt).</summary>
    private static bool Shown(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        for (DependencyObject? current = element; current is not null and not Window; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    private static void Raise(UIElement target, RoutedEventArgs args)
    {
        args.Source = target;
        target.RaiseEvent(args);
    }

    private static void PointerDown(UIElement target) =>
        Raise(target, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });

    private static void FocusArrives(UIElement target) =>
        Raise(target, new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, target) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });

    private static void FocusLeaves(UIElement target) =>
        Raise(target, new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, target, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

    private static void KeyDown(UIElement target, Key key) =>
        Raise(target, new KeyEventArgs(Keyboard.PrimaryDevice, new KeySource(), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    // ---------- Punkt 7: Titel folgt der Quelle ----------

    [Fact]
    public void Header_shows_the_overline_the_source_as_title_and_the_stats_of_the_selection()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new Size(1600, 1000));
            Assert.Equal(LibraryStrings.Header_Overline, Named<TextBlock>(window, "HeaderOverline").Text);
            var title = Named<CrossfadeText>(window, "HeaderTitle");
            Assert.Equal("Alle Spiele", title.Text);
            Assert.Equal("7 Spiele", Named<TextBlock>(window, "HeaderCount").Text);
            Assert.Equal(LibraryText.Format(LibraryStrings.Stats_HdrAuto, 3), Named<TextBlock>(window, "HeaderHdrAuto").Text);
            Assert.Equal(LibraryText.Format(LibraryStrings.Stats_New, 4), Named<TextBlock>(window, "HeaderNew").Text);

            // Über das Feld „Quelle“ der Werkzeugleiste gewählt: Titel und Zahlen folgen.
            var combo = Named<ComboBox>(window, "SourceFilter");
            combo.SelectedItem = model.SourceOptions.Single(o => o.Source == GameSource.Steam);
            Arrange(window, new Size(1600, 1000));
            Assert.Equal("Steam-Spiele", title.Text);
            Assert.Equal("4 Spiele", Named<TextBlock>(window, "HeaderCount").Text);
            Assert.True(Shown(Named<FrameworkElement>(window, "HeaderNewStat")));

            // Eigene Spiele: nichts neu, die Angabe „neu“ verschwindet.
            combo.SelectedItem = model.SourceOptions.Single(o => o.Source == GameSource.Manual);
            Arrange(window, new Size(1600, 1000));
            Assert.Equal("Eigene Spiele", title.Text);
            Assert.Equal("1 Spiel", Named<TextBlock>(window, "HeaderCount").Text);
            Assert.False(Shown(Named<FrameworkElement>(window, "HeaderNewStat")));

            // Filter dazu: „x von y Spielen“.
            combo.SelectedItem = model.SourceOptions.Single(o => o.Source == GameSource.Steam);
            Named<FilterToggle>(window, "FilterAutoButton").IsChecked = true;
            Arrange(window, new Size(1600, 1000));
            Assert.Equal("2 von 4 Spielen", Named<TextBlock>(window, "HeaderCount").Text);

            // Das Fenster selbst heißt weiter „HDRpilot – Spielebibliothek“.
            Assert.Equal(LibraryStrings.Window_Title, window.Title);
            window.Close();
        });
    }

    [Theory]
    [InlineData(1000, 620)]
    [InlineData(1280, 800)]
    [InlineData(1600, 1000)]
    [InlineData(1920, 1080)]
    [InlineData(3440, 1440)]
    public void Title_block_stays_in_the_header_and_overline_title_and_stats_share_their_left_edge(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            foreach (var view in new[] { LibraryViewMode.List, LibraryViewMode.Grid, LibraryViewMode.Shelf })
            {
                // Längster Titel: „Battle.net-Spiele“.
                var context = SampleContext();
                context.ConfigService.Update(c => c.Games.Add(Game(GameSource.BattleNet, "11", "Kingdom Come", isNew: false)));
                var (window, model, _) = Open(new Size(width, height), view, context);
                model.SelectedSource = model.SourceOptions.Single(o => o.Source == GameSource.BattleNet);
                Arrange(window, new Size(width, height));
                Assert.Equal("Battle.net-Spiele", Named<CrossfadeText>(window, "HeaderTitle").Text);

                var header = Bounds(window, Named<FrameworkElement>(window, "HeaderArea"));
                var block = Bounds(window, Named<FrameworkElement>(window, "HeaderTitleBlock"));
                var overline = Bounds(window, Named<FrameworkElement>(window, "HeaderOverline"));
                var title = Bounds(window, Named<FrameworkElement>(window, "HeaderTitle"));
                var stats = Named<FrameworkElement>(window, "HeaderStats");
                var where = $"{view} {width}×{height}";
                output.WriteLine(FormattableString.Invariant($"{where}: Kopf {header} Block {block} Kennzeile {overline} Titel {title} Kennzahlen {(Shown(stats) ? Bounds(window, stats) : Rect.Empty)}"));

                // Höchstens 2 px über den 52 px hohen Kopf hinaus, nie bis zum Inhalt darunter (beginnt 12 px tiefer).
                Assert.True(block.Top >= header.Top - 2 && block.Bottom <= header.Bottom + 2, $"{where}: Titelblock {block}, Kopf {header}");
                Assert.InRange(title.Left - overline.Left, -0.5, 0.5);
                Assert.True(overline.Bottom <= title.Top + 4, $"{where}: Kennzeile {overline} über dem Titel {title}");
                if (Shown(stats))
                {
                    var statsBox = Bounds(window, stats);
                    Assert.InRange(statsBox.Left - overline.Left, -0.5, 0.5);
                    Assert.True(statsBox.Top >= title.Bottom - 4, $"{where}: Kennzahlen {statsBox} unter dem Titel {title}");
                }

                // Der Pfeil steht rechts neben dem Titel, beides in der Kopfzeile links der Bildschirm-Kapsel.
                var button = Named<MenuButton>(window, "SourceTitleButton");
                var chevron = Bounds(window, Part<TextBlock>(button, "Chevron"));
                Assert.True(chevron.Left >= title.Right + 4, $"{where}: Pfeil {chevron} rechts vom Titel {title}");
                Assert.True(chevron.Right < Bounds(window, Named<FrameworkElement>(window, "NitsCapsule")).Left, $"{where}: Pfeil vor der Kapsel");
                window.Close();
            }
        });
    }

    [Fact]
    public void Source_menu_lists_every_source_with_its_count_ticks_the_choice_and_selects_like_the_toolbar()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new Size(1600, 1000));
            var preview = window.CreateSourceMenuPreview();
            Assert.NotNull(preview);
            var menu = Assert.IsType<ContextMenu>(preview!.Element);
            Assert.Equal("SourceMenu", AutomationProperties.GetAutomationId(menu));
            Assert.Equal(LibraryStrings.SourceMenu_Name, AutomationProperties.GetName(menu));
            WpfTestHost.Layout(menu, new Size(double.PositiveInfinity, double.PositiveInfinity));

            var items = model.SourceOptions.Select(o => (MenuItem)menu.ItemContainerGenerator.ContainerFromItem(o)).ToList();
            Assert.Equal(["Alle Spiele", "Steam-Spiele", "GOG-Spiele", "Eigene Spiele"], items.Select(i => (string)i.Header));
            Assert.Equal([7, 4, 2, 1], items.Select(i => (int)i.Tag));
            Assert.Equal([true, false, false, false], items.Select(i => i.IsChecked));
            Assert.All(items, i => Assert.False(i.IsCheckable)); // wählen, nicht umschalten
            Assert.Equal("Steam-Spiele (4)", AutomationProperties.GetName(items[1]));
            Assert.Equal(LibraryStrings.SourceMenu_Selected, AutomationProperties.GetItemStatus(items[0]));

            // Lage: linksbündig mit dem Titel, 4 px darunter (Menüfläche nach dem Rand für den Schatten).
            var button = Named<MenuButton>(window, "SourceTitleButton");
            var buttonBox = button.TransformToAncestor(Named<Visual>(window, "Root")).TransformBounds(new Rect(button.RenderSize));
            Assert.Equal(buttonBox.Left - MenuButton.MenuShadowMargin.Left, preview.Position.X, 1.0);
            Assert.Equal(buttonBox.Bottom + MenuButton.MenuGap - MenuButton.MenuShadowMargin.Top, preview.Position.Y, 1.0);
            Assert.Equal(new Thickness(8, 4, 12, 18), ((FrameworkElement)VisualTreeHelper.GetChild(menu, 0)).Margin); // Rand von Lib.ContextMenu

            // „GOG-Spiele“ wählen wie ein Klick: Leiste, Haken, Titel und Kennzahlen folgen.
            var gog = items[2];
            Assert.True(gog.Command.CanExecute(gog.CommandParameter));
            gog.Command.Execute(gog.CommandParameter);
            Arrange(window, new Size(1600, 1000));
            Assert.Equal(GameSource.Gog, ((SourceFilterOption)Named<ComboBox>(window, "SourceFilter").SelectedItem).Source);
            Assert.Equal([false, false, true, false], items.Select(i => i.IsChecked));
            Assert.Equal("GOG-Spiele", Named<CrossfadeText>(window, "HeaderTitle").Text);
            Assert.Equal("2 Spiele", Named<TextBlock>(window, "HeaderCount").Text);
            window.Close();
        });
    }

    [Fact]
    public void Title_button_is_a_menu_button_for_automation_with_name_help_and_expand_collapse()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new Size(1600, 1000));
            var button = Named<MenuButton>(window, "SourceTitleButton");
            Assert.Equal("SourceTitleButton", AutomationProperties.GetAutomationId(button));
            Assert.Equal("Quelle: Alle Spiele", AutomationProperties.GetName(button));
            Assert.Equal(LibraryStrings.Header_SourceHelp, AutomationProperties.GetHelpText(button));
            Assert.True(button.Focusable && button.IsTabStop);
            Assert.True(FocusCue.GetEnabled(button));
            Assert.Equal("SourceMenu", AutomationProperties.GetAutomationId(button.ContextMenu!));

            var peer = UIElementAutomationPeer.CreatePeerForElement(button);
            var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
            Assert.NotNull(peer.GetPattern(PatternInterface.Invoke));
            Assert.Equal(nameof(MenuButton), peer.GetClassName());

            model.SelectedSource = model.SourceOptions.Single(o => o.Source == GameSource.Gog);
            Assert.Equal("Quelle: GOG-Spiele", AutomationProperties.GetName(button));

            // Geht das Menü auf (Klick, Tastatur, Rechtsklick), meldet der Knopf „aufgeklappt“: Schleier und gedrehter Pfeil.
            var menu = button.ContextMenu!;
            var chevron = Part<TextBlock>(button, "Chevron");
            Assert.Equal(0.0, Part<Border>(button, "Body").Opacity);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            Assert.True(button.IsMenuOpen);
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            Assert.Equal(180.0, ((RotateTransform)chevron.RenderTransform).Angle);
            Assert.Equal(1.0, Part<Border>(button, "Body").Opacity);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent, menu));
            Assert.False(button.IsMenuOpen);
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
            Assert.Equal(0.0, ((RotateTransform)chevron.RenderTransform).Angle);

            // Mit der Tastatur geöffnet steht der Fokus auf der gewählten Quelle, ohne Wahl auf dem ersten Eintrag.
            var preview = window.CreateSourceMenuPreview()!;
            WpfTestHost.Layout(preview.Element, new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Equal("GOG-Spiele", MenuButton.EntryToFocus(menu)?.Header);
            var plain = new ContextMenu { Items = { new MenuItem { Header = "a", IsEnabled = false }, new MenuItem { Header = "b" }, new MenuItem { Header = "c" } } };
            Assert.Equal("b", MenuButton.EntryToFocus(plain)?.Header);
            window.Close();
        });
    }

    [Theory]
    [InlineData(EffectLevel.Full, true, 280, true)]
    [InlineData(EffectLevel.Reduced, true, 150, false)]
    [InlineData(EffectLevel.Off, true, 0, false)]
    [InlineData(EffectLevel.Full, false, 0, false)] // ohne Hardware-Darstellung oder ungeladen: sofort
    public void Title_crossfade_follows_the_effect_level(EffectLevel level, bool enabled, int milliseconds, bool moves)
    {
        Assert.Equal((TimeSpan.FromMilliseconds(milliseconds), moves), CrossfadeText.TransitionFor(level, enabled));
    }

    [Fact]
    public void Title_crossfades_from_the_old_to_the_new_text_and_leaves_no_clock_behind()
    {
        WpfTestHost.Run(() =>
        {
            var motion = LibraryMotion.Instance;
            var before = motion.Level;
            var text = new CrossfadeText { Text = "Alle Spiele" };
            System.Windows.Documents.TextElement.SetFontSize(text, 21);
            using var host = new HwndSource(new HwndSourceParameters("HdrPilot.Tests.CrossfadeHost")
            {
                WindowStyle = WsPopup,
                ExtendedWindowStyle = WsExToolWindow | WsExNoActivate,
                PositionX = -32000,
                PositionY = -32000,
                Width = 0,
                Height = 0,
            })
            {
                SizeToContent = SizeToContent.WidthAndHeight,
                RootVisual = new Border { Child = text },
            };
            try
            {
                WpfTestHost.DoEvents();
                Assert.True(text.IsLoaded, "nicht geladen");

                motion.SetLevel(EffectLevel.Off);
                text.Text = "Steam-Spiele";
                Assert.Equal(TimeSpan.Zero, text.LastTransition); // Aus: sofort, ohne alten Text
                Assert.Equal(string.Empty, text.FadingText);
                Assert.False(text.IsAnimating);
                text.UpdateLayout();
                var shortWidth = text.ActualWidth;
                Assert.True(shortWidth > 0);

                motion.SetLevel(EffectLevel.Full);
                if (!LibraryMotion.Enabled)
                {
                    output.WriteLine("Ohne Hardware-Darstellung: Überblendung nicht abgespielt.");
                    return; // ohne Hardware-Darstellung laufen keine Übergänge (geprüft in Title_crossfade_follows_the_effect_level)
                }

                text.Text = "Battle.net-Spiele";
                Assert.Equal(TimeSpan.FromMilliseconds(280), text.LastTransition);
                Assert.True(text.IsAnimating);
                Assert.Equal("Steam-Spiele", text.FadingText); // der alte Text blendet aus …
                Assert.Equal("Battle.net-Spiele", text.Text); // … der neue ein

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                var widths = new List<double>();
                while (text.IsAnimating && DateTime.UtcNow < deadline)
                {
                    WpfTestHost.DoEvents();
                    text.UpdateLayout();
                    widths.Add(text.ActualWidth);
                    Thread.Sleep(15);
                }

                Assert.False(text.IsAnimating, "Überblendung endet nicht");
                Assert.Equal(string.Empty, text.FadingText);
                Assert.False(text.HasAnimatedProperties); // keine Uhr bleibt hängen
                Assert.Equal(1.0, text.Progress);
                text.UpdateLayout();
                var longWidth = text.ActualWidth;
                Assert.True(longWidth > shortWidth, $"Breite {longWidth:0.0} nach {shortWidth:0.0}");
                Assert.All(widths, w => Assert.InRange(w, Math.Min(shortWidth, longWidth) - 0.5, longWidth + 0.5)); // gleitet, springt nicht über
                output.WriteLine(FormattableString.Invariant($"Überblendung: {widths.Count} Bilder, Breite {shortWidth:0.0} → {longWidth:0.0}, Zwischenwerte {string.Join(' ', widths.Select(w => w.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))}"));

                motion.SetLevel(EffectLevel.Reduced);
                text.Text = "GOG-Spiele";
                Assert.Equal(TimeSpan.FromMilliseconds(150), text.LastTransition);
            }
            finally
            {
                motion.SetLevel(before);
                host.RootVisual = null;
            }
        });
    }

    // ---------- Punkt 4: Filterknöpfe ohne bleibenden Ring ----------

    [Theory]
    [InlineData(false, false, false, false)] // Fokus vom Programm ohne Tastatur
    [InlineData(false, true, true, false)] // Klick: auch nach einer Taste vorher kein Ring
    [InlineData(false, false, true, true)] // Tab oder Pfeile
    [InlineData(true, true, false, true)] // Windows zeigt Fokus immer
    public void Focus_ring_shows_for_keyboard_focus_only(bool keyboardCues, bool pointerPressed, bool lastInputKeyboard, bool expected)
    {
        WpfTestHost.Run(() =>
        {
            InputDevice? device = lastInputKeyboard ? Keyboard.PrimaryDevice : Mouse.PrimaryDevice;
            Assert.Equal(expected, FocusCue.ShowsOnFocus(keyboardCues, pointerPressed, device));
        });
    }

    [Fact]
    public void Filter_toggles_look_selected_only_while_on_and_show_the_ring_only_after_keyboard_use()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = Open(new Size(1600, 1000));
            foreach (var name in new[] { "FilterAutoButton", "FilterNewButton", "FilterHdrButton" })
            {
                var toggle = Named<FilterToggle>(window, name);
                Assert.True(FocusCue.GetEnabled(toggle), name);
                var ring = Part<Border>(toggle, "Focus");
                var on = Part<Border>(toggle, "PART_On");
                var always = SystemParameters.KeyboardCues; // „Zugriffstasten unterstreichen“: Windows zeigt Fokus immer

                // Mausklick: Druck, Fokus, an … und wieder aus. Danach sieht der Knopf aus wie vorher (kein Ring, keine Fläche).
                PointerDown(toggle);
                FocusArrives(toggle);
                toggle.IsChecked = true;
                Assert.Equal(1.0, on.Opacity);
                PointerDown(toggle);
                toggle.IsChecked = false;
                Arrange(window, new Size(1600, 1000));
                Assert.Equal(0.0, on.Opacity);
                Assert.Equal(always ? Visibility.Visible : Visibility.Collapsed, ring.Visibility);
                Assert.Equal(always, FocusCue.GetIsVisible(toggle));

                // Leertaste auf dem Knopf: jetzt wird mit der Tastatur bedient, der Ring erscheint.
                KeyDown(toggle, Key.Space);
                Assert.True(FocusCue.GetIsVisible(toggle), name);
                Assert.Equal(Visibility.Visible, ring.Visibility);

                // Nur Umschalt oder Alt zählen nicht; ein Klick nimmt den Ring wieder weg, ebenso der Fokuswechsel.
                PointerDown(toggle);
                Assert.Equal(always, FocusCue.GetIsVisible(toggle));
                KeyDown(toggle, Key.LeftShift);
                Assert.Equal(always, FocusCue.GetIsVisible(toggle));
                KeyDown(toggle, Key.Space);
                FocusLeaves(toggle);
                Assert.False(FocusCue.GetIsVisible(toggle));
                Assert.Equal(Visibility.Collapsed, ring.Visibility);
            }

            Assert.False(model.OnlySelected || model.OnlyNew || model.OnlyHdrCapable);
            window.Close();
        });
    }

    // ---------- Punkt 2: Erklärungen zu Windows Auto HDR ----------

    [Fact]
    public void Auto_hdr_info_starts_collapsed_opens_by_automation_and_stays_open_for_the_window_only()
    {
        WpfTestHost.Run(() =>
        {
            var context = SampleContext();
            context.ConfigService.Update(c => c.FindGame("Steam:4")!.AutoHdr = true);
            var (window, model, _) = Open(new Size(1460, 1400), context: context);
            var toggle = Named<DisclosureToggle>(window, "AutoHdrInfoToggle");
            var info = Named<FrameworkElement>(window, "AutoHdrInfo");
            Assert.Equal(LibraryStrings.Details_AutoHdrInfoName, AutomationProperties.GetName(toggle));
            Assert.Equal(LibraryStrings.Details_AutoHdrInfoHelp, AutomationProperties.GetHelpText(toggle));
            Assert.True(FocusCue.GetEnabled(toggle));
            Assert.False(Shown(info));
            Assert.Equal(LibraryStrings.Details_AutoHdrMore, Part<TextBlock>(toggle, "Label").Text);

            // UI-Automation: Aus-/Einklappen (und Umschalten wie jeder Umschaltknopf).
            var peer = UIElementAutomationPeer.CreatePeerForElement(toggle);
            var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            Assert.NotNull(peer.GetPattern(PatternInterface.Toggle));
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
            expand.Expand();
            Arrange(window, new Size(1460, 1400));
            Assert.True(model.IsAutoHdrInfoExpanded);
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            Assert.True(Shown(info));
            Assert.Equal(LibraryStrings.Details_AutoHdrLess, Part<TextBlock>(toggle, "Label").Text);

            // Gilt für das Fenster: ein anderes Spiel gewählt, bleibt offen.
            model.SelectedGame = model.Games.Single(g => g.Name == "Bernsteinhafen");
            Arrange(window, new Size(1460, 1400));
            Assert.True(Named<DisclosureToggle>(window, "AutoHdrInfoToggle").IsChecked);
            Assert.True(Shown(info));

            expand.Collapse();
            Arrange(window, new Size(1460, 1400));
            Assert.False(model.IsAutoHdrInfoExpanded);
            Assert.False(Shown(info));

            // Nicht gespeichert: Die Konfiguration kennt kein Feld dafür, ein neues Fenster beginnt zugeklappt.
            var updates = context.Updates;
            expand.Expand();
            Assert.Equal(updates, context.Updates); // nichts geschrieben
            Assert.DoesNotContain("expand", context.Files.ReadAllText(ConfigPath), StringComparison.OrdinalIgnoreCase);
            window.Close();
            var (again, againModel, _) = Open(new Size(1460, 1400), context: context);
            Assert.False(againModel.IsAutoHdrInfoExpanded);
            Assert.False(Shown(Named<FrameworkElement>(again, "AutoHdrInfo")));
            again.Close();
        });
    }

    [Theory]
    [InlineData(true, false, false, true)] // Auto HDR an, „HDR automatisch“ aus: der Hinweis bleibt stehen
    [InlineData(false, false, false, false)] // Auto HDR aus: nur Erklärung, zugeklappt
    [InlineData(true, true, false, false)] // „HDR automatisch“ an: nichts zu tun
    [InlineData(false, false, true, true)] // aufgeklappt wie bisher
    [InlineData(true, true, true, false)]
    public void Needs_hdr_hint_stays_visible_collapsed_only_where_the_choice_would_run_into_nothing(bool autoHdr, bool hdrAuto, bool expanded, bool shown)
    {
        WpfTestHost.Run(() =>
        {
            var context = SampleContext();
            context.ConfigService.Update(c =>
            {
                c.FindGame("Steam:4")!.AutoHdr = autoHdr;
                c.FindGame("Steam:4")!.HdrAuto = hdrAuto;
            });
            var (window, model, _) = Open(new Size(1460, 1400), context: context);
            model.IsAutoHdrInfoExpanded = expanded;
            Arrange(window, new Size(1460, 1400));

            var hint = (FrameworkElement)WpfTestHost.Visuals(Named<DependencyObject>(window, "PictureSection")).OfType<TextBlock>()
                .Single(t => AutomationProperties.GetAutomationId(t) == "DetailAutoHdrNeedsHdrHint");
            Assert.Equal(shown, Shown(hint));
            Assert.Equal(shown, model.Details!.ShowAutoHdrNeedsHdrLine);
            Assert.Equal(autoHdr && !hdrAuto, model.Details.IsAutoHdrNeedsHdrEssential);

            // Live: „HDR automatisch“ in der Zeile umgeschaltet (aus wird an und umgekehrt).
            model.SelectedGame!.HdrAuto = !hdrAuto;
            Arrange(window, new Size(1460, 1400));
            Assert.Equal(hdrAuto && (expanded || autoHdr), Shown(hint));
            window.Close();
        });
    }

    /// <summary>Tastenereignisse brauchen eine Quelle; das Fenster wird in Tests nie angezeigt.</summary>
    private sealed class KeySource : PresentationSource
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
