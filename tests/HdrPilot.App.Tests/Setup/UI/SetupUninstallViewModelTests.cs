using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using static HdrPilot.App.Tests.Setup.UI.SetupFixtures;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>Entfernen: Rückfrage (Daten standardmäßig behalten, Steam-Warnung, Übergabe an die Kopie) und Fortschritt der Kopie.</summary>
public sealed class SetupUninstallViewModelTests : IDisposable
{
    private readonly SetupTestFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Confirm_keeps_data_by_default_and_lists_steam_games()
    {
        var model = new UninstallViewModel(Uninstall("1.1.0", "Cyberpunk 2077", "Steam-App 1091500"), Args(), _ => Task.CompletedTask, _folder.Log);

        Assert.Equal(Format(SetupStrings.UninstallTitle, "1.1.0"), model.Title);
        Assert.False(model.RemoveData);
        Assert.True(model.HasSteamGames);
        Assert.Equal(["Cyberpunk 2077", "Steam-App 1091500"], model.SteamGames);
        Assert.True(model.ConfirmCommand.CanExecute(null));
        Assert.Equal(SetupExitCodes.Cancelled, model.ExitCode);

        Assert.False(new UninstallViewModel(Uninstall("1.1.0"), Args(), _ => Task.CompletedTask, _folder.Log).HasSteamGames);
        Assert.True(new UninstallViewModel(Uninstall("1.1.0"), Args("--uninstall", "--remove-data"), _ => Task.CompletedTask, _folder.Log).RemoveData);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirm_hands_off_with_the_data_choice_and_closes_with_ok(bool removeData)
    {
        var handed = new List<bool>();
        var model = new UninstallViewModel(Uninstall(), Args("--uninstall"), r =>
        {
            handed.Add(r);
            return Task.CompletedTask;
        }, _folder.Log);
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;
        model.RemoveData = removeData;

        await model.ConfirmAsync();

        Assert.Equal([removeData], handed);
        Assert.Equal(1, closed);
        Assert.Equal(SetupExitCodes.Ok, model.ExitCode);
        Assert.False(model.HasError);
    }

    [Fact]
    public async Task Failed_hand_off_shows_the_error_stays_open_and_returns_failed()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var model = new UninstallViewModel(Uninstall(), Args("--uninstall"), async _ =>
        {
            attempts++;
            await gate.Task;
            throw new IOException("Kopie gescheitert");
        }, _folder.Log);
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        var run = model.ConfirmAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.ConfirmCommand.CanExecute(null));
        Assert.False(model.CancelCommand.CanExecute(null));
        gate.SetResult();
        await run;

        Assert.Equal(0, closed);
        Assert.True(model.HasError);
        Assert.Equal(Format(SetupStrings.SetupErrorUnexpected, "Kopie gescheitert", _folder.Log.FilePath), model.ErrorText);
        Assert.Equal(SetupExitCodes.Failed, model.ExitCode);
        Assert.True(model.ConfirmCommand.CanExecute(null)); // noch einmal versuchen geht
        Assert.Equal(1, attempts);
        Assert.Contains("Kopie gescheitert", _folder.LogText, StringComparison.Ordinal);

        model.CancelCommand.Execute(null);
        Assert.Equal(1, closed);
        Assert.Equal(SetupExitCodes.Failed, model.ExitCode);
    }

    [Fact]
    public void Disk_full_while_copying_is_named_as_such()
    {
        var full = new IOException("voll", unchecked((int)0x80070070));

        Assert.Equal(SetupErrorKind.DiskFull, UninstallHandoff.ErrorKindFor(full));
        Assert.Equal(SetupErrorKind.Unexpected, UninstallHandoff.ErrorKindFor(new UnauthorizedAccessException()));
    }

    [Fact]
    public async Task Without_installed_exe_removal_is_still_offered_and_the_copy_decides()
    {
        // Ordner von Hand gelöscht: der Eintrag unter „Installierte Apps“ kann noch da sein, das weiß erst die Engine.
        var handed = new List<bool>();
        var model = new UninstallViewModel(Uninstall(installed: null), Args("--uninstall"), r =>
        {
            handed.Add(r);
            return Task.CompletedTask;
        }, _folder.Log);
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        Assert.Equal(SetupStrings.UninstallWindowTitle, model.Title);
        Assert.True(model.ConfirmCommand.CanExecute(null));
        Assert.Equal(SetupExitCodes.Cancelled, model.ExitCode);
        await model.ConfirmAsync();
        Assert.Equal([false], handed);
        Assert.Equal(1, closed);
        Assert.Equal(SetupExitCodes.Ok, model.ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Progress_success_says_where_the_settings_are_or_that_they_are_gone(bool removeData)
    {
        var engine = new FakeUninstallEngine(Uninstall(), _folder.Locations);
        var model = new UninstallProgressViewModel(engine, removeData, @"C:\Users\Test\AppData\Roaming\HDRpilot", _folder.Log);

        Assert.Equal(removeData, model.StepList.StateOf(UninstallStep.RemoveData) is not null);
        Assert.False(model.CloseCommand.CanExecute(null));
        await model.RunAsync();

        Assert.Equal(removeData, Assert.Single(engine.Calls).RemoveData);
        Assert.True(model.IsDone);
        Assert.True(model.Succeeded);
        Assert.True(model.CloseCommand.CanExecute(null));
        Assert.Equal(SetupExitCodes.Ok, model.ExitCode);
        var expected = SetupStrings.UninstallDone + " " + (removeData
            ? SetupStrings.UninstallDoneDataRemoved
            : Format(SetupStrings.UninstallDoneDataKept, @"C:\Users\Test\AppData\Roaming\HDRpilot"));
        Assert.Equal(expected, model.ResultText);
        Assert.All(model.Steps, s => Assert.Equal(StepState.Done, s.State));
    }

    [Fact]
    public async Task Progress_error_marks_the_running_step_and_maps_the_exit_code()
    {
        var engine = new FakeUninstallEngine(Uninstall(), _folder.Locations)
        {
            Report = [UninstallStep.StopRunningApp],
            Pending = new TaskCompletionSource<SetupResult>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var model = new UninstallProgressViewModel(engine, removeData: false, "C:\\Config", _folder.Log);

        var run = model.RunAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.IsDone);
        Assert.Equal(StepState.Running, model.StepList.StateOf(UninstallStep.StopRunningApp));
        engine.Pending.SetResult(SetupResult.Failed(SetupErrorKind.AppStillRunning, "hängt"));
        await run;

        Assert.True(model.HasError);
        Assert.Equal(StepState.Failed, model.StepList.StateOf(UninstallStep.StopRunningApp));
        Assert.Equal(StepState.Pending, model.StepList.StateOf(UninstallStep.RemoveFiles));
        Assert.Equal(SetupStrings.SetupErrorAppStillRunning, model.ResultText);
        Assert.Equal(SetupExitCodes.AppStillRunning, model.ExitCode);
        Assert.Contains("AppStillRunning", _folder.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Progress_engine_exception_is_an_unexpected_error()
    {
        var engine = new FakeUninstallEngine(Uninstall(), _folder.Locations) { Failure = new InvalidOperationException("Registry weg") };
        var model = new UninstallProgressViewModel(engine, removeData: true, "C:\\Config", _folder.Log);

        await model.RunAsync();

        Assert.Equal(SetupExitCodes.Failed, model.ExitCode);
        Assert.Equal(Format(SetupStrings.SetupErrorUnexpected, "Registry weg", _folder.Log.FilePath), model.ResultText);
        Assert.True(model.CloseCommand.CanExecute(null));
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
