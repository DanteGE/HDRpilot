using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using static HdrPilot.App.Tests.Setup.UI.SetupFixtures;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>Setup-Fenster ohne Fenster: Vorbelegung je Art, Bestätigung einer älteren Version, Fortschritt, Ergebnis, Erneut versuchen.</summary>
public sealed class SetupViewModelTests : IDisposable
{
    private readonly SetupTestFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Fresh_install_turns_on_autostart_and_launch_but_no_desktop_shortcut()
    {
        var model = Model(Inspection(installed: null));

        Assert.Equal(SetupKind.Install, model.Kind);
        Assert.True(model.Autostart);
        Assert.False(model.DesktopShortcut);
        Assert.True(model.LaunchAfter);
        Assert.Equal(Format(SetupStrings.SetupTitleInstall, "1.1.0"), model.Title);
        Assert.False(model.HasVersionInfo);
        Assert.False(model.ShowRunningNote);
        Assert.False(model.IsDowngrade);
        Assert.Equal(SetupStrings.SetupInstallButton, model.InstallButtonText);
        Assert.Equal(SetupFixtures.InstallDir, model.InstallDirectory);
        Assert.Equal(Format(SetupStrings.SetupDataNote, Path.Combine(_folder.Root, "Config"), Path.Combine(_folder.Root, "Config")), model.DataNote);
        Assert.True(model.InstallCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void Update_keeps_the_previous_choices_and_shows_old_and_new_version(bool autostart, bool desktop, bool running)
    {
        var model = Model(Inspection("1.0.0", autostart: autostart, desktop: desktop, running: running));

        Assert.Equal(SetupKind.Update, model.Kind);
        Assert.Equal(autostart, model.Autostart);
        Assert.Equal(desktop, model.DesktopShortcut);
        Assert.Equal(running, model.LaunchAfter); // lief die App, startet sie wieder
        Assert.Equal(running, model.ShowRunningNote);
        Assert.Equal(SetupStrings.SetupTitleUpdate, model.Title);
        Assert.Equal(Format(SetupStrings.SetupVersionUpdate, "1.0.0", "1.1.0"), model.VersionInfo);
        Assert.Equal(SetupStrings.SetupUpdateButton, model.InstallButtonText);
    }

    [Fact]
    public void Reinstall_names_the_version_and_uses_the_install_button()
    {
        var model = Model(Inspection("1.1.0", autostart: true));

        Assert.Equal(SetupKind.Reinstall, model.Kind);
        Assert.Equal(Format(SetupStrings.SetupTitleReinstall, "1.1.0"), model.Title);
        Assert.True(model.Autostart);
        Assert.False(model.HasVersionInfo);
        Assert.Equal(SetupStrings.SetupInstallButton, model.InstallButtonText);
    }

    [Fact]
    public void Command_line_switches_preset_the_options()
    {
        var model = Model(Inspection(installed: null), Args("--autostart", "off", "--desktop-shortcut", "on", "--no-launch"));

        Assert.False(model.Autostart);
        Assert.True(model.DesktopShortcut);
        Assert.False(model.LaunchAfter);
    }

    [Fact]
    public async Task Downgrade_needs_explicit_confirmation_before_install()
    {
        var engine = Engine(Inspection("1.2.0"));
        var model = new SetupViewModel(engine, engine.Inspection, Args(), _folder.Log);
        var raised = 0;
        model.InstallCommand.CanExecuteChanged += (_, _) => raised++;

        Assert.True(model.IsDowngrade);
        Assert.Equal(SetupStrings.SetupTitleDowngrade, model.Title);
        Assert.Equal(Format(SetupStrings.SetupVersionDowngrade, "1.2.0", "1.1.0"), model.VersionInfo);
        Assert.False(model.InstallCommand.CanExecute(null));
        await model.InstallAsync();
        Assert.Empty(engine.Calls); // gesperrt heißt: auch direkt nichts

        model.AllowDowngrade = true;
        Assert.True(raised > 0);
        Assert.True(model.InstallCommand.CanExecute(null));
        await model.InstallAsync();
        Assert.True(Assert.Single(engine.Calls).AllowDowngrade);
    }

    [Fact]
    public async Task Progress_shows_reported_steps_live_and_blocks_closing()
    {
        var engine = Engine(Inspection(installed: null));
        engine.Report = [SetupStep.CheckSetupFile, SetupStep.CopyFiles];
        engine.Pending = new TaskCompletionSource<SetupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new SetupViewModel(engine, engine.Inspection, Args(), _folder.Log) { LaunchAfter = false };

        var run = model.InstallAsync();

        Assert.Equal(SetupPage.Progress, model.Page);
        Assert.True(model.IsBusy);
        Assert.False(model.CancelCommand.CanExecute(null));
        Assert.False(model.InstallCommand.CanExecute(null));
        Assert.Equal(StepState.Done, model.StepList.StateOf(SetupStep.CheckSetupFile));
        Assert.Equal(StepState.Running, model.StepList.StateOf(SetupStep.CopyFiles));
        Assert.Equal(StepState.Pending, model.StepList.StateOf(SetupStep.Register));
        Assert.Null(model.StepList.StateOf(SetupStep.StopRunningApp)); // App lief nicht
        Assert.Null(model.StepList.StateOf(SetupStep.LaunchApp)); // nicht gewünscht
        Assert.Equal(
            [SetupStrings.SetupStepCheckSetupFile, SetupStrings.SetupStepCopyFiles, SetupStrings.SetupStepShortcuts, SetupStrings.SetupStepAutostart, SetupStrings.SetupStepRegister],
            model.Steps.Select(s => s.Text));

        engine.Pending.SetResult(FakeSetupEngine.Success(null, new Version(1, 1, 0)));
        await run;

        Assert.False(model.IsBusy);
        Assert.All(model.Steps, s => Assert.Equal(StepState.Done, s.State));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("1.0.0", true)]
    public async Task Success_shows_the_done_text_and_closes_with_ok(string? installed, bool isUpdate)
    {
        var model = Model(Inspection(installed));
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        await model.InstallAsync();

        Assert.Equal(SetupPage.Result, model.Page);
        Assert.True(model.Succeeded);
        Assert.False(model.HasError);
        Assert.False(model.CanRetry);
        Assert.False(model.ShowCancel);
        Assert.Equal(SetupExitCodes.Ok, model.ExitCode);
        Assert.Equal(Format(isUpdate ? SetupStrings.SetupDoneUpdate : SetupStrings.SetupDoneInstall, "1.1.0"), model.ResultText);

        model.PrimaryCommand.Execute(null); // Eingabetaste = Fertig
        Assert.Equal(1, closed);
        Assert.Equal(SetupExitCodes.Ok, model.ExitCode);
    }

    [Fact]
    public async Task App_still_running_offers_retry_and_retry_installs_again()
    {
        var engine = Engine(Inspection("1.0.0", running: true));
        engine.Report = [SetupStep.CheckSetupFile, SetupStep.StopRunningApp];
        engine.Results.Enqueue(SetupResult.Failed(SetupErrorKind.AppStillRunning, "läuft noch", new Version(1, 0, 0)));
        var model = new SetupViewModel(engine, engine.Inspection, Args(), _folder.Log);

        await model.InstallAsync();

        Assert.True(model.HasError);
        Assert.True(model.CanRetry);
        Assert.True(model.ShowCancel);
        Assert.True(model.RetryCommand.CanExecute(null));
        Assert.Equal(SetupStrings.SetupErrorAppStillRunning, model.ResultText);
        Assert.Equal(SetupExitCodes.AppStillRunning, model.ExitCode);
        Assert.Equal(StepState.Failed, model.StepList.StateOf(SetupStep.StopRunningApp));
        Assert.Equal(StepState.Done, model.StepList.StateOf(SetupStep.CheckSetupFile));

        engine.Report = [SetupStep.CheckSetupFile, SetupStep.CopyFiles];
        model.RetryCommand.Execute(null);
        await WaitUntil(() => model.IsResultPage && engine.Calls.Count == 2);

        Assert.True(model.Succeeded);
        Assert.Equal(SetupExitCodes.Ok, model.ExitCode);
        Assert.Equal(engine.Calls[0], engine.Calls[1]); // gleiche Optionen
        Assert.All(model.Steps, s => Assert.Equal(StepState.Done, s.State));
    }

    [Theory]
    [InlineData(SetupErrorKind.AccessDenied, SetupExitCodes.Failed)]
    [InlineData(SetupErrorKind.DowngradeNotAllowed, SetupExitCodes.DowngradeRefused)]
    [InlineData(SetupErrorKind.SetupFileDamaged, SetupExitCodes.Failed)]
    public async Task Other_errors_offer_only_cancel_and_keep_their_exit_code(SetupErrorKind error, int exitCode)
    {
        var engine = Engine(Inspection("1.0.0"));
        engine.Results.Enqueue(SetupResult.Failed(error, "x"));
        var model = new SetupViewModel(engine, engine.Inspection, Args(), _folder.Log);
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        await model.InstallAsync();

        Assert.True(model.HasError);
        Assert.False(model.CanRetry);
        Assert.False(model.RetryCommand.CanExecute(null));
        Assert.True(model.ShowCancel);
        Assert.Equal(SetupText.Error(error, "x", _folder.Log.FilePath), model.ResultText);
        Assert.NotEqual(SetupStrings.SetupErrorUnexpected, model.ResultText);

        model.CancelCommand.Execute(null);
        Assert.Equal(1, closed);
        Assert.Equal(exitCode, model.ExitCode);
    }

    [Fact]
    public async Task Engine_exception_becomes_an_unexpected_error_with_detail_and_log_path()
    {
        var engine = Engine(Inspection("1.0.0"));
        engine.Failure = new IOException("Datenträger getrennt");
        var model = new SetupViewModel(engine, engine.Inspection, Args(), _folder.Log);

        await model.InstallAsync();

        Assert.True(model.HasError);
        Assert.Equal(Format(SetupStrings.SetupErrorUnexpected, "Datenträger getrennt", _folder.Log.FilePath), model.ResultText);
        Assert.Equal(SetupExitCodes.Failed, model.ExitCode);
        Assert.Contains("Datenträger getrennt", _folder.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancel_before_start_closes_with_cancelled_and_installs_nothing()
    {
        var engine = Engine(Inspection(installed: null));
        var model = new SetupViewModel(engine, engine.Inspection, Args(), _folder.Log);
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        model.CancelCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal(SetupExitCodes.Cancelled, model.ExitCode);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public async Task Enter_installs_with_the_chosen_options()
    {
        var engine = Engine(Inspection("1.0.0", autostart: true, running: true));
        var model = new SetupViewModel(engine, engine.Inspection, Args(), _folder.Log) { DesktopShortcut = true, Autostart = false };

        model.PrimaryCommand.Execute(null);
        await WaitUntil(() => model.IsResultPage);

        Assert.Equal(new InstallOptions(Autostart: false, DesktopShortcut: true, LaunchAfterInstall: true, AllowDowngrade: false), Assert.Single(engine.Calls));
    }

    [Fact]
    public void Step_list_inserts_unexpected_steps_in_order_and_marks_failures()
    {
        var list = new StepList<SetupStep>(SetupText.Step);
        list.Reset([SetupStep.CheckSetupFile, SetupStep.CopyFiles, SetupStep.Register]);

        list.Begin(SetupStep.CheckSetupFile);
        list.Begin(SetupStep.StopRunningApp); // nicht erwartet (App lief doch): an seiner Stelle einfügen
        Assert.Equal([SetupStrings.SetupStepCheckSetupFile, SetupStrings.SetupStepStopRunningApp, SetupStrings.SetupStepCopyFiles, SetupStrings.SetupStepRegister], list.Items.Select(i => i.Text));
        Assert.Equal(StepState.Done, list.StateOf(SetupStep.CheckSetupFile));
        Assert.Equal(StepState.Running, list.StateOf(SetupStep.StopRunningApp));

        list.Fail();
        Assert.Equal(StepState.Failed, list.StateOf(SetupStep.StopRunningApp));
        Assert.Equal(StepState.Pending, list.StateOf(SetupStep.CopyFiles));
        Assert.Equal(SetupStrings.SetupStepCopyFiles, list.Items[2].ToString());
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Zustand nicht erreicht");
    }

    private FakeSetupEngine Engine(SetupInspection inspection) => new(inspection, _folder.Locations);

    private SetupViewModel Model(SetupInspection inspection, SetupArguments? args = null) =>
        new(Engine(inspection), inspection, args ?? Args(), _folder.Log);
}
