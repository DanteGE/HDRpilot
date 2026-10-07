using System.Windows;
using System.Windows.Automation;
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
        "TargetDisplays", "PollInterval", "GracePeriod", "LaunchTimeout", "AutostartToggle", "NotificationsToggle",
        "HotkeyFallbackToggle", "SearchFolders", "AddSearchFolderButton", "RemoveSearchFolderButton", "LogLevel",
        "HelperPatterns", "ResetHelperPatternsButton", "OpenLogFolderButton", "SaveSettingsButton", "CancelSettingsButton",
    ];

    private static readonly string[] TrayIds = ["TrayToggleHdr", "TrayPause", "TrayOpen", "TraySettings", "TrayLogs", "TrayExit"];

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
    public void Check_pop_animation_follows_the_windows_animation_setting()
    {
        WpfTestHost.Run(() =>
        {
            var box = new CheckBox
            {
                Style = (Style)Application.Current.FindResource("Hp.Shell.CheckCard"),
                DataContext = new DisplayOptionViewModel("p", "Bildschirm", true, true, false, true, false),
            };
            box.Measure(new Size(400, 100));
            box.Arrange(new Rect(0, 0, 400, 100));
            var check = (FrameworkElement)box.Template.FindName("Check", box);

            box.IsChecked = true;
            WpfTestHost.DoEvents();

            Assert.Equal(1.0, check.Opacity);
            Assert.Equal(ShellMotion.IsEnabled, check.RenderTransform.HasAnimatedProperties);
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

    private static SettingsWindow CreateSettingsWindow(out FakeShellContext context)
    {
        var config = ShellTestFactory.NewConfig(new InMemoryFileSystem(), c => c.Settings.SearchFolders = [@"D:\Spielordner"]);
        context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
        var window = new SettingsWindow();
        window.Attach(new SettingsViewModel(context, new FakeFolderPicker()));
        return window;
    }

    private static (int, int) Limits(Slider slider) => ((int)slider.Minimum, (int)slider.Maximum);

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
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
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
