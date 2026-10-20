using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hosting;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Config;

/// <summary>
/// Speichern, während ein anderer Prozess config.json hält (Prüfung 2026-10-20): Windows verweigert dann das atomare Ersetzen.
/// <see cref="FileReplace"/> versucht es nach kurzen Pausen erneut (die echte Platte prüft <c>PhysicalFileSystemTests</c>);
/// bleibt die Datei gesperrt (hier eine Attrappe: ein Leser ohne Freigabe zum Löschen über alle Versuche), wird die Änderung
/// gemeldet und nicht übernommen, nie halb geschrieben, eine eben gelesene fremde Änderung geht nicht verloren, und
/// Aufzeichnungen der Sitzungslogik holt das nächste Speichern nach, spätestens ein einmaliger Versuch nach
/// <see cref="ConfigService.PendingRetryDelay"/> oder das Beenden.
/// </summary>
public sealed class ConfigSaveTests
{
    private const string Path = @"C:\Users\Test\AppData\Roaming\HdrPilot\config.json";
    private const string GameKey = "Manual:a";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    private ConfigService Service() => new(new ConfigStore(_fs, Path, _clock), _clock);

    /// <summary>Ein Leser hält die Datei ohne Freigabe zum Löschen: Ersetzen scheitert wie unter Windows mit „Zugriff verweigert“.</summary>
    private void HoldByReader(bool held) => _fs.FailDeleting(Path, held ? new UnauthorizedAccessException("Der Zugriff auf den Pfad wurde verweigert.") : null);

