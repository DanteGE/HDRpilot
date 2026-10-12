using System.Collections;
using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// „Windows Auto HDR“ im Hauptfenster (E88), auf dem WPF-Testhost erzeugt und angeordnet, nie angezeigt: Abschnitt „BILD“
/// unter „AUTOMATIK“, in der Liste nur das Kennzeichen „AUTO HDR“ hinter dem Namen, kein zweiter Schalter. Mit
/// HDRPILOT_LIBRARY_RENDER_DIR zusätzlich hell und dunkel als PNG zur Sichtprüfung.
/// </summary>
public sealed class AutoHdrLayoutTests
{
    private const string LongName = "Ein Spiel mit einem wirklich sehr, sehr langen Namen, der in keine Zeile passt";

    private static (MainWindow Window, TestLibraryContext Context, LibraryViewModel Model) Open(string selectedKey, double? ratio = null)
    {
        EnsureLibraryResources();
        var plain = Game(GameSource.Gog, "1207658924", "SDR-Spiel", isNew: false);
        plain.AutoHdr = true;
        plain.KnownExecutables = [@"D:\GOG-Spiele\SDR\game.exe", @"D:\GOG-Spiele\SDR\launcher.exe"];
        var native = Game(GameSource.Steam, "3008130", "Dying Light: The Beast", isNew: false);
        var longName = Game(GameSource.Epic, "Long", LongName, hdrAuto: true, isNew: false);
        longName.AutoHdr = true;
        var context = new TestLibraryContext(plain, native, longName, Game(GameSource.Xbox, "Palworld", "Palworld", isNew: true));
        context.HdrSupport["Steam:3008130"] = HdrSupportStatus.Native;
        context.HdrSupport["Epic:Long"] = HdrSupportStatus.Limited;

        if (ratio is not null)
        {
            context.ConfigService.Update(c => c.Settings.LibraryDetailsRatio = ratio);
        }

        var model = new LibraryViewModel(context, new FakeDialogs());
        model.SelectedGame = model.Games.Single(g => g.Key == selectedKey); // vor Attach: ohne Einblend-Animation
        var window = new MainWindow();
        window.Attach(model);
        return (window, context, model);
    }

