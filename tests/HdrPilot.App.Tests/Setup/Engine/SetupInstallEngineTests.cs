using System.Security.Cryptography;
using HdrPilot.App.Services;
using HdrPilot.App.Setup;
using HdrPilot.Core.Abstractions;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>Installation und Aktualisierung in einer Testablage (Temp-Ordner, Registry-Attrappe, App-Attrappe).</summary>
public sealed class SetupInstallEngineTests : IDisposable
{
    private static readonly InstallOptions Quiet = new(Autostart: true, DesktopShortcut: false, LaunchAfterInstall: false);

    private readonly SetupHarness _h = new();

    public void Dispose() => _h.Dispose();

    private SetupLocations L => _h.Locations;

    private string? Autostart => _h.Registry.Get(RegistryHiveKind.CurrentUser, L.RunKeyPath, AutostartService.ValueName) as string;

    [Fact]
    public async Task Fresh_install_copies_files_creates_shortcut_autostart_and_entry_and_opens_the_window()
    {
        var progress = new SyncProgress<SetupStep>();
        Assert.Equal(SetupKind.Install, _h.Setup("1.1.0").Inspect().Kind);

        var result = await _h.InstallAsync("1.1.0", new InstallOptions(Autostart: true, DesktopShortcut: false, LaunchAfterInstall: true), progress: progress);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.Error);
        Assert.Null(result.PreviousVersion);
        Assert.Equal(new Version(1, 1, 0), result.NewVersion);
        Assert.True(result.AppStarted);
        Assert.Equal(SetupHarness.ExeBytes("1.1.0"), File.ReadAllBytes(L.ExePath)); // byte-gleich mit der portablen EXE
        Assert.Equal("README 1.1.0", File.ReadAllText(_h.InstalledFile("README.md")));
        Assert.Equal("NOTICES 1.1.0", File.ReadAllText(_h.InstalledFile("THIRD-PARTY-NOTICES.txt")));
        Assert.Equal("CLI 1.1.0", File.ReadAllText(_h.InstalledFile(@"cli\hdrpilot.exe")));
        AssertNoLeftovers();

        var link = SetupLinks.Read(L.StartMenuShortcut);
        Assert.Equal(L.ExePath, link.TargetPath, ignoreCase: true);
        Assert.Equal(L.ExtraLaunchArguments, link.Arguments);
        Assert.Equal(L.InstallDirectory, link.WorkingDirectory, ignoreCase: true);
        Assert.Equal("HDRpilot", link.Description);
        Assert.False(File.Exists(L.DesktopShortcut));

        Assert.Equal(AutostartService.BuildCommand(L.ExePath), Autostart);
        var entry = Assert.IsType<UninstallEntry>(_h.Entries.Entry);
        Assert.Equal("1.1.0", entry.DisplayVersion);
        Assert.Equal(L.InstallDirectory, entry.InstallLocation);
        Assert.True(entry.EstimatedSizeKb >= 1);

