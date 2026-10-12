using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;
using static HdrPilot.Tests.AutoHdr.AutoHdrHarness;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Weg je Spiel (E88) über den echten Dienst: Registry als Attrappe im Speicher, Zustandsdatei im Speicher, echte
/// Konfiguration. Prüft Eintragen, exaktes Zurücknehmen, extern geänderte und unsaubere Werte, Absturzsicherheit, Abgleich
/// ohne Registry-Zugriff im Leerlauf und die Rücknahme auf Wunsch.
/// </summary>
public sealed class AutoHdrRegistryEngineTests
{
    private readonly AutoHdrHarness _h = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Start_writes_the_token_for_games_with_the_option_and_remembers_the_original()
    {
        var a = _h.AddGame("1", ExeA);
        _h.AddGame("2", ExeB, autoHdr: false, hdrAuto: true);

        _h.Service.Start();

        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA));
        Assert.Null(_h.Value(ExeB)); // Gegenprobe: „HDR automatisch“ allein schreibt nichts
        Assert.Equal(["set " + ExeA + "=AutoHDREnable=2097;"], _h.Store.Operations);
        var entry = Assert.Single(_h.State!.Entries);
        Assert.Equal(AutoHdrEntryStatus.Applied, entry.Status);
        Assert.Equal(GpuPreferenceValueKind.Missing, entry.Original.Kind);
        Assert.Equal("AutoHDREnable=2097;", entry.Written);
        Assert.Equal(["Steam:1"], entry.Games);
        Assert.Equal(26300, entry.WindowsBuild);
        Assert.Equal(_h.Store.KeyName, _h.State!.RegistryKey);
        Assert.Equal(AutoHdrTargetState.Applied, _h.StateOf(a, ExeA));
        Assert.True(_h.Logged("eingetragen (AutoHDREnable=2097)", LogLevel.Information));
    }

    [Fact]
    public void Switching_off_restores_the_exact_original_and_removes_the_state_file()
    {
        _h.Store.SetString(ExeA, "GpuPreference=2;");
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        Assert.Equal("GpuPreference=2;AutoHDREnable=2097;", _h.Value(ExeA));

        _h.SetAutoHdr(a, false);

        Assert.Equal("GpuPreference=2;", _h.Value(ExeA));
        Assert.False(_h.Fs.FileExists(_h.Paths.AutoHdrStateFile));
        Assert.Same(AutoHdrGameStatus.None, _h.Service.GetStatus(a));
        Assert.True(_h.Logged("zurückgenommen (wieder „GpuPreference=2;“"));
    }

    [Fact]
    public void Missing_value_is_deleted_again_when_switched_off()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();

        _h.SetAutoHdr(a, false);

        Assert.False(_h.Store.Values.ContainsKey(ExeA));
        Assert.Equal(["set " + ExeA + "=AutoHDREnable=2097;", "delete " + ExeA], _h.Store.Operations);
    }

    [Fact]
    public void Value_that_is_already_on_is_neither_written_nor_taken_back()
    {
        _h.Store.SetString(ExeA, "AutoHDREnable=2097;");
        var a = _h.AddGame("1", ExeA);

        _h.Service.Start();
        Assert.Equal(AutoHdrTargetState.AlreadyOn, _h.StateOf(a, ExeA));
        Assert.Null(_h.State);

        _h.SetAutoHdr(a, false);

        Assert.Empty(_h.Store.Operations);
        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA)); // vom Nutzer: bleibt an
    }

    [Fact]
    public void Externally_changed_value_is_left_alone_and_can_be_reapplied_by_switching_off_and_on()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        _h.Store.SetString(ExeA, "AutoHDREnable=2096;"); // in Windows „Auto HDR nicht verwenden“ gewählt

        _h.Restart().Start();

        Assert.Equal(AutoHdrTargetState.ExternallyChanged, _h.StateOf(a, ExeA));
        Assert.Equal(AutoHdrEntryStatus.ExternallyChanged, Assert.Single(_h.State!.Entries).Status);
        Assert.True(_h.Logged("extern geändert (jetzt „AutoHDREnable=2096;“)"));
        Assert.Equal("AutoHDREnable=2096;", _h.Value(ExeA));

        // Weiterer Abgleich: wird nicht von selbst neu geschrieben.
        _h.TouchUnrelated();
        _h.Service.OnStartAnnounced(_h.Current.FindGame(a)!, SessionTrigger.LaunchedViaApp, _h.Clock.UtcNow);
        Assert.Equal("AutoHDREnable=2096;", _h.Value(ExeA));

        _h.SetAutoHdr(a, false);
        Assert.Equal("AutoHDREnable=2096;", _h.Value(ExeA));
        Assert.Null(_h.State);

        _h.SetAutoHdr(a, true);
        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA));
        Assert.Equal("2096", Assert.Single(_h.State!.Entries).OriginalToken);

        _h.SetAutoHdr(a, false);
        Assert.Equal("AutoHDREnable=2096;", _h.Value(ExeA));
    }

    [Fact]
    public void Deleted_value_stays_deleted_when_switched_off()
    {
        _h.Store.SetString(ExeA, "GpuPreference=2;");
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        _h.Store.Set(ExeA, GpuPreferenceValue.Missing); // „Zurücksetzen“ in den Windows-Einstellungen
        var before = _h.Store.Operations.Count;

        _h.SetAutoHdr(a, false);

        Assert.False(_h.Store.Values.ContainsKey(ExeA));
        Assert.Equal(before, _h.Store.Operations.Count);
        Assert.True(_h.Logged("extern geändert"));
    }

    [Fact]
    public void Windows_adding_app_status_keeps_the_entry_applied_and_off_takes_back_only_the_token()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        _h.Store.SetString(ExeA, "AutoHDREnable=2097;AppStatus=4;"); // Windows ergänzt nach dem ersten Spielstart

        _h.Restart().Start();
        Assert.Equal(AutoHdrTargetState.Applied, _h.StateOf(a, ExeA));
        Assert.Equal("AutoHDREnable=2097;", Assert.Single(_h.State!.Entries).Written); // nicht übernommen

        _h.SetAutoHdr(a, false);

        Assert.Equal("AppStatus=4;", _h.Value(ExeA));
        Assert.True(_h.Logged("nur AutoHDREnable zurückgenommen"));
    }

    [Fact]
    public void Unclean_values_are_never_touched_and_logged_once()
    {
        _h.Store.Set(ExeA, new GpuPreferenceValue(GpuPreferenceValueKind.DWord, "1"));
        _h.Store.SetString(ExeB, "0");
        var a = _h.AddGame("1", ExeA);
        var b = _h.AddGame("2", ExeB);

        _h.Service.Start();
        _h.SetAutoHdr(a, false);
        _h.SetAutoHdr(a, true);

        Assert.Empty(_h.Store.Operations);
        Assert.Null(_h.State);
        Assert.Equal(AutoHdrTargetState.Unsupported, _h.StateOf(a, ExeA));
        Assert.Equal(AutoHdrTargetState.Unsupported, _h.StateOf(b, ExeB));
        Assert.Equal(1, _h.LogCount(ExeA + " ist kein sauberer REG_SZ (REG_DWORD 1)"));
    }

    [Fact]
    public void Embedded_nul_value_is_never_touched()
    {
        _h.Store.SetString(ExeA, "GpuPreference=0;\0xy");
        var a = _h.AddGame("1", ExeA);

        _h.Service.Start();

        Assert.Empty(_h.Store.Operations);
        Assert.Equal(AutoHdrTargetState.Unsupported, _h.StateOf(a, ExeA));
        Assert.True(_h.Logged(@"GpuPreference=0;\0xy"));
    }

    [Fact]
    public void Failed_state_save_writes_nothing_and_retries_at_the_next_reconcile()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Fs.FailWriting(_h.Paths.AutoHdrStateFile, new IOException("Platte voll"));

        _h.Service.Start();

        Assert.Empty(_h.Store.Operations);
        Assert.Equal(AutoHdrTargetState.Failed, _h.StateOf(a, ExeA));
        Assert.True(_h.Logged("wird nicht eingetragen"));

        _h.Fs.FailWriting(_h.Paths.AutoHdrStateFile, null);
        _h.TouchUnrelated(); // jede Konfigurationsänderung holt den Fehler nach

        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA));
        Assert.Equal(AutoHdrTargetState.Applied, _h.StateOf(a, ExeA));
    }

    [Fact]
    public void Failed_registry_write_keeps_no_entry_and_is_retried_before_the_next_start()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Store.FailWrites = new UnauthorizedAccessException("Zugriff verweigert");

        _h.Service.Start();

        Assert.Null(_h.State);
        Assert.Equal(AutoHdrTargetState.Failed, _h.StateOf(a, ExeA));
        Assert.True(_h.Logged("ließ sich nicht eintragen (Zugriff verweigert)", LogLevel.Warning));

        _h.Store.FailWrites = null;
        _h.Service.OnStartAnnounced(_h.Current.FindGame(a)!, SessionTrigger.LaunchedViaApp, _h.Clock.UtcNow);

        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA));
        Assert.Equal(AutoHdrTargetState.Applied, _h.StateOf(a, ExeA));
    }

    [Fact]
    public void Registry_read_failure_marks_the_target_failed_without_writing()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Store.FailReads = new System.Security.SecurityException("verweigert");

        _h.Service.Start();

        Assert.Empty(_h.Store.Operations);
        Assert.Null(_h.State);
        Assert.Equal(AutoHdrTargetState.Failed, _h.StateOf(a, ExeA));
    }

    [Fact]
    public void Pending_entry_after_a_crash_before_the_write_is_written_now()
    {
        var a = _h.AddGame("1", ExeA, autoHdr: true);
        _h.Store.SetString(ExeA, "GpuPreference=2;");
        _h.WriteState(State(Entry(ExeA, AutoHdrEntryStatus.Pending, "GpuPreference=2;", "GpuPreference=2;AutoHDREnable=2097;")));

        _h.Service.Start();

        Assert.Equal(["set " + ExeA + "=GpuPreference=2;AutoHDREnable=2097;"], _h.Store.Operations);
        Assert.Equal(AutoHdrEntryStatus.Applied, Assert.Single(_h.State!.Entries).Status);
        Assert.Equal(AutoHdrTargetState.Applied, _h.StateOf(a, ExeA));
    }

    [Fact]
    public void Pending_entry_after_a_crash_after_the_write_is_only_marked_applied()
    {
        _h.AddGame("1", ExeA);
        _h.Store.SetString(ExeA, "GpuPreference=2;AutoHDREnable=2097;");
        _h.WriteState(State(Entry(ExeA, AutoHdrEntryStatus.Pending, "GpuPreference=2;", "GpuPreference=2;AutoHDREnable=2097;")));

        _h.Service.Start();

        Assert.Empty(_h.Store.Operations);
        Assert.Equal(AutoHdrEntryStatus.Applied, Assert.Single(_h.State!.Entries).Status);
    }

    [Fact]
    public void Pending_entry_of_a_game_switched_off_meanwhile_is_dropped_without_touching()
    {
        _h.AddGame("1", ExeA, autoHdr: false);
        _h.Store.SetString(ExeA, "GpuPreference=2;");
        _h.WriteState(State(Entry(ExeA, AutoHdrEntryStatus.Pending, "GpuPreference=2;", "GpuPreference=2;AutoHDREnable=2097;")));

        _h.Service.Start();

        Assert.Empty(_h.Store.Operations);
        Assert.Null(_h.State);
    }

    [Fact]
    public void Removing_a_game_restores_only_its_own_values()
    {
        var a = _h.AddGame("1", ExeA);
        var b = _h.AddGame("2", ExeB);
        _h.Store.SetString(ExeB, "GpuPreference=1;");
        _h.Service.Start();

        _h.RemoveGame(a);

        Assert.False(_h.Store.Values.ContainsKey(ExeA));
        Assert.Equal("GpuPreference=1;AutoHDREnable=2097;", _h.Value(ExeB));
        Assert.Equal(ExeB, Assert.Single(_h.State!.Entries).ValueName);
        Assert.True(_h.Logged("Spiel entfernt"));
        Assert.Equal(AutoHdrTargetState.Applied, _h.StateOf(b, ExeB));
    }

    [Fact]
    public void Exe_shared_by_two_games_stays_while_one_of_them_wants_it()
    {
        var a = _h.AddGame("1", ExeA);
        var b = _h.AddGame("2", ExeA);
        _h.Service.Start();
        Assert.Single(_h.Store.Operations);
        Assert.Equal(["Steam:1", "Steam:2"], Assert.Single(_h.State!.Entries).Games);

        _h.SetAutoHdr(a, false);
        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA));
        Assert.Equal(["Steam:2"], Assert.Single(_h.State!.Entries).Games);

        _h.SetAutoHdr(b, false);
        Assert.False(_h.Store.Values.ContainsKey(ExeA));
    }

    [Fact]
    public void Learned_exe_is_written_and_an_excluded_exe_is_taken_back()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        var learned = DirA + @"\bin\x64\SpielA-Win64-Shipping.exe";

        _h.Config.RecordDetectedExecutables(a, [learned]); // wie die Sitzungslogik beim ersten Lauf
        Assert.Equal("AutoHDREnable=2097;", _h.Value(learned));

        _h.Config.UpdateGame(a, g => g.ExcludedExecutables.Add(Path.GetFileName(ExeA)));
        Assert.False(_h.Store.Values.ContainsKey(ExeA));
        Assert.Equal("AutoHDREnable=2097;", _h.Value(learned));
        Assert.True(_h.Logged("EXE ist kein Ziel mehr"));
    }

    [Fact]
    public void Unchanged_targets_cause_no_registry_access_on_config_changes()
    {
        _h.AddGame("1", ExeA);
        _h.Service.Start();
        var reads = _h.Store.Reads;

        _h.TouchUnrelated();
        _h.TouchUnrelated();

        Assert.Equal(reads, _h.Store.Reads);

        // Gegenprobe: ein neues Ziel liest und schreibt.
        _h.AddGame("2", ExeB);
        Assert.True(_h.Store.Reads > reads);
    }

    [Fact]
    public void Idle_start_without_the_option_and_without_state_reads_nothing_and_writes_no_file()
    {
        _h.AddGame("1", ExeA, autoHdr: false, hdrAuto: true);
        var files = _h.Fs.FileExists(_h.Paths.AutoHdrStateFile);

        _h.Service.Start();
        _h.TouchUnrelated();
        _h.Service.OnCycle(["Steam:1"], _h.Current, false, _h.Clock.UtcNow);

        Assert.False(files);
        Assert.Equal(0, _h.Store.Reads);
        Assert.Empty(_h.Store.Operations);
        Assert.False(_h.Fs.FileExists(_h.Paths.AutoHdrStateFile));
        Assert.False(_h.Fs.FileExists(_h.Paths.AutoHdrGlobalFile));
        Assert.Equal(0, _h.ChangedEvents);
    }

    [Fact]
    public async Task State_for_another_key_makes_the_engine_inert()
    {
        var a = _h.AddGame("1", ExeA);
        var other = State(Entry(ExeA, AutoHdrEntryStatus.Applied, null, "AutoHDREnable=2097;"));
        other.RegistryKey = AutoHdrRegistryLocation.Real.KeyName;
        _h.WriteState(other);

        _h.Service.Start();
        _h.TouchUnrelated();

        Assert.Equal(0, _h.Store.Reads);
        Assert.Empty(_h.Store.Operations);
        Assert.Equal(AutoHdrTargetState.Failed, _h.StateOf(a, ExeA));
        Assert.True(_h.Logged("gehört zu " + AutoHdrRegistryLocation.Real.KeyName, LogLevel.Warning));
        var rollback = await _h.Service.RollbackAllAsync(Ct);
        Assert.Equal("Zustand gehört zu " + AutoHdrRegistryLocation.Real.KeyName, rollback.Error);
        Assert.True(_h.Current.FindGame(a)!.AutoHdr); // nichts ausgeschaltet
        Assert.True(_h.Fs.FileExists(_h.Paths.AutoHdrStateFile));
    }

    [Fact]
    public void Corrupt_state_file_is_backed_up_and_the_engine_continues()
    {
        _h.AddGame("1", ExeA);
        _h.Fs.AddFile(_h.Paths.AutoHdrStateFile, "{ kaputt");

        _h.Service.Start();

        var backup = Assert.Single(_h.Fs.EnumerateFiles(_h.Paths.DataDirectory, "autohdr-state.json.defekt-*.bak"));
        Assert.Equal("{ kaputt", _h.Fs.ReadAllText(backup));
        Assert.True(_h.Logged("war defekt", LogLevel.Warning));
        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA));
        Assert.Single(_h.State!.Entries);
    }

    [Fact]
    public void Unreadable_state_file_is_left_alone_and_nothing_is_written()
    {
        _h.AddGame("1", ExeA);
        _h.Fs.AddFile(_h.Paths.AutoHdrStateFile, "{}");
        _h.Fs.FailReading(_h.Paths.AutoHdrStateFile, new IOException("gesperrt"));

        _h.Service.Start();

        Assert.Equal(0, _h.Store.Reads);
        Assert.Empty(_h.Store.Operations);
        Assert.Empty(_h.Fs.EnumerateFiles(_h.Paths.DataDirectory, "*.bak"));
    }

    [Fact]
    public void Changed_build_is_logged()
    {
        _h.AddGame("1", ExeA);
        var entry = Entry(ExeA, AutoHdrEntryStatus.Applied, null, "AutoHDREnable=2097;");
        entry.WindowsBuild = 26200;
        _h.WriteState(State(entry));
        _h.Store.SetString(ExeA, "AutoHDREnable=2097;");

        _h.Service.Start();

        Assert.True(_h.Logged("Windows-Build seit dem Eintragen geändert (26200 → 26300)"));
        Assert.Empty(_h.Store.Operations);
    }

    [Fact]
    public void Status_lists_targets_per_game_and_none_for_unknown_or_switched_off_games()
    {
        _h.Store.Set(ExeB, new GpuPreferenceValue(GpuPreferenceValueKind.DWord, "1"));
        var game = NewGame("1", ExeA);
        game.ExtraExecutables = [ExeB];
        _h.Config.Update(c => c.Games.Add(game));
        var noExe = _h.AddGame("2", ExeA);
        _h.Config.UpdateGame(noExe, g => g.KnownExecutables = []);
        var off = _h.AddGame("3", ExeB, autoHdr: false);

        _h.Service.Start();

        var status = _h.Service.GetStatus(game.Key);
        Assert.Equal([new AutoHdrTargetStatus(ExeA, AutoHdrTargetState.Applied), new AutoHdrTargetStatus(ExeB, AutoHdrTargetState.Unsupported)], status.Targets);
        Assert.Equal(1, status.Count(AutoHdrTargetState.Applied));
        Assert.Empty(_h.Service.GetStatus(noExe).Targets);
        Assert.Same(AutoHdrGameStatus.None, _h.Service.GetStatus(off));
        Assert.Same(AutoHdrGameStatus.None, _h.Service.GetStatus("Steam:999"));
    }

    [Fact]
    public void Changed_is_raised_only_when_a_status_changes()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        Assert.Equal(1, _h.ChangedEvents);

        _h.TouchUnrelated();
        Assert.Equal(1, _h.ChangedEvents);

        _h.SetAutoHdr(a, false);
        Assert.Equal(2, _h.ChangedEvents);
    }

    [Fact]
    public void Hooks_and_config_changes_do_nothing_before_start()
    {
        var a = _h.AddGame("1", ExeA);
        var service = _h.Service; // nicht gestartet

        _h.TouchUnrelated();
        service.OnStartAnnounced(_h.Current.FindGame(a)!, SessionTrigger.LaunchedViaApp, _h.Clock.UtcNow);

        Assert.Equal(0, _h.Store.Reads);
        Assert.Same(AutoHdrGameStatus.None, service.GetStatus(a));
    }

    [Fact]
    public void Failed_restore_keeps_the_entry_and_retries()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        _h.Store.FailWrites = new IOException("Registry gesperrt");

        _h.SetAutoHdr(a, false);
        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA));
        Assert.Single(_h.State!.Entries);
        Assert.True(_h.Logged("ließ sich nicht zurücknehmen", LogLevel.Warning));

        _h.Store.FailWrites = null;
        _h.TouchUnrelated();
        Assert.False(_h.Store.Values.ContainsKey(ExeA));
        Assert.Null(_h.State);
    }

    [Fact]
    public async Task Rollback_switches_all_games_off_and_counts_what_happened()
    {
        var a = _h.AddGame("1", ExeA);
        var b = _h.AddGame("2", ExeB);
        _h.Store.SetString(ExeB, "GpuPreference=2;");
        _h.Service.Start();
        _h.Store.SetString(ExeB, "AutoHDREnable=2096;"); // extern geändert
        _h.Restart().Start();

        var result = await _h.Service.RollbackAllAsync(Ct);

        Assert.Equal(new AutoHdrRollbackResult(2, 1, 1, 0), result);
        Assert.True(result.Success);
        Assert.False(_h.Current.FindGame(a)!.AutoHdr);
        Assert.False(_h.Current.FindGame(b)!.AutoHdr);
        Assert.False(_h.Store.Values.ContainsKey(ExeA));
        Assert.Equal("AutoHDREnable=2096;", _h.Value(ExeB));
        Assert.Null(_h.State);
        Assert.True(_h.ChangedEvents > 0);

        // Gegenprobe: nichts mehr da.
        Assert.Equal(new AutoHdrRollbackResult(0, 0, 0, 0), await _h.Service.RollbackAllAsync(Ct));
    }

    [Fact]
    public async Task Rollback_counts_failures_once_and_keeps_them()
    {
        _h.AddGame("1", ExeA);
        _h.Service.Start();
        _h.Store.FailWrites = new IOException("Registry gesperrt");

        var result = await _h.Service.RollbackAllAsync(Ct);

        Assert.Equal(new AutoHdrRollbackResult(1, 0, 0, 1), result);
        Assert.False(result.Success);
        Assert.Single(_h.State!.Entries);
    }

    [Fact]
    public async Task Rollback_that_cannot_save_the_config_touches_nothing()
    {
        var a = _h.AddGame("1", ExeA);
        _h.Service.Start();
        var operations = _h.Store.Operations.Count;
        _h.Fs.FailWriting(_h.Paths.ConfigFile, new IOException("Datei gesperrt"));

        var result = await _h.Service.RollbackAllAsync(Ct);

        Assert.NotNull(result.Error);
        Assert.Contains("Datei gesperrt", result.Error, StringComparison.Ordinal);
        Assert.False(result.Success);
        Assert.Equal(operations, _h.Store.Operations.Count);
        Assert.Equal("AutoHDREnable=2097;", _h.Value(ExeA));
        Assert.True(_h.Current.FindGame(a)!.AutoHdr);
        Assert.Single(_h.State!.Entries);
    }

    internal static AutoHdrStateData State(params AutoHdrStateEntry[] entries) => new()
    {
        RegistryKey = AutoHdrRegistryLocation.ForTest("Fake").KeyName,
        Entries = [.. entries],
    };

    internal static AutoHdrStateEntry Entry(string exe, AutoHdrEntryStatus status, string? original, string written) => new()
    {
        ValueName = exe,
        Games = ["Steam:1"],
        Status = status,
        Original = original is null
            ? new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.Missing }
            : new AutoHdrOriginalValue { Kind = GpuPreferenceValueKind.String, Text = original },
        OriginalToken = null,
        Written = written,
        WindowsBuild = 26300,
        WrittenAt = DateTimeOffset.UnixEpoch,
    };
}