    private static void Arrange(MainWindow window, Size size)
    {
        WpfTestHost.Layout(window, size);
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, size);
    }

    [Fact]
    public void Picture_section_follows_automation_and_holds_the_switch_with_its_hints()
    {
        WpfTestHost.Run(() =>
        {
            var (window, context, model) = Open("Gog:1207658924");
            Arrange(window, new Size(1460, 1400));

            var order = ((Panel)window.FindName("DetailsContent")).Children.Cast<UIElement>().ToList();
            var automation = order.IndexOf((UIElement)window.FindName("AutomationSection"));
            var picture = order.IndexOf((UIElement)window.FindName("PictureSection"));
            var executables = order.IndexOf((UIElement)window.FindName("ExecutablesSection"));
            Assert.True(automation >= 0 && picture == automation + 1 && executables > picture, $"Reihenfolge {automation}/{picture}/{executables}");

            var pictureIds = Ids((DependencyObject)window.FindName("PictureSection"));
            foreach (var id in new[] { "DetailAutoHdr", "DetailAutoHdrStartHint", "DetailAutoHdrNativeHint", "DetailAutoHdrNeedsHdrHint" })
            {
                Assert.True(pictureIds.ContainsKey(id), "fehlt im Abschnitt BILD: " + id);
            }

            Assert.False(pictureIds.ContainsKey("DetailHdrAuto")); // „HDR automatisch“ bleibt in der Automatik
            var automationIds = Ids((DependencyObject)window.FindName("AutomationSection"));
            Assert.True(automationIds.ContainsKey("DetailHdrAuto"));
            Assert.False(automationIds.ContainsKey("DetailAutoHdr"));

            var toggle = Assert.IsType<CheckBox>(pictureIds["DetailAutoHdr"]);
            Assert.Equal(LibraryStrings.Details_AutoHdr, AutomationProperties.GetName(toggle));
            Assert.True(toggle.IsChecked);
            var hdrAuto = automationIds["DetailHdrAuto"];
            Assert.True(
                toggle.TranslatePoint(default, window).Y > hdrAuto.TranslatePoint(default, window).Y,
                "Windows Auto HDR steht unter HDR automatisch");

            // Sichtbar: „wirkt nur beim Start über HDRpilot“ und „HDR automatisch aus“; kein Hinweis auf eigenes HDR (laut
            // Wiki unbekannt).
            var start = (TextBlock)pictureIds["DetailAutoHdrStartHint"];
            Assert.True(Shown(start));
            Assert.Equal(LibraryStrings.Details_AutoHdrStartHint, start.Text);
            Assert.True(start.TranslatePoint(default, window).Y > toggle.TranslatePoint(default, window).Y, "Hinweis unter dem Schalter");
            Assert.True(Shown(pictureIds["DetailAutoHdrNeedsHdrHint"]));
            Assert.False(Shown(pictureIds["DetailAutoHdrNativeHint"]));
            Assert.True(model.Details!.AutoHdr);

            toggle.IsChecked = false; // wie ein Klick: schreibt die Option, der Hinweis bleibt (auch vor dem Einschalten klar)
            Arrange(window, new Size(1460, 1400));
            Assert.False(context.Config.FindGame("Gog:1207658924")!.AutoHdr);
            Assert.True(Shown(start));
            Assert.True(Shown(pictureIds["DetailAutoHdrNeedsHdrHint"]));
            window.Close();
        });
    }

    [Fact]
    public void Native_hdr_subdues_the_row_but_the_switch_stays_enabled()
    {
        WpfTestHost.Run(() =>
        {
            var (native, nativeContext, _) = Open("Steam:3008130");
            Arrange(native, new Size(1460, 1400));
            var ids = Ids((DependencyObject)native.FindName("PictureSection"));
            var toggle = (CheckBox)ids["DetailAutoHdr"];

            // Titel und Schalter blasser, aber deutlich heller als ein gesperrter Schalter (0,4) …
            Assert.InRange(EffectiveOpacity((FrameworkElement)native.FindName("AutoHdrTitle")), 0.6, 0.8);
            Assert.InRange(EffectiveOpacity(toggle), 0.6, 0.8);

            // … die Erklärung und der Hinweis bleiben voll lesbar.
            Assert.Equal(1.0, EffectiveOpacity((FrameworkElement)native.FindName("AutoHdrExplanation")));
            Assert.True(toggle.IsEnabled);
            Assert.True(Shown(ids["DetailAutoHdrNativeHint"]));
            Assert.Equal(1.0, EffectiveOpacity(ids["DetailAutoHdrNativeHint"]));
            Assert.Equal(LibraryStrings.Details_AutoHdrNative, ((TextBlock)ids["DetailAutoHdrNativeHint"]).Text);
            toggle.IsChecked = true;
            Assert.True(nativeContext.Config.FindGame("Steam:3008130")!.AutoHdr);
            native.Close();

            var (plain, _, _) = Open("Gog:1207658924"); // Gegenprobe: ohne eigenes HDR volle Deckkraft
            Arrange(plain, new Size(1460, 1400));
            var plainIds = Ids((DependencyObject)plain.FindName("PictureSection"));
            Assert.Equal(1.0, EffectiveOpacity((FrameworkElement)plain.FindName("AutoHdrTitle")));
            Assert.Equal(1.0, EffectiveOpacity(plainIds["DetailAutoHdr"]));
            plain.Close();
        });
    }

    [Fact]
    public void List_rows_keep_exactly_one_switch_and_show_the_badge_only_for_auto_hdr_games()
    {
        WpfTestHost.Run(() =>
        {
            var (window, context, _) = Open("Gog:1207658924");
            Arrange(window, new Size(1460, 900));

            var rows = WpfTestHost.Visuals((DependencyObject)window.FindName("GameList")).OfType<ListBoxItem>().ToList();
            Assert.Equal(4, rows.Count);
            foreach (var row in rows)
            {
                var visuals = WpfTestHost.Visuals(row).OfType<FrameworkElement>().ToList();
                var key = ((GameItemViewModel)row.DataContext).Key;
                var toggle = Assert.Single(visuals.OfType<CheckBox>());
                Assert.Equal("HdrAutoToggle", AutomationProperties.GetAutomationId(toggle));
                Assert.DoesNotContain(visuals, v => AutomationProperties.GetAutomationId(v) == "DetailAutoHdr");

                var badge = Assert.Single(visuals, v => AutomationProperties.GetAutomationId(v) == "AutoHdrBadge");
                Assert.Equal(LibraryStrings.Badge_AutoHdr, ((TextBlock)badge).Text);
                Assert.Equal(LibraryStrings.Badge_AutoHdrTooltip, AutomationProperties.GetName(badge));
                var frame = (Border)visuals.Single(v => v is Border b && b.Style == window.FindResource("Lib.AutoHdrBadge"));
                Assert.Equal(LibraryStrings.Badge_AutoHdrTooltip, frame.ToolTip);
                var expected = key is "Gog:1207658924" or "Epic:Long";
                Assert.True(expected == Shown(badge), $"{key}: Kennzeichen sichtbar = {Shown(badge)}");
            }

            // Von außen eingeschaltet (etwa per Kommandozeile): das Kennzeichen erscheint, ohne dass die Zeile neu entsteht.
            context.ConfigService.Update(c => c.FindGame("Xbox:Palworld")!.AutoHdr = true);
            Arrange(window, new Size(1460, 900));
            var palworld = rows.Single(r => ((GameItemViewModel)r.DataContext).Key == "Xbox:Palworld");
            Assert.True(Shown(WpfTestHost.Visuals(palworld).OfType<FrameworkElement>().Single(v => AutomationProperties.GetAutomationId(v) == "AutoHdrBadge")));

            // Kopf der Liste: keine neue Spalte und kein neuer Schalter außerhalb der Zeilen.
            var listIds = Ids((DependencyObject)window.FindName("ListArea"));
            Assert.DoesNotContain(listIds.Keys, id => id.Contains("AutoHdr", StringComparison.Ordinal));
            window.Close();
        });
    }

    [Fact]
    public void Badge_icon_is_the_auto_hdr_icon_and_used_nowhere_else_in_the_window()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open("Gog:1207658924");
            Arrange(window, new Size(1460, 1400));
            var badgeStyle = window.FindResource("Lib.AutoHdrBadge");
            // Symbole sind einzelne Zeichen aus dem privaten Bereich (Segoe Fluent Icons).
            var glyphs = WpfTestHost.Visuals(window).OfType<TextBlock>().Where(t => t.Text.Length == 1 && t.Text[0] is >= '' and <= '').ToList();

            var badgeGlyphs = glyphs.Where(t => InBadge(t, badgeStyle)).Select(t => t.Text).Distinct().ToList();
            Assert.Equal([AppGlyphs.AutoHdr], badgeGlyphs); // dasselbe Symbol wie der Tray-Eintrag „Auto HDR“
            Assert.DoesNotContain(glyphs, t => t.Text == AppGlyphs.AutoHdr && !InBadge(t, badgeStyle));

            // Gegenprobe der Suche: die Sonne von „HDR automatisch“ (Filter) ist dabei, aber nicht im Kennzeichen.
            Assert.Contains(glyphs, t => t.Text == "");
            Assert.DoesNotContain("", badgeGlyphs);
            window.Close();
        });

        static bool InBadge(DependencyObject element, object badgeStyle)
        {
            for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is Border border && ReferenceEquals(border.Style, badgeStyle))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Fact]
    public void Badge_sits_right_after_a_short_name_and_a_long_name_is_trimmed_instead()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open("Gog:1207658924");
            Arrange(window, new Size(1000, 800));
            var rows = WpfTestHost.Visuals((DependencyObject)window.FindName("GameList")).OfType<ListBoxItem>()
                .ToDictionary(r => ((GameItemViewModel)r.DataContext).Key);

            foreach (var (key, trimmed) in new[] { ("Gog:1207658924", false), ("Epic:Long", true) })
            {
                var panel = WpfTestHost.Visuals(rows[key]).OfType<NameWithBadgesPanel>().Single();
                var name = (TextBlock)panel.Children[0];
                var badge = (FrameworkElement)panel.Children[1];
                var nameRight = name.TranslatePoint(new Point(name.ActualWidth, 0), panel).X;
                var badgeLeft = badge.TranslatePoint(default, panel).X - badge.Margin.Left;
                Assert.InRange(badgeLeft - nameRight, -0.5, 0.5); // direkt hinter dem Namen, nicht am rechten Rand
                Assert.True(badge.TranslatePoint(new Point(badge.ActualWidth, 0), panel).X <= panel.ActualWidth + 0.5, $"{key}: Kennzeichen ragt hinaus");
                Assert.True(badge.ActualWidth > 30, $"{key}: Kennzeichen ganz sichtbar ({badge.ActualWidth:0.0})");
                var textWidth = new FormattedText(name.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(name.FontFamily, name.FontStyle, name.FontWeight, name.FontStretch), name.FontSize, Brushes.Black, 1.0).Width;
                Assert.True(trimmed == (name.ActualWidth + 1 < textWidth), $"{key}: Name gekürzt = {name.ActualWidth + 1 < textWidth}");
            }

            window.Close();
        });
    }

    [Fact]
    public void Picture_section_wraps_at_the_narrowest_split_with_every_hint_shown()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, model) = Open("Steam:3008130", LibraryLayout.MinDetailsRatio);
            model.Details!.AutoHdr = true;
            Arrange(window, new Size(1000, 1600));
            window.FitDetailsColumn();
            Arrange(window, new Size(1000, 1600));

            var section = (FrameworkElement)window.FindName("PictureSection");
            var ids = Ids(section);
            Assert.True(Shown(ids["DetailAutoHdrNativeHint"]) && Shown(ids["DetailAutoHdrNeedsHdrHint"]) && Shown(ids["DetailAutoHdrStartHint"]));
            foreach (var element in WpfTestHost.Visuals(section).OfType<FrameworkElement>().Where(e => Shown(e)))
            {
                var bounds = element.TransformToAncestor(section).TransformBounds(new Rect(element.RenderSize));
                Assert.True(bounds.Right <= section.ActualWidth + 0.5, $"{element.GetType().Name} {AutomationProperties.GetAutomationId(element)} ragt hinaus");
            }

            window.Close();
        });
    }

    [Fact]
    public void Every_text_in_the_picture_section_and_the_badge_comes_from_the_resources()
    {
        WpfTestHost.Run(() =>
        {
            var (window, _, _) = Open("Steam:3008130");
            Arrange(window, new Size(1460, 1400));
            var texts = ResourceTexts();

            var section = (DependencyObject)window.FindName("PictureSection");
            var shown = WpfTestHost.Visuals(section).OfType<TextBlock>().Select(t => t.Text).Where(IsWords).ToList();
            Assert.True(shown.Count >= 4, "Texte im Abschnitt: " + shown.Count);
            Assert.All(shown, t => Assert.Contains(t, texts));

            var badgeTexts = WpfTestHost.Visuals((DependencyObject)window.FindName("GameList")).OfType<Border>()
                .Where(b => b.Style == window.FindResource("Lib.AutoHdrBadge"))
                .SelectMany(b => WpfTestHost.Visuals(b).OfType<TextBlock>()).Select(t => t.Text).Where(IsWords).Distinct().ToList();
            Assert.Equal([LibraryStrings.Badge_AutoHdr], badgeTexts);
            window.Close();
        });

        // Gegenprobe der Prüfung selbst: ein fester Text fiele auf.
        Assert.DoesNotContain("Auto HDR an", ResourceTexts());
    }

    [Fact]
    public void Picture_section_and_badge_render_in_both_themes_for_review()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var (dark, suffix) in new[] { (true, "dunkel"), (false, "hell") })
            {
                try
                {
                    WpfTestHost.UseTheme(dark);
                    var (window, _, _) = Open(dark ? "Gog:1207658924" : "Steam:3008130");
                    Arrange(window, new Size(1460, 1300));
                    var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);

                    // Eingeschaltete Schalter gleiten erst in ihre Endlage: vorher abgebildet, sähen sie aus wie aus.
                    WaitForSwitches(root);
                    Assert.Empty(UnsettledSwitches(root));
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.RenderSize.Width), (int)Math.Ceiling(root.RenderSize.Height), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    Assert.True(bitmap.PixelWidth >= 1460 && bitmap.PixelHeight >= 620, $"Bild {bitmap.PixelWidth}x{bitmap.PixelHeight}");

                    // Das Kennzeichen ist zu sehen: seine Fläche unterscheidet sich vom Zeilenhintergrund daneben.
                    var badge = WpfTestHost.Visuals((DependencyObject)window.FindName("GameList")).OfType<Border>()
                        .First(b => b.Style == window.FindResource("Lib.AutoHdrBadge") && Shown(b));
                    var inside = badge.TranslatePoint(new Point(2, badge.ActualHeight / 2), root);
                    var outside = badge.TranslatePoint(new Point(-4, badge.ActualHeight / 2), root);
                    Assert.NotEqual(Pixel(bitmap, inside), Pixel(bitmap, outside));
                    SaveForReview(bitmap, $"bibliothek-autohdr-{suffix}.png");
                    window.Close();
                }
                finally
                {
                    WpfTestHost.UseTheme(dark: true);
                }
            }
        });
    }

    /// <summary>
    /// Angeordnet und sichtbar: selbst und alle Vorfahren sichtbar, mit Fläche. IsVisible taugt hier nicht, weil das
    /// Fenster nie angezeigt wird.
    /// </summary>
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

    /// <summary>
    /// Eingeschaltete große Schalter (Lib.ToggleSwitch), deren Leuchtspur noch nicht ganz sichtbar ist: Sie gleiten mit
    /// Animationen 160 bis 200 ms; ohne Animationen stehen sie sofort richtig.
    /// </summary>
    private static List<CheckBox> UnsettledSwitches(FrameworkElement root) =>
        WpfTestHost.Visuals(root).OfType<CheckBox>()
            .Where(c => c.IsChecked == true && c.Template?.FindName("TrackOn", c) is UIElement { Opacity: < 0.999 })
            .ToList();

    /// <summary>Pumpt die Nachrichten, bis alle Schalter angekommen sind (höchstens 3 s).</summary>
    private static void WaitForSwitches(FrameworkElement root)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (UnsettledSwitches(root).Count > 0 && DateTime.UtcNow < deadline)
        {
            WpfTestHost.DoEvents();
            Thread.Sleep(15);
        }
    }

    /// <summary>Deckkraft, wie sie auf dem Bildschirm ankommt: das Produkt mit allen Vorfahren bis zum Fenster.</summary>
    private static double EffectiveOpacity(FrameworkElement element)
    {
        var opacity = 1.0;
        for (DependencyObject? current = element; current is not null and not Window; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement ui)
            {
                opacity *= ui.Opacity;
            }
        }

        return opacity;
    }

    private static uint Pixel(BitmapSource bitmap, Point point)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixels, 4, 0);
        return BitConverter.ToUInt32(pixels, 0);
    }

    private static HashSet<string> ResourceTexts()
    {
        var set = LibraryStrings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
        return set.Cast<DictionaryEntry>().Select(e => e.Value as string).OfType<string>().ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Text mit Buchstaben (keine Symbolzeichen aus Segoe Fluent Icons).</summary>
    private static bool IsWords(string text) => text.Any(char.IsLetter);

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

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
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
}