    private ConfigService ServiceWithGame(IClock? clock = null, ILogger? logger = null)
    {
        var service = clock is null ? Service() : new ConfigService(new ConfigStore(_fs, Path, clock), clock, logger);
        service.Update(c => c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "a", Name = "A", InstallDir = @"D:\A" }));
        return service;
    }

    /// <summary>Was auf der Platte steht (frisch gelesen).</summary>
    private GameConfig Saved() => Service().Current.FindGame(GameKey)!;

    /// <summary>Wartet, bis die Bedingung gilt (das erneute Speichern läuft hinter einer Wartezeit), längstens 5 s.</summary>
    private static void Eventually(Func<bool> condition, string what)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Nicht eingetreten: " + what);
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void ConfigSave_replace_retries_while_a_reader_holds_the_file()
    {
        // Attrappe des Ersetzens: Ein Leser hält die Datei für drei Versuche, dann ist er fertig.
        var attempts = 0;
        var waited = new List<TimeSpan>();

        FileReplace.Run(
            () =>
            {
                if (++attempts <= 3)
                {
                    throw new UnauthorizedAccessException("Zugriff verweigert");
                }
            },
            waited.Add);

        Assert.Equal(4, attempts);
        Assert.Equal(FileReplace.RetryDelays.Take(3), waited);
    }

    [Fact]
    public void ConfigSave_replace_gives_up_after_the_last_pause()
    {
        var attempts = 0;
        var waited = new List<TimeSpan>();
        var sharing = new IOException("Freigabeverletzung", unchecked((int)0x80070020));

        var ex = Assert.Throws<IOException>(() => FileReplace.Run(
            () =>
            {
                attempts++;
                throw sharing;
            },
            waited.Add));

        Assert.Same(sharing, ex);
        Assert.Equal(FileReplace.RetryDelays.Count + 1, attempts);
        Assert.Equal(FileReplace.RetryDelays, waited);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), FileReplace.MaxWait); // kurz genug für ein Speichern auf dem UI-Thread
        Assert.True(FileReplace.RetryDelays.SequenceEqual(FileReplace.RetryDelays.Order()), "Pausen wachsen");
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070))] // Platte voll
    [InlineData(unchecked((int)0x80070003))] // Pfad nicht gefunden
    [InlineData(unchecked((int)0x80004005))] // unbekannter Fehler
    public void ConfigSave_replace_does_not_retry_errors_that_a_second_try_cannot_fix(int hresult)
    {
        var attempts = 0;
        var waited = new List<TimeSpan>();

        Assert.Throws<IOException>(() => FileReplace.Run(
            () =>
            {
                attempts++;
                throw new IOException("Fehler", hresult);
            },
            waited.Add));

        Assert.Equal(1, attempts);
        Assert.Empty(waited);
        Assert.True(FileReplace.IsLocked(new IOException("Sperrverletzung", unchecked((int)0x80070021))));
        Assert.False(FileReplace.IsLocked(new FileNotFoundException("weg")));
    }

    [Fact]
    public void ConfigSave_blocked_by_a_reader_is_reported_and_leaves_the_file_whole()
    {
        var service = ServiceWithGame();
        var before = _fs.ReadAllBytes(Path);
        var revision = service.Revision;
        HoldByReader(true);

        var ex = Assert.Throws<ConfigSaveException>(() => service.Update(c => c.Settings.GracePeriodSeconds = 44));

        Assert.Contains("verweigert", ex.Message, StringComparison.Ordinal);
        Assert.Equal(10, service.Current.Settings.GracePeriodSeconds); // nicht übernommen, der Aufrufer meldet es
        Assert.Equal(revision, service.Revision);
        Assert.Equal(before, _fs.ReadAllBytes(Path)); // nicht halb geschrieben

        HoldByReader(false);
        service.Update(c => c.Settings.GracePeriodSeconds = 44); // erneut speichern geht
        Assert.Equal(44, Service().Current.Settings.GracePeriodSeconds);
    }

    [Fact]
    public void ConfigSave_session_records_blocked_by_a_reader_are_caught_up_with_the_next_save()
    {
        // „Zuletzt gespielt“ und gemerkte EXEs schreibt die Sitzungslogik im Hintergrund. Scheitert das, wirft es nicht und
        // geht nicht still verloren: Das nächste Speichern (hier eine Einstellung) nimmt beides mit.
        var service = ServiceWithGame();
        var played = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);
        HoldByReader(true);

        service.RecordLastPlayed(GameKey, played);
        service.RecordDetectedExecutables(GameKey, [@"D:\A\a.exe", @"D:\A\b.exe"]);
        service.RecordDetectedExecutables(GameKey, [@"d:\a\A.EXE"]); // dieselbe, anders geschrieben
        Assert.Null(service.Current.FindGame(GameKey)!.LastPlayed);
        Assert.Empty(service.Current.FindGame(GameKey)!.DetectedExecutables);

        HoldByReader(false);
        service.Update(c => c.Settings.GracePeriodSeconds = 44);

        var saved = Service().Current;
        Assert.Equal(44, saved.Settings.GracePeriodSeconds);
        Assert.Equal(played, saved.FindGame(GameKey)!.LastPlayed);
        Assert.Equal([@"D:\A\a.exe", @"D:\A\b.exe"], saved.FindGame(GameKey)!.DetectedExecutables);

        // Einmal nachgeholt ist erledigt: „Neu lernen“ danach bleibt leer.
        service.UpdateGame(GameKey, g => g.DetectedExecutables.Clear());
        service.Update(c => c.Settings.GracePeriodSeconds = 45);
        Assert.Empty(Service().Current.FindGame(GameKey)!.DetectedExecutables);
    }

    [Fact]
    public void ConfigSave_session_records_are_saved_again_once_after_a_pause()
    {
        // Prüfung 2026-10-20: Eine Aufzeichnung, deren Speichern scheiterte, lag nur im Speicher, bis zufällig etwas anderes
        // gespeichert wurde (stürzte die App vorher ab, war sie verloren). Jetzt wird nach PendingRetryDelay einmal erneut
        // gespeichert, für alles Offene zusammen.
        var clock = new ManualDelayClock();
        var service = ServiceWithGame(clock);
        var played = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);
        HoldByReader(true);

        service.RecordLastPlayed(GameKey, played);
        service.RecordDetectedExecutables(GameKey, [@"D:\A\a.exe"]);

        Assert.Equal([ConfigService.PendingRetryDelay], clock.Requested); // ein Zeitgeber für beide
        Assert.Equal(TimeSpan.FromSeconds(30), ConfigService.PendingRetryDelay);
        Assert.Null(Saved().LastPlayed);

        HoldByReader(false);
        clock.Elapse();

        Eventually(() => Saved().LastPlayed == played, "erneut gespeichert");
        Assert.Equal([@"D:\A\a.exe"], Saved().DetectedExecutables);
        Assert.Equal(played, service.Current.FindGame(GameKey)!.LastPlayed);
        Assert.True(service.FlushPending()); // nichts mehr offen
        Assert.Single(clock.Requested);
    }

    [Theory]
    [InlineData("zuletzt gespielt")]
    [InlineData("gemerkte EXEs")]
    public void ConfigSave_each_kind_of_failed_session_record_gets_the_retry(string record)
    {
        // Jede Aufzeichnung für sich: Scheitert nur sie, startet sie den einen Versuch.
        var clock = new ManualDelayClock();
        var service = ServiceWithGame(clock);
        var played = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);
        HoldByReader(true);
        if (record == "zuletzt gespielt")
        {
            service.RecordLastPlayed(GameKey, played);
        }
        else
        {
            service.RecordDetectedExecutables(GameKey, [@"D:\A\a.exe"]);
        }

        Assert.Equal([ConfigService.PendingRetryDelay], clock.Requested);
        HoldByReader(false);
        clock.Elapse();

        Eventually(() => record == "zuletzt gespielt" ? Saved().LastPlayed == played : Saved().DetectedExecutables.Count == 1, "erneut gespeichert: " + record);
    }

    [Fact]
    public void ConfigSave_session_records_retry_once_and_are_flushed_at_shutdown()
    {
        // Bleibt die Datei gesperrt, scheitert auch der eine Versuch; danach startet kein weiterer Zeitgeber (keine Schleife bei
        // einer dauerhaft gesperrten Datei). Das Beenden (FlushPending) speichert, was offen ist; eine neue gescheiterte
        // Aufzeichnung bekommt wieder einen Versuch.
        var clock = new ManualDelayClock();
        var log = new ListLogger<ConfigService>();
        var service = ServiceWithGame(clock, log);
        var played = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);
        HoldByReader(true);
        service.RecordLastPlayed(GameKey, played);

        clock.Elapse();

        Eventually(() => log.Contains(LogLevel.Warning, "weiter nicht gespeichert"), "Versuch gescheitert");
        Assert.Single(clock.Requested);
        Assert.Equal(0, clock.Waiting);
        Assert.Null(Saved().LastPlayed);
        Assert.False(service.FlushPending());

        HoldByReader(false);
        var writes = _fs.ReadAllText(Path);
        Assert.True(service.FlushPending()); // beim Beenden
        Assert.Equal(played, Saved().LastPlayed);
        Assert.NotEqual(writes, _fs.ReadAllText(Path));

        // Nichts offen: Beenden schreibt nichts.
        var revision = service.Revision;
        Assert.True(service.FlushPending());
        Assert.Equal(revision, service.Revision);

        HoldByReader(true);
        service.RecordLastPlayed(GameKey, played.AddHours(1));
        Assert.Equal(2, clock.Requested.Count);
        Assert.Equal(1, clock.Waiting);
    }

    [Fact]
    public async Task ConfigSave_session_records_are_flushed_when_the_automation_host_ends()
    {
        // Verdrahtung: Beenden im Tray und das Ende von „hdrpilot watch“ (AutomationHost.DisposeAsync) speichern, was offen
        // ist. Ablage im Speicher, Protokolle in einem Temp-Ordner; die Überwachung läuft nicht, es wird nie HDR geschaltet.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hdrpilot-config-flush-" + Guid.NewGuid().ToString("N")[..8]);
        var paths = AppPaths.InDirectory(dir);
        var played = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);
        try
        {
            await using (var host = new AutomationHost(paths, _fs, new FakeRegistry(), new FakeEnvironment(), _clock, autoHdrStore: new FakeGpuPreferenceStore()))
            {
                host.Config.Update(c => c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "a", Name = "A", InstallDir = @"D:\A" }));
                _fs.FailDeleting(paths.ConfigFile, new UnauthorizedAccessException("Der Zugriff auf den Pfad wurde verweigert."));
                host.Config.RecordLastPlayed(GameKey, played); // scheitert, der eine Versuch (Uhr der Tests: sofort) auch
                Assert.Null(Read().LastPlayed);
                _fs.FailDeleting(paths.ConfigFile, null);
            }

            Assert.Equal(played, Read().LastPlayed);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        GameConfig Read() => new ConfigService(new ConfigStore(_fs, paths.ConfigFile, _clock), _clock).Current.FindGame(GameKey)!;
    }

    [Fact]
    public void ConfigSave_caught_up_last_played_never_replaces_a_newer_one()
    {
        // Ein anderer Prozess hat inzwischen ein neueres „zuletzt gespielt“ gespeichert; das nachgeholte ältere überschreibt es
        // nicht. Ebenso behält die Warteschlange von zwei gescheiterten Aufzeichnungen die neuere.
        var service = ServiceWithGame();
        var older = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);
        var newer = older.AddHours(2);
        HoldByReader(true);
        service.RecordLastPlayed(GameKey, older);
        HoldByReader(false);
        Service().UpdateGame(GameKey, g => g.LastPlayed = newer);

        service.Update(c => c.Settings.GracePeriodSeconds = 44); // liest die fremde Änderung, holt dabei das ältere nach

        Assert.Equal(newer, Service().Current.FindGame(GameKey)!.LastPlayed);

        var again = ServiceWithGame();
        HoldByReader(true);
        again.RecordLastPlayed(GameKey, newer.AddHours(1));
        again.RecordLastPlayed(GameKey, older); // später gemeldet, aber älter
        HoldByReader(false);
        again.Update(c => c.Settings.GracePeriodSeconds = 45);
        Assert.Equal(newer.AddHours(1), Service().Current.FindGame(GameKey)!.LastPlayed);
    }

    [Fact]
    public void ConfigSave_external_change_read_before_a_failed_save_is_not_lost()
    {
        // Die Kommandozeile fügt ein Spiel hinzu; das nächste Speichern der App liest das zuerst und scheitert dann. Danach
        // muss die App die fremde Änderung weiter als neu erkennen, sonst schriebe ihr nächstes Speichern den alten Stand
        // darüber.
        var app = ServiceWithGame();
        var cli = Service();
        cli.Update(c => c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "b", Name = "B", InstallDir = @"D:\B" }));
        HoldByReader(true);
        Assert.Throws<ConfigSaveException>(() => app.Update(c => c.Settings.GracePeriodSeconds = 44));
        HoldByReader(false);

        Assert.True(app.ReloadIfChangedOnDisk());
        Assert.NotNull(app.Current.FindGame("Manual:b"));
        app.Update(c => c.Settings.GracePeriodSeconds = 45);
        var saved = Service().Current;
        Assert.NotNull(saved.FindGame("Manual:b"));
        Assert.Equal(45, saved.Settings.GracePeriodSeconds);
    }

    [Fact]
    public void ConfigSave_external_change_survives_a_mutation_that_throws()
    {
        // Wie oben, nur scheitert die Änderung selbst (UpdateGame eines inzwischen entfernten Spiels).
        var app = ServiceWithGame();
        var cli = Service();
        cli.Update(c => c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "b", Name = "B", InstallDir = @"D:\B" }));

        Assert.Throws<KeyNotFoundException>(() => app.UpdateGame("Manual:fehlt", g => g.HdrAuto = true));

        app.Update(c => c.Settings.GracePeriodSeconds = 45);
        Assert.NotNull(Service().Current.FindGame("Manual:b"));
    }

    /// <summary>Uhr, deren Wartezeiten erst enden, wenn der Test sie ablaufen lässt (Zeitgeber des erneuten Speicherns).</summary>
    private sealed class ManualDelayClock : IClock
    {
        private readonly List<TaskCompletionSource> _waiting = [];

        public DateTimeOffset UtcNow { get; } = new(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);

        /// <summary>Angefragte Wartezeiten, in der Reihenfolge.</summary>
        public List<TimeSpan> Requested { get; } = [];

        public int Waiting
        {
            get
            {
                lock (_waiting)
                {
                    return _waiting.Count;
                }
            }
        }

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            var done = new TaskCompletionSource();
            lock (_waiting)
            {
                Requested.Add(delay);
                _waiting.Add(done);
            }

            return done.Task;
        }

        /// <summary>Lässt alle wartenden Zeitgeber ablaufen.</summary>
        public void Elapse()
        {
            List<TaskCompletionSource> due;
            lock (_waiting)
            {
                due = [.. _waiting];
                _waiting.Clear();
            }

            foreach (var done in due)
            {
                done.SetResult();
            }
        }
    }
}
