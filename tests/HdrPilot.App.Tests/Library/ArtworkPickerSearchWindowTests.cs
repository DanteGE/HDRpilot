using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Tests.Theming;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Fenster „Bild wählen…“ mit SteamGridDB (E93, G14.4), auf dem WPF-Testhost erzeugt und angeordnet, nie angezeigt: nie höher
/// als die Arbeitsfläche, wächst nur und um die Mitte, nur die Mitte scrollt (auch per Mausrad über Kacheln), die Leiste
/// kostet keine Breite, Kacheln füllen 830 DIP genau, Logos ganz auf dem Schachbrett, Tab-Reihenfolge, Tasten der Spielwahl,
/// Kennungen und Muster für Bildschirmleser, hoher Kontrast, Effektstufen, Farben nur aus dem Thema, Übernehmen ohne Fehler.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed partial class ArtworkPickerSearchWindowTests
{
    private const string SteamKey = "Steam:9990150";

    public static TheoryData<double, double> WorkAreas => new()
    {
        { 1280, 760 },
        { 1024, 616 },
        { 1920, 1040 },
        { 3440, 1400 },
    };

    [Theory]
    [MemberData(nameof(WorkAreas))]
    public void The_dialog_never_exceeds_the_work_area_and_only_the_middle_scrolls(double width, double height)
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Cover);
            Search(picker, ArtworkKind.Cover, more: 2);
            var work = new Rect(0, 0, width, height);

            dialog.FitToWorkArea(work);
            var root = Arrange(dialog, dialog.MaxHeight);

            Assert.Equal(height - DialogPlacement.Margin, dialog.MaxHeight);
            Assert.True(root.ActualHeight <= dialog.MaxHeight + 0.5, root.ActualHeight + " > " + dialog.MaxHeight);
            var scroll = Find<ScrollViewer>(dialog, "Scroll");
            Assert.True(scroll.ScrollableHeight > 0); // 72 Cover passen nie ganz
            var apply = Find<Button>(dialog, "ApplyButton");
            Assert.True(apply.TranslatePoint(new Point(0, apply.ActualHeight), root).Y <= root.ActualHeight + 0.5); // Knöpfe bleiben sichtbar
            var tabs = Find<RadioButton>(dialog, "TabHero");
            Assert.True(tabs.TranslatePoint(default, root).Y >= 0);
            dialog.Close();
        });
    }

    [Fact]
    public void Placement_grows_around_the_centre_and_stays_inside_the_work_area()
    {
        var work = new Rect(0, 0, 1280, 760);

        Assert.Equal(400, DialogPlacement.GrowTop(work, 450, 500, 600)); // je 50 nach oben und unten
        Assert.Equal(12, DialogPlacement.GrowTop(work, 30, 500, 700)); // oben nie über den Rand
        Assert.Equal(760 - 12 - 700, DialogPlacement.GrowTop(work, 200, 500, 700)); // unten nie darunter
        Assert.Equal(736, DialogPlacement.MaxHeightFor(work, 300));
        Assert.Equal(640, DialogPlacement.MaxHeightFor(new Rect(0, 0, 800, 500), 640)); // nie unter der Mindesthöhe

        // Bildschirm über dem Hauptbildschirm (Arbeitsfläche beginnt nicht bei 0).
        var upper = new Rect(-200, -1080, 1920, 1040);
        Assert.Equal(-1080 + 12, DialogPlacement.GrowTop(upper, -1070, 600, 900));
        Assert.Equal(-1080 + 1040 - 12 - 900, DialogPlacement.GrowTop(upper, -300, 600, 900));
        Assert.Equal(-700, DialogPlacement.GrowTop(upper, -650, 600, 700));
    }

    [Fact]
    public void The_window_never_shrinks_while_open()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Cover);
            dialog.FitToWorkArea(new Rect(0, 0, 1280, 760));
            var ready = Arrange(dialog, dialog.MaxHeight).DesiredSize.Height;
            Search(picker, ArtworkKind.Cover);
            var results = Arrange(dialog, dialog.MaxHeight).DesiredSize.Height;
            Assert.True(results > ready);

            dialog.Grow(ready, results);
            Assert.Equal(results, dialog.MinHeight);

            // Zurück zu einem Reiter ohne Ergebnisse: weniger Inhalt, das Fenster bleibt so hoch.
            picker.Kind = ArtworkKind.Hero;
            dialog.Grow(results, ready);
            Assert.Equal(results, dialog.MinHeight);
            Assert.True(Arrange(dialog, dialog.MaxHeight).DesiredSize.Height >= results - 0.5);

            // Nie über die Arbeitsfläche hinaus.
            dialog.FitToWorkArea(new Rect(0, 0, 1024, 616));
            dialog.MinHeight = 0;
            dialog.Grow(500, 900);
            Assert.Equal(dialog.MaxHeight, dialog.MinHeight);
            dialog.Close();
        });
    }

    [Theory]
    [InlineData(ArtworkKind.Cover, 130, 6)]
    [InlineData(ArtworkKind.Hero, 200, 4)]
    [InlineData(ArtworkKind.Logo, 200, 4)]
    public void Tiles_fill_the_content_width_exactly_per_kind(ArtworkKind kind, double tile, int perRow)
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(kind);
            Load(picker);
            Search(picker, kind);
            Arrange(dialog, double.PositiveInfinity);
            var content = Find<StackPanel>(dialog, "ScrollContent");
            var results = Find<ListBox>(dialog, "Results");

            Assert.Equal(830, content.ActualWidth);
            Assert.Equal(830, perRow * tile + (perRow - 1) * 10);
            var containers = picker.Online.Results.Take(perRow + 1).Select(r => (FrameworkElement)results.ItemContainerGenerator.ContainerFromItem(r)).ToList();
            for (var i = 0; i < perRow; i++)
            {
                var at = containers[i].TranslatePoint(default, content);
                Assert.Equal(i * (tile + 10), at.X, 1);
                Assert.Equal(tile, containers[i].ActualWidth, 1);
            }

            Assert.Equal(830, containers[perRow - 1].TranslatePoint(new Point(tile, 0), content).X, 1); // rechte Kante genau 830
            Assert.Equal(0, containers[perRow].TranslatePoint(default, content).X, 1); // nächste Reihe beginnt links

            // Lokale Kacheln und „Eigene Datei…“ in derselben Form.
            var local = Find<ListBox>(dialog, "Candidates");
            var automatic = (FrameworkElement)local.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.Equal(tile, automatic.ActualWidth, 1);
            Assert.Equal(tile, Find<Button>(dialog, "PickFileButton").ActualWidth, 1);
            Assert.Equal(tile + 10, Find<Button>(dialog, "PickFileButton").TranslatePoint(default, content).X, 1);
            Assert.Equal(automatic.ActualHeight, containers[0].ActualHeight, 1); // gleiche Zeilenhöhe lokal und online
            dialog.Close();
        });
    }

    [Fact]
    public void Logos_sit_uniformly_on_the_checker_plate_and_are_never_cropped()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Logo);
            Load(picker);
            Search(picker, ArtworkKind.Logo);
            Pump(() => picker.Online.Results[0].Preview is not null);
            Arrange(dialog, double.PositiveInfinity);

            foreach (var list in new[] { Find<ListBox>(dialog, "Results"), Find<ListBox>(dialog, "Candidates") })
            {
                var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var presenter = Descendants(container).OfType<ContentPresenter>().First();
                var art = (Border)presenter.ContentTemplate.FindName("Art", presenter);
                var plate = (Border)presenter.ContentTemplate.FindName("Plate", presenter);
                var box = (Grid)presenter.ContentTemplate.FindName("PreviewBox", presenter);

                Assert.Equal(Stretch.Uniform, ImageFill.GetStretch(art));
                Assert.Equal(new Thickness(12), art.Margin);
                Assert.IsType<DrawingBrush>(plate.Background);
                Assert.Equal(104, box.ActualHeight, 1);
                if (art.Background is ImageBrush brush)
                {
                    Assert.Equal(Stretch.Uniform, brush.Stretch); // ganz, nie beschnitten
                }
            }

            var plateBrush = (DrawingBrush)dialog.FindResource("Lib.LogoPlate");
            Assert.True(plateBrush.IsFrozen);
            Assert.Equal(new Rect(0, 0, 16, 16), plateBrush.Viewport);
            Assert.Equal(TileMode.Tile, plateBrush.TileMode);
            dialog.Close();
        });
    }

    [Fact]
    public void Mouse_wheel_over_tiles_scrolls_the_dialog()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Cover);
            Search(picker, ArtworkKind.Cover, more: 1);
            dialog.FitToWorkArea(new Rect(0, 0, 1024, 616));
            Arrange(dialog, dialog.MaxHeight);
            var scroll = Find<ScrollViewer>(dialog, "Scroll");
            var results = Find<ListBox>(dialog, "Results");
            var tile = (UIElement)results.ItemContainerGenerator.ContainerFromIndex(3);
            Assert.Equal(0, scroll.VerticalOffset);

            tile.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent, Source = tile });
            Arrange(dialog, dialog.MaxHeight);

            Assert.True(scroll.VerticalOffset > 0);
            Assert.True(ShellScrollChains(results));
            dialog.Close();
        });
    }

    [Fact]
    public void The_scroll_bar_takes_no_content_width()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Hero);
            Search(picker, ArtworkKind.Hero, more: 2);
            dialog.FitToWorkArea(new Rect(0, 0, 1280, 760));
            Arrange(dialog, dialog.MaxHeight);
            var scroll = Find<ScrollViewer>(dialog, "Scroll");
            var content = Find<StackPanel>(dialog, "ScrollContent");
            var bar = Descendants(scroll).OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);

            Assert.Equal(850, scroll.ActualWidth, 1); // 830 Inhalt + 20 im rechten Rand
            Assert.Equal(830, content.ActualWidth, 1);
            Assert.Equal(Visibility.Visible, bar.Visibility);
            Assert.Equal(8, bar.ActualWidth, 1);
            var left = bar.TranslatePoint(default, scroll).X;
            Assert.Equal(836, left, 1); // mittig im Streifen 830–850
            Assert.True(content.TranslatePoint(new Point(830, 0), scroll).X <= left);
            dialog.Close();
        });
    }

    [Fact]
    public void Tab_order_follows_the_spec()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Cover);
            Search(picker, ArtworkKind.Cover);
            picker.Online.IsMatchOpen = true;
            Arrange(dialog, double.PositiveInfinity);

            var order = PreOrder(dialog).OfType<FrameworkElement>().Where(e => e.Name.Length > 0).Select(e => e.Name).ToList();
            var expected = new[] { "TabHero", "TabLogo", "TabCover", "Candidates", "PickFileButton", "MatchButton", "MatchSearch", "MatchSearchButton", "MatchEntries", "SearchButton", "Results", "MoreButton", "SettingsLink", "RetryButton", "ResetButton", "CancelButton", "ApplyButton" };
            var positions = expected.Select(name => order.IndexOf(name)).ToList();

            Assert.DoesNotContain(-1, positions);
            Assert.Equal(positions.Order(), positions);
            Assert.All(PreOrder(dialog).OfType<FrameworkElement>(), e => Assert.Equal(int.MaxValue, KeyboardNavigation.GetTabIndex(e))); // keine eigene Reihenfolge
            dialog.Close();
        });
    }

    [Fact]
    public void Focus_moves_to_the_first_result_only_while_focus_is_in_the_section()
    {
        // Die Ansicht folgt nur aus dem Abschnitt heraus; eine Kachel nach gescheitertem Übernehmen immer.
        Assert.True(ArtworkPickerDialog.FollowsFocus(SteamGridDbFocusTarget.FirstResult, focusInSection: true));
        Assert.False(ArtworkPickerDialog.FollowsFocus(SteamGridDbFocusTarget.FirstResult, focusInSection: false));
        Assert.False(ArtworkPickerDialog.FollowsFocus(SteamGridDbFocusTarget.FirstNewResult, focusInSection: false));
        Assert.True(ArtworkPickerDialog.FollowsFocus(SteamGridDbFocusTarget.Tile, focusInSection: false));

        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Cover);
            var requests = new List<SteamGridDbFocusRequestEventArgs>();
            picker.Online.FocusRequest += (_, e) => requests.Add(e);
            Search(picker, ArtworkKind.Cover);
            Assert.Contains(requests, r => r.Target == SteamGridDbFocusTarget.FirstResult && ReferenceEquals(r.Item, picker.Online.Results[0]));

            var more = picker.Online.MoreAsync(ArtworkKind.Cover);
            Pump(() => more.IsCompleted);
            Assert.Contains(requests, r => r.Target == SteamGridDbFocusTarget.FirstNewResult && ReferenceEquals(r.Item, picker.Online.Results[24]));
            dialog.Close();
        });
    }

    [Fact]
    public void Esc_in_the_chooser_collapses_it_and_keeps_the_dialog_open()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Hero);
            picker.Online.IsMatchOpen = true;
            Arrange(dialog, double.PositiveInfinity);
            var box = Find<TextBox>(dialog, "MatchSearch");

            var escape = Press(box, Key.Escape);

            Assert.True(escape.Handled); // erreicht „Abbrechen“ (IsCancel) nicht
            Assert.False(picker.Online.IsMatchOpen);
            Assert.True(dialog.IsLoaded || !dialog.IsVisible);
            Assert.False(picker.SettingsRequested);
            dialog.Close();
        });
    }

    [Fact]
    public void Enter_in_the_chooser_box_searches_and_does_not_apply()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, sgdb) = Open(ArtworkKind.Hero);
            picker.Online.IsMatchOpen = true;
            picker.Online.MatchSearchText = "Nebelfahrt";
            Arrange(dialog, double.PositiveInfinity);

            var enter = Press(Find<TextBox>(dialog, "MatchSearch"), Key.Enter);
            Pump(() => !picker.Online.IsMatchSearching);

            Assert.True(enter.Handled); // erreicht „Übernehmen“ (IsDefault) nicht
            Assert.Contains("games:Nebelfahrt", sgdb.Session!.ApiCalls);
            Assert.Empty(sgdb.Session.Downloads);
            Assert.False(picker.IsApplying);
            Assert.Equal(3, picker.Online.MatchEntries.Count - 1); // Steam-Eintrag und drei Treffer
            dialog.Close();
        });
    }

    [Fact]
    public void Automation_ids_names_patterns_and_live_regions_are_set()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Cover);
            Search(picker, ArtworkKind.Cover);
            picker.Online.IsMatchOpen = true;
            Arrange(dialog, double.PositiveInfinity);
            var ids = PreOrder(dialog).Select(AutomationProperties.GetAutomationId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

            foreach (var id in new[]
            {
                "ArtworkPickerDialog", "ArtworkTabHero", "ArtworkTabLogo", "ArtworkTabCover", "ArtworkCandidates", "ArtworkPickFileButton", "ArtworkResetButton", "ArtworkCancelButton",
                "ArtworkApplyButton", "ArtworkPickerMessage", "ArtworkSgdbSection", "ArtworkSgdbMatchButton", "ArtworkSgdbAmbiguous", "ArtworkSgdbMatchPanel", "ArtworkSgdbMatchSearch",
                "ArtworkSgdbMatchSearchButton", "ArtworkSgdbMatchList", "ArtworkSgdbSearchButton", "ArtworkSgdbResults", "ArtworkSgdbMoreButton", "ArtworkSgdbRetryButton",
                "ArtworkSgdbSettingsLink", "ArtworkSgdbStatus", "ArtworkSgdbAnnouncement", "ArtworkSgdbProgress", "ArtworkPickerBusy",
            })
            {
                Assert.Contains(id, ids);
            }

            var section = Find<FrameworkElement>(dialog, "SgdbSection");
            Assert.Equal(AutomationControlType.Group, UIElementAutomationPeer.CreatePeerForElement(section).GetAutomationControlType());
            Assert.Equal(LibraryStrings.Picker_SgdbSectionName, AutomationProperties.GetName(section));
            var match = Find<ExpandToggle>(dialog, "MatchButton");
            var peer = UIElementAutomationPeer.CreatePeerForElement(match);
            Assert.NotNull(peer.GetPattern(PatternInterface.ExpandCollapse));
            Assert.NotNull(peer.GetPattern(PatternInterface.Toggle));
            Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbMatchName, "Kupferwald"), AutomationProperties.GetName(match));
            Assert.Equal(LibraryStrings.Picker_SgdbMatchHowSteam, AutomationProperties.GetHelpText(match));
            Assert.Equal(AutomationControlType.Group, UIElementAutomationPeer.CreatePeerForElement(Find<FrameworkElement>(dialog, "MatchPanel")).GetAutomationControlType());
            Assert.Equal(LibraryStrings.Picker_SgdbMatchListName, AutomationProperties.GetName(Find<FrameworkElement>(dialog, "MatchList")));
            Assert.Equal(LibraryStrings.Picker_SgdbMatchSearchName, AutomationProperties.GetName(Find<FrameworkElement>(dialog, "MatchSearch")));
            Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbSearchName, LibraryStrings.Picker_KindPluralCover), AutomationProperties.GetName(Find<FrameworkElement>(dialog, "SearchButton")));
            Assert.Equal(LibraryStrings.Picker_SgdbSearchHelp, AutomationProperties.GetHelpText(Find<FrameworkElement>(dialog, "SearchButton")));
            Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbMoreName, LibraryStrings.Picker_KindPluralCover), AutomationProperties.GetName(Find<FrameworkElement>(dialog, "MoreButton")));
            Assert.Equal(LibraryStrings.Picker_SgdbRetry, AutomationProperties.GetName(Find<FrameworkElement>(dialog, "RetryButton")));
            Assert.Equal(LibraryStrings.Picker_SgdbOpenSettingsHelp, AutomationProperties.GetHelpText(Find<FrameworkElement>(dialog, "SettingsLink")));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Find<FrameworkElement>(dialog, "Announcement")));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Find<FrameworkElement>(dialog, "BusyLine")));
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(Descendants(dialog).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "ArtworkSgdbProgress")));

            // Kacheln heißen nach Bildart, Autor und Größe; Einträge der Spielwahl mit Status „gewählt“.
            var results = Find<ListBox>(dialog, "Results");
            var tile = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.Equal(picker.Online.Results[0].AutomationName, AutomationProperties.GetName(tile));
            Assert.Equal(LibraryStrings.Picker_SgdbItemHelp, AutomationProperties.GetHelpText(tile));
            var entry = Descendants(Find<FrameworkElement>(dialog, "MatchEntries")).OfType<Button>().First();
            Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbSteamEntry, "Kupferwald"), AutomationProperties.GetName(entry));
            Assert.Equal(LibraryStrings.Picker_SgdbEntryCurrent, AutomationProperties.GetItemStatus(entry));
            Assert.NotNull(UIElementAutomationPeer.CreatePeerForElement(entry).GetPattern(PatternInterface.Invoke));
            dialog.Close();
        });
    }

    public static TheoryData<string, bool> ContrastCases => new()
    {
        { "Nachthimmel", true },
        { "Nachthimmel", false },
        { "Wüste", true },
        { "Wüste", false },
    };

    [Theory]
    [MemberData(nameof(ContrastCases))]
    public void High_contrast_keeps_every_text_readable(string theme, bool dark)
    {
        WpfTestHost.Run(() =>
        {
            var palette = theme == "Nachthimmel" ? HighContrastPalette.NightSky : HighContrastPalette.Desert;
            var (dialog, picker, _) = Open(ArtworkKind.Cover);
            UseTheme(dialog, dark);
            Load(picker);
            Search(picker, ArtworkKind.Cover);
            picker.Online.IsMatchOpen = true;
            dialog.SetHighContrast(palette);
            Arrange(dialog, double.PositiveInfinity);

            var failures = new List<string>();
            foreach (var text in Descendants(dialog).OfType<TextBlock>().Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text) && t.ActualWidth > 0))
            {
                if (text.Foreground is not SolidColorBrush fore)
                {
                    continue;
                }

                var back = EffectiveBackground(text, palette.Window);
                var ratio = Contrast(fore.Color, back);
                if (ratio < 4.5)
                {
                    failures.Add($"{text.Text} ({ratio:0.0}:1)");
                }
            }

            Assert.True(failures.Count == 0, string.Join(", ", failures.Take(10)));
            Assert.Equal(palette.Window, ((SolidColorBrush)dialog.FindResource(ThemeKeys.BrushPlate)).Color);
            Assert.Equal(palette.Window, ((SolidColorBrush)dialog.FindResource("Lib.LogoPlate")).Color);
            Assert.Equal(palette.Highlight, ((SolidColorBrush)dialog.FindResource(ThemeKeys.BrushSelectionRing)).Color);
            Assert.Equal(palette.WindowText, ((SolidColorBrush)dialog.FindResource("Lib.Brush.ContrastFrame")).Color);
            Assert.Equal(new Thickness(1), (Thickness)dialog.FindResource("Lib.ContrastFrameThickness"));

            // Gegenprobe: ohne Kontrast gelten wieder die Pinsel des Themas.
            dialog.SetHighContrast(null);
            Assert.IsNotType<SolidColorBrush>(dialog.FindResource("Lib.LogoPlate"));
            dialog.Close();
        });
    }

    [Fact]
    public void Effect_level_off_starts_no_animation_and_full_reveals_once()
    {
        WpfTestHost.Run(() =>
        {
            var previous = LibraryMotion.Instance.Level;
            try
            {
                LibraryMotion.Instance.SetLevel(EffectLevel.Off);
                var (dialog, picker, _) = Open(ArtworkKind.Hero);
                Arrange(dialog, double.PositiveInfinity);
                Search(picker, ArtworkKind.Hero);
                Arrange(dialog, double.PositiveInfinity);
                WpfTestHost.DoEvents();
                Assert.DoesNotContain(Descendants(dialog).OfType<UIElement>(), e => e.HasAnimatedProperties);
                dialog.Close();

                LibraryMotion.Instance.SetLevel(EffectLevel.Full);
                var (full, fullPicker, _) = Open(ArtworkKind.Hero);
                Arrange(full, double.PositiveInfinity);
                Search(fullPicker, ArtworkKind.Hero);
                Arrange(full, double.PositiveInfinity);
                WpfTestHost.DoEvents();
                if (LibraryMotion.Enabled)
                {
                    Assert.Contains(Descendants(full).OfType<ListBoxItem>(), e => e.HasAnimatedProperties); // einmal eingeblendet
                }

                var until = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < until && Descendants(full).OfType<UIElement>().Any(e => e.HasAnimatedProperties))
                {
                    WpfTestHost.DoEvents();
                    Thread.Sleep(20);
                }

                Assert.DoesNotContain(Descendants(full).OfType<UIElement>(), e => e.HasAnimatedProperties); // danach läuft nichts mehr
                full.Close();
            }
            finally
            {
                LibraryMotion.Instance.SetLevel(previous);
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_new_element_uses_theme_resources_in_light_and_dark(bool dark)
    {
        var root = ThemeTokenFileTests.RepositoryRoot();
        var dialogXaml = File.ReadAllText(Path.Combine(root, "src", "HdrPilot.App", "Views", "Library", "ArtworkPickerDialog.xaml"));
        Assert.Empty(LiteralColor().Matches(StripComments(dialogXaml)));

        var library = File.ReadAllText(Path.Combine(root, "src", "HdrPilot.App", "Themes", "Library.xaml"));
        var start = library.IndexOf("„Bild wählen…“ mit SteamGridDB (E93)", StringComparison.Ordinal);
        Assert.True(start > 0);
        var block = StripComments(library[start..]);
        var plate = Regex.Match(block, "<DrawingBrush x:Key=\"Lib.LogoPlate\".*?</DrawingBrush>", RegexOptions.Singleline);
        Assert.True(plate.Success);
        Assert.Equal(2, LiteralColor().Matches(plate.Value).Count); // die beiden Grautöne des Schachbretts
        Assert.Equal(2, LiteralColor().Matches(block).Count); // sonst keine feste Farbe

        WpfTestHost.Run(() =>
        {
            var (dialog, picker, _) = Open(ArtworkKind.Cover);
            UseTheme(dialog, dark);
            Search(picker, ArtworkKind.Cover);
            Arrange(dialog, double.PositiveInfinity);
            var plateBrush = (SolidColorBrush)dialog.FindResource(ThemeKeys.BrushPlate);
            Assert.Equal(dark ? Color.FromRgb(0x0B, 0x0C, 0x11) : Color.FromRgb(0xE4, 0xE7, 0xEE), plateBrush.Color);
            var tile = (ListBoxItem)Find<ListBox>(dialog, "Results").ItemContainerGenerator.ContainerFromIndex(0);
            var presenter = Descendants(tile).OfType<ContentPresenter>().First();
            var platePart = (Border)presenter.ContentTemplate.FindName("Plate", presenter);
            Assert.Equal(plateBrush.Color, ((SolidColorBrush)platePart.Background).Color);
            dialog.Close();
        });
    }

    [Fact]
    public void Apply_busy_state_disables_everything_but_cancel()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, sgdb) = Open(ArtworkKind.Cover);
            Search(picker, ArtworkKind.Cover);
            picker.Selected = picker.Online.Results[1];
            sgdb.Session!.HoldDownloads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var prepare = picker.PrepareApplyAsync();
            Arrange(dialog, double.PositiveInfinity);

            foreach (var name in new[] { "Tabs", "LocalTiles", "PickFileButton", "MatchButton", "Results", "MoreButton", "ResetButton", "ApplyButton" })
            {
                Assert.False(Find<UIElement>(dialog, name).IsEnabled, name + " bleibt bedienbar");
            }

            Assert.True(Find<UIElement>(dialog, "CancelButton").IsEnabled);
            Assert.Equal(Visibility.Visible, Find<UIElement>(dialog, "BusyLine").Visibility);
            Assert.Equal(LibraryStrings.Picker_SgdbDownloading, Find<TextBlock>(dialog, "BusyLine").Text);

            sgdb.Session.HoldDownloads.SetResult();
            Pump(() => prepare.IsCompleted);
            Arrange(dialog, double.PositiveInfinity);
            Assert.True(Find<UIElement>(dialog, "ApplyButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Find<UIElement>(dialog, "BusyLine").Visibility);
            dialog.Close();
        });
    }

    [Fact]
    public void Apply_after_the_dialog_started_closing_never_sets_the_dialog_result()
    {
        WpfTestHost.Run(() =>
        {
            var (dialog, picker, sgdb) = Open(ArtworkKind.Hero);
            Search(picker, ArtworkKind.Hero);
            picker.Selected = picker.Online.Results[0];
            sgdb.Session!.HoldDownloads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var apply = dialog.ApplyCoreAsync();
            dialog.BeginClosing(); // Abbrechen, Esc oder X während des Ladens
            picker.CloseSession();
            sgdb.Session.HoldDownloads.SetResult();
            Pump(() => apply.IsCompleted);

            Assert.False(apply.Result); // kein Dialogergebnis (das Setzen würfe bei einem schließenden Fenster)
            Assert.Null(apply.Exception);
            Assert.False(dialog.ApplyCoreAsync().Result); // auch ein zweites Übernehmen tut nichts mehr
            dialog.Close();
        });
    }

    [GeneratedRegex("#[0-9A-Fa-f]{6,8}\\b")]
    private static partial Regex LiteralColor();

    private static string StripComments(string xaml) => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static (ArtworkPickerDialog Dialog, ArtworkPickerViewModel Picker, FakeSteamGridDbSearch Sgdb) Open(ArtworkKind kind)
    {
        EnsureLibraryResources();
        var sgdb = new FakeSteamGridDbSearch();
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "9990150", "Kupferwald", isNew: false),
            Game(GameSource.Gog, "1207658930", "Nebelfahrt", isNew: false))
        {
            Artwork = new FakeArtworkService(),
        };
        context.SteamGridDb = sgdb;
        var vm = new LibraryViewModel(context, new FakeDialogs());
        var picker = new ArtworkPickerViewModel(vm, vm.Games.Single(g => g.Key == SteamKey), kind);
        var dialog = new ArtworkPickerDialog(picker);
        return (dialog, picker, sgdb);
    }

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }

    private static void UseTheme(Window window, bool dark) =>
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Tokens." + (dark ? "Dark" : "Light") + ".xaml", UriKind.Absolute) });

    private static void Load(ArtworkPickerViewModel picker)
    {
        var load = picker.LoadAsync();
        Pump(() => load.IsCompleted);
    }

    private static void Search(ArtworkPickerViewModel picker, ArtworkKind kind, int more = 0)
    {
        var search = picker.Online.SearchAsync(kind);
        Pump(() => search.IsCompleted && picker.Online.State == SteamGridDbSearchState.Results);
        for (var i = 0; i < more; i++)
        {
            var count = picker.Online.Results.Count;
            var task = picker.Online.MoreAsync(kind);
            Pump(() => task.IsCompleted && picker.Online.Results.Count > count);
        }
    }

    /// <summary>Arbeitet die Warteschlange ab, bis die Bedingung gilt (Aufgaben der Attrappe enden auf dem Host-Thread).</summary>
    private static void Pump(Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++)
        {
            WpfTestHost.DoEvents();
            Thread.Sleep(5);
        }

        Assert.True(done());
    }

    private static FrameworkElement Arrange(Window window, double height)
    {
        WpfTestHost.Layout(window, new Size(880, height));
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, new Size(880, height));
        return (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
    }

    private static T Find<T>(Window window, string name)
        where T : class => Assert.IsAssignableFrom<T>(window.FindName(name) ?? throw new InvalidOperationException(name + " fehlt"));

    private static KeyEventArgs Press(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new KeySource(), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = target };
        target.RaiseEvent(args);
        return args;
    }

    private static bool ShellScrollChains(ListBox list) => HdrPilot.App.Views.Shell.ShellScroll.GetChainWheel(list);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) => PreOrder(root).Skip(1);

    /// <summary>Visueller Baum in Dokumentreihenfolge (wie die Tab-Reihenfolge ohne TabIndex).</summary>
    private static IEnumerable<DependencyObject> PreOrder(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var child in PreOrder(VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// Erste deckende Fläche hinter einem Element: ein Vorfahr (Border, Panel, Steuerelement) oder, wie in Vorlagen üblich,
    /// ein früheres Geschwister im selben Raster (die Fläche „Body“ unter dem Inhalt); sonst die Fensterfarbe.
    /// </summary>
    private static Color EffectiveBackground(DependencyObject element, Color fallback)
    {
        static Color? Solid(DependencyObject candidate) => candidate switch
        {
            Border { Background: SolidColorBrush { Color.A: > 200 } b } border when border.Opacity > 0.5 && border.IsVisible => b.Color,
            Panel { Background: SolidColorBrush { Color.A: > 200 } p } panel when panel.Opacity > 0.5 => p.Color,
            Control { Background: SolidColorBrush { Color.A: > 200 } c } control when control.Opacity > 0.5 => c.Color,
            _ => null,
        };

        var child = element;
        for (var current = VisualTreeHelper.GetParent(element); current is not null; child = current, current = VisualTreeHelper.GetParent(current))
        {
            if (current is Panel panel)
            {
                var index = panel.Children.IndexOf((UIElement)child);
                for (var i = index - 1; i >= 0; i--)
                {
                    if (panel.Children[i] is Border sibling && Solid(sibling) is { } under)
                    {
                        return under;
                    }
                }
            }

            if (Solid(current) is { } color)
            {
                return color;
            }
        }

        return fallback;
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

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
