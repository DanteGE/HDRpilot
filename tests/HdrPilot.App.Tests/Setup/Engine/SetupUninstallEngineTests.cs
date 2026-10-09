using HdrPilot.App.Services;
using HdrPilot.App.Setup;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Sessions;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>Entfernen in einer Testablage. HDR wird nur über eine Attrappe „geschaltet“, nie am echten Bildschirm.</summary>
public sealed class SetupUninstallEngineTests : IDisposable
{
    private readonly SetupHarness _h = new();

    public void Dispose() => _h.Dispose();

    private SetupLocations L => _h.Locations;

    private string? Autostart => _h.Registry.Get(RegistryHiveKind.CurrentUser, L.RunKeyPath, AutostartService.ValueName) as string;

    [Fact]
    public async Task Uninstall_removes_only_own_files_and_keeps_unknown_files_and_folders()
    {
        await InstallAsync(desktop: false);
        File.WriteAllText(_h.InstalledFile("HDRpilot.exe.old"), "Rest");
        File.WriteAllText(_h.InstalledFile("notizen.txt"), "vom Nutzer");
        Directory.CreateDirectory(_h.InstalledFile("Mods"));
        File.WriteAllText(_h.InstalledFile(@"Mods\mod.txt"), "vom Nutzer");
        var progress = new SyncProgress<UninstallStep>();

        var result = await _h.UninstallAsync(progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new Version(1, 1, 0), result.PreviousVersion);
        foreach (var file in SetupLocations.InstalledFiles)
        {
            Assert.False(File.Exists(_h.InstalledFile(file)), file);
        }

        Assert.False(File.Exists(_h.InstalledFile("HDRpilot.exe.old")));
        Assert.False(Directory.Exists(_h.InstalledFile("cli")));
        Assert.True(File.Exists(_h.InstalledFile("notizen.txt")));
        Assert.True(File.Exists(_h.InstalledFile(@"Mods\mod.txt")));
        Assert.Null(_h.Entries.Entry);
        Assert.Equal([UninstallStep.RemoveFiles, UninstallStep.RemoveShortcuts, UninstallStep.RemoveAutostart, UninstallStep.Unregister], progress.Items);
    }

    [Fact]
    public async Task Uninstall_without_foreign_files_removes_the_program_folder()
    {
        await InstallAsync(desktop: false);

        Assert.True((await _h.UninstallAsync()).Success);

        Assert.False(Directory.Exists(L.InstallDirectory));
        Assert.True(Directory.Exists(Path.GetDirectoryName(L.InstallDirectory)));
    }

