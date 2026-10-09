using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using static HdrPilot.App.Tests.Setup.UI.SetupFixtures;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>
/// Abläufe von Setup und Entfernen mit einer Attrappe der Umgebung (keine Fenster, Prozesse oder echten Orte):
/// abgelehnte Befehlszeilen ändern nichts, die Kopie zum Entfernen bekommt die Wahl aus dem Fenster und einen
/// vollständigen Testordner, wartet auf die richtige Prozess-ID, räumt immer auf und schreibt „ERGEBNIS“ als letzte Zeile.
/// </summary>
public sealed class SetupRunnerTests : IDisposable
{
    private readonly SetupTestFolder _folder = new();
    private readonly FakeSetupEngine _setup;
    private readonly FakeUninstallEngine _uninstall;
    private readonly FakeSetupHost _host;

    public SetupRunnerTests()
    {
        _setup = new FakeSetupEngine(Inspection(installed: null), _folder.Locations);
        _uninstall = new FakeUninstallEngine(Uninstall(), _folder.Locations);
        _host = new FakeSetupHost(_setup, _uninstall);
    }

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData(SetupModeKind.Setup, false)]
    [InlineData(SetupModeKind.Setup, true)]
    [InlineData(SetupModeKind.Uninstall, false)]
    [InlineData(SetupModeKind.Uninstall, true)]
    [InlineData(SetupModeKind.UninstallContinue, false)]
    [InlineData(SetupModeKind.UninstallContinue, true)]
    public async Task Test_root_switch_without_folder_changes_nothing_in_every_mode(SetupModeKind kind, bool quiet)
    {
        // Windows PowerShell 5.1 lässt ein leeres Argument weg: aus „--test-root $leer“ wird „--test-root“ allein.
        var mode = Mode(kind, quiet ? ["--quiet", "--test-root"] : ["--test-root"]);

        var code = await SetupRunner.RunAsync(_host, mode, _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.BadArguments, code);
        Assert.Empty(_setup.Calls);
        Assert.Empty(_uninstall.Calls);
        Assert.Empty(_host.HandOffs);
        var expected = new List<string>();
        if (!quiet)
        {
            expected.Add("ShowError");
            Assert.Equal(SetupText.RefusedArguments("--test-root", _folder.Log.FilePath), Assert.Single(_host.Errors)); // „nichts geändert“
        }

        if (kind == SetupModeKind.UninstallContinue)
        {
            expected.Add("ScheduleSelfRemoval");
            AssertResultIsTheLastLineAfterRemoval(SetupExitCodes.BadArguments);
        }

        Assert.Equal(expected, _host.Calls);
    }

    [Theory]
    [InlineData("--TEST-ROOT")]
    [InlineData(@"--test-root=C:\Test")]
    [InlineData("--test-root", "--quiet")] // Ordner leer verworfen: der nächste Schalter wäre der Ordner
    public async Task Setup_window_refuses_test_root_variants_without_a_usable_folder(params string[] args)
    {
        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.Setup, args), _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.BadArguments, code);
        Assert.Equal(["ShowError"], _host.Calls);
        Assert.Contains("--test-root", Assert.Single(_host.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Befehlszeile abgelehnt", _folder.LogText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "--uninstall")]
    [InlineData(true, "--remove-data")]
    [InlineData(true, "--uninstall", "--remove-data")]
    [InlineData(true, "--uninstall-continue", "--wait-pid", "77")]
    [InlineData(false, "--uninstall")]
    [InlineData(false, "--wait-pid", "77")]
    public async Task Setup_file_with_uninstall_switches_installs_nothing(bool quiet, params string[] args)
    {
        var mode = Mode(SetupModeKind.Setup, quiet ? ["--quiet", .. args] : args);

        var code = await SetupRunner.RunAsync(_host, mode, _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.BadArguments, code);
        Assert.Empty(_setup.Calls);
        string[] expected = quiet ? [] : ["ShowError"];
        Assert.Equal(expected, _host.Calls);
    }

    [Theory]
    [InlineData(SetupModeKind.Setup)]
    [InlineData(SetupModeKind.Uninstall)]
    public async Task Quiet_run_with_an_unknown_switch_changes_nothing(SetupModeKind kind)
    {
        var code = await SetupRunner.RunAsync(_host, Mode(kind, ["--quiet", "--bogus"]), _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.BadArguments, code);
        Assert.Empty(_host.Calls);
        Assert.Empty(_setup.Calls);
        Assert.Contains("--bogus", _folder.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_window_tolerates_an_unknown_switch_and_returns_the_window_code()
    {
        _host.WindowExitCode = SetupExitCodes.Cancelled;

        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.Setup, ["--bogus"]), _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Cancelled, code);
        Assert.Equal(["CreateSetup", "ShowSetup"], _host.Calls);
        Assert.Contains("--bogus", _folder.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quiet_setup_installs_without_window()
    {
        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.Setup, ["--quiet", "--no-launch", "--test-root", _folder.Root]), _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Ok, code);
        Assert.Equal(["CreateSetup"], _host.Calls);
        Assert.False(Assert.Single(_setup.Calls).LaunchAfterInstall);
    }

    [Fact]
    public async Task Quiet_uninstall_hands_off_at_once_with_own_pid_and_absolute_test_root()
    {
        var mode = Mode(SetupModeKind.Uninstall, ["--quiet", "--remove-data", "--test-root", "relativ"]);

        var code = await SetupRunner.RunAsync(_host, mode, _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Ok, code);
        Assert.Equal(["HandOff"], _host.Calls); // ohne Rückfrage und ohne Bestandsaufnahme
        Assert.Equal(["--uninstall-continue", "--wait-pid", "4242", "--quiet", "--remove-data", "--test-root", Path.GetFullPath("relativ")], Assert.Single(_host.HandOffs));
        Assert.Empty(_uninstall.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uninstall_window_hands_off_with_the_choice_from_the_window(bool removeData)
    {
        // --remove-data belegt den Haken nur vor; entscheidend ist, was im Fenster steht.
        _host.ConfirmRemoveData = removeData;
        var mode = Mode(SetupModeKind.Uninstall, removeData ? ["--test-root", _folder.Root] : ["--remove-data", "--test-root", _folder.Root]);

        var code = await SetupRunner.RunAsync(_host, mode, _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Ok, code);
        Assert.Equal(["CreateUninstall", "ShowUninstall", "HandOff"], _host.Calls);
        List<string> expected = ["--uninstall-continue", "--wait-pid", "4242"];
        if (removeData)
        {
            expected.Add("--remove-data");
        }

        expected.AddRange(["--test-root", _folder.Root]);
        Assert.Equal(expected, Assert.Single(_host.HandOffs));
        Assert.Empty(_uninstall.Calls); // entfernt wird erst in der Kopie
    }

    [Fact]
    public async Task Cancelled_uninstall_window_hands_off_nothing()
    {
        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.Uninstall, []), _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Cancelled, code);
        Assert.Empty(_host.HandOffs);
    }

    [Fact]
    public async Task Copy_waits_for_the_installed_exe_removes_and_ends_with_the_result_line()
    {
        var mode = Mode(SetupModeKind.UninstallContinue, ["--quiet", "--remove-data", "--test-root", _folder.Root]);

        var code = await SetupRunner.RunAsync(_host, mode, _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Ok, code);
        Assert.Equal(["Wait 77", "CreateUninstall", "ScheduleSelfRemoval"], _host.Calls);
        Assert.True(Assert.Single(_uninstall.Calls).RemoveData);
        AssertResultIsTheLastLineAfterRemoval(SetupExitCodes.Ok);
    }

    [Fact]
    public async Task Copy_reports_the_engine_result_as_exit_code()
    {
        _uninstall.Result = SetupResult.Failed(SetupErrorKind.NotInstalled, "weg");

        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.UninstallContinue, ["--quiet"]), _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.NotInstalled, code);
        Assert.False(Assert.Single(_uninstall.Calls).RemoveData);
        AssertResultIsTheLastLineAfterRemoval(SetupExitCodes.NotInstalled);
    }

    [Fact]
    public async Task Copy_with_window_shows_progress_and_still_cleans_up_after_an_exception()
    {
        _uninstall.Failure = new InvalidOperationException("Registry weg");

        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.UninstallContinue, []), _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Failed, code);
        Assert.Equal(["Wait 77", "CreateUninstall", "ShowUninstallProgress " + _folder.Locations.DataPaths.ConfigDirectory, "ShowError", "ScheduleSelfRemoval"], _host.Calls);
        // Beim Entfernen ist womöglich schon etwas weg: „nicht vollständig“, nicht „die bisherige Version ist unverändert“.
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, HdrPilot.App.Resources.SetupStrings.UninstallErrorUnexpected, "Registry weg", _folder.Log.FilePath), Assert.Single(_host.Errors));
        AssertResultIsTheLastLineAfterRemoval(SetupExitCodes.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copy_with_an_unknown_switch_removes_nothing_but_cleans_up(bool quiet)
    {
        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.UninstallContinue, quiet ? ["--quiet", "--bogus"] : ["--bogus"]), _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.BadArguments, code);
        string[] expected = quiet ? ["ScheduleSelfRemoval"] : ["ShowError", "ScheduleSelfRemoval"];
        Assert.Equal(expected, _host.Calls);
        Assert.Empty(_uninstall.Calls);
        AssertResultIsTheLastLineAfterRemoval(SetupExitCodes.BadArguments);
    }

    [Fact]
    public void Continue_arguments_take_the_window_choice_and_an_absolute_test_root()
    {
        var args = Args("--uninstall", "--quiet", "--remove-data", "--test-root", @"tr\sub", "--bogus");

        Assert.Equal(["--uninstall-continue", "--wait-pid", "9", "--quiet", "--test-root", Path.GetFullPath(@"tr\sub")], SetupRunner.ContinueArguments(args, removeData: false, 9));
        Assert.Equal(["--uninstall-continue", "--wait-pid", "9", "--remove-data"], SetupRunner.ContinueArguments(Args("--uninstall"), removeData: true, 9));
        Assert.Equal(_folder.Root, SetupRunner.ContinueArguments(Args("--test-root", _folder.Root), removeData: false, 9)[^1]);
    }

    /// <summary>Setup-Datei, installierte EXE (<c>--uninstall</c>) oder Kopie (<c>--uninstall-continue --wait-pid 77</c>), wie beim Start erkannt.</summary>
    private SetupMode Mode(SetupModeKind kind, string[] args)
    {
        var exe = kind switch
        {
            SetupModeKind.Setup => _folder.CreateSetupFile(),
            SetupModeKind.Uninstall => _folder.CreateFile("HDRpilot.exe"),
            _ => _folder.CreateFile("HDRpilot-Uninstall.exe"),
        };
        string[] prefix = kind switch
        {
            SetupModeKind.Setup => [],
            SetupModeKind.Uninstall => ["--uninstall"],
            _ => ["--uninstall-continue", "--wait-pid", "77"],
        };

        Assert.True(SetupMode.TryDetect(exe, [.. prefix, .. args], out var mode));
        Assert.Equal(kind, mode.Kind);
        return mode;
    }

    private void AssertResultIsTheLastLineAfterRemoval(int code)
    {
        var lines = _folder.LogLines;
        Assert.True(lines.Length >= 2, "Protokoll zu kurz");
        Assert.EndsWith("ERGEBNIS: " + code.ToString(System.Globalization.CultureInfo.InvariantCulture), lines[^1], StringComparison.Ordinal);
        Assert.EndsWith(FakeSetupHost.RemovalMarker, lines[^2], StringComparison.Ordinal);
    }
}
