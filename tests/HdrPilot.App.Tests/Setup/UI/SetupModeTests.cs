using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using static HdrPilot.App.Tests.Setup.UI.SetupFixtures;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>Weiche beim Start (Setup-Datei, Entfernen, normal), Optionen ohne Fenster und Exit-Codes.</summary>
public sealed class SetupModeTests : IDisposable
{
    private readonly SetupTestFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Exe_with_setup_package_runs_as_setup_with_its_switches()
    {
        var exe = _folder.CreateFile("HDRpilot.exe");
        var readme = _folder.CreateFile("README.md", "# HDRpilot"u8.ToArray());
        var setup = Path.Combine(_folder.Root, "HDRpilot-Setup-1.1.0.exe");
        SetupBundle.Write(exe, new Dictionary<string, string> { ["README.md"] = readme }, setup);

        Assert.True(SetupMode.TryDetect(setup, ["--quiet", "--no-launch", "--test-root", _folder.Root], out var mode));
        Assert.Equal(SetupModeKind.Setup, mode.Kind);
        Assert.NotNull(mode.Bundle);
        Assert.True(mode.Arguments.Quiet);
        Assert.True(mode.Arguments.NoLaunch);
        Assert.Equal(_folder.Root, mode.Arguments.TestRoot);

        // Auch ohne Schalter (Doppelklick) bleibt eine Setup-Datei ein Setup; mit --uninstall auch, aber dann lehnt der
        // Lauf ab, statt zu installieren.
        Assert.True(SetupMode.TryDetect(setup, [], out var plain));
        Assert.Equal(SetupModeKind.Setup, plain.Kind);
        Assert.Empty(SetupMode.RefusedArguments(plain.Kind, plain.Arguments));
        Assert.True(SetupMode.TryDetect(setup, ["--uninstall"], out var odd));
        Assert.Equal(SetupModeKind.Setup, odd.Kind);
        Assert.Equal(["--uninstall"], SetupMode.RefusedArguments(odd.Kind, odd.Arguments));
    }

    [Theory]
    [InlineData(SetupModeKind.Setup, new[] { "--test-root" }, new[] { "--test-root" })] // Ordner fehlt: nie die echte Installation
    [InlineData(SetupModeKind.Uninstall, new[] { "--uninstall", "--Test-Root" }, new[] { "--Test-Root" })]
    [InlineData(SetupModeKind.UninstallContinue, new[] { "--uninstall-continue", "--test-root" }, new[] { "--test-root" })]
    [InlineData(SetupModeKind.Setup, new[] { "--test-root=C:\\Test" }, new[] { "--test-root=C:\\Test" })]
    [InlineData(SetupModeKind.Setup, new[] { "--test-root", "--quiet" }, new[] { "--test-root", "--quiet" })] // Schalter als Ordner
    [InlineData(SetupModeKind.Setup, new[] { "--uninstall", "--quiet", "--remove-data" }, new[] { "--uninstall", "--remove-data" })]
    [InlineData(SetupModeKind.Setup, new[] { "--uninstall-continue", "--wait-pid", "12" }, new[] { "--uninstall-continue", "--wait-pid", "12" })]
    [InlineData(SetupModeKind.Setup, new[] { "--quiet", "--bogus" }, new[] { "--bogus" })] // ohne Fenster jeder unbekannte Schalter
    [InlineData(SetupModeKind.Uninstall, new[] { "--uninstall", "--quiet", "--autostart", "vielleicht" }, new[] { "--autostart", "vielleicht" })]
    [InlineData(SetupModeKind.UninstallContinue, new[] { "--uninstall-continue", "--bogus" }, new[] { "--bogus" })] // Kopie: auch mit Fenster
    [InlineData(SetupModeKind.Setup, new[] { "--bogus" }, new string[0])] // mit Fenster sieht der Nutzer, was geschieht
    [InlineData(SetupModeKind.Uninstall, new[] { "--uninstall", "--bogus" }, new string[0])]
    [InlineData(SetupModeKind.Setup, new[] { "--quiet", "--no-launch", "--autostart", "off", "--allow-downgrade", "--test-root", "C:\\Test" }, new string[0])]
    [InlineData(SetupModeKind.Uninstall, new[] { "--uninstall", "--quiet", "--remove-data", "--test-root", "C:\\Test" }, new string[0])]
    [InlineData(SetupModeKind.UninstallContinue, new[] { "--uninstall-continue", "--wait-pid", "12", "--quiet", "--remove-data", "--test-root", "C:\\Test" }, new string[0])]
    public void Refused_arguments_are_those_that_would_do_something_else_than_asked(SetupModeKind kind, string[] args, string[] expected)
    {
        Assert.Equal(expected, SetupMode.RefusedArguments(kind, Args(args)));
    }

