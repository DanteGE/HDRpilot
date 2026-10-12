using System.Globalization;
using System.Text.Json;
using HdrPilot.App.Resources;
using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using HdrPilot.App.Tests.Setup.UI;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>
/// Entfernen nimmt die Auto-HDR-Werte von HDRpilot zurück (E88): je Spiel und den globalen Schalter, mit denselben Regeln
/// wie die App, vor dem Löschen von Dateien und Daten. Registry nur als Attrappe (<see cref="SetupHarness.AutoHdr"/>).
/// </summary>
public sealed class SetupUninstallAutoHdrTests : IDisposable
{
    private const string ExeA = @"D:\Games\Spiel A\SpielA.exe";
    private const string ExeB = @"D:\Games\Spiel B\SpielB.exe";
    private const string Global = AutoHdrRegistryLocation.GlobalValueName;

    private readonly SetupHarness _h = new();

    public void Dispose() => _h.Dispose();

    private SetupLocations L => _h.Locations;

    [Fact]
    public async Task Without_auto_hdr_state_the_step_is_not_reported_and_nothing_is_read()
    {
        await InstallAsync();
        var progress = new SyncProgress<UninstallStep>();

        var result = await _h.UninstallAsync(progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain(UninstallStep.RestoreAutoHdr, progress.Items);
        Assert.Equal(0, _h.AutoHdr.Reads);
        Assert.False(result.Has(SetupWarning.AutoHdrNotRestored));
    }

    [Fact]
    public async Task Values_are_restored_before_files_and_data_are_removed_and_the_state_files_deleted()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(ExeA, "GpuPreference=2;AutoHDREnable=2097;");
        _h.AutoHdr.SetString(ExeB, "AutoHDREnable=2097;AppStatus=4;"); // Windows hat AppStatus ergänzt
        _h.AutoHdr.SetString(Global, "AutoHDREnable=1;SwapEffectUpgradeEnable=1;"); // Absturz der App mit gesetzter Notlösung
        WriteState(
            Entry(ExeA, "GpuPreference=2;", "GpuPreference=2;AutoHDREnable=2097;"),
            Entry(ExeB, null, "AutoHDREnable=2097;"));
        WriteMarker();
        var progress = new SyncProgress<UninstallStep>();

        var result = await _h.UninstallAsync(removeData: true, progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.Warnings);
        Assert.Equal("GpuPreference=2;", _h.AutoHdr.GetString(ExeA));
        Assert.Equal("AppStatus=4;", _h.AutoHdr.GetString(ExeB));
        Assert.Equal("AutoHDREnable=0;SwapEffectUpgradeEnable=1;", _h.AutoHdr.GetString(Global));
        Assert.False(File.Exists(L.DataPaths.AutoHdrStateFile));
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        var steps = progress.Items.ToList();
        Assert.True(steps.IndexOf(UninstallStep.RestoreAutoHdr) >= 0);
        Assert.True(steps.IndexOf(UninstallStep.RestoreAutoHdr) < steps.IndexOf(UninstallStep.RemoveFiles));
        Assert.True(steps.IndexOf(UninstallStep.RestoreAutoHdr) < steps.IndexOf(UninstallStep.RemoveData));
        Assert.Contains("Schritt: RestoreAutoHdr", _h.LogText, StringComparison.Ordinal);
        Assert.Contains(L.AutoHdrLocation.KeyName, _h.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Externally_changed_values_stay_and_the_state_files_still_go_without_remove_data()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(ExeA, "AutoHDREnable=2096;"); // Nutzer hat in Windows umgestellt
        WriteState(Entry(ExeA, null, "AutoHDREnable=2097;"));

        var result = await _h.UninstallAsync(removeData: false);

        Assert.True(result.Success, result.Message);
        Assert.False(result.Has(SetupWarning.AutoHdrNotRestored));
        Assert.Equal("AutoHDREnable=2096;", _h.AutoHdr.GetString(ExeA));
        Assert.Empty(_h.AutoHdr.Operations);
        Assert.False(File.Exists(L.DataPaths.AutoHdrStateFile));
        Assert.Contains("extern geändert", _h.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registry_failure_keeps_the_state_and_is_only_a_warning()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(ExeA, "AutoHDREnable=2097;");
        WriteState(Entry(ExeA, null, "AutoHDREnable=2097;"));
        _h.AutoHdr.FailWrites = new UnauthorizedAccessException("Zugriff verweigert");

        var result = await _h.UninstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(result.Has(SetupWarning.AutoHdrNotRestored));
        Assert.True(File.Exists(L.DataPaths.AutoHdrStateFile));
        Assert.Equal("AutoHDREnable=2097;", _h.AutoHdr.GetString(ExeA));
        Assert.False(File.Exists(L.ExePath)); // Entfernen lief trotzdem weiter
    }

    [Fact]
    public async Task State_for_another_key_is_skipped_and_kept()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(ExeA, "AutoHDREnable=2097;");
        WriteState(AutoHdrRegistryLocation.Real.KeyName, Entry(ExeA, null, "AutoHDREnable=2097;"));

        var result = await _h.UninstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(result.Has(SetupWarning.AutoHdrNotRestored));
        Assert.Equal(0, _h.AutoHdr.Reads);
        Assert.True(File.Exists(L.DataPaths.AutoHdrStateFile));
        Assert.Contains("Zustand gehört zu " + AutoHdrRegistryLocation.Real.KeyName, _h.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Real_install_restores_the_real_key_and_a_test_root_its_own()
    {
        // Gegenprobe zur Testablage: nur ohne --config-dir der echte Schlüssel (hier nur verglichen, nie geöffnet).
        Assert.True(L.AutoHdrLocation.IsTest);
        Assert.Equal(AutoHdrRegistryLocation.ForConfigDirectory(L.DataPaths.ConfigDirectory), L.AutoHdrLocation);
        Assert.Equal(L.AutoHdrLocation.KeyName, _h.AutoHdr.KeyName);
    }

    [Fact]
    public async Task Progress_marks_the_auto_hdr_step_and_appends_the_hint()
    {
        using var folder = new SetupTestFolder();
        var engine = new FakeUninstallEngine(SetupFixtures.Uninstall(), folder.Locations)
        {
            Report = [UninstallStep.RestoreAutoHdr, UninstallStep.RemoveFiles],
            Result = new SetupResult(true, null, null, new Version(1, 1, 0), null, false, Warnings: [SetupWarning.AutoHdrNotRestored]),
        };
        var model = new UninstallProgressViewModel(engine, removeData: false, @"C:\Users\Test\AppData\Roaming\HDRpilot", folder.Log);

        await model.RunAsync();

        Assert.True(model.Succeeded);
        Assert.Equal(StepState.Failed, model.StepList.StateOf(UninstallStep.RestoreAutoHdr));
        Assert.Equal(StepState.Done, model.StepList.StateOf(UninstallStep.RemoveFiles));
        Assert.NotEqual(StepState.Failed, model.StepList.StateOf(UninstallStep.RestoreHdr));
        var expected = SetupStrings.UninstallDone + " "
            + string.Format(CultureInfo.CurrentCulture, SetupStrings.UninstallDoneDataKept, @"C:\Users\Test\AppData\Roaming\HDRpilot")
            + " " + SetupStrings.UninstallAutoHdrNotRestored;
        Assert.Equal(expected, model.ResultText);
    }

    private async Task InstallAsync()
    {
        var result = await _h.InstallAsync("1.1.0", new InstallOptions(Autostart: false, DesktopShortcut: false, LaunchAfterInstall: false));
        Assert.True(result.Success, result.Message);
    }

    private void WriteState(params AutoHdrStateEntry[] entries) => WriteState(L.AutoHdrLocation.KeyName, entries);

    private void WriteState(string key, params AutoHdrStateEntry[] entries)
    {
        Directory.CreateDirectory(L.DataPaths.DataDirectory);
        File.WriteAllText(L.DataPaths.AutoHdrStateFile, JsonSerializer.Serialize(new AutoHdrStateData { RegistryKey = key, Entries = [.. entries] }, ConfigJson.Options));
    }

    private void WriteMarker()
    {
        Directory.CreateDirectory(L.DataPaths.DataDirectory);
        var marker = new AutoHdrGlobalMarker
        {
            RegistryKey = L.AutoHdrLocation.KeyName,
            Status = AutoHdrEntryStatus.Applied,
            Original = new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.String, Text = "AutoHDREnable=0;SwapEffectUpgradeEnable=1;" },
            OriginalToken = "0",
            Written = "AutoHDREnable=1;SwapEffectUpgradeEnable=1;",
            Games = ["Steam:1"],
        };
        File.WriteAllText(L.DataPaths.AutoHdrGlobalFile, JsonSerializer.Serialize(marker, ConfigJson.Options));
    }

    private static AutoHdrStateEntry Entry(string exe, string? original, string written) => new()
    {
        ValueName = exe,
        Games = ["Steam:1"],
        Status = AutoHdrEntryStatus.Applied,
        Original = original is null
            ? new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.Missing }
            : new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.String, Text = original },
        Written = written,
        WindowsBuild = 26300,
    };
}