    [Fact]
    public async Task Uninstall_removes_own_and_game_shortcuts_but_not_foreign_ones()
    {
        await InstallAsync(desktop: true);
        var foreign = _h.ForeignExe();
        var gameInMenu = Path.Combine(L.GameShortcutsDirectory, "Cyberpunk 2077 (HDR).lnk");
        var gameOnDesktop = Path.Combine(L.DesktopDirectory, "Cyberpunk 2077 (HDR).lnk");
        var foreignInMenu = Path.Combine(L.GameShortcutsDirectory, "Fremd.lnk");
        var foreignOnDesktop = Path.Combine(L.DesktopDirectory, "Anderes Programm.lnk");
        Directory.CreateDirectory(L.GameShortcutsDirectory);
        WriteLink(gameInMenu, L.ExePath, "--run \"Gog:1423049311\"");
        WriteLink(gameOnDesktop, L.ExePath, "--run \"Gog:1423049311\"");
        WriteLink(foreignInMenu, foreign, string.Empty);
        WriteLink(foreignOnDesktop, foreign, string.Empty);

        var result = await _h.UninstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(L.StartMenuShortcut));
        Assert.False(File.Exists(L.DesktopShortcut));
        Assert.False(File.Exists(gameInMenu));
        Assert.False(File.Exists(gameOnDesktop));
        Assert.True(File.Exists(foreignInMenu));
        Assert.True(File.Exists(foreignOnDesktop));
        Assert.True(Directory.Exists(L.GameShortcutsDirectory)); // enthält noch eine fremde Verknüpfung
    }

    [Fact]
    public async Task Empty_game_shortcut_folder_is_removed_and_a_foreign_start_menu_shortcut_stays()
    {
        await InstallAsync(desktop: false);
        Directory.CreateDirectory(L.GameShortcutsDirectory);
        WriteLink(Path.Combine(L.GameShortcutsDirectory, "Spiel (HDR).lnk"), L.ExePath, "--run \"Steam:1\"");
        var foreign = _h.ForeignExe();
        WriteLink(L.StartMenuShortcut, foreign, string.Empty);

        Assert.True((await _h.UninstallAsync()).Success);

        Assert.False(Directory.Exists(L.GameShortcutsDirectory));
        Assert.Equal(foreign, ShellSetupShortcuts.Instance.ReadTarget(L.StartMenuShortcut), ignoreCase: true);
    }

    [Fact]
    public async Task Settings_and_data_are_kept_by_default()
    {
        await InstallAsync(desktop: false);
        var config = WriteData();

        var result = await _h.UninstallAsync(removeData: false);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(config));
        Assert.True(File.Exists(Path.Combine(L.DataPaths.LogDirectory, "hdrpilot.log")));
    }

    [Fact]
    public async Task Settings_and_data_are_removed_on_request_but_never_their_parent()
    {
        await InstallAsync(desktop: false);
        WriteData();
        var progress = new SyncProgress<UninstallStep>();

        var result = await _h.UninstallAsync(removeData: true, progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.False(Directory.Exists(L.DataPaths.ConfigDirectory));
        Assert.False(Directory.Exists(L.DataPaths.DataDirectory));
        Assert.True(Directory.Exists(_h.Root));
        Assert.True(File.Exists(_h.Log.FilePath));
        Assert.Equal(UninstallStep.RemoveData, progress.Items[^1]);
    }

    [Fact]
    public async Task Saved_hdr_state_is_restored_through_the_restorer_and_then_deleted()
    {
        await InstallAsync(desktop: false);
        SaveSessionState(
            new DisplayClaimState { MonitorDevicePath = @"\\?\DISPLAY#AAA", FriendlyName = "Monitor A", WasHdrOn = false, Restore = true, Games = ["Steam:1"] },
            new DisplayClaimState { MonitorDevicePath = @"\\?\DISPLAY#BBB", FriendlyName = "Monitor B", WasHdrOn = false, Restore = false, Games = ["Steam:2"] });
        var progress = new SyncProgress<UninstallStep>();

        var result = await _h.UninstallAsync(progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.Equal([(@"\\?\DISPLAY#AAA", false)], _h.Hdr.Calls);
        Assert.False(File.Exists(L.DataPaths.SessionStateFile));
        Assert.Equal(UninstallStep.RestoreHdr, progress.Items[0]);
    }

    [Fact]
    public async Task Failed_hdr_restore_keeps_the_state_file_and_still_uninstalls()
    {
        await InstallAsync(desktop: false);
        SaveSessionState(new DisplayClaimState { MonitorDevicePath = @"\\?\DISPLAY#AAA", FriendlyName = "Monitor A", WasHdrOn = true, Restore = true });
        _h.Hdr.Status = HdrSetStatus.NotConnected;

        var result = await _h.UninstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal([(@"\\?\DISPLAY#AAA", true)], _h.Hdr.Calls);
        Assert.True(File.Exists(L.DataPaths.SessionStateFile));
        Assert.False(File.Exists(L.ExePath));
    }

    [Fact]
    public async Task Nothing_installed_is_reported_as_not_installed()
    {
        var result = await _h.UninstallAsync();

        Assert.False(result.Success);
        Assert.Equal(SetupErrorKind.NotInstalled, result.Error);
        Assert.Equal(0, _h.App.StopCalls);
    }

    [Fact]
    public async Task Entry_without_program_files_is_cleaned_up()
    {
        await InstallAsync(desktop: false);
        File.Delete(L.ExePath);

        var result = await _h.UninstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.Null(result.PreviousVersion);
        Assert.Null(_h.Entries.Entry);
    }

    [Fact]
    public async Task Own_autostart_is_removed_and_a_foreign_one_stays()
    {
        await InstallAsync(desktop: false);
        Assert.NotNull(Autostart);
        Assert.True((await _h.UninstallAsync()).Success);
        Assert.Null(Autostart);

        await InstallAsync(desktop: false);
        const string Foreign = "\"D:\\Portable\\HDRpilot.exe\" --minimized";
        _h.Registry.Set(RegistryHiveKind.CurrentUser, L.RunKeyPath, AutostartService.ValueName, Foreign);
        Assert.True((await _h.UninstallAsync()).Success);
        Assert.Equal(Foreign, Autostart);
    }

    [Fact]
    public async Task Running_app_is_stopped_first()
    {
        await InstallAsync(desktop: false);
        _h.App.Running = true;
        var progress = new SyncProgress<UninstallStep>();

        var result = await _h.UninstallAsync(progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, _h.App.StopCalls);
        Assert.Equal(UninstallStep.StopRunningApp, progress.Items[0]);
        Assert.Empty(_h.App.Started);
    }

    [Fact]
    public async Task App_that_does_not_exit_leaves_everything_in_place()
    {
        await InstallAsync(desktop: true);
        _h.App.Running = true;
        _h.App.StopSucceeds = false;

        var result = await _h.UninstallAsync(removeData: true);

        Assert.Equal(SetupErrorKind.AppStillRunning, result.Error);
        Assert.True(File.Exists(L.ExePath));
        Assert.True(File.Exists(L.StartMenuShortcut));
        Assert.True(File.Exists(L.DesktopShortcut));
        Assert.NotNull(Autostart);
        Assert.NotNull(_h.Entries.Entry);
    }

    [Fact]
    public async Task Locked_exe_is_reported_and_the_entry_stays_for_another_try()
    {
        await InstallAsync(desktop: false);
        var files = new FailingFileOperations((op, path) =>
            op == "Delete" && path.Equals(L.ExePath, StringComparison.OrdinalIgnoreCase) ? new UnauthorizedAccessException("Zugriff verweigert") : null);

        var result = await _h.UninstallAsync(files: files);

        Assert.Equal(SetupErrorKind.FilesInUse, result.Error);
        Assert.True(File.Exists(L.ExePath));
        Assert.False(File.Exists(_h.InstalledFile("README.md")));
        Assert.False(File.Exists(L.StartMenuShortcut));
        Assert.NotNull(_h.Entries.Entry);
    }

    [Fact]
    public async Task Failure_in_the_registry_is_not_reported_as_a_program_folder_problem()
    {
        await InstallAsync(desktop: false);
        _h.Entries.FailOnDelete = new UnauthorizedAccessException("Registry gesperrt");

        var result = await _h.UninstallAsync();

        Assert.Equal(SetupErrorKind.Unexpected, result.Error);
        Assert.StartsWith("Eintrag entfernen: ", result.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(L.ExePath));
    }

    [Fact]
    public async Task Shortcut_in_use_is_not_reported_as_a_file_in_the_program_folder()
    {
        await InstallAsync(desktop: false);
        var files = new FailingFileOperations((op, path) =>
            op == "Delete" && path.Equals(L.StartMenuShortcut, StringComparison.OrdinalIgnoreCase) ? FailingFileOperations.SharingViolation() : null);

        var result = await _h.UninstallAsync(files: files);

        Assert.Equal(SetupErrorKind.Unexpected, result.Error);
        Assert.Contains(L.StartMenuShortcut, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(L.ExePath));
    }

    [Fact]
    public async Task Second_uninstall_at_the_same_time_is_refused()
    {
        await InstallAsync(desktop: false);
        using var other = new HeldSetupLock(SetupSupport.MutexNameFor(L));

        var result = await _h.UninstallAsync();

        Assert.Equal(SetupErrorKind.AnotherSetupRunning, result.Error);
        Assert.True(File.Exists(L.ExePath));
    }

    [Fact]
    public async Task Inspect_reports_version_steam_games_data_folders_and_running_app()
    {
        await InstallAsync(desktop: false);
        _h.Steam.Games = ["Cyberpunk 2077", "Steam-App 1245620"];
        _h.App.Running = true;

        var inspection = _h.Uninstall().Inspect();

        Assert.Equal(new Version(1, 1, 0), inspection.InstalledVersion);
        Assert.Equal(L.InstallDirectory, inspection.InstallDirectory);
        Assert.Equal(["Cyberpunk 2077", "Steam-App 1245620"], inspection.SteamGamesWithLaunchOption);
        Assert.Equal(L.DataPaths.ConfigDirectory, inspection.ConfigDirectory);
        Assert.Equal(L.DataPaths.DataDirectory, inspection.DataDirectory);
        Assert.True(inspection.AppRunning);
    }

    private async Task InstallAsync(bool desktop)
    {
        var result = await _h.InstallAsync("1.1.0", new InstallOptions(Autostart: true, DesktopShortcut: desktop, LaunchAfterInstall: false));
        Assert.True(result.Success, result.Message);
    }

    private string WriteData()
    {
        Directory.CreateDirectory(L.DataPaths.LogDirectory);
        var config = L.DataPaths.ConfigFile;
        File.WriteAllText(config, "{}");
        File.WriteAllText(Path.Combine(L.DataPaths.LogDirectory, "hdrpilot.log"), "Protokoll");
        return config;
    }

    private void SaveSessionState(params DisplayClaimState[] claims)
    {
        Directory.CreateDirectory(L.DataPaths.DataDirectory);
        new FileSessionStateStore(PhysicalFileSystem.Instance, L.DataPaths.SessionStateFile).Save(new SessionStateData { Displays = [.. claims] });
    }

    private static void WriteLink(string path, string target, string arguments) =>
        ShellSetupShortcuts.Instance.Write(path, target, arguments, Path.GetDirectoryName(target)!, "Test");
}