    [Fact]
    public void Installed_exe_with_uninstall_switch_runs_the_uninstall_confirmation()
    {
        var exe = _folder.CreateFile("HDRpilot.exe");

        Assert.True(SetupMode.TryDetect(exe, ["--uninstall", "--remove-data"], out var mode));
        Assert.Equal(SetupModeKind.Uninstall, mode.Kind);
        Assert.Null(mode.Bundle);
        Assert.True(mode.Arguments.RemoveData);
        Assert.False(mode.Arguments.Quiet);
    }

    [Fact]
    public void Continue_switch_runs_the_temp_copy_part()
    {
        var exe = _folder.CreateFile("HDRpilot-Uninstall.exe");
        var handOff = SetupArguments.Parse(["--uninstall", "--quiet", "--test-root", _folder.Root]).ToContinueArguments(4242);

        Assert.True(SetupMode.TryDetect(exe, handOff, out var mode));
        Assert.Equal(SetupModeKind.UninstallContinue, mode.Kind);
        Assert.Equal(4242, mode.Arguments.WaitPid);
        Assert.True(mode.Arguments.Quiet);
        Assert.Equal(_folder.Root, mode.Arguments.TestRoot);
    }

    [Theory]
    [InlineData]
    [InlineData("--show")]
    [InlineData("--minimized")]
    [InlineData("--config-dir", @"D:\Test", "--settings")]
    [InlineData("--run", "Steam:1091500")]
    [InlineData("--wrap", "--", "game.exe", "--uninstall")] // Spielbefehl hinter "--" ist nie ein Schalter
    public void Normal_exe_without_setup_switches_starts_normally(params string[] args)
    {
        var exe = _folder.CreateFile("HDRpilot.exe");

        Assert.False(SetupMode.TryDetect(exe, args, out var mode));
        Assert.Null(mode);
        Assert.False(SetupMode.TryDetect(null, args, out _));
    }

    [Fact]
    public void Damaged_or_truncated_package_is_not_taken_for_a_setup()
    {
        var exe = _folder.CreateFile("HDRpilot.exe");
        var setup = Path.Combine(_folder.Root, "HDRpilot-Setup-1.1.0.exe");
        SetupBundle.Write(exe, new Dictionary<string, string>(), setup);
        var bytes = File.ReadAllBytes(setup);
        var truncated = _folder.CreateFile("truncated.exe", bytes[1..]);

        Assert.False(SetupMode.TryDetect(truncated, [], out _));
        Assert.True(SetupMode.TryDetect(setup, [], out _));
    }

    [Theory]
    [InlineData(null, false, false, new string[0], true, false, true)] // neu: Autostart an, Desktop aus, starten
    [InlineData("1.0.0", false, true, new string[0], false, true, true)] // Aktualisierung: bisheriger Stand
    [InlineData("1.0.0", true, false, new string[0], true, false, true)]
    [InlineData("1.0.0", true, true, new[] { "--autostart", "off", "--desktop-shortcut", "off", "--no-launch" }, false, false, false)]
    [InlineData(null, false, false, new[] { "--autostart", "aus", "--desktop-shortcut", "an" }, false, true, true)]
    public void Quiet_options_follow_switches_then_previous_state(string? installed, bool autostart, bool desktop, string[] args, bool expectAutostart, bool expectDesktop, bool expectLaunch)
    {
        var options = SetupMode.QuietOptions(Args(args), Inspection(installed, autostart: autostart, desktop: desktop));

        Assert.Equal(new InstallOptions(expectAutostart, expectDesktop, expectLaunch, AllowDowngrade: false), options);
        Assert.True(SetupMode.QuietOptions(Args("--allow-downgrade"), Inspection("2.0.0")).AllowDowngrade);
    }

