using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Resources;
using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Views.Shell;
using static HdrPilot.App.Tests.Setup.UI.SetupFixtures;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>
/// Fenster von Setup und Entfernen auf dem WPF-Testhost: erzeugt, angeordnet und gerendert, nie angezeigt. Prüft die
/// Kennungen für die UI-Automation (Gate G7.3), die Bindungen und was je Zustand sichtbar ist. Mit der
/// Umgebungsvariable HDRPILOT_SETUP_RENDER_DIR werden die Fenster hell und dunkel als PNG zur Sichtprüfung abgelegt.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class SetupWindowViewTests : IDisposable
{
    private static readonly string[] SetupIds =
    [
        "SetupTitle", "SetupVersionInfo", "SetupInstallDir", "SetupDataNote", "SetupRunningNote", "SetupAutostart",
        "SetupDesktopShortcut", "SetupLaunchAfter", "SetupAllowDowngrade", "SetupInstallButton", "SetupCancelButton",
        "SetupRetryButton", "SetupCloseButton", "SetupCloseErrorButton", "SetupProgress", "SetupResultText",
    ];

    private static readonly string[] UninstallIds = ["UninstallTitle", "UninstallRemoveData", "UninstallSteamWarning", "UninstallConfirmButton", "UninstallCancelButton"];

    private static readonly string[] ProgressIds = ["UninstallProgress", "UninstallResultText", "UninstallCloseButton"];

    private readonly SetupTestFolder _folder = new();

    /// <summary>Ohne Animationen: Schalter und Einblendungen stehen sofort im Endzustand (Bilder, Sichtbarkeit).</summary>
    public SetupWindowViewTests() => WpfTestHost.Run(() => ShellMotion.Override = false);

    public void Dispose()
    {
        WpfTestHost.Run(() => ShellMotion.Override = null);
        _folder.Dispose();
    }

    [Fact]
    public void Setup_window_exposes_every_automation_id_and_binds_the_options()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = CreateSetupWindow(Inspection("1.0.0", autostart: true, running: true));
            var ids = Ids(window);

            Assert.Equal("SetupWindow", AutomationProperties.GetAutomationId(window));
            Assert.Equal(SetupStrings.SetupWindowTitle, window.Title);
            foreach (var id in SetupIds)
            {
                Assert.True(ids.ContainsKey(id), "AutomationId fehlt: " + id);
            }

            Assert.Equal(SetupStrings.SetupTitleUpdate, Text(ids["SetupTitle"]));
            Assert.Equal(model.VersionInfo, Text(ids["SetupVersionInfo"]));
            Assert.Equal(SetupFixtures.InstallDir, Text(ids["SetupInstallDir"]));
            Assert.Equal(model.DataNote, Text(ids["SetupDataNote"]));
            Assert.Equal(SetupStrings.SetupUpdateButton, AutomationProperties.GetName(ids["SetupInstallButton"]));
            Assert.All(new[] { "SetupAutostart", "SetupDesktopShortcut", "SetupLaunchAfter", "SetupAllowDowngrade" }, id => Assert.IsType<CheckBox>(ids[id]));
            Assert.All(new[] { "SetupAutostart", "SetupDesktopShortcut", "SetupLaunchAfter", "SetupInstallButton", "SetupCancelButton" }, id => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(ids[id])), "Name fehlt: " + id));

            Assert.True(Displayed(ids["SetupVersionInfo"]));
            Assert.True(Displayed(ids["SetupRunningNote"]));
            Assert.True(Displayed(ids["SetupInstallButton"]));
            Assert.True(Displayed(ids["SetupCancelButton"]));
            Assert.False(Displayed(ids["SetupAllowDowngrade"]));
            Assert.False(Displayed(ids["SetupProgress"]));
            Assert.False(Displayed(ids["SetupRetryButton"]));
            Assert.False(Displayed(ids["SetupCloseButton"]));
            Assert.False(Displayed(ids["SetupCloseErrorButton"]));

            var autostart = (CheckBox)ids["SetupAutostart"];
            Assert.True(autostart.IsChecked);
            autostart.IsChecked = false;
            Assert.False(model.Autostart);
            Assert.True(((CheckBox)ids["SetupLaunchAfter"]).IsChecked); // lief die App, startet sie wieder
            window.Close();
        });
    }

    [Fact]
    public void Older_version_needs_the_confirmation_box_before_the_button_works()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, _) = CreateSetupWindow(Inspection("1.2.0"));
            var ids = Ids(window);

            Assert.True(Displayed(ids["SetupAllowDowngrade"]));
            Assert.False(ids["SetupInstallButton"].IsEnabled);
            ((CheckBox)ids["SetupAllowDowngrade"]).IsChecked = true;
            Assert.True(model.AllowDowngrade);
            Assert.True(ids["SetupInstallButton"].IsEnabled);
            Assert.Equal(SetupStrings.SetupInstallButton, AutomationProperties.GetName(ids["SetupInstallButton"]));
            window.Close();
        });
    }

    [Fact]
    public void Result_page_shows_the_steps_the_text_and_the_matching_buttons()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, engine) = CreateSetupWindow(Inspection("1.0.0", running: true));
            engine.Results.Enqueue(SetupResult.Failed(SetupErrorKind.AppStillRunning, "hängt"));
            model.InstallAsync().GetAwaiter().GetResult(); // Attrappe endet sofort
            WpfTestHost.Layout(window, new Size(620, double.PositiveInfinity));
            WpfTestHost.DoEvents();
            var ids = Ids(window);

            Assert.Equal(SetupStrings.SetupErrorAppStillRunning, Text(ids["SetupResultText"]));
            Assert.True(Displayed(ids["SetupProgress"]));
            Assert.True(Displayed(ids["SetupResultText"]));
            Assert.True(Displayed(ids["SetupRetryButton"]));
            Assert.True(Displayed(ids["SetupCancelButton"]));
            Assert.False(Displayed(ids["SetupCloseButton"]));
            Assert.False(Displayed(ids["SetupCloseErrorButton"]));
            Assert.False(Displayed(ids["SetupInstallButton"]));
            Assert.False(Displayed(ids["SetupAutostart"]));
            var list = (ListBox)ids["SetupProgress"];
            var rows = list.Items.Cast<ProgressStepItem>().ToList();
            Assert.Contains(rows, r => r.State == StepState.Failed);

            // Bedienungshilfen: Zustand jeder Zeile als Text aus den Ressourcen, nicht als Enum-Name.
            for (var i = 0; i < rows.Count; i++)
            {
                var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(i));
                Assert.Equal(SetupText.State(rows[i].State), AutomationProperties.GetItemStatus(container));
            }

            var failedRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(rows.First(r => r.State == StepState.Failed));
            Assert.Equal(SetupStrings.SetupStepStateFailed, AutomationProperties.GetItemStatus(failedRow));
            SaveForReview(window, "setup-fehler");

            model.RetryCommand.Execute(null);
            WpfTestHost.DoEvents();
            WpfTestHost.Layout(window, new Size(620, double.PositiveInfinity));
            Assert.True(Displayed(ids["SetupCloseButton"]));
            Assert.False(Displayed(ids["SetupRetryButton"]));
            Assert.False(Displayed(ids["SetupCancelButton"]));
            Assert.Equal(model.ResultText, Text(ids["SetupResultText"]));
            SaveForReview(window, "setup-fertig");

            ((Button)ids["SetupCloseButton"]).Command.Execute(null);
            Assert.Null(window.DataContext); // geschlossen und abgemeldet
        });
    }

    [Fact]
    public void Error_without_a_useful_retry_offers_only_close()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, engine) = CreateSetupWindow(Inspection("1.0.0"));
            engine.Results.Enqueue(SetupResult.Failed(SetupErrorKind.AccessDenied, "verweigert"));
            model.InstallAsync().GetAwaiter().GetResult();
            WpfTestHost.Layout(window, new Size(620, double.PositiveInfinity));
            WpfTestHost.DoEvents();
            var ids = Ids(window);

            Assert.Equal(SetupStrings.SetupErrorAccessDenied, Text(ids["SetupResultText"]));
            Assert.True(Displayed(ids["SetupCloseErrorButton"]));
            Assert.Equal(SetupStrings.SetupCloseErrorButton, AutomationProperties.GetName(ids["SetupCloseErrorButton"]));
            Assert.True(ids["SetupCloseErrorButton"].IsEnabled);
            Assert.False(Displayed(ids["SetupCancelButton"]));
            Assert.False(Displayed(ids["SetupRetryButton"]));
            Assert.False(Displayed(ids["SetupCloseButton"]));
            SaveForReview(window, "setup-fehler-schliessen");

            ((Button)ids["SetupCloseErrorButton"]).Command.Execute(null);
            Assert.Null(window.DataContext); // geschlossen
            Assert.Equal(SetupExitCodes.Failed, model.ExitCode);
        });
    }

    [Fact]
    public void Setup_window_cannot_be_closed_while_installing()
    {
        WpfTestHost.Run(() =>
        {
            var (window, model, engine) = CreateSetupWindow(Inspection(installed: null));
            engine.Pending = new TaskCompletionSource<SetupResult>();
            _ = model.InstallAsync();

            window.Close();
            Assert.Same(model, window.DataContext); // Schließen abgelehnt

            engine.Pending.SetResult(FakeSetupEngine.Success(null, new Version(1, 1, 0)));
            WpfTestHost.DoEvents();
            window.Close();
            Assert.Null(window.DataContext);
            Assert.Equal(SetupExitCodes.Ok, model.ExitCode);
        });
    }

    [Fact]
    public void Uninstall_window_asks_about_data_and_warns_about_steam_only_with_entries()
    {
        WpfTestHost.Run(() =>
        {
            var handed = new List<bool>();
            var model = new UninstallViewModel(Uninstall("1.1.0", "Cyberpunk 2077", "Hades II"), Args("--uninstall"), r =>
            {
                handed.Add(r);
                return Task.CompletedTask;
            }, _folder.Log);
            var window = new UninstallWindow();
            window.Attach(model);
            WpfTestHost.Layout(window, new Size(580, double.PositiveInfinity));
            WpfTestHost.DoEvents();
            var ids = Ids(window);

            Assert.Equal("UninstallWindow", AutomationProperties.GetAutomationId(window));
            foreach (var id in UninstallIds)
            {
                Assert.True(ids.ContainsKey(id), "AutomationId fehlt: " + id);
            }

            Assert.Equal(model.Title, Text(ids["UninstallTitle"]));
            var removeData = (CheckBox)ids["UninstallRemoveData"];
            Assert.False(removeData.IsChecked);
            Assert.Equal(SetupStrings.UninstallRemoveData, AutomationProperties.GetName(removeData));
            Assert.True(Displayed(ids["UninstallSteamWarning"]));
            Assert.Equal(SetupStrings.UninstallSteamWarning, Text(ids["UninstallSteamWarning"]));
            Assert.Equal(2, ((ItemsControl)ids["UninstallSteamGames"]).Items.Count);
            Assert.Equal(SetupStrings.UninstallConfirmButton, AutomationProperties.GetName(ids["UninstallConfirmButton"]));
            SaveForReview(window, "entfernen-rueckfrage");

            removeData.IsChecked = true;
            ((Button)ids["UninstallConfirmButton"]).Command.Execute(null);
            WpfTestHost.DoEvents();
            Assert.Equal([true], handed);
            Assert.Null(window.DataContext);

            var plain = new UninstallWindow();
            plain.Attach(new UninstallViewModel(Uninstall("1.1.0"), Args("--uninstall"), _ => Task.CompletedTask, _folder.Log));
            WpfTestHost.Layout(plain, new Size(580, double.PositiveInfinity));
            WpfTestHost.DoEvents();
            Assert.False(Displayed(Ids(plain)["UninstallSteamWarning"]));
            plain.Close();

            // Ohne installierte EXE: Titel ohne Version, „Entfernen“ und die Datenfrage bleiben (die Kopie entscheidet).
            var missing = new UninstallWindow();
            missing.Attach(new UninstallViewModel(Uninstall(installed: null), Args("--uninstall"), _ => Task.CompletedTask, _folder.Log));
            WpfTestHost.Layout(missing, new Size(580, double.PositiveInfinity));
            WpfTestHost.DoEvents();
            var missingIds = Ids(missing);
            Assert.Equal(SetupStrings.UninstallWindowTitle, Text(missingIds["UninstallTitle"]));
            Assert.True(Displayed(missingIds["UninstallConfirmButton"]));
            Assert.True(missingIds["UninstallConfirmButton"].IsEnabled);
            Assert.True(Displayed(missingIds["UninstallRemoveData"]));
            missing.Close();
        });
    }

    [Fact]
    public void Uninstall_progress_window_enables_done_only_after_the_result()
    {
        WpfTestHost.Run(() =>
        {
            var engine = new FakeUninstallEngine(Uninstall(), _folder.Locations)
            {
                Report = [UninstallStep.StopRunningApp, UninstallStep.RestoreHdr],
                Pending = new TaskCompletionSource<SetupResult>(),
            };
            var model = new UninstallProgressViewModel(engine, removeData: false, @"C:\Users\Test\AppData\Roaming\HDRpilot", _folder.Log);
            var window = new UninstallProgressWindow();
            window.Attach(model);
            var run = model.RunAsync();
            WpfTestHost.Layout(window, new Size(580, double.PositiveInfinity));
            WpfTestHost.DoEvents();
            var ids = Ids(window);

            Assert.Equal("UninstallProgressWindow", AutomationProperties.GetAutomationId(window));
            foreach (var id in ProgressIds)
            {
                Assert.True(ids.ContainsKey(id), "AutomationId fehlt: " + id);
            }

            Assert.False(ids["UninstallCloseButton"].IsEnabled);
            Assert.False(Displayed(ids["UninstallResultText"]));
            Assert.Equal(6, ((ListBox)ids["UninstallProgress"]).Items.Count); // ohne „Daten löschen“
            SaveForReview(window, "entfernen-laeuft");

            engine.Pending.SetResult(new SetupResult(true, null, null, new Version(1, 1, 0), null, false));
            WpfTestHost.DoEvents();
            Assert.True(run.IsCompleted);
            WpfTestHost.Layout(window, new Size(580, double.PositiveInfinity));

            Assert.True(ids["UninstallCloseButton"].IsEnabled);
            Assert.True(Displayed(ids["UninstallResultText"]));
            Assert.Equal(model.ResultText, Text(ids["UninstallResultText"]));
            SaveForReview(window, "entfernen-fertig");
            window.Close();
            Assert.Null(window.DataContext);
        });
    }

    [Fact]
    public void Uninstall_progress_window_marks_only_the_failed_step_after_a_mid_run_failure()
    {
        WpfTestHost.Run(() =>
        {
            var readme = Path.Combine(_folder.Locations.InstallDirectory, "README.md");
            var engine = new FakeUninstallEngine(Uninstall(), _folder.Locations)
            {
                Report = [UninstallStep.StopRunningApp, UninstallStep.RestoreHdr, UninstallStep.RemoveFiles, UninstallStep.RemoveShortcuts, UninstallStep.RemoveAutostart, UninstallStep.Unregister],
                Result = SetupResult.Failed(SetupErrorKind.FilesInUse, "gesperrt") with { FailedUninstallStep = UninstallStep.RemoveFiles, FailedFile = readme },
            };
            var model = new UninstallProgressViewModel(engine, removeData: false, @"C:\Users\Test\AppData\Roaming\HDRpilot", _folder.Log);
            var window = new UninstallProgressWindow();
            window.Attach(model);
            model.RunAsync().GetAwaiter().GetResult(); // Attrappe endet sofort
            WpfTestHost.Layout(window, new Size(580, double.PositiveInfinity));
            WpfTestHost.DoEvents();
            var ids = Ids(window);
            var list = (ListBox)ids["UninstallProgress"];

            var statuses = Enumerable.Range(0, list.Items.Count)
                .Select(i => AutomationProperties.GetItemStatus(list.ItemContainerGenerator.ContainerFromIndex(i)))
                .ToList();
            var done = SetupStrings.SetupStepStateDone;
            Assert.Equal([done, done, SetupStrings.SetupStepStateFailed, done, done, done], statuses);
            Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, SetupStrings.UninstallErrorFilesInUse, readme), Text(ids["UninstallResultText"]));
            Assert.True(ids["UninstallCloseButton"].IsEnabled);
            SaveForReview(window, "entfernen-fehler");
            window.Close();
        });
    }

    [Fact]
    public void Setup_windows_render_in_both_themes()
    {
        WpfTestHost.Run(() =>
        {
            try
            {
                foreach (var dark in new[] { true, false })
                {
                    WpfTestHost.UseTheme(dark);
                    var theme = dark ? "dunkel" : "hell";
                    foreach (var (name, inspection) in new[] { ("neu", Inspection(null)), ("update", Inspection("1.0.0", autostart: true, running: true)), ("aelter", Inspection("1.2.0")) })
                    {
                        var (window, _, _) = CreateSetupWindow(inspection);
                        var bitmap = Render(window, 620);
                        Assert.True(bitmap.PixelWidth >= 600 && bitmap.PixelHeight > 300, $"{name}: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                        Save(bitmap, $"setup-{name}-{theme}.png");
                        window.Close();
                    }
                }
            }
            finally
            {
                WpfTestHost.UseTheme(dark: true);
            }
        });
    }

    private (SetupWindow Window, SetupViewModel Model, FakeSetupEngine Engine) CreateSetupWindow(SetupInspection inspection)
    {
        var engine = new FakeSetupEngine(inspection, _folder.Locations);
        var model = new SetupViewModel(engine, inspection, Args(), _folder.Log);
        var window = new SetupWindow();
        window.Attach(model);
        WpfTestHost.Layout(window, new Size(620, double.PositiveInfinity));
        WpfTestHost.DoEvents();
        return (window, model, engine);
    }

    private static Dictionary<string, FrameworkElement> Ids(DependencyObject root) =>
        Logical(root).OfType<FrameworkElement>()
            .Where(e => !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(e)))
            .ToDictionary(AutomationProperties.GetAutomationId);

    private static string Text(FrameworkElement element) => ((TextBlock)element).Text;

    /// <summary>Sichtbar im Fenster: das Element und alle Vorfahren stehen auf Visible und es hat eine Größe.</summary>
    private static bool Displayed(FrameworkElement element)
    {
        // Bis unter das Fenster: ein nie angezeigtes Fenster selbst steht auf Collapsed.
        for (DependencyObject? current = element; current is not null and not Window; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return element.ActualWidth > 0 && element.ActualHeight > 0;
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

    private static RenderTargetBitmap Render(Window window, double width)
    {
        WpfTestHost.Layout(window, new Size(width, double.PositiveInfinity));
        WpfTestHost.DoEvents();
        WpfTestHost.Layout(window, new Size(width, double.PositiveInfinity));
        var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        const double scale = 1.25;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static void SaveForReview(Window window, string name)
    {
        if (ReviewDirectory() is not null)
        {
            Save(Render(window, window.Width), name + ".png");
        }
    }

    private static void Save(BitmapSource bitmap, string name)
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

    private static string? ReviewDirectory()
    {
        var dir = Environment.GetEnvironmentVariable("HDRPILOT_SETUP_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        Directory.CreateDirectory(dir);
        return dir;
    }
}
