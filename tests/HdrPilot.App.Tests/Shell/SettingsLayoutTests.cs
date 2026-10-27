using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Raster der Einstellungen (Anmerkung 4 des Nutzers, G10.8): Höchstbreite je Bereich, mehrere Spalten bei breiten Fenstern,
/// im Vollbild 3440×1440 nichts in die Länge gezogen, bei 1280×800 nichts abgeschnitten. Reine Aufteilung und das echte
/// Fenster im Layout (die UI-Automation am Bildschirm ist auf dessen Größe begrenzt, hier nicht).
/// </summary>
public sealed class SettingsLayoutTests
{
    [Theory]
    [InlineData(600, 1)]
    [InlineData(948, 1)]
    [InlineData(1100, 2)]
    [InlineData(1588, 2)]
    [InlineData(2200, 3)]
    [InlineData(3108, 3)]
    public void Settings_grid_columns_follow_the_width(double width, int columns)
    {
        var layout = SettingsGridLayout.Compute(width);

        Assert.Equal(columns, layout.Count);
        Assert.True(layout.Width <= SettingsGridLayout.MaxColumnWidth, $"Spalte {layout.Width} breiter als erlaubt");
        Assert.True(layout.UsedWidth <= width + 0.5, $"Raster {layout.UsedWidth} breiter als {width}");
        Assert.True(Math.Abs((layout.Left * 2) + layout.UsedWidth - width) <= 1.5, "Raster nicht mittig");
        if (columns > 1)
        {
            Assert.True(layout.Width >= SettingsGridLayout.MinColumnWidth, $"Spalte {layout.Width} schmaler als die Mindestbreite");
        }
    }

    [Fact]
    public void Settings_grid_never_stretches_at_any_width()
    {
        var random = new Random(1234);
        for (var i = 0; i < 2000; i++)
        {
            var width = 300 + (random.NextDouble() * 5000);
            var layout = SettingsGridLayout.Compute(width);
            Assert.InRange(layout.Count, 1, SettingsGridLayout.MaxColumns);
            Assert.True(layout.Width <= SettingsGridLayout.MaxColumnWidth);
            Assert.True(layout.UsedWidth <= width + 0.5);
            Assert.True(layout.Count == 1 || layout.Width >= SettingsGridLayout.MinColumnWidth);
        }

        // Gegenprobe: ohne Höchstbreite füllte eine Spalte bei 3108 px die ganze Breite.
        Assert.True(SettingsGridLayout.Compute(3108, 520, double.MaxValue, 24, 3).Width > 1000);
    }

    [Theory]
    [InlineData(new[] { 8, 4, 2 }, 720, 76, 8)]
    [InlineData(new[] { 8, 4, 2 }, 480, 76, 4)]
    [InlineData(new[] { 8, 4, 2 }, 200, 76, 2)]
    [InlineData(new[] { 3, 1 }, 480, 150, 3)]
    [InlineData(new[] { 3, 1 }, 400, 150, 1)]
    public void Adaptive_grid_picks_the_largest_allowed_column_count(int[] allowed, double width, double min, int expected) =>
        Assert.Equal(expected, AdaptiveGrid.ChooseColumns(width, allowed, min, 8, 8));

