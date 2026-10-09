using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using HdrPilot.App.Tests.Setup.Engine;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Platform;
using static HdrPilot.App.Tests.Setup.UI.SetupFixtures;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>
/// Setup und Entfernen im Container einer anderen App (E84): Vor jeder Änderung wird geprüft, ob der Lauf darin steckt;
/// dann ändert er nichts, sagt im Fenster warum und endet still mit <see cref="SetupExitCodes.Container"/>.
/// Prüfläufe (<c>--test-root</c>) prüfen nur, wenn ein Prüfskript es verlangt. Mit Attrappen der Umgebung und, als
/// Gegenstück, mit den echten Engines in einer Testablage (Registry als Attrappe).
/// </summary>
public sealed class SetupContainerTests : IDisposable
{
    private const string Package = "Claude_pzs8sxrjxfjjc";
    private readonly SetupTestFolder _folder = new();
    private readonly SetupLocations _normal;
    private readonly FakeSetupEngine _setup;
    private readonly FakeUninstallEngine _uninstall;
    private readonly FakeSetupHost _host;

    public SetupContainerTests()
    {
        // Normale Orte (kein Prüflauf), nur eben im Testordner: so läuft die Prüfung wie beim Nutzer.
        var root = _folder.Root;
        _normal = new SetupLocations(
            Path.Combine(root, "Local", "Programs", "HDRpilot"),
            Path.Combine(root, "StartMenu", "Programs"),
            Path.Combine(root, "Desktop"),
            @"Software\HDRpilot.Tests\Setup\Container",
            new AppPaths(Path.Combine(root, "Roaming", "HDRpilot"), Path.Combine(root, "Local", "HDRpilot")),
            null);
        Assert.False(_normal.IsTest);
        _setup = new FakeSetupEngine(Inspection(installed: "1.0.0"), _normal);
        _uninstall = new FakeUninstallEngine(Uninstall(), _normal);
        _host = new FakeSetupHost(_setup, _uninstall);
    }

    public void Dispose() => _folder.Dispose();

