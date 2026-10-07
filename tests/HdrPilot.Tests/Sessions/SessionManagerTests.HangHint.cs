using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Sessions;

/// <summary>
/// Hinweis „Spiel hängt im Hintergrund“. Anlass (G4.5): Palworld lief nach dem Schließen ohne Fenster und ohne CPU
/// weiter; HdrPilot ließ HDR richtigerweise an, der Nutzer wusste aber nicht warum. Geprüft wird alle 30 s, ein Hinweis
/// kommt nach 2 min beobachteter Ruhe aller Prozesse der Sitzung, höchstens einmal je Sitzung.
/// </summary>
public sealed partial class SessionManagerTests
{
    private IEnumerable<SessionNotification> HangHints => _notifications.Where(n => n.Kind == NotificationKind.GameSeemsHung);

    /// <summary>Startet Spiel A (erster Zyklus um t0, zugleich die erste Messung); der Prozess verbraucht keine CPU-Zeit.</summary>
    private async Task<int> StartQuietGame()
    {
        _config.AddGame("1", DirA);
        await Poll();
        var pid = _processes.StartExe(ExeA);
        _activity.CpuTime[pid] = 5_000_000;
        return pid;
    }

    [Fact]
    public async Task Game_without_window_and_cpu_gets_one_hint_after_two_minutes_and_keeps_hdr()
    {
        await StartQuietGame();

        // Prüfungen bei t0 (erste Messung), 30 s (ruhig seit hier), 60, 90, 120: noch keine 2 min beobachtet.
        await Poll(150);
        Assert.Empty(HangHints);

        await Poll(); // 150 s: seit 120 s ruhig
        var hint = Assert.Single(HangHints);
        Assert.Equal(
            "„Spiel 1“ scheint im Hintergrund zu hängen (kein Fenster, keine Aktivität). HDR bleibt an, bis das Spiel beendet ist. "
            + "Zum Beenden den Task-Manager nutzen, oder im Infobereich „HDR jetzt ausschalten“.",
            hint.Message);
        Assert.Equal("Spiel hängt im Hintergrund?", hint.Title);
        Assert.True(_log.Contains(LogLevel.Information, "\"Spiel 1\" scheint im Hintergrund zu hängen: seit 120 s"));

        await Poll(30 * 60);
        Assert.Single(HangHints); // höchstens einmal je Sitzung
        Assert.True(PrimaryOn); // nichts zurückgestellt, nichts beendet
        Assert.Equal(SessionPhase.Running, Assert.Single(Manager.Sessions).Phase);
        Assert.Equal(0, _hdr.SetCalls(enable: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activity_restarts_the_two_minutes(bool pidReused)
    {
        var pid = await StartQuietGame();
        await Poll(100); // ruhig seit 30 s

        if (pidReused)
        {
            _activity.CreateTime[pid] = 2; // gleiche PID, anderer Prozess: zählt nie als ruhig
        }
        else
        {
            _activity.Burn(pid);
        }

        // 120 s: aktiv; ruhig wieder ab 150 s, also Hinweis erst bei 270 s.
        await Poll(170);
        Assert.Empty(HangHints);
        await Poll();
        Assert.Single(HangHints);
    }

    [Fact]
    public async Task Visible_or_minimized_window_means_no_hint()
    {
        var pid = await StartQuietGame();
        _activity.Windows.Add(pid); // etwa ein minimiertes, angehaltenes Xbox-Spiel: Fenster sichtbar, keine CPU

        await Poll(10 * 60);
        Assert.Empty(HangHints);
        Assert.True(_activity.WindowQueries > 0);

        _activity.Windows.Remove(pid); // Fenster weg, Prozess bleibt (wie Palworld)
        await Poll(120); // Prüfung bei 600 s: ruhig ab hier
        Assert.Empty(HangHints);
        await Poll();
        Assert.Single(HangHints);
    }

    [Fact]
    public async Task All_processes_of_the_game_must_be_quiet()
    {
        var game = await StartQuietGame();
        var server = _processes.StartExe(DirA + @"\bin\x64\SpielA_Server.exe");
        _activity.CpuTime[server] = 0;

        for (var i = 0; i < 60; i++)
        {
            _activity.Burn(server); // ein Prozess des Spiels arbeitet noch
            await Poll(10);
        }

        Assert.Empty(HangHints);
        Assert.Equal(2, Assert.Single(Manager.Sessions).ProcessIds.Count);
        Assert.Contains(game, Assert.Single(Manager.Sessions).ProcessIds);

        await Poll(180); // jetzt sind beide ruhig
        Assert.Single(HangHints);
    }

    [Fact]
    public async Task Check_runs_only_while_a_game_runs_and_every_30_seconds()
    {
        _config.AddGame("1", DirA);
        await Poll(10 * 60); // kein Spiel: nichts wird abgefragt
        Assert.Equal(0, _activity.CpuQueries);

        var pid = _processes.StartExe(ExeA);
        for (var i = 0; i < 120; i++)
        {
            _activity.Burn(pid); // das Spiel arbeitet
            await Poll();
        }

        Assert.Equal(4, _activity.CpuQueries); // bei 0, 30, 60 und 90 s
        Assert.Equal(0, _activity.WindowQueries); // Fenster nur bei Spielen ohne CPU-Zuwachs
        Assert.Equal(0, _activity.Releases);

        _processes.Stop(pid);
        await Poll(); // Nachlaufzeit: keine Prüfung mehr, Puffer frei
        Assert.Equal(4, _activity.CpuQueries);
        Assert.Equal(1, _activity.Releases);

        await Poll(10 * 60);
        Assert.Equal(4, _activity.CpuQueries);
        Assert.Equal(1, _activity.Releases);
        Assert.Empty(HangHints);
    }

    [Fact]
    public async Task Hint_comes_once_per_session_even_after_activity_in_between()
    {
        var pid = await StartQuietGame();
        await Poll(151);
        Assert.Single(HangHints);

        _activity.Burn(pid);
        await Poll(30);
        Assert.True(_log.Contains(LogLevel.Information, "\"Spiel 1\" ist wieder aktiv"));

        await Poll(10 * 60); // wieder ruhig, länger als 2 min
        Assert.Single(HangHints);
    }

    [Fact]
    public async Task Hint_waits_while_a_fullscreen_app_runs_so_it_is_not_swallowed()
    {
        // Im Vollbild zeigt das Tray keine Hinweise an; der einzige Hinweis der Sitzung käme sonst nie beim Nutzer an.
        await StartQuietGame();
        _fullscreen = true;

        await Poll(151);
        Assert.Empty(HangHints);
        Assert.Equal(1, _suppressedQueries); // gefragt wird erst, wenn der Hinweis fällig ist

        await Poll(60);
        Assert.Empty(HangHints);

        _fullscreen = false;
        await Poll(30);
        Assert.Single(HangHints);
        Assert.True(_log.Contains(LogLevel.Information, "\"Spiel 1\" scheint im Hintergrund zu hängen: seit 210 s"));
    }

    [Fact]
    public async Task Hint_waits_while_the_screen_is_locked_so_it_is_not_swallowed()
    {
        // Gesperrter Bildschirm oder Bildschirmschoner: das Tray zeigt auch dann nichts an (nur Protokoll). Genau so
        // bleibt ein hängendes Spiel unbemerkt (Nutzer beendet das Spiel und geht weg); der Hinweis kommt bei der Rückkehr.
        await StartQuietGame();
        _userAway = true;

        await Poll(151);
        Assert.Empty(HangHints);
        await Poll(10 * 60);
        Assert.Empty(HangHints);
        Assert.Equal(0, _fullscreenQueries); // Vollbild allein reicht als Frage nicht

        _userAway = false;
        await Poll(30);
        Assert.Single(HangHints);
    }

    [Fact]
    public async Task Failing_notification_state_query_does_not_hold_the_hint_back()
    {
        _options = new SessionManagerOptions
        {
            ActivityProbe = _activity,
            FullscreenAppRunning = () => false,
            NotificationsSuppressed = () => throw new InvalidOperationException("Shell nicht erreichbar"),
        };
        await StartQuietGame();

        await Poll(151);

        Assert.Single(HangHints);
        Assert.True(_log.Contains(LogLevel.Warning, "Hinweise werden angezeigt“ fehlgeschlagen"));
    }

    [Fact]
    public async Task Unreadable_cpu_time_never_raises_a_hint()
    {
        _config.AddGame("1", DirA);
        await Poll();
        _processes.StartExe(ExeA); // keine CPU-Zeit lesbar (Abfrage gescheitert)

        await Poll(10 * 60);

        Assert.Empty(HangHints);
        Assert.Equal(0, _activity.WindowQueries);
    }

    [Fact]
    public async Task A_new_session_of_the_same_game_can_hint_again()
    {
        var pid = await StartQuietGame();
        await Poll(151);
        Assert.Single(HangHints);

        _processes.Stop(pid); // im Task-Manager beendet: Sitzung endet nach der Nachlaufzeit
        await Poll(12);
        Assert.Empty(Manager.Sessions);
        Assert.False(PrimaryOn);

        var again = _processes.StartExe(ExeA);
        _activity.CpuTime[again] = 0;
        await Poll(151);
        Assert.Equal(2, HangHints.Count());
    }
}
