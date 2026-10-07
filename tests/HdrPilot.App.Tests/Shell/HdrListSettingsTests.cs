using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Core.Config;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>Abschnitt „HDR-Liste (PCGamingWiki)“ der Einstellungen: Schalter, Stand, „Jetzt abgleichen“, Konto.</summary>
public sealed class HdrListSettingsTests
{
    private static readonly DateTimeOffset Stand = new(2026, 10, 7, 12, 3, 0, TimeSpan.Zero);

    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public void Sync_on_start_is_on_by_default_and_saved_with_the_other_settings()
    {
        var (vm, ctx) = Create();
        Assert.True(vm.HdrListSync);
        Assert.True(new AppSettings().HdrListSync);

        vm.HdrListSync = false;
        Assert.True(vm.HasChanges);
        Assert.True(vm.Save());

        Assert.False(ShellTestFactory.NewConfig(_fs).Current.Settings.HdrListSync);
        Assert.Equal([false], ctx.SavedCalls);

        var (again, _) = Create();
        Assert.False(again.HdrListSync);
        again.HdrListSync = true;
        again.Cancel();
        Assert.False(ShellTestFactory.NewConfig(_fs).Current.Settings.HdrListSync); // Abbrechen verwirft
    }

    [Fact]
    public void Status_line_shows_never_stand_and_last_error()
    {
        var (vm, ctx) = Create();
        var list = vm.HdrList;
        Assert.Equal(ShellStrings.HdrListNever, list.StatusText);
        Assert.False(list.HasStatusError);
        Assert.Equal(ShellStrings.HdrListModeAnonymous, list.ModeText);

        ctx.FakeHdrList.Status = new HdrListStatus(Stand, null, 412, 650, false, false, null);
        list.Refresh();
        var date = Stand.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Assert.Equal($"Stand: {date}, 412 von 650 Spielen geprüft", list.StatusText);

        ctx.FakeHdrList.Status = new HdrListStatus(Stand, PcgwErrorKind.RateLimited, 412, 650, false, false, null);
        list.Refresh();
        Assert.Equal($"Stand: {date}, 412 von 650 Spielen geprüft · Letzter Versuch: zu viele Anfragen, später erneut", list.StatusText);
        Assert.True(list.HasStatusError);

        ctx.FakeHdrList.Status = new HdrListStatus(null, PcgwErrorKind.Network, 0, 650, false, false, null);
        list.Refresh();
        Assert.Equal("Noch nicht abgeglichen · Letzter Versuch: PCGamingWiki nicht erreichbar", list.StatusText);

        ctx.FakeHdrList.IsRunning = true;
        list.Refresh();
        Assert.Equal(ShellStrings.HdrListSyncing, list.StatusText);
        Assert.True(list.IsSyncing);
        Assert.False(list.SyncNowCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(PcgwErrorKind.Network, "PCGamingWiki nicht erreichbar")]
    [InlineData(PcgwErrorKind.RateLimited, "zu viele Anfragen, später erneut")]
    [InlineData(PcgwErrorKind.LoginFailed, "Anmeldung abgelehnt (Benutzername oder Bot-Passwort falsch)")]
    [InlineData(PcgwErrorKind.PermissionDenied, "dem Bot-Passwort fehlt das Cargo-Recht")]
    [InlineData(PcgwErrorKind.AccountUnreadable, "gespeichertes Konto nicht lesbar, bitte neu eintragen")]
    [InlineData(PcgwErrorKind.InvalidResponse, "unerwartete Antwort von PCGamingWiki")]
    public void Every_error_has_a_readable_reason(PcgwErrorKind error, string expected)
    {
        Assert.Equal(expected, HdrListViewModel.ErrorText(error));
        Assert.False(string.IsNullOrWhiteSpace(HdrListViewModel.AccountErrorText(error)));
    }

    [Fact]
    public async Task Sync_now_runs_once_and_shows_progress_until_done()
    {
        var (vm, ctx) = Create();
        var pending = new TaskCompletionSource<HdrListRunResult>();
        ctx.FakeHdrList.PendingSync = pending;

        var run = vm.HdrList.SyncNowAsync();
        Assert.True(vm.HdrList.IsSyncing);
        Assert.Equal(ShellStrings.HdrListSyncing, vm.HdrList.StatusText);
        Assert.Equal(1, ctx.FakeHdrList.SyncCalls);

        ctx.FakeHdrList.Status = new HdrListStatus(Stand, null, 3, 5, false, false, null);
        pending.SetResult(new HdrListRunResult(HdrListRunOutcome.Completed, null, 2, 3, 3, false));
        await run;

        Assert.False(vm.HdrList.IsSyncing);
        Assert.StartsWith("Stand: ", vm.HdrList.StatusText, StringComparison.Ordinal);
        Assert.True(vm.HdrList.SyncNowCommand.CanExecute(null));
        Assert.False(vm.HasChanges); // „Jetzt abgleichen“ ist keine ungespeicherte Einstellung
    }

    [Fact]
    public async Task Save_account_validates_before_asking_the_wiki()
    {
        var (vm, ctx) = Create();
        var list = vm.HdrList;

        list.UserName = "Dante_GE"; // ohne @BotName
        list.SetPassword("geheim");
        await list.SaveAccountAsync();
        Assert.Equal(ShellStrings.PcgwUserInvalid, list.AccountMessage);
        Assert.True(list.AccountMessageIsError);

        list.UserName = "Dante_GE@HdrPilot";
        list.SetPassword(string.Empty);
        await list.SaveAccountAsync();
        Assert.Equal(ShellStrings.PcgwPasswordMissing, list.AccountMessage);

        Assert.Empty(ctx.FakeHdrList.SaveCalls);
    }

    [Fact]
    public async Task Successful_test_stores_the_account_and_clears_the_password()
    {
        var (vm, ctx) = Create();
        var list = vm.HdrList;
        var cleared = 0;
        list.PasswordClearRequested += (_, _) => cleared++;

        list.UserName = "  Dante_GE@HdrPilot ";
        list.SetPassword("abc123geheim");
        await list.SaveAccountAsync();

        Assert.Equal([("Dante_GE@HdrPilot", (string?)"abc123geheim")], ctx.FakeHdrList.SaveCalls);
        Assert.Equal(1, cleared);
        Assert.Equal(ShellStrings.PcgwSaved, list.AccountMessage);
        Assert.False(list.AccountMessageIsError);
        Assert.True(list.HasAccount);
        Assert.Contains("Dante_GE@HdrPilot", list.AccountUserText, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123geheim", list.AccountUserText, StringComparison.Ordinal);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.HdrListModeAccount, "Dante_GE@HdrPilot"), list.ModeText);
        Assert.True(list.RemoveAccountCommand.CanExecute(null));

        // Erneut testen ohne neues Passwort: das gespeicherte wird benutzt (null an den Dienst).
        await list.SaveAccountAsync();
        Assert.Equal(("Dante_GE@HdrPilot", (string?)null), ctx.FakeHdrList.SaveCalls[1]);
    }

    [Theory]
    [InlineData(PcgwErrorKind.LoginFailed)]
    [InlineData(PcgwErrorKind.PermissionDenied)]
    [InlineData(PcgwErrorKind.RateLimited)]
    [InlineData(PcgwErrorKind.Network)]
    [InlineData(PcgwErrorKind.LoginThrottled)]
    public async Task Failed_test_keeps_the_password_and_explains(PcgwErrorKind error)
    {
        var (vm, ctx) = Create();
        var list = vm.HdrList;
        var cleared = 0;
        list.PasswordClearRequested += (_, _) => cleared++;
        ctx.FakeHdrList.NextSaveResult = error;

        list.UserName = "Dante_GE@HdrPilot";
        list.SetPassword("falsch");
        await list.SaveAccountAsync();

        Assert.Equal(HdrListViewModel.AccountErrorText(error), list.AccountMessage);
        Assert.True(list.AccountMessageIsError);
        Assert.Equal(0, cleared);
        Assert.False(list.HasAccount);
        Assert.False(list.IsTestingAccount);
        Assert.True(list.SaveAccountCommand.CanExecute(null));
    }

    [Fact]
    public void Account_error_texts_name_the_cause()
    {
        Assert.Contains("Benutzername", HdrListViewModel.AccountErrorText(PcgwErrorKind.LoginFailed), StringComparison.Ordinal);
        Assert.Contains("Create, query and delete data through the Cargo extension", HdrListViewModel.AccountErrorText(PcgwErrorKind.PermissionDenied), StringComparison.Ordinal);
        Assert.Contains("zu viele Anfragen", HdrListViewModel.AccountErrorText(PcgwErrorKind.RateLimited), StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_account_deletes_it_and_returns_to_anonymous_mode()
    {
        var (vm, ctx) = Create();
        ctx.FakeHdrList.Status = new HdrListStatus(Stand, null, 5, 5, true, false, "Dante_GE@HdrPilot");
        vm.HdrList.Refresh();
        Assert.True(vm.HdrList.HasAccount);

        vm.HdrList.RemoveAccountCommand.Execute(null);

        Assert.Equal(1, ctx.FakeHdrList.RemoveCalls);
        Assert.False(vm.HdrList.HasAccount);
        Assert.Equal(string.Empty, vm.HdrList.UserName);
        Assert.Equal(ShellStrings.PcgwRemoved, vm.HdrList.AccountMessage);
        Assert.Equal(ShellStrings.HdrListModeAnonymous, vm.HdrList.ModeText);
        Assert.False(vm.HdrList.RemoveAccountCommand.CanExecute(null));
    }

    [Fact]
    public void Stored_account_name_is_prefilled()
    {
        var config = ShellTestFactory.NewConfig(_fs);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
        ctx.FakeHdrList.Status = new HdrListStatus(null, null, 0, 0, true, false, "Dante_GE@HdrPilot");

        var vm = new SettingsViewModel(ctx, new FakeFolderPicker());

        Assert.Equal("Dante_GE@HdrPilot", vm.HdrList.UserName);
        Assert.True(vm.HdrList.HasAccount);
    }

    [Fact]
    public void Links_open_the_pcgamingwiki_pages_and_failures_are_reported()
    {
        var (vm, ctx) = Create();

        vm.HdrList.OpenCreateAccountCommand.Execute(null);
        vm.HdrList.OpenBotPasswordsCommand.Execute(null);
        vm.HdrList.OpenSiteCommand.Execute(null);
        vm.HdrList.OpenLicenseCommand.Execute(null);

        Assert.Equal(
            [
                "https://www.pcgamingwiki.com/wiki/Special:CreateAccount",
                "https://www.pcgamingwiki.com/wiki/Special:BotPasswords",
                "https://www.pcgamingwiki.com/",
                "https://creativecommons.org/licenses/by-nc-sa/3.0/",
            ],
            ctx.OpenedPages);

        ctx.OpenWebPageFailure = new System.ComponentModel.Win32Exception(2);
        vm.HdrList.OpenSiteCommand.Execute(null);
        Assert.True(vm.HdrList.AccountMessageIsError);
        Assert.StartsWith("Die Seite ließ sich nicht öffnen", vm.HdrList.AccountMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_changes_refresh_the_status_and_closing_unsubscribes()
    {
        var (vm, ctx) = Create();
        Assert.Equal(1, ctx.FakeHdrList.Subscribers);

        ctx.FakeHdrList.Status = new HdrListStatus(Stand, null, 1, 2, false, false, null);
        ctx.FakeHdrList.RaiseChanged(); // ohne SynchronizationContext: sofort
        Assert.StartsWith("Stand: ", vm.HdrList.StatusText, StringComparison.Ordinal);

        vm.Dispose();
        Assert.Equal(0, ctx.FakeHdrList.Subscribers);
    }

    [Fact]
    public void Window_section_has_named_controls_and_passes_the_password_only_to_the_view_model()
    {
        WpfTestHost.Run(() =>
        {
            var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
            var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
            var window = new SettingsWindow();
            var model = new SettingsViewModel(ctx, new FakeFolderPicker());
            window.Attach(model);
            WpfTestHost.Layout(window, new Size(1080, 1400));
            WpfTestHost.DoEvents();

            var ids = Logical(window).OfType<FrameworkElement>()
                .Where(e => !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(e)))
                .ToDictionary(AutomationProperties.GetAutomationId);
            foreach (var id in new[]
            {
                "HdrListSyncToggle", "HdrListSyncNowButton", "HdrListStatus", "PcgwCreateAccountLink", "PcgwBotPasswordsLink",
                "PcgwUserName", "PcgwBotPassword", "PcgwSaveAccountButton", "PcgwRemoveAccountButton", "PcgwSiteLink", "PcgwLicenseLink",
            })
            {
                Assert.True(ids.ContainsKey(id), "AutomationId fehlt: " + id);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(ids[id])), "Name fehlt: " + id);
            }

            Assert.Contains("PcgwAccountMessage", ids.Keys);
            Assert.Contains("PcgwAccountStored", ids.Keys);
            var toggle = Assert.IsType<CheckBox>(ids["HdrListSyncToggle"]);
            Assert.True(toggle.IsChecked);
            toggle.IsChecked = false;
            Assert.False(model.HdrListSync);

            // Bot-Passwort: PasswordBox gibt es nur ans ViewModel weiter, nach Erfolg wird das Feld geleert.
            var box = Assert.IsType<PasswordBox>(ids["PcgwBotPassword"]);
            ((TextBox)ids["PcgwUserName"]).Text = "Dante_GE@HdrPilot";
            box.Password = "abc123geheim";
            model.HdrList.SaveAccountAsync().GetAwaiter().GetResult();
            Assert.Equal([("Dante_GE@HdrPilot", (string?)"abc123geheim")], ctx.FakeHdrList.SaveCalls);
            Assert.Equal(string.Empty, box.Password);

            Assert.Contains(ShellStrings.PcgwAttribution, Logical(window).OfType<TextBlock>().Select(t => t.Text));
            Assert.Equal("HDR-Angaben: PCGamingWiki (pcgamingwiki.com), Lizenz CC BY-NC-SA 3.0", ShellStrings.PcgwAttribution);
            window.Close();
            Assert.Equal(0, ctx.FakeHdrList.Subscribers);
        });
    }

    [Fact]
    public void Settings_with_hdr_list_render_in_both_themes_for_review()
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
                    ctx.FakeHdrList.Status = new HdrListStatus(Stand, dark ? null : PcgwErrorKind.RateLimited, 412, 646, false, false, dark ? null : "Dante_GE@HdrPilot");
                    ctx.FakeHdrList.NextSaveResult = PcgwErrorKind.PermissionDenied;
                    var window = new SettingsWindow();
                    var model = new SettingsViewModel(ctx, new FakeFolderPicker());
                    window.Attach(model);
                    if (dark)
                    {
                        model.HdrList.UserName = "Dante_GE@HdrPilot";
                        model.HdrList.SetPassword("x");
                        model.HdrList.SaveAccountAsync().GetAwaiter().GetResult(); // Meldung „fehlt das Cargo-Recht“
                    }

                    WpfTestHost.Layout(window, new Size(1080, 2200));
                    WpfTestHost.DoEvents();
                    WpfTestHost.Layout(window, new Size(1080, 2200));
                    var section = (FrameworkElement)window.FindName("HdrListSection");
                    Assert.True(section.ActualHeight > 200 && section.ActualWidth > 900, $"Abschnitt {section.ActualWidth}x{section.ActualHeight}");
                    SaveForReview((Visual)VisualTreeHelper.GetChild(window, 0), $"einstellungen-hdrliste-{suffix}.png");
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
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        Directory.CreateDirectory(dir);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, name));
        encoder.Save(stream);
    }

    private (SettingsViewModel Vm, FakeShellContext Context) Create()
    {
        var config = ShellTestFactory.NewConfig(_fs);
        var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
        return (new SettingsViewModel(ctx, new FakeFolderPicker()), ctx);
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
