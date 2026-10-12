using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Oberflächen-Tests auf einem STA-Thread. Fenster werden nur erzeugt, nie angezeigt. Mit der Umgebungsvariable
/// HDRPILOT_SHELL_RENDER_DIR werden Tray-Menü und Tray-Symbole zusätzlich als PNG zur Sichtprüfung abgelegt.
/// </summary>
public sealed class ShellViewTests
{
    private static readonly string[] SettingsIds =
    [
        "TargetDisplays", "PollInterval", "GracePeriod", "RestartBufferMinutes", "RestartBufferSeconds", "LaunchTimeout",
        "AutostartToggle", "NotificationsToggle", "HotkeyFallbackToggle", "SearchFolders", "AddSearchFolderButton", "RemoveSearchFolderButton", "LogLevel",
        "HelperPatterns", "ResetHelperPatternsButton", "OpenLogFolderButton", "SaveSettingsButton", "CancelSettingsButton",
    ];

    private static readonly string[] TrayIds = ["TrayVersion", "TrayToggleHdr", "TrayPause", "TrayOpen", "TraySettings", "TrayLogs", "TrayExit"];

    /// <summary>Maßstab der Bilder von <see cref="Render"/> (1,5 wie ein Bildschirm mit 150 %).</summary>
    private const double RenderScale = 1.5;

    [Fact]
    public void Settings_window_exposes_every_automation_id_with_a_name()
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateSettingsWindow(out _);
            WpfTestHost.DoEvents();
            Assert.Equal("SettingsWindow", AutomationProperties.GetAutomationId(window));

