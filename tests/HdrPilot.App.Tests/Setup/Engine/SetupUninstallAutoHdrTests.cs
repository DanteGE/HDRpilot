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
/// Entfernen stellt einen globalen Auto-HDR-Schalter zurück, den ein Absturz von HDRpilot gesetzt hinterließ (E88, G9.4):
/// mit derselben Regel wie die App (eine Änderung durch den Nutzer bleibt), vor dem Löschen von Dateien und Daten. Werte je
/// Spiel-EXE gibt es nicht. Registry nur als Attrappe (<see cref="SetupHarness.AutoHdr"/>).
/// </summary>
public sealed class SetupUninstallAutoHdrTests : IDisposable
{
    private const string Global = AutoHdrRegistryLocation.GlobalValueName;
    private const string GlobalOff = "AutoHDREnable=0;SwapEffectUpgradeEnable=1;";
    private const string GlobalOn = "AutoHDREnable=1;SwapEffectUpgradeEnable=1;";
    private static readonly DateTimeOffset SetAt = new(2026, 10, 12, 20, 15, 30, 123, TimeSpan.FromHours(2));

    private readonly SetupHarness _h = new();

    public void Dispose() => _h.Dispose();

    private SetupLocations L => _h.Locations;

    [Fact]
    public async Task Without_a_marker_the_step_is_not_reported_and_nothing_is_read()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, GlobalOn); // vom Nutzer an: kein Merker, also nicht von HDRpilot
        var progress = new SyncProgress<UninstallStep>();

        var result = await _h.UninstallAsync(progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain(UninstallStep.RestoreAutoHdr, progress.Items);
        Assert.Equal(0, _h.AutoHdr.Reads);
        Assert.Equal(GlobalOn, _h.AutoHdr.GetString(Global));
        Assert.False(result.Has(SetupWarning.AutoHdrNotRestored));
    }

    [Fact]
    public async Task Switch_left_on_by_a_crash_is_restored_before_files_and_data_are_removed()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, GlobalOn);
        WriteMarker();
        var progress = new SyncProgress<UninstallStep>();

        var result = await _h.UninstallAsync(removeData: true, progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.Warnings);
        Assert.Equal(GlobalOff, _h.AutoHdr.GetString(Global));
        Assert.Equal(["set " + Global + "=" + GlobalOff], _h.AutoHdr.Operations);
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        var steps = progress.Items.ToList();
        Assert.True(steps.IndexOf(UninstallStep.RestoreAutoHdr) >= 0);
        Assert.True(steps.IndexOf(UninstallStep.RestoreAutoHdr) < steps.IndexOf(UninstallStep.RemoveFiles));
        Assert.True(steps.IndexOf(UninstallStep.RestoreAutoHdr) < steps.IndexOf(UninstallStep.RemoveData));
        Assert.Contains("Schritt: RestoreAutoHdr", _h.LogText, StringComparison.Ordinal);
        Assert.Contains(L.AutoHdrLocation.KeyName, _h.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_original_is_deleted_again_and_the_marker_goes_without_remove_data()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, "AutoHDREnable=1;");
        WriteMarker(original: null, originalToken: null, written: "AutoHDREnable=1;");

        var result = await _h.UninstallAsync(removeData: false);

        Assert.True(result.Success, result.Message);
        Assert.Null(_h.AutoHdr.GetString(Global));
        Assert.Equal(["delete " + Global], _h.AutoHdr.Operations);
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        Assert.True(Directory.Exists(L.DataPaths.DataDirectory)); // ohne „Daten löschen“ bleibt die Ablage
    }

    [Fact]
    public async Task Users_change_stays_and_the_marker_still_goes()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, "AutoHDREnable=0;SwapEffectUpgradeEnable=0;"); // Nutzer hat in Windows umgestellt
        WriteMarker();

        var result = await _h.UninstallAsync(removeData: false);

        Assert.True(result.Success, result.Message);
        Assert.False(result.Has(SetupWarning.AutoHdrNotRestored));
        Assert.Equal("AutoHDREnable=0;SwapEffectUpgradeEnable=0;", _h.AutoHdr.GetString(Global));
        Assert.Empty(_h.AutoHdr.Operations);
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        Assert.Contains("extern geändert", _h.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Released_marker_is_only_deleted_and_never_restored()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, GlobalOn); // die Wahl des Nutzers; die App konnte den erledigten Merker nicht löschen
        WriteMarker(status: AutoHdrEntryStatus.Released);

        var result = await _h.UninstallAsync(removeData: false);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.Warnings);
        Assert.Equal(GlobalOn, _h.AutoHdr.GetString(Global));
        Assert.Empty(_h.AutoHdr.Operations);
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        Assert.Contains("ist erledigt", _h.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Marker_with_a_matching_done_mark_is_only_deleted_and_never_restored()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, GlobalOn); // die Wahl des Nutzers; die App konnte den gesperrten Merker nicht löschen
        WriteMarker(setAt: SetAt);
        WriteDone(SetAt, GlobalOn);

        var result = await _h.UninstallAsync(removeData: false);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.Warnings);
        Assert.Equal(GlobalOn, _h.AutoHdr.GetString(Global));
        Assert.Empty(_h.AutoHdr.Operations);
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalDoneFile));
        Assert.Contains("ist erledigt", _h.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Done_mark_of_an_earlier_marker_does_not_stop_the_restore()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, GlobalOn);
        WriteMarker(setAt: SetAt);
        WriteDone(SetAt.AddMinutes(-5), GlobalOn); // Rest eines früheren Setzens mit demselben Text

        var result = await _h.UninstallAsync(removeData: false);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.Warnings);
        Assert.Equal(GlobalOff, _h.AutoHdr.GetString(Global));
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        Assert.False(File.Exists(L.DataPaths.AutoHdrGlobalDoneFile));
    }

    [Fact]
    public async Task Defective_done_mark_keeps_everything_and_is_only_a_warning()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, GlobalOn);
        WriteMarker(setAt: SetAt);
        File.WriteAllText(L.DataPaths.AutoHdrGlobalDoneFile, "{ kaputt");

        var result = await _h.UninstallAsync(removeData: false);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Has(SetupWarning.AutoHdrNotRestored));
        Assert.Equal(GlobalOn, _h.AutoHdr.GetString(Global));
        Assert.Empty(_h.AutoHdr.Operations);
        Assert.True(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        Assert.True(File.Exists(L.DataPaths.AutoHdrGlobalDoneFile));
    }

    [Fact]
    public async Task Registry_failure_keeps_the_marker_and_is_only_a_warning()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, GlobalOn);
        WriteMarker();
        _h.AutoHdr.FailWrites = new UnauthorizedAccessException("Zugriff verweigert");

        var result = await _h.UninstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(result.Has(SetupWarning.AutoHdrNotRestored));
        Assert.True(File.Exists(L.DataPaths.AutoHdrGlobalFile));
        Assert.Equal(GlobalOn, _h.AutoHdr.GetString(Global));
        Assert.False(File.Exists(L.ExePath)); // Entfernen lief trotzdem weiter
        Assert.Contains("der globale Auto-HDR-Schalter ist nicht zurückgestellt", _h.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("Auto-HDR-Werte", _h.LogText, StringComparison.Ordinal); // es gibt nur den einen Schalter
    }

    [Fact]
    public async Task Marker_for_another_key_is_skipped_and_kept()
    {
        await InstallAsync();
        _h.AutoHdr.SetString(Global, GlobalOn);
        WriteMarker(key: AutoHdrRegistryLocation.Real.KeyName);

        var result = await _h.UninstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(result.Has(SetupWarning.AutoHdrNotRestored));
        Assert.Equal(0, _h.AutoHdr.Reads);
        Assert.True(File.Exists(L.DataPaths.AutoHdrGlobalFile));
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

    private void WriteDone(DateTimeOffset setAt, string written)
    {
        Directory.CreateDirectory(L.DataPaths.DataDirectory);
        var done = new AutoHdrGlobalDone { SetAt = setAt, Written = written };
        File.WriteAllText(L.DataPaths.AutoHdrGlobalDoneFile, JsonSerializer.Serialize(done, ConfigJson.Options));
    }

    private void WriteMarker(
        string? key = null,
        string? original = GlobalOff,
        string? originalToken = "0",
        string written = GlobalOn,
        AutoHdrEntryStatus status = AutoHdrEntryStatus.Applied,
        DateTimeOffset setAt = default)
    {
        Directory.CreateDirectory(L.DataPaths.DataDirectory);
        var marker = new AutoHdrGlobalMarker
        {
            RegistryKey = key ?? L.AutoHdrLocation.KeyName,
            Status = status,
            Original = original is null
                ? new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.Missing }
                : new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.String, Text = original },
            OriginalToken = originalToken,
            Written = written,
            Games = ["Steam:1"],
            WindowsBuild = 26300,
            SetAt = setAt,
        };
        File.WriteAllText(L.DataPaths.AutoHdrGlobalFile, JsonSerializer.Serialize(marker, ConfigJson.Options));
    }
}