        Assert.Equal([(L.ExePath, "--show " + L.ExtraLaunchArguments)], _h.App.Started);
        Assert.Equal([SetupStep.CheckSetupFile, SetupStep.CopyFiles, SetupStep.Shortcuts, SetupStep.Autostart, SetupStep.Register, SetupStep.LaunchApp], progress.Items);
        Assert.Equal(0, _h.App.StopCalls);
    }

    [Fact]
    public async Task Update_stops_the_running_app_replaces_everything_keeps_settings_and_restarts_in_the_tray()
    {
        Assert.True((await _h.InstallAsync("1.0.0", new InstallOptions(true, DesktopShortcut: true, false))).Success);
        var config = Path.Combine(L.DataPaths.ConfigDirectory, "config.json");
        Directory.CreateDirectory(L.DataPaths.ConfigDirectory);
        File.WriteAllText(config, "{ \"games\": [ { \"source\": \"Steam\", \"sourceId\": \"1\", \"name\": \"Spiel\" } ] }");
        var configHash = SHA256.HashData(File.ReadAllBytes(config));
        _h.App.Running = true;
        var engine = _h.Setup("1.1.0");
        var inspection = engine.Inspect();

        var result = await engine.InstallAsync(new InstallOptions(Autostart: true, DesktopShortcut: true, LaunchAfterInstall: true), null, TestContext.Current.CancellationToken);

        Assert.Equal(SetupKind.Update, inspection.Kind);
        Assert.True(inspection.Registered);
        Assert.True(inspection.AppRunning);
        Assert.True(inspection.AutostartEnabled);
        Assert.True(inspection.DesktopShortcutExists);
        Assert.True(result.Success, result.Message);
        Assert.Equal(new Version(1, 0, 0), result.PreviousVersion);
        Assert.Equal(new Version(1, 1, 0), result.NewVersion);
        Assert.Equal(1, _h.App.StopCalls);
        Assert.Equal(SetupHarness.ExeBytes("1.1.0"), File.ReadAllBytes(L.ExePath));
        Assert.Equal("README 1.1.0", File.ReadAllText(_h.InstalledFile("README.md")));
        Assert.Equal("CLI 1.1.0", File.ReadAllText(_h.InstalledFile(@"cli\hdrpilot.exe")));
        AssertNoLeftovers();
        Assert.Equal(configHash, SHA256.HashData(File.ReadAllBytes(config)));
        Assert.Equal("1.1.0", _h.Entries.Entry!.DisplayVersion);
        Assert.True(ShellSetupShortcuts.Instance.PointsTo(L.DesktopShortcut, L.ExePath));
        Assert.Equal([(L.ExePath, "--minimized " + L.ExtraLaunchArguments)], _h.App.Started);
    }

    [Fact]
    public async Task Failure_while_replacing_the_command_line_rolls_everything_back_and_restarts_the_old_version()
    {
        Assert.True((await _h.InstallAsync("1.0.0")).Success);
        _h.App.Running = true;
        var files = new FailingFileOperations((op, path) =>
            op == "Replace" && path.EndsWith(@"cli\hdrpilot.exe", StringComparison.OrdinalIgnoreCase) ? FailingFileOperations.SharingViolation() : null);

        var result = await _h.InstallAsync("1.1.0", new InstallOptions(true, false, true), files);

        Assert.False(result.Success);
        Assert.Equal(SetupErrorKind.FilesInUse, result.Error);
        Assert.Equal(new Version(1, 0, 0), result.PreviousVersion);
        Assert.Contains("hdrpilot.exe", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SetupHarness.ExeBytes("1.0.0"), File.ReadAllBytes(L.ExePath)); // alte EXE bleibt lauffähig
        Assert.Equal("README 1.0.0", File.ReadAllText(_h.InstalledFile("README.md")));
        Assert.Equal("NOTICES 1.0.0", File.ReadAllText(_h.InstalledFile("THIRD-PARTY-NOTICES.txt")));
        Assert.Equal("CLI 1.0.0", File.ReadAllText(_h.InstalledFile(@"cli\hdrpilot.exe")));
        AssertNoLeftovers();
        Assert.Equal("1.0.0", _h.Entries.Entry!.DisplayVersion);
        Assert.True(result.AppStarted);
        Assert.Equal([(L.ExePath, "--minimized " + L.ExtraLaunchArguments)], _h.App.Started);
        Assert.Contains("Zurückgenommen", _h.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_while_registering_restores_files_shortcuts_autostart_and_entry()
    {
        Assert.True((await _h.InstallAsync("1.0.0", new InstallOptions(Autostart: true, DesktopShortcut: true, LaunchAfterInstall: false))).Success);
        var desktopBefore = File.ReadAllBytes(L.DesktopShortcut);
        var autostartBefore = Autostart;
        _h.Entries.FailOnWrite = new UnauthorizedAccessException("Registry gesperrt");

        var result = await _h.InstallAsync("1.1.0", new InstallOptions(Autostart: false, DesktopShortcut: false, LaunchAfterInstall: true));

        // Kein „Kein Schreibzugriff auf den Programmordner“: der war beschreibbar, gescheitert ist die Registry.
        Assert.Equal(SetupErrorKind.Unexpected, result.Error);
        Assert.StartsWith("Register: ", result.Message, StringComparison.Ordinal);
        Assert.Equal(SetupHarness.ExeBytes("1.0.0"), File.ReadAllBytes(L.ExePath));
        Assert.Equal("CLI 1.0.0", File.ReadAllText(_h.InstalledFile(@"cli\hdrpilot.exe")));
        AssertNoLeftovers();
        Assert.Equal(desktopBefore, File.ReadAllBytes(L.DesktopShortcut));
        Assert.Equal(autostartBefore, Autostart);
        Assert.Equal("1.0.0", _h.Entries.Entry!.DisplayVersion);
        Assert.Empty(_h.App.Started); // lief vorher nicht: wird auch nicht gestartet
    }

    [Fact]
    public async Task Failure_while_registering_a_fresh_install_leaves_nothing_behind()
    {
        _h.Entries.FailOnWrite = new IOException("kaputt");

        var result = await _h.InstallAsync("1.1.0", new InstallOptions(Autostart: true, DesktopShortcut: true, LaunchAfterInstall: true));

        Assert.Equal(SetupErrorKind.Unexpected, result.Error);
        Assert.False(File.Exists(L.ExePath));
        Assert.False(Directory.Exists(L.InstallDirectory)); // auch keine leeren Ordner
        Assert.False(Directory.Exists(Path.GetDirectoryName(L.InstallDirectory)));
        Assert.False(File.Exists(L.StartMenuShortcut));
        Assert.False(File.Exists(L.DesktopShortcut));
        Assert.Null(Autostart);
        Assert.Null(_h.Entries.Entry);
        Assert.Empty(_h.App.Started);
    }

    [Fact]
    public async Task Failure_while_copying_a_fresh_install_leaves_no_empty_folders()
    {
        var files = new FailingFileOperations((op, path) =>
            op == "Create" && path.EndsWith(@"cli\hdrpilot.exe.new", StringComparison.OrdinalIgnoreCase) ? FailingFileOperations.DiskFull() : null);

        var result = await _h.InstallAsync("1.1.0", files: files);

        Assert.Equal(SetupErrorKind.DiskFull, result.Error);
        Assert.False(Directory.Exists(L.InstallDirectory));
    }

    [Fact]
    public async Task Failed_fresh_install_keeps_a_program_folder_that_was_already_there()
    {
        Directory.CreateDirectory(L.InstallDirectory);
        File.WriteAllText(_h.InstalledFile("notizen.txt"), "vom Nutzer");
        _h.Entries.FailOnWrite = new IOException("kaputt");

        var result = await _h.InstallAsync("1.1.0");

        Assert.False(result.Success);
        Assert.True(File.Exists(_h.InstalledFile("notizen.txt")));
        Assert.False(Directory.Exists(_h.InstalledFile("cli"))); // selbst angelegt und leer: weg
        Assert.Equal([_h.InstalledFile("notizen.txt")], Directory.GetFileSystemEntries(L.InstallDirectory));
    }

    [Fact]
    public async Task Full_disk_after_copying_is_still_reported_as_full_disk()
    {
        _h.Entries.FailOnWrite = FailingFileOperations.DiskFull();

        var result = await _h.InstallAsync("1.1.0");

        Assert.Equal(SetupErrorKind.DiskFull, result.Error);
    }

    [Fact]
    public async Task Portable_copy_that_was_stopped_for_a_failed_install_is_started_again()
    {
        var portable = _h.ForeignExe(); // läuft mit derselben Einzelinstanz-Kennung aus einem anderen Ordner
        _h.App.Running = true;
        _h.App.InstanceExe = portable;
        _h.Entries.FailOnWrite = new IOException("kaputt");

        var result = await _h.InstallAsync("1.1.0", new InstallOptions(true, false, LaunchAfterInstall: true));

        Assert.Equal(SetupErrorKind.Unexpected, result.Error);
        Assert.Equal(1, _h.App.StopCalls);
        Assert.False(File.Exists(L.ExePath));
        Assert.True(result.AppStarted);
        Assert.Equal([(portable, "--minimized " + L.ExtraLaunchArguments)], _h.App.Started);
    }

    [Fact]
    public async Task Portable_copy_that_was_stopped_is_started_again_instead_of_the_installed_version()
    {
        Assert.True((await _h.InstallAsync("1.0.0")).Success);
        var portable = _h.ForeignExe();
        _h.App.Running = true;
        _h.App.InstanceExe = portable;
        var files = new FailingFileOperations((op, path) =>
            op == "Replace" && path.Equals(L.ExePath, StringComparison.OrdinalIgnoreCase) ? FailingFileOperations.SharingViolation() : null);

        var result = await _h.InstallAsync("1.1.0", new InstallOptions(true, false, true), files);

        Assert.Equal(SetupErrorKind.FilesInUse, result.Error);
        Assert.Equal(SetupHarness.ExeBytes("1.0.0"), File.ReadAllBytes(L.ExePath));
        Assert.Equal([(portable, "--minimized " + L.ExtraLaunchArguments)], _h.App.Started);
    }

    [Fact]
    public async Task Reinstalling_the_same_version_repairs_the_files_and_opens_the_window_when_the_app_was_not_running()
    {
        Assert.True((await _h.InstallAsync("1.1.0")).Success);
        File.WriteAllText(_h.InstalledFile("README.md"), "kaputt");
        var engine = _h.Setup("1.1.0");
        Assert.Equal(SetupKind.Reinstall, engine.Inspect().Kind);

        var result = await engine.InstallAsync(new InstallOptions(true, false, LaunchAfterInstall: true), null, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Equal(result.PreviousVersion, result.NewVersion);
        Assert.Equal("README 1.1.0", File.ReadAllText(_h.InstalledFile("README.md")));
        AssertNoLeftovers();
        Assert.Equal([(L.ExePath, "--show " + L.ExtraLaunchArguments)], _h.App.Started);
    }

    [Fact]
    public async Task Older_version_is_refused_without_explicit_permission()
    {
        Assert.True((await _h.InstallAsync("1.2.0")).Success);
        _h.App.Running = true;
        var engine = _h.Setup("1.1.0");
        Assert.Equal(SetupKind.Downgrade, engine.Inspect().Kind);

        var result = await engine.InstallAsync(new InstallOptions(true, false, true), null, TestContext.Current.CancellationToken);

        Assert.Equal(SetupErrorKind.DowngradeNotAllowed, result.Error);
        Assert.Equal(new Version(1, 2, 0), result.PreviousVersion);
        Assert.Equal(SetupHarness.ExeBytes("1.2.0"), File.ReadAllBytes(L.ExePath));
        Assert.Equal(0, _h.App.StopCalls);
        Assert.Empty(_h.App.Started);
    }

    [Fact]
    public async Task Older_version_is_installed_when_explicitly_allowed()
    {
        Assert.True((await _h.InstallAsync("1.2.0")).Success);

        var result = await _h.InstallAsync("1.1.0", new InstallOptions(true, false, false, AllowDowngrade: true));

        Assert.True(result.Success, result.Message);
        Assert.Equal(new Version(1, 2, 0), result.PreviousVersion);
        Assert.Equal(SetupHarness.ExeBytes("1.1.0"), File.ReadAllBytes(L.ExePath));
        Assert.Equal("1.1.0", _h.Entries.Entry!.DisplayVersion);
    }

    [Fact]
    public async Task App_that_does_not_exit_stops_the_setup_before_anything_changes()
    {
        Assert.True((await _h.InstallAsync("1.0.0")).Success);
        _h.App.Running = true;
        _h.App.StopSucceeds = false;

        var result = await _h.InstallAsync("1.1.0", new InstallOptions(true, false, true));

        Assert.Equal(SetupErrorKind.AppStillRunning, result.Error);
        Assert.Equal(1, _h.App.StopCalls);
        Assert.Equal(SetupHarness.ExeBytes("1.0.0"), File.ReadAllBytes(L.ExePath));
        Assert.Equal("1.0.0", _h.Entries.Entry!.DisplayVersion);
        Assert.Empty(_h.App.Started);
        AssertNoLeftovers();
    }

    [Fact]
    public async Task Autostart_off_removes_only_our_own_value()
    {
        Assert.True((await _h.InstallAsync("1.1.0", new InstallOptions(Autostart: true, false, false))).Success);
        Assert.NotNull(Autostart);

        Assert.True((await _h.InstallAsync("1.1.0", new InstallOptions(Autostart: false, false, false))).Success);

        Assert.Null(Autostart);
        Assert.False(_h.Setup("1.1.0").Inspect().AutostartEnabled);
    }

    [Fact]
    public async Task Autostart_off_leaves_a_foreign_value_untouched()
    {
        const string Foreign = "\"D:\\Portable\\HDRpilot\\HDRpilot.exe\" --minimized";
        _h.Registry.Set(RegistryHiveKind.CurrentUser, L.RunKeyPath, AutostartService.ValueName, Foreign);
        Assert.True(_h.Setup("1.1.0").Inspect().AutostartEnabled); // nichts installiert: Vorbelegung an

        Assert.True((await _h.InstallAsync("1.1.0", new InstallOptions(Autostart: false, false, false))).Success);

        Assert.Equal(Foreign, Autostart);
        Assert.False(_h.Setup("1.1.0").Inspect().AutostartEnabled);
    }

    [Fact]
    public async Task Desktop_shortcut_off_removes_only_our_own_shortcut()
    {
        Assert.True((await _h.InstallAsync("1.1.0", new InstallOptions(true, DesktopShortcut: true, false))).Success);
        Assert.True(_h.Setup("1.1.0").Inspect().DesktopShortcutExists);

        Assert.True((await _h.InstallAsync("1.1.0", new InstallOptions(true, DesktopShortcut: false, false))).Success);

        Assert.False(File.Exists(L.DesktopShortcut));
        Assert.True(File.Exists(L.StartMenuShortcut));
    }

    [Fact]
    public async Task Desktop_shortcut_off_keeps_a_foreign_hdrpilot_shortcut()
    {
        var foreign = _h.ForeignExe();
        Directory.CreateDirectory(L.DesktopDirectory);
        ShellSetupShortcuts.Instance.Write(L.DesktopShortcut, foreign, string.Empty, Path.GetDirectoryName(foreign)!, "fremd");
        Assert.False(_h.Setup("1.1.0").Inspect().DesktopShortcutExists);

        Assert.True((await _h.InstallAsync("1.1.0", new InstallOptions(true, DesktopShortcut: false, false))).Success);

        Assert.Equal(foreign, ShellSetupShortcuts.Instance.ReadTarget(L.DesktopShortcut), ignoreCase: true);
    }

    [Fact]
    public async Task Damaged_setup_file_is_refused_before_anything_changes()
    {
        Assert.True((await _h.InstallAsync("1.0.0")).Success);
        var bundle = _h.Bundle("1.1.0");
        var bytes = File.ReadAllBytes(bundle.FilePath);
        bytes[20] ^= 0x10;
        File.WriteAllBytes(bundle.FilePath, bytes);
        _h.App.Running = true;

        var result = await _h.Setup(SetupBundle.TryOpen(bundle.FilePath)!, "1.1.0").InstallAsync(Quiet, null, TestContext.Current.CancellationToken);

        Assert.Equal(SetupErrorKind.SetupFileDamaged, result.Error);
        Assert.Equal(SetupHarness.ExeBytes("1.0.0"), File.ReadAllBytes(L.ExePath));
        Assert.Equal(0, _h.App.StopCalls);
    }

    [Fact]
    public async Task Exe_that_differs_from_the_checksum_after_writing_is_refused_and_rolled_back()
    {
        Assert.True((await _h.InstallAsync("1.0.0")).Success);
        var files = new FailingFileOperations((_, _) => null, corrupt: path => path.EndsWith("HDRpilot.exe.new", StringComparison.OrdinalIgnoreCase));

        var result = await _h.InstallAsync("1.1.0", files: files);

        Assert.Equal(SetupErrorKind.SetupFileDamaged, result.Error);
        Assert.Equal(SetupHarness.ExeBytes("1.0.0"), File.ReadAllBytes(L.ExePath));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task Full_disk_and_missing_write_access_are_reported_and_leave_the_old_version()
    {
        Assert.True((await _h.InstallAsync("1.0.0")).Success);
        var full = new FailingFileOperations((op, path) =>
            op == "Create" && path.EndsWith(@"cli\hdrpilot.exe.new", StringComparison.OrdinalIgnoreCase) ? FailingFileOperations.DiskFull() : null);
        var denied = new FailingFileOperations((op, path) => op == "Create" ? new UnauthorizedAccessException("verweigert") : null);

        var diskFull = await _h.InstallAsync("1.1.0", files: full);
        var accessDenied = await _h.InstallAsync("1.1.0", files: denied);

        Assert.Equal(SetupErrorKind.DiskFull, diskFull.Error);
        Assert.Equal(SetupErrorKind.AccessDenied, accessDenied.Error);
        Assert.Equal(SetupHarness.ExeBytes("1.0.0"), File.ReadAllBytes(L.ExePath));
        Assert.Equal("CLI 1.0.0", File.ReadAllText(_h.InstalledFile(@"cli\hdrpilot.exe")));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task Second_setup_at_the_same_time_is_refused()
    {
        using var other = new HeldSetupLock(SetupSupport.MutexNameFor(L));

        var result = await _h.InstallAsync("1.1.0");

        Assert.Equal(SetupErrorKind.AnotherSetupRunning, result.Error);
        Assert.False(File.Exists(L.ExePath));
    }

    [Fact]
    public async Task Lock_left_behind_by_a_crashed_setup_is_taken_over()
    {
        Mutex? crashed = null;
        var thread = new Thread(() =>
        {
            crashed = new Mutex(initiallyOwned: false, SetupSupport.MutexNameFor(L));
            crashed.WaitOne(); // Thread endet ohne Freigabe: die Sperre ist „verlassen“
        });
        thread.Start();
        thread.Join();
        try
        {
            var result = await _h.InstallAsync("1.1.0");

            Assert.True(result.Success, result.Message);
        }
        finally
        {
            crashed?.Dispose();
        }
    }

    [Fact]
    public async Task Start_menu_shortcut_of_a_test_installation_starts_the_test_instance()
    {
        Assert.True((await _h.InstallAsync("1.1.0", new InstallOptions(true, DesktopShortcut: true, false))).Success);

        Assert.Equal(L.ExtraLaunchArguments, SetupLinks.Read(L.DesktopShortcut).Arguments);
        Assert.Contains("--config-dir", L.ExtraLaunchArguments, StringComparison.Ordinal);
    }

    private void AssertNoLeftovers()
    {
        if (!Directory.Exists(L.InstallDirectory))
        {
            return;
        }

        var leftovers = Directory.GetFiles(L.InstallDirectory, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".new", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(leftovers);
    }
}