    [Theory]
    [InlineData(null, SetupExitCodes.Ok)]
    [InlineData(SetupErrorKind.DowngradeNotAllowed, SetupExitCodes.DowngradeRefused)]
    [InlineData(SetupErrorKind.AppStillRunning, SetupExitCodes.AppStillRunning)]
    [InlineData(SetupErrorKind.NotInstalled, SetupExitCodes.NotInstalled)]
    [InlineData(SetupErrorKind.FilesInUse, SetupExitCodes.Failed)]
    [InlineData(SetupErrorKind.SetupFileDamaged, SetupExitCodes.Failed)]
    [InlineData(SetupErrorKind.AnotherSetupRunning, SetupExitCodes.Failed)]
    [InlineData(SetupErrorKind.Unexpected, SetupExitCodes.Failed)]
    public void Exit_codes_follow_the_result(SetupErrorKind? error, int expected)
    {
        var result = error is { } kind ? SetupResult.Failed(kind, "x") : FakeSetupEngine.Success(null, new Version(1, 1, 0));

        Assert.Equal(expected, SetupMode.ExitCodeFor(result));
    }

    [Fact]
    public async Task Quiet_setup_refuses_an_older_version_unless_allowed()
    {
        var engine = new FakeSetupEngine(Inspection("1.2.0"), _folder.Locations);

        Assert.Equal(SetupExitCodes.DowngradeRefused, await SetupRunner.RunQuietSetupAsync(engine, engine.Inspection, Args("--quiet"), _folder.Log));
        Assert.Empty(engine.Calls);

        Assert.Equal(SetupExitCodes.Ok, await SetupRunner.RunQuietSetupAsync(engine, engine.Inspection, Args("--quiet", "--allow-downgrade"), _folder.Log));
        Assert.True(Assert.Single(engine.Calls).AllowDowngrade);
    }

    [Fact]
    public async Task Quiet_setup_installs_with_quiet_options_and_returns_the_engine_code()
    {
        var engine = new FakeSetupEngine(Inspection("1.0.0", autostart: true, desktop: true, running: true), _folder.Locations);
        engine.Results.Enqueue(SetupResult.Failed(SetupErrorKind.AppStillRunning, "hängt"));

        Assert.Equal(SetupExitCodes.AppStillRunning, await SetupRunner.RunQuietSetupAsync(engine, engine.Inspection, Args("--quiet"), _folder.Log));
        Assert.Equal(new InstallOptions(true, true, true, false), Assert.Single(engine.Calls));
        Assert.Contains("AppStillRunning", _folder.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quiet_uninstall_in_the_copy_passes_the_data_choice_and_maps_the_result()
    {
        var engine = new FakeUninstallEngine(Uninstall(), _folder.Locations) { Result = SetupResult.Failed(SetupErrorKind.NotInstalled, "weg") };

        Assert.Equal(SetupExitCodes.NotInstalled, await SetupRunner.RunQuietUninstallAsync(engine, removeData: true, _folder.Log));
        Assert.True(Assert.Single(engine.Calls).RemoveData);

        engine.Result = null;
        Assert.Equal(SetupExitCodes.Ok, await SetupRunner.RunQuietUninstallAsync(engine, removeData: false, _folder.Log));
    }

    [Fact]
    public void Test_root_keeps_everything_under_the_test_folder()
    {
        var locations = SetupMode.LocationsFor(Args("--test-root", _folder.Root));

        Assert.True(locations.IsTest);
        Assert.StartsWith(_folder.Root, locations.InstallDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(_folder.Root, locations.DataPaths.ConfigDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(_folder.Root, "setup.log"), SetupLog.For(locations, _folder.Root).FilePath);
        Assert.False(SetupMode.LocationsFor(Args()).IsTest);
    }
}
