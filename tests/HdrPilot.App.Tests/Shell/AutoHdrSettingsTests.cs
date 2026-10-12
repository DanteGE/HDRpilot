using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.Tests.Library;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Einstellungen zu „Windows Auto HDR“ (E88): Notlösung „Auto HDR global während des Spiels“ (Standard aus, mit
/// „Speichern“) und „Auto-HDR-Einträge zurücknehmen“ (wirkt sofort, mit Ergebnis darunter).
/// </summary>
public sealed class AutoHdrSettingsTests
{
    private readonly InMemoryFileSystem _fs = new();

    private (SettingsViewModel Vm, FakeShellContext Context) Create(Action<AppConfig>? setup = null)
    {
        var config = ShellTestFactory.NewConfig(_fs, setup);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
        return (new SettingsViewModel(ctx, new FakeFolderPicker()), ctx);
    }

    private static string Format(string template, params object[] args) => string.Format(CultureInfo.CurrentCulture, template, args);

    [Fact]
    public void Global_fallback_is_off_by_default_and_saved_with_the_other_settings()
    {
        var (vm, _) = Create();
        Assert.False(vm.AutoHdrGlobalFallback);
        Assert.False(vm.HasChanges);

        vm.AutoHdrGlobalFallback = true;
        Assert.True(vm.HasChanges);
        Assert.False(ShellTestFactory.NewConfig(_fs).Current.Settings.AutoHdrGlobalFallback); // erst mit „Speichern“

        Assert.True(vm.Save());
        Assert.Contains("\"autoHdrGlobalFallback\": true", _fs.ReadAllText(ShellTestFactory.ConfigPath), StringComparison.Ordinal);
        var (reopened, _) = Create();
        Assert.True(reopened.AutoHdrGlobalFallback);

        reopened.AutoHdrGlobalFallback = false;
        Assert.True(reopened.Save());
        Assert.False(ShellTestFactory.NewConfig(_fs).Current.Settings.AutoHdrGlobalFallback);
    }

    [Fact]
    public void Cancel_discards_the_fallback_and_switching_back_clears_the_changes_marker()
    {
        var (vm, ctx) = Create();
        vm.AutoHdrGlobalFallback = true;
        vm.AutoHdrGlobalFallback = false;
        Assert.False(vm.HasChanges); // zurückgestellt: nichts mehr zu speichern

        vm.AutoHdrGlobalFallback = true;
        vm.Cancel();
        Assert.False(ctx.Config.Current.Settings.AutoHdrGlobalFallback);
        Assert.Empty(ctx.SavedCalls);
    }

    [Fact]
    public void Rollback_calls_the_service_once_and_reports_what_was_taken_back() => Sta.Run(async () =>
    {
        var (vm, ctx) = Create(c => c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "a", Name = "A", AutoHdr = true }));
        ctx.FakeAutoHdr.NextRollback = new AutoHdrRollbackResult(GamesSwitchedOff: 2, Restored: 3, LeftAsIs: 0, Failed: 0);
        vm.AutoHdrGlobalFallback = true; // ungespeicherte Änderung bleibt ungespeichert
        Assert.False(vm.HasAutoHdrRollbackMessage);

        var command = (AsyncRelayCommand)vm.RollbackAutoHdrCommand;
        command.Execute(null);
        await Sta.Finished(command);

        Assert.Equal(1, ctx.FakeAutoHdr.RollbackCalls);
        Assert.Equal(Format(ShellStrings.AutoHdrRollbackDone, 3, 2), vm.AutoHdrRollbackMessage);
        Assert.True(vm.HasAutoHdrRollbackMessage);
        Assert.False(vm.AutoHdrRollbackIsWarning);
        Assert.True(vm.HasChanges);
        Assert.False(ctx.Config.Current.Settings.AutoHdrGlobalFallback); // die Rücknahme speichert keine Einstellungen
        Assert.Empty(ctx.SavedCalls);
    });

    [Fact]
    public void Rollback_messages_cover_nothing_to_do_left_as_is_failures_and_errors()
    {
        Assert.Equal(ShellStrings.AutoHdrRollbackNothing, SettingsViewModel.AutoHdrRollbackText(new AutoHdrRollbackResult(0, 0, 0, 0)));

        // Nur Spiele ausgeschaltet (noch nichts eingetragen): trotzdem „erledigt“, nicht „nichts vorhanden“.
        Assert.Equal(Format(ShellStrings.AutoHdrRollbackDone, 0, 1), SettingsViewModel.AutoHdrRollbackText(new AutoHdrRollbackResult(1, 0, 0, 0)));

        Assert.Equal(
            Format(ShellStrings.AutoHdrRollbackDone, 1, 1) + " " + Format(ShellStrings.AutoHdrRollbackLeftAsIs, 2),
            SettingsViewModel.AutoHdrRollbackText(new AutoHdrRollbackResult(1, 1, 2, 0)));
        Assert.Equal(
            Format(ShellStrings.AutoHdrRollbackDone, 4, 0) + " " + Format(ShellStrings.AutoHdrRollbackLeftAsIs, 1) + " " + Format(ShellStrings.AutoHdrRollbackFailed, 3),
            SettingsViewModel.AutoHdrRollbackText(new AutoHdrRollbackResult(0, 4, 1, 3)));

        var error = SettingsViewModel.AutoHdrRollbackText(new AutoHdrRollbackResult(0, 0, 0, 0, "Datenträger voll"));
        Assert.Equal(Format(ShellStrings.AutoHdrRollbackError, "Datenträger voll"), error);
        Assert.DoesNotContain(ShellStrings.AutoHdrRollbackNothing, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(2, null, true)]
    [InlineData(0, "Konfiguration gesperrt", true)]
    public void Failures_and_errors_are_shown_as_a_warning(int failed, string? error, bool warning) => Sta.Run(async () =>
    {
        var (vm, ctx) = Create();
        ctx.FakeAutoHdr.NextRollback = new AutoHdrRollbackResult(1, 1, 0, failed, error);

        await vm.RollbackAutoHdrAsync();

        Assert.Equal(warning, vm.AutoHdrRollbackIsWarning);
        Assert.Equal(SettingsViewModel.AutoHdrRollbackText(ctx.FakeAutoHdr.NextRollback), vm.AutoHdrRollbackMessage);
    });

    [Fact]
    public void Button_is_disabled_while_the_rollback_runs() => Sta.Run(async () =>
    {
        var (vm, ctx) = Create();
        var pending = new TaskCompletionSource<AutoHdrRollbackResult>();
        ctx.FakeAutoHdr.PendingRollback = pending;
        var command = vm.RollbackAutoHdrCommand;
        Assert.True(command.CanExecute(null));

        command.Execute(null);
        Assert.True(vm.IsRollingBackAutoHdr);
        Assert.False(command.CanExecute(null));
        command.Execute(null); // zweiter Klick während der Rücknahme: kein zweiter Aufruf
        Assert.Equal(1, ctx.FakeAutoHdr.RollbackCalls);

        pending.SetResult(new AutoHdrRollbackResult(0, 0, 0, 0));
        await Sta.Finished((AsyncRelayCommand)command);
        Assert.False(vm.IsRollingBackAutoHdr);
        Assert.True(command.CanExecute(null));
        Assert.Equal(ShellStrings.AutoHdrRollbackNothing, vm.AutoHdrRollbackMessage);
    });

    [Fact]
    public void A_throwing_service_is_reported_and_a_closed_window_gets_no_message() => Sta.Run(async () =>
    {
        var (vm, ctx) = Create();
        ctx.FakeAutoHdr.RollbackFailure = new UnauthorizedAccessException("Zugriff verweigert");

        await vm.RollbackAutoHdrAsync();
        Assert.Equal(Format(ShellStrings.AutoHdrRollbackError, "Zugriff verweigert"), vm.AutoHdrRollbackMessage);
        Assert.True(vm.AutoHdrRollbackIsWarning);
        Assert.True(vm.RollbackAutoHdrCommand.CanExecute(null));

        // Fenster während der Rücknahme geschlossen: das Ergebnis kommt an, ändert aber nichts mehr.
        var (closed, closedCtx) = Create();
        var pending = new TaskCompletionSource<AutoHdrRollbackResult>();
        closedCtx.FakeAutoHdr.PendingRollback = pending;
        var run = closed.RollbackAutoHdrAsync();
        closed.Dispose();
        pending.SetResult(new AutoHdrRollbackResult(1, 1, 0, 0));
        await run;
        Assert.Null(closed.AutoHdrRollbackMessage);
    });

    [Fact]
    public void Settings_window_shows_the_section_in_the_left_column_with_named_controls()
    {
        WpfTestHost.Run(() =>
        {
            var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
            var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
            ctx.FakeAutoHdr.NextRollback = new AutoHdrRollbackResult(1, 2, 1, 0);
            var window = new SettingsWindow();
            var model = new SettingsViewModel(ctx, new FakeFolderPicker());
            window.Attach(model);
            WpfTestHost.Layout(window, new Size(1080, 2400));
            WpfTestHost.DoEvents();

            var section = (FrameworkElement)window.FindName("AutoHdrSection");
            var startup = (FrameworkElement)window.FindName("StartupSection");
            var left = Assert.IsType<StackPanel>(startup.Parent);
            Assert.Same(left, section.Parent);
            Assert.Equal(left.Children.IndexOf(startup) + 1, left.Children.IndexOf(section));
            Assert.Equal(ShellStrings.SectionAutoHdr, ((HeaderedContentControl)section).Header);

            var ids = Logical(section).OfType<FrameworkElement>()
                .Where(e => !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(e)))
                .ToDictionary(AutomationProperties.GetAutomationId);
            var toggle = Assert.IsType<CheckBox>(ids["AutoHdrGlobalFallbackToggle"]);
            Assert.Equal(ShellStrings.AutoHdrGlobalFallbackLabel, AutomationProperties.GetName(toggle));
            var button = Assert.IsType<Button>(ids["AutoHdrRollbackButton"]);
            Assert.Equal(ShellStrings.AutoHdrRollbackButton, AutomationProperties.GetName(button));
            var message = Assert.IsType<TextBlock>(ids["AutoHdrRollbackMessage"]);
            Assert.Equal(Visibility.Collapsed, message.Visibility);

            Assert.False(toggle.IsChecked);
            toggle.IsChecked = true;
            Assert.True(model.AutoHdrGlobalFallback);
            Assert.True(button.IsEnabled);
            button.Command.Execute(null);
            WpfTestHost.DoEvents();
            Assert.Equal(1, ctx.FakeAutoHdr.RollbackCalls);
            Assert.Equal(Visibility.Visible, message.Visibility);
            Assert.Equal(model.AutoHdrRollbackMessage, message.Text);
            Assert.Equal(string.Empty, AutomationProperties.GetName(message)); // UI-Automation liest den Text selbst

            WpfTestHost.Layout(window, new Size(1080, 2400));
            Assert.True(section.ActualHeight > 150, $"Abschnitt {section.ActualWidth}x{section.ActualHeight}");
            foreach (var element in WpfTestHost.Visuals(section).OfType<FrameworkElement>().Where(e => e.ActualWidth > 0 && e.Visibility == Visibility.Visible))
            {
                var bounds = element.TransformToAncestor(section).TransformBounds(new Rect(element.RenderSize));
                Assert.True(bounds.Right <= section.ActualWidth + 0.5, $"{element.GetType().Name} ragt hinaus: {bounds.Right:0.0} > {section.ActualWidth:0.0}");
            }

            window.Close();
        });
    }

    [Fact]
    public void Settings_with_auto_hdr_render_in_both_themes_for_review()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var (dark, suffix) in new[] { (true, "dunkel"), (false, "hell") })
            {
                try
                {
                    WpfTestHost.UseTheme(dark);
                    var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
                    var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
                    ctx.FakeAutoHdr.NextRollback = dark ? new AutoHdrRollbackResult(2, 3, 1, 0) : new AutoHdrRollbackResult(0, 1, 0, 2);
                    var window = new SettingsWindow();
                    var model = new SettingsViewModel(ctx, new FakeFolderPicker()) { AutoHdrGlobalFallback = dark };
                    window.Attach(model);
                    model.RollbackAutoHdrCommand.Execute(null);
                    WpfTestHost.Layout(window, new Size(1080, 2400));
                    WpfTestHost.DoEvents();
                    WpfTestHost.Layout(window, new Size(1080, 2400));
                    Assert.True(model.HasAutoHdrRollbackMessage);
                    SaveForReview((Visual)VisualTreeHelper.GetChild(window, 0), $"einstellungen-autohdr-{suffix}.png");
                    window.Close();
                }
                finally
                {
                    WpfTestHost.UseTheme(dark: true);
                }
            }
        });
    }

    private static void SaveForReview(Visual root, string name)
    {
        var dir = Environment.GetEnvironmentVariable("HDRPILOT_SHELL_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        var size = ((FrameworkElement)root).RenderSize;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        Directory.CreateDirectory(dir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, name));
        encoder.Save(stream);
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
}