    [Theory]
    [InlineData(1280, 800, 1)]
    [InlineData(1920, 1080, 2)]
    [InlineData(3440, 1440, 3)]
    public void Settings_window_lays_out_sections_within_max_width_and_side_by_side(int width, int height, int columns)
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateWindow(out _);
            WpfTestHost.Layout(window, new Size(width, height));
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(width, height));
            var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            var page = (ScrollViewer)window.FindName("Page");
            var pageBounds = page.TransformToAncestor(root).TransformBounds(new Rect(page.RenderSize));

            foreach (var gridName in new[] { "GeneralGrid", "AppearanceGrid", "AdvancedGrid" })
            {
                var grid = (SettingsGridPanel)window.FindName(gridName);
                Assert.Equal(columns, grid.Columns.Count);
                var sections = grid.Children.OfType<FrameworkElement>().ToList();
                foreach (var section in sections)
                {
                    var bounds = section.TransformToAncestor(root).TransformBounds(new Rect(section.RenderSize));
                    Assert.True(bounds.Width <= SettingsGridLayout.MaxColumnWidth + 2, $"{section.Name}: {bounds.Width} px breit bei {width}×{height}");
                    Assert.True(bounds.Left >= pageBounds.Left - 0.5 && bounds.Right <= pageBounds.Right + 0.5, $"{section.Name} ragt aus der Seite: {bounds} in {pageBounds}");
                }

                var tops = sections.Select(s => Math.Round(s.TransformToAncestor(grid).Transform(default).Y)).ToList();
                var sideBySide = tops.GroupBy(t => t).Max(g => g.Count());
                Assert.Equal(Math.Min(columns, sections.Count), sideBySide);
            }

            window.Close();
        });
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1920, 1080)]
    public void Settings_window_clips_no_labels_on_the_appearance_page(int width, int height)
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateWindow(out _);
            WpfTestHost.Layout(window, new Size(width, height));
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(width, height));

            // Vorlagen: jeder Name ganz sichtbar; Farbfelder: Rolle ganz sichtbar; Kartentitel ohne Auslassung.
            var page = (FrameworkElement)window.FindName("AppearanceGrid");
            var texts = WpfTestHost.Visuals(page).OfType<TextBlock>().Where(t => t.Visibility == Visibility.Visible && t.TextTrimming != TextTrimming.None && t.TextWrapping == TextWrapping.NoWrap).ToList();
            Assert.NotEmpty(texts);
            var presets = WpfTestHost.Visuals(page).OfType<RadioButton>().Where(r => r.DataContext is PalettePresetItem).ToList();
            Assert.Equal(8, presets.Count);
            foreach (var name in presets.SelectMany(r => WpfTestHost.Visuals(r).OfType<TextBlock>()).Where(t => t.Text.Length > 0 && t.Text.Length > 3))
            {
                Assert.True(name.ActualWidth >= NaturalWidth(name) - 0.5, $"Vorlage „{name.Text}“ abgeschnitten bei {width}");
            }

            foreach (var header in WpfTestHost.Visuals(page).OfType<HeaderedContentControl>())
            {
                var title = WpfTestHost.Visuals(header).OfType<TextBlock>().First(t => t.Text == (string)header.Header);
                Assert.True(title.ActualWidth >= NaturalWidth(title) - 0.5, $"Titel „{title.Text}“ abgeschnitten bei {width}");
            }

            window.Close();
        });
    }

    [Fact]
    public void Settings_navigation_jumps_to_the_appearance_section_and_marks_it()
    {
        WpfTestHost.Run(() =>
        {
            var window = CreateWindow(out _);
            WpfTestHost.Layout(window, new Size(1920, 1080));
            var page = (ScrollViewer)window.FindName("Page");
            Assert.Equal(0, page.VerticalOffset);
            Assert.Equal("NavGeneral", window.CurrentSection);

            Assert.True(window.ScrollToSection("NavAppearanceTray"));
            WpfTestHost.Layout(window, new Size(1920, 1080));
            Assert.True(page.VerticalOffset > 500, $"nicht gesprungen: {page.VerticalOffset}");
            Assert.Equal("NavAppearanceTray", window.CurrentSection);
            Assert.False(window.ScrollToSection("GibtEsNicht"));

            // Jede neue Kennung ist ohne Navigation im Baum (UI-Automation findet sie).
            var ids = WpfTestHost.Visuals(window).OfType<FrameworkElement>().Select(AutomationProperties.GetAutomationId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();
            foreach (var id in NewIds)
            {
                Assert.Contains(id, ids);
            }

            window.Close();
        });
    }

    internal static readonly string[] NewIds =
    [
        "SettingsNav", "NavGeneral", "NavDisplays", "NavAutomation", "NavSearchFolders", "NavHelpers", "NavAppearance", "NavAppearanceColors",
        "NavAppearanceArtwork", "NavAppearanceTray", "NavAdvanced", "NavLogs", "SectionPalette", "SectionPreview", "SectionTheme", "SectionEffects",
        "SectionArtwork", "SectionTray", "PaletteModeDuotone", "PaletteModeTritone", "PaletteSwapButton", "PalettePresets", "AutoTextOnAccentToggle",
        "BackgroundFollowsGameToggle", "ColorsFollowGameToggle", "ThemeSystem", "ThemeLight", "ThemeDark", "EffectFull", "EffectReduced", "EffectOff", "PauseWhilePlayingToggle",
        "PauseInBackgroundToggle", "RespectWindowsAnimationsToggle", "MinimizeToTrayAfterLaunchToggle", "ArtworkCoverageTable", "FallbackExamples",
        "SteamGridDbToggle", "SteamGridDbKey", "SteamGridDbCheckButton", "SteamGridDbStatus", "TrayPreview", "TrayMenuPreview", "TaskbarPreviewLight",
        "TaskbarPreviewDark", "PreviewHdrToggle", "AppearanceResetButton", "AppearanceAppliedHint",
    ];

    internal static SettingsWindow CreateWindow(out FakeShellContext context, Action<AppConfig>? setup = null)
    {
        var config = ShellTestFactory.NewConfig(new InMemoryFileSystem(), setup);
        context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
        var window = new SettingsWindow();
        window.Attach(new SettingsViewModel(context, new FakeFolderPicker()));
        return window;
    }

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
}