            var elements = Logical(window).OfType<FrameworkElement>()
                .Where(e => !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(e)))
                .ToDictionary(AutomationProperties.GetAutomationId);
            foreach (var id in SettingsIds)
            {
                Assert.True(elements.ContainsKey(id), "AutomationId fehlt: " + id);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(elements[id])), "Name fehlt: " + id);
            }

            Assert.IsType<ListBox>(elements["TargetDisplays"]);
            Assert.IsType<ListBox>(elements["SearchFolders"]);
            Assert.IsType<ComboBox>(elements["LogLevel"]);
            Assert.True(((TextBox)elements["HelperPatterns"]).AcceptsReturn);
            Assert.All(new[] { "AutostartToggle", "NotificationsToggle", "HotkeyFallbackToggle" }, id => Assert.IsType<CheckBox>(elements[id]));
        });
    }

    [Fact]
    public void Slider_limits_match_the_view_model()
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateSettingsWindow(out _);
            var sliders = Logical(window).OfType<Slider>().ToDictionary(AutomationProperties.GetAutomationId);

            Assert.Equal((SettingsViewModel.PollIntervalMin, SettingsViewModel.PollIntervalMax), Limits(sliders["PollInterval"]));
            Assert.Equal((SettingsViewModel.GracePeriodMin, SettingsViewModel.GracePeriodMax), Limits(sliders["GracePeriod"]));
            Assert.Equal((SettingsViewModel.LaunchTimeoutMin, SettingsViewModel.LaunchTimeoutMax), Limits(sliders["LaunchTimeout"]));
            Assert.Equal((0, SettingsViewModel.RestartBufferMinutesMax), Limits(sliders["RestartBufferMinutes"]));
            Assert.Equal((0, SettingsViewModel.RestartBufferSecondsMax), Limits(sliders["RestartBufferSeconds"]));
        });
    }

    [Fact]
    public void Settings_window_binds_to_the_view_model_and_releases_it()
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateSettingsWindow(out var ctx);
            var poll = Logical(window).OfType<Slider>().Single(s => AutomationProperties.GetAutomationId(s) == "PollInterval");
            WpfTestHost.DoEvents();

            Assert.Equal(1000, poll.Value);
            poll.Value = 2000;
            Assert.Equal(2000, ((SettingsViewModel)window.DataContext).PollIntervalMs);
            Assert.Equal(1, ctx.Subscribers);

            window.Close();
            Assert.Equal(0, ctx.Subscribers);
            Assert.Null(window.DataContext);
        });
    }

    [Fact]
    public void Mouse_wheel_over_a_list_scrolls_the_page_when_the_list_cannot_scroll()
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateSettingsWindow(out _);
            WpfTestHost.Layout(window, new Size(1080, 560));
            var page = Logical(window).OfType<ScrollViewer>().Single();
            Assert.True(page.ScrollableHeight > 100, "Seite muss scrollen können");

            // Über einer Bildschirm-Zeile (Liste ohne eigenen Bildlauf): die Seite scrollt.
            var displayRow = WpfTestHost.Visuals(window).OfType<CheckBox>().First(c => c.DataContext is DisplayOptionViewModel);
            Wheel(displayRow, -120);
            Assert.True(page.VerticalOffset > 0, "Seite hat nicht gescrollt");
            window.Close();
        });
    }

    [Fact]
    public void Mouse_wheel_in_the_pattern_editor_scrolls_the_editor_first_then_the_page()
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateSettingsWindow(out _);
            WpfTestHost.Layout(window, new Size(1080, 560));
            var page = Logical(window).OfType<ScrollViewer>().Single();
            var editor = Logical(window).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "HelperPatterns");
            var inner = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
            Assert.True(inner.ScrollableHeight > 0, "Standardliste muss im Editor scrollen");

            Wheel(editor, -120);
            Assert.True(inner.VerticalOffset > 0, "Editor hat nicht gescrollt");
            Assert.Equal(0, page.VerticalOffset);

            inner.ScrollToEnd();
            window.UpdateLayout();
            Wheel(editor, -120);
            Assert.True(page.VerticalOffset > 0, "Seite hat am Ende des Editors nicht übernommen");
            window.Close();
        });
    }

    [Fact]
    public void Tray_menu_exposes_all_items_and_renders()
    {
        WpfTestHost.Run(() =>
        {
            var host = new FakeTrayHost();
            var model = new TrayMenuViewModel(host, new TrayState(HdrOn: true, HdrSupported: true, Paused: false, ActiveGames: ["Cyberpunk 2077"]), ex => throw ex);
            var menu = new TrayMenu { DataContext = model };
            WpfTestHost.DoEvents();

            var items = menu.Items.OfType<MenuItem>().ToDictionary(AutomationProperties.GetAutomationId);
            Assert.Equal(TrayIds.Order(), items.Keys.Order());
            Assert.True(items["TrayPause"].IsCheckable);
            Assert.All(items.Values, i => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(i))));
            Assert.Equal(ShellStrings.TrayMenuToggleHdrOff, items["TrayToggleHdr"].Header);

            var bitmap = Render(menu);
            Assert.True(bitmap.PixelWidth > 200 && bitmap.PixelHeight > 200);
            SaveForReview(bitmap, "traymenu-dunkel.png");
        });
    }

    [Fact]
    public void Tray_menu_commands_reach_the_host()
    {
        WpfTestHost.Run(() =>
        {
            var host = new FakeTrayHost();
            var model = new TrayMenuViewModel(host, TrayState.Initial, ex => throw ex);
            var changed = new List<string?>();
            model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            model.ToggleHdrCommand.Execute(null);
            model.IsPaused = true;
            model.OpenCommand.Execute(null);
            model.SettingsCommand.Execute(null);
            model.LogsCommand.Execute(null);
            model.ExitCommand.Execute(null);

            Assert.Equal(["toggle", "pause:True", "open", "settings", "logs", "exit"], host.Calls);
            Assert.Contains(nameof(TrayMenuViewModel.IsPaused), changed);
            Assert.Equal(ShellStrings.TrayStatusPaused, model.StatusDetail);

            model.Update(new TrayState(HdrOn: true, HdrSupported: true, Paused: false, ActiveGames: ["A", "B"]));
            Assert.Equal(ShellStrings.TrayStatusHdrOn, model.StatusTitle);
            Assert.Equal(ShellStrings.TrayMenuToggleHdrOff, model.ToggleHdrText);
            Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, ShellStrings.TrayStatusGames, 2), model.StatusDetail);
            Assert.False(model.IsPaused);
        });
    }

    [Fact]
    public void Tray_menu_status_without_hdr_display_says_so()
    {
        var model = new TrayMenuViewModel(new FakeTrayHost(), new TrayState(HdrOn: false, HdrSupported: false, Paused: false, ActiveGames: []), ex => throw ex);

        Assert.Equal(ShellStrings.TrayStatusNoHdr, model.StatusDetail);
        Assert.Equal(ShellStrings.TrayStatusHdrOff, model.StatusTitle);
        Assert.Equal(ShellStrings.TrayMenuToggleHdrOn, model.ToggleHdrText);
    }

    [Fact]
    public void Tray_menu_command_failures_are_reported_not_thrown()
    {
        var errors = new List<Exception>();
        var model = new TrayMenuViewModel(new ThrowingTrayHost(), TrayState.Initial, errors.Add);

        model.OpenCommand.Execute(null);
        model.LogsCommand.Execute(null);
        model.ToggleHdrCommand.Execute(null);
        model.IsPaused = true;

        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void Tray_menu_and_icons_render_in_both_themes_for_review()
    {
        WpfTestHost.Run(() =>
        {
            try
            {
                WpfTestHost.UseTheme(dark: false);
                var model = new TrayMenuViewModel(new FakeTrayHost(), new TrayState(HdrOn: false, HdrSupported: true, Paused: true, ActiveGames: []), ex => throw ex);
                var bitmap = Render(new TrayMenu { DataContext = model });
                Assert.True(bitmap.PixelWidth > 200);
                SaveForReview(bitmap, "traymenu-hell.png");
            }
            finally
            {
                WpfTestHost.UseTheme(dark: true);
            }
        });

        if (ReviewDirectory() is { } dir)
        {
            foreach (var size in new[] { 16, 20, 24, 32 })
            {
                foreach (var (spec, name) in new[] { (new TrayIconSpec(true, false), "an"), (new TrayIconSpec(false, false), "aus"), (new TrayIconSpec(true, true), "an-pause"), (new TrayIconSpec(false, true), "aus-pause") })
                {
                    using var icon = TrayIconRenderer.Render(spec, size);
                    using var big = new System.Drawing.Bitmap(size * 8, size * 8);
                    using (var g = System.Drawing.Graphics.FromImage(big))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        g.DrawImage(icon, 0, 0, size * 8, size * 8);
                    }

                    big.Save(Path.Combine(dir, $"tray-{size}-{name}.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
    }

    [Fact]
    public void AppVersion_badge_sits_in_the_settings_header_and_fits_at_minimum_width()
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateSettingsWindow(out _);
            WpfTestHost.Layout(window, new Size(window.MinWidth, window.MinHeight));
            var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            Rect Bounds(FrameworkElement e) => e.TransformToAncestor(root).TransformBounds(new Rect(e.RenderSize));

            var text = Logical(window).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "SettingsVersion");
            Assert.Equal(AppVersionTests.ExpectedDisplayText(), text.Text);
            Assert.Equal(text.Text, AutomationProperties.GetName(text));

            var header = (FrameworkElement)window.FindName("Header");
            var pill = (FrameworkElement)window.FindName("VersionPill");
            Assert.True(pill.IsDescendantOf(header), "Version gehört in den Kopf");
            var pillBounds = Bounds(pill);
            var headerBounds = Bounds(header);
            Assert.True(pillBounds.Width > 0 && pillBounds.Height > 0, "Version nicht sichtbar");
            Assert.True(pillBounds.Left >= headerBounds.Left && pillBounds.Right <= headerBounds.Right + 0.5, $"Version ragt aus dem Kopf: {pillBounds} in {headerBounds}");
            Assert.True(pillBounds.Bottom <= Bounds((FrameworkElement)window.FindName("AccentLine")).Top, "Version reicht in die Akzentlinie");
            Assert.True(text.ActualWidth >= NaturalWidth(text) - 0.5, "Versionstext abgeschnitten");

            // Rechts neben Titel und Unterzeile, ohne dass diese umbrechen müssen
            foreach (var title in Logical(window).OfType<TextBlock>().Where(t => t.Text == ShellStrings.SettingsHeading || t.Text == ShellStrings.SettingsSubheading))
            {
                Assert.True(Bounds(title).Left + NaturalWidth(title) <= pillBounds.Left, $"„{title.Text}“ stößt bei {window.MinWidth} px an die Version");
            }

            // Fensterrahmen: Minimieren, Maximieren, Schließen liegen in der Titelzeile darüber
            var captions = (FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)window.Template.FindName("PART_Minimize", window));
            var captionBounds = Bounds(captions);
            Assert.True(captionBounds.Width > 0, "Titelzeilen-Schaltflächen nicht gefunden");
            Assert.False(pillBounds.IntersectsWith(captionBounds), $"Version überdeckt die Titelzeilen-Schaltflächen: {pillBounds} / {captionBounds}");
            Assert.True(pillBounds.Top >= captionBounds.Bottom, "Version liegt in der Titelzeile");
            window.Close();
        });
    }

    [Fact]
    public void AppVersion_line_tops_the_tray_menu_and_is_skipped_by_the_keyboard()
    {
        WpfTestHost.Run(() =>
        {
            var model = new TrayMenuViewModel(new FakeTrayHost(), TrayState.Initial, ex => throw ex);
            var menu = new TrayMenu { DataContext = model };
            WpfTestHost.Layout(menu, new Size(double.PositiveInfinity, double.PositiveInfinity));
            WpfTestHost.DoEvents();

            Assert.Equal(AppVersionTests.ExpectedLabelText(), model.VersionText);
            var version = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.Equal("TrayVersion", AutomationProperties.GetAutomationId(version));
            Assert.Equal(AppVersionTests.ExpectedLabelText(), version.Header);
            Assert.Equal(AppVersionTests.ExpectedLabelText(), AutomationProperties.GetName(version));
            Assert.IsType<Separator>(menu.Items[1]);

            // Nicht anklickbar, nicht fokussierbar, ohne Befehl
            Assert.False(version.IsEnabled);
            Assert.False(version.Focusable);
            Assert.False(version.IsCheckable);
            Assert.Null(version.Command);
            Assert.True(version.ActualHeight > 0, "Versionszeile nicht sichtbar");

            // Pfeiltasten: WPF springt nur zu aktiven, fokussierbaren Einträgen (ItemsControl.FindFocusable). Pfeil nach
            // unten vom Start trifft „HDR jetzt …“, Pfeil nach oben von dort „HDRpilot beenden“ (phase6-traymenu).
            // Mit Tastendrücken am geöffneten Menü: TrayMenuKeyboardTests.
            var navigable = menu.Items.OfType<Control>().Where(c => c.IsEnabled && c.Focusable).Select(AutomationProperties.GetAutomationId).ToList();
            Assert.Equal("TrayToggleHdr", navigable[0]);
            Assert.Equal("TrayExit", navigable[^1]);
            Assert.DoesNotContain("TrayVersion", navigable);

            // UI Automation: Menüeintrag, nicht aktiv; Aufrufen wird abgewiesen
            var peer = UIElementAutomationPeer.CreatePeerForElement(version);
            Assert.Equal(AutomationControlType.MenuItem, peer.GetAutomationControlType());
            Assert.Equal("TrayVersion", peer.GetAutomationId());
            Assert.False(peer.IsEnabled());
            var invoke = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            Assert.Throws<ElementNotEnabledException>(invoke.Invoke);
            menu.DataContext = null;
        });
    }

    [Fact]
    public void AppVersion_line_renders_readably_in_the_tray_menu_in_both_themes()
    {
        WpfTestHost.Run(() =>
        {
            try
            {
                foreach (var dark in new[] { true, false })
                {
                    WpfTestHost.UseTheme(dark);
                    var model = new TrayMenuViewModel(new FakeTrayHost(), TrayState.Initial, ex => throw ex);
                    var menu = new TrayMenu { DataContext = model };
                    var bitmap = Render(menu);

                    // Text der Versionszeile sichtbar gezeichnet, nicht nur Platz dafür: Vergleich mit derselben Zeile ohne
                    // Text (Opacity 0). Reine Farbpixel hängen bei der dünnen Schrift zu stark von den Ziffern der Version
                    // ab (gemessen 29 bei „Version 1.3.0“, 59 bei „HDRpilot 1.2.0“); sie zeigen hier nur noch, dass der
                    // Text in der Textfarbe (TextSecondary) steht.
                    var version = menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "TrayVersion");
                    var text = WpfTestHost.Visuals(version).OfType<TextBlock>().Single(t => t.Text == model.VersionText);
                    var area = text.TransformToAncestor(menu).TransformBounds(new Rect(text.RenderSize));
                    var (ink, total) = TextInk(bitmap, area, RenderScale, Token("Hp.Brush.TextSecondary"));
                    var opacity = text.Opacity;
                    text.Opacity = 0;
                    var blank = Render(menu);
                    text.Opacity = opacity;
                    var drawn = ChangedPixels(bitmap, blank, area, RenderScale);
                    Assert.True(drawn >= 80 && ink >= 10 && ink <= total / 2,
                        $"Versionszeile im Tray-Menü ({(dark ? "dunkel" : "hell")}) nicht lesbar: {drawn} von {total} Pixeln durch den Text gezeichnet, {ink} davon in Textfarbe");
                    menu.DataContext = null;
                }
            }
            finally
            {
                WpfTestHost.UseTheme(dark: true);
            }
        });
    }

    [Fact]
    public void AppVersion_in_settings_header_renders_in_both_themes_for_review()
    {
        WpfTestHost.Run(() =>
        {
            try
            {
                foreach (var dark in new[] { true, false })
                {
                    WpfTestHost.UseTheme(dark);
                    var window = CreateSettingsWindow(out _);
                    WpfTestHost.Layout(window, new Size(window.MinWidth, window.MinHeight));
                    var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                    var accent = (FrameworkElement)window.FindName("AccentLine");
                    var bottom = accent.TransformToAncestor(root).TransformBounds(new Rect(accent.RenderSize)).Bottom + 16;

                    const double scale = RenderScale;
                    var full = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    full.Render(root);
                    var head = new CroppedBitmap(full, new Int32Rect(0, 0, full.PixelWidth, (int)Math.Ceiling(bottom * scale)));

                    // Der Versionstext ist zu lesen: genug Pixel in Textfarbe, aber nicht die ganze Fläche (sonst hätte die
                    // Pille selbst die Textfarbe). Gemessen: 214 von 2875 in beiden Themen.
                    var text = Logical(window).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "SettingsVersion");
                    var (ink, total) = TextInk(full, text.TransformToAncestor(root).TransformBounds(new Rect(text.RenderSize)), scale, Token("Hp.Brush.TextSecondary"));
                    Assert.True(ink >= 100 && ink <= total / 2, $"Versions-Pille ({(dark ? "dunkel" : "hell")}) nicht lesbar: {ink} von {total} Pixeln in Textfarbe");

                    SaveForReview(head, $"einstellungen-kopf-{(dark ? "dunkel" : "hell")}.png");
                    window.Close();
                }
            }
            finally
            {
                WpfTestHost.UseTheme(dark: true);
            }
        });
    }

    private static SettingsWindow CreateSettingsWindow(out FakeShellContext context)
    {
        var config = ShellTestFactory.NewConfig(new InMemoryFileSystem(), c => c.Settings.SearchFolders = [@"D:\Spielordner"]);
        context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
        var window = new SettingsWindow();
        window.Attach(new SettingsViewModel(context, new FakeFolderPicker()));
        return window;
    }

    private static (int, int) Limits(Slider slider) => ((int)slider.Minimum, (int)slider.Maximum);

    /// <summary>Farbe eines Farb-Tokens im gerade geladenen Thema (hell/dunkel).</summary>
    private static Color Token(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    /// <summary>
    /// Pixel in Textfarbe: deckende Pixel im Bereich (DIP, Bild mit <paramref name="scale"/> gerendert), die je Farbkanal
    /// höchstens 24 von <paramref name="color"/> abweichen, dazu alle Pixel im Bereich. Die Kantenglättung mischt nur die
    /// Ränder der Striche, ihr Inneres hat genau die Textfarbe. Ist der Text nicht zu sehen (Opacity 0, ausgeblendet, in
    /// Hintergrundfarbe), bleibt keins übrig; ein Hintergrundverlauf allein trifft die Textfarbe nicht.
    /// </summary>
    private static (int Ink, int Total) TextInk(BitmapSource bitmap, Rect area, double scale, Color color)
    {
        const int tolerance = 24;
        var x = Math.Max(0, (int)Math.Floor(area.X * scale));
        var y = Math.Max(0, (int)Math.Floor(area.Y * scale));
        var width = Math.Min(bitmap.PixelWidth - x, (int)Math.Ceiling(area.Right * scale) - x);
        var height = Math.Min(bitmap.PixelHeight - y, (int)Math.Ceiling(area.Bottom * scale) - y);
        var pixels = new int[width * height];
        bitmap.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);

        // Pbgra32 als int: Alpha in den obersten Bits, dann Rot, Grün, Blau; deckende Pixel sind nicht vormultipliziert
        var ink = pixels.Count(p => (p >>> 24) == 0xFF
            && Math.Abs(((p >> 16) & 0xFF) - color.R) <= tolerance
            && Math.Abs(((p >> 8) & 0xFF) - color.G) <= tolerance
            && Math.Abs((p & 0xFF) - color.B) <= tolerance);
        return (ink, pixels.Length);
    }

    /// <summary>Pixel im Bereich, die sich zwischen zwei gleich großen Bildern deutlich unterscheiden (ein Kanal um mehr als 48).</summary>
    private static int ChangedPixels(BitmapSource a, BitmapSource b, Rect area, double scale)
    {
        var x = Math.Max(0, (int)Math.Floor(area.X * scale));
        var y = Math.Max(0, (int)Math.Floor(area.Y * scale));
        var width = Math.Min(a.PixelWidth - x, (int)Math.Ceiling(area.Right * scale) - x);
        var height = Math.Min(a.PixelHeight - y, (int)Math.Ceiling(area.Bottom * scale) - y);
        var first = new int[width * height];
        var second = new int[width * height];
        a.CopyPixels(new Int32Rect(x, y, width, height), first, width * 4, 0);
        b.CopyPixels(new Int32Rect(x, y, width, height), second, width * 4, 0);
        var changed = 0;
        for (var i = 0; i < first.Length; i++)
        {
            for (var shift = 0; shift < 32; shift += 8)
            {
                if (Math.Abs(((first[i] >> shift) & 0xFF) - ((second[i] >> shift) & 0xFF)) > 48)
                {
                    changed++;
                    break;
                }
            }
        }
        return changed;
    }

    /// <summary>Breite des Textes in einer Zeile, unabhängig vom Platz, den das Layout ihm gerade gibt.</summary>
    private static double NaturalWidth(TextBlock text)
    {
        var probe = new TextBlock
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
            FontStretch = text.FontStretch,
            TextWrapping = TextWrapping.NoWrap,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    /// <summary>Mausrad wie vom Eingabesystem: erst PreviewMouseWheel (tunnelnd), dann MouseWheel mit denselben Daten.</summary>
    private static void Wheel(UIElement target, int delta)
    {
        var args = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        };
        target.RaiseEvent(args);
        args.RoutedEvent = UIElement.MouseWheelEvent;
        target.RaiseEvent(args);
        if (PresentationSource.FromVisual(target) is null)
        {
            // Ohne Fenster-Handle ordnet niemand neu an: Layout selbst nachziehen.
            ((FrameworkElement)VisualTreeRoot(target)).UpdateLayout();
        }
    }

    private static DependencyObject VisualTreeRoot(DependencyObject element)
    {
        while (VisualTreeHelper.GetParent(element) is { } parent)
        {
            element = parent;
        }

        return element;
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

    private static RenderTargetBitmap Render(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * RenderScale), (int)Math.Ceiling(element.ActualHeight * RenderScale), 96 * RenderScale, 96 * RenderScale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private static string? ReviewDirectory()
    {
        var dir = Environment.GetEnvironmentVariable("HDRPILOT_SHELL_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SaveForReview(BitmapSource bitmap, string name)
    {
        if (ReviewDirectory() is not { } dir)
        {
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, name));
        encoder.Save(stream);
    }
}
