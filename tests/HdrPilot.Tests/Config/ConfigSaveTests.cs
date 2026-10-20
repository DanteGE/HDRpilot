using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>
/// Speichern, während ein anderer Prozess config.json hält (Prüfung 2026-10-20): Windows verweigert dann das atomare Ersetzen.
/// <see cref="FileReplace"/> versucht es nach kurzen Pausen erneut (die echte Platte prüft <c>PhysicalFileSystemTests</c>);
/// bleibt die Datei gesperrt (hier eine Attrappe: ein Leser ohne Freigabe zum Löschen über alle Versuche), wird die Änderung
/// gemeldet und nicht übernommen, nie halb geschrieben, eine eben gelesene fremde Änderung geht nicht verloren, und
/// Aufzeichnungen der Sitzungslogik holt das nächste Speichern nach.
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

    private ConfigService ServiceWithGame()
    {
        var service = Service();
        service.Update(c => c.Games.Add(new GameConfig { Source = GameSource.Manual, SourceId = "a", Name = "A", InstallDir = @"D:\A" }));
        return service;
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
}