    private const string ContainerCheck = "DetectContainer";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_in_a_container_changes_nothing_and_says_why(bool quiet)
    {
        _host.ContainerPackage = Package;

        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.Setup, quiet ? ["--quiet"] : []), _normal, _folder.Log);

        Assert.Equal(SetupExitCodes.Container, code);
        Assert.Equal(7, code);
        string[] expected = quiet ? [ContainerCheck] : [ContainerCheck, "ShowError"];
        Assert.Equal(expected, _host.Calls); // keine Bestandsaufnahme, kein Setup
        Assert.Empty(_setup.Calls);
        if (!quiet)
        {
            var text = Assert.Single(_host.Errors);
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, SetupStrings.SetupErrorContainer, Package), text);
            Assert.Contains("(" + Package + ")", text, StringComparison.Ordinal);
            Assert.Contains("nichts geändert", text, StringComparison.Ordinal);
            Assert.Contains("Doppelklick im Explorer", text, StringComparison.Ordinal);
        }

        Assert.Contains("Läuft im Container der App " + Package, _folder.LogText, StringComparison.Ordinal);
        Assert.Contains("Nichts geändert, Exit-Code 7", _folder.LogText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uninstall_in_a_container_hands_nothing_off(bool quiet)
    {
        _host.ContainerPackage = Package;
        _host.ConfirmRemoveData = true;

        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.Uninstall, quiet ? ["--quiet", "--remove-data"] : []), _normal, _folder.Log);

        Assert.Equal(SetupExitCodes.Container, code);
        string[] expected = quiet ? [ContainerCheck] : [ContainerCheck, "ShowError"];
        Assert.Equal(expected, _host.Calls); // keine Rückfrage, keine Kopie
        Assert.Empty(_host.HandOffs);
        Assert.Empty(_uninstall.Calls);
        if (!quiet)
        {
            Assert.Equal(SetupText.Container(SetupModeKind.Uninstall, Package), Assert.Single(_host.Errors));
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, SetupStrings.UninstallErrorContainer, Package), Assert.Single(_host.Errors));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uninstall_copy_in_a_container_removes_nothing_but_cleans_up(bool quiet)
    {
        _host.ContainerPackage = Package;

        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.UninstallContinue, quiet ? ["--quiet", "--remove-data"] : ["--remove-data"]), _normal, _folder.Log);

        Assert.Equal(SetupExitCodes.Container, code);
        string[] expected = quiet ? [ContainerCheck, "ScheduleSelfRemoval"] : [ContainerCheck, "ShowError", "ScheduleSelfRemoval"];
        Assert.Equal(expected, _host.Calls); // ohne Warten und Entfernen
        Assert.Empty(_uninstall.Calls);
        if (!quiet)
        {
            Assert.Contains("Installierte Apps", Assert.Single(_host.Errors), StringComparison.Ordinal);
        }

        var lines = _folder.LogLines;
        Assert.EndsWith("ERGEBNIS: 7", lines[^1], StringComparison.Ordinal);
        Assert.EndsWith(FakeSetupHost.RemovalMarker, lines[^2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Probe schreiben oder suchen: IOException: Datenträger voll")]
    public async Task Without_a_certain_container_the_setup_runs_as_usual(string? problem)
    {
        _host.ContainerProblem = problem;

        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.Setup, ["--quiet", "--no-launch"]), _normal, _folder.Log);

        Assert.Equal(SetupExitCodes.Ok, code);
        Assert.Equal([ContainerCheck, "CreateSetup"], _host.Calls);
        Assert.Single(_setup.Calls);
        Assert.Contains("Container-Prüfung: kein Container erkannt (Probe in " + FakeSetupHost.ContainerProbeFolder + " nicht umgeleitet).", _folder.LogText, StringComparison.Ordinal);
        if (problem is not null)
        {
            Assert.Contains("Container-Prüfung mit Probe in " + FakeSetupHost.ContainerProbeFolder + ": " + problem, _folder.LogText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Test_root_skips_the_check_unless_a_gate_script_forces_it()
    {
        _host.ContainerPackage = Package;
        var mode = Mode(SetupModeKind.Setup, ["--quiet", "--no-launch", "--test-root", _folder.Root]);

        var skipped = await SetupRunner.RunAsync(_host, mode, _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Ok, skipped);
        Assert.Equal(["CreateSetup"], _host.Calls);
        Assert.Contains("Container-Prüfung im Prüflauf übersprungen", _folder.LogText, StringComparison.Ordinal);

        _host.Calls.Clear();
        _host.ContainerCheckForced = true;
        var forced = await SetupRunner.RunAsync(_host, mode, _folder.Locations, _folder.Log);

        Assert.Equal(SetupExitCodes.Container, forced);
        Assert.Equal([ContainerCheck], _host.Calls);
        Assert.Single(_setup.Calls); // nur der erste Lauf
    }

    [Fact]
    public async Task Refused_command_line_still_wins_over_the_container()
    {
        _host.ContainerPackage = Package;

        var code = await SetupRunner.RunAsync(_host, Mode(SetupModeKind.Setup, ["--quiet", "--bogus"]), _normal, _folder.Log);

        Assert.Equal(SetupExitCodes.BadArguments, code);
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public void Real_host_forces_the_check_only_with_the_variable_set_to_one()
    {
        var host = new WpfSetupHost(null!); // die Anwendung braucht nur ein Fenster
        var before = Environment.GetEnvironmentVariable(SetupRunner.ForceContainerCheckVariable);
        try
        {
            Environment.SetEnvironmentVariable(SetupRunner.ForceContainerCheckVariable, null);
            Assert.False(host.ContainerCheckForced);
            Environment.SetEnvironmentVariable(SetupRunner.ForceContainerCheckVariable, "0");
            Assert.False(host.ContainerCheckForced);
            Environment.SetEnvironmentVariable(SetupRunner.ForceContainerCheckVariable, "1");
            Assert.True(host.ContainerCheckForced);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SetupRunner.ForceContainerCheckVariable, before);
        }

        Assert.Equal("HDRPILOT_TEST_CONTAINER_CHECK", SetupRunner.ForceContainerCheckVariable);
    }

    [Fact]
    public async Task Real_engines_install_and_remove_nothing_in_a_container()
    {
        using var harness = new SetupHarness();
        var host = new HarnessHost(harness) { ContainerPackage = Package };
        var bundle = harness.Bundle("1.1.0");
        Assert.True(SetupMode.TryDetect(bundle.FilePath, ["--quiet", "--no-launch", "--test-root", harness.Root], out var setup));

        Assert.Equal(SetupExitCodes.Container, await SetupRunner.RunAsync(host, setup, harness.Locations, harness.Log));

        Assert.False(Directory.Exists(harness.Locations.InstallDirectory));
        Assert.False(File.Exists(harness.Locations.StartMenuShortcut));
        Assert.Null(harness.Entries.Entry);
        Assert.Null(harness.Registry.OpenKey(RegistryHiveKind.CurrentUser, "Software"));
        Assert.Empty(host.Created);

        // Gegenstück ohne Container: dieselbe Befehlszeile installiert wirklich.
        host.ContainerPackage = null;
        Assert.Equal(SetupExitCodes.Ok, await SetupRunner.RunAsync(host, setup, harness.Locations, harness.Log));
        Assert.True(File.Exists(harness.Locations.ExePath));
        Assert.NotNull(harness.Entries.Entry);

        // Die Kopie zum Entfernen im Container lässt die Installation stehen.
        host.ContainerPackage = Package;
        var exe = Path.Combine(harness.Root, "HDRpilot-Uninstall.exe");
        File.Copy(harness.Locations.ExePath, exe);
        Assert.True(SetupMode.TryDetect(exe, ["--uninstall-continue", "--wait-pid", "77", "--quiet", "--remove-data", "--test-root", harness.Root], out var continueMode));
        Assert.Equal(SetupExitCodes.Container, await SetupRunner.RunAsync(host, continueMode, harness.Locations, harness.Log));
        Assert.True(File.Exists(harness.Locations.ExePath));
        Assert.NotNull(harness.Entries.Entry);
        Assert.True(File.Exists(harness.Locations.StartMenuShortcut));
        Assert.Equal(["CreateSetup"], host.Created); // nur das Gegenstück
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

    /// <summary>
    /// Umgebung mit den echten Engines der Testablage (Registry und Eintrag als Attrappe, Dateien und Verknüpfungen im
    /// Temp-Ordner); die Container-Prüfung ist verlangt wie in einem Prüfskript und liefert <see cref="ContainerPackage"/>.
    /// Nur stille Läufe: Fenster gibt es hier nicht.
    /// </summary>
    private sealed class HarnessHost(SetupHarness harness) : ISetupHost
    {
        public string? ContainerPackage { get; set; }

        public List<string> Created { get; } = [];

        public int ProcessId => 4242;

        public bool ContainerCheckForced => true;

        public ContainerRedirection DetectContainer() => new(FakeSetupHost.ContainerProbeFolder, ContainerPackage);

        public ISetupEngine CreateSetup(SetupLocations locations, SetupBundle bundle, SetupLog log)
        {
            Created.Add("CreateSetup");
            return harness.Setup(bundle, "1.1.0");
        }

        public IUninstallEngine CreateUninstall(SetupLocations locations, SetupLog log)
        {
            Created.Add("CreateUninstall");
            return harness.Uninstall();
        }

        public Task<int> ShowSetupAsync(ISetupEngine engine, SetupInspection inspection, SetupArguments arguments, SetupLog log) => throw new NotSupportedException();

        public Task<int> ShowUninstallAsync(UninstallInspection inspection, SetupArguments arguments, Func<bool, Task> handOff, SetupLog log) => throw new NotSupportedException();

        public Task<int> ShowUninstallProgressAsync(IUninstallEngine engine, bool removeData, string configDirectory, SetupLog log) => throw new NotSupportedException();

        public void ShowError(string title, string text) => throw new NotSupportedException();

        public Task HandOffAsync(IReadOnlyList<string> arguments, SetupLog log) => throw new NotSupportedException();

        public Task WaitUntilGoneAsync(int processId, SetupLog log) => Task.CompletedTask;

        public void ScheduleSelfRemoval(SetupLog log)
        {
        }
    }
}
