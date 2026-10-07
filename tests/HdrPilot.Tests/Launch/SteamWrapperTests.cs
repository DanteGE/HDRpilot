using HdrPilot.Core.Launch;

namespace HdrPilot.Tests.Launch;

/// <summary>
/// Ablauf der Steam-Startoption mit Attrappen für Instanz, Spielstart und Umgebung; die Rückmeldung läuft über echte
/// benannte Ereignisse (die Attrappe der Instanz antwortet wie die App über <see cref="PrepareSignal"/>).
/// </summary>
public sealed class SteamWrapperTests : IDisposable
{
    private const string Exe = @"D:\Steam Library\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";
    private const string GameDir = @"D:\Steam Library\steamapps\common\Cyberpunk 2077";
    private const string Raw = @"""C:\Program Files\HdrPilot\HdrPilot.exe"" --wrap -- """ + Exe + @""" -skipStartScreen";

    private readonly string _logDir = Path.Combine(Path.GetTempPath(), "HdrPilotTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _events = [];
    private readonly FakeChannel _channel;
    private readonly FakeStarter _starter;
    private readonly FakeContext _context;

    public SteamWrapperTests()
    {
        _channel = new FakeChannel(_events);
        _starter = new FakeStarter(_events);
        _context = new FakeContext(_events);
        _context.Environment["SteamAppId"] = "1091500";
    }

    private WrapLog Log => new(_logDir);

    public void Dispose()
    {
        if (Directory.Exists(_logDir))
        {
            Directory.Delete(_logDir, recursive: true);
        }
    }

    private WrapOutcome Run(string raw = Raw, WrapOptions? options = null) =>
        new SteamWrapper(_channel, _starter, _context, Log, options ?? new WrapOptions { ReadyTimeout = TimeSpan.FromSeconds(5) })
            .Run(WrapCommandLine.TryParse(raw)!);

    private string[] LogLines() => File.ReadAllLines(Path.Combine(_logDir, WrapLog.FileName));

    [Fact]
    public void Running_instance_prepares_then_wrapper_settles_then_starts_game()
    {
        var outcome = Run();

        Assert.True(outcome.Prepared);
        Assert.Equal("Steam:1091500", outcome.GameKey);
        var sent = Assert.Single(_channel.Sent);
        Assert.Equal(4, sent.Count);
        Assert.Equal(["--prepare", "Steam:1091500", "--ready-event"], sent.Take(3));
        Assert.True(PrepareSignal.IsValidName(sent[3]));
        Assert.DoesNotContain("--wrap", sent);
        Assert.Equal(["laeuft?", "senden", "warten 1.5 s", "starten"], _events);
        Assert.Equal([TimeSpan.FromSeconds(1.5)], _context.Sleeps);

        var started = Assert.Single(_starter.Started);
        Assert.Equal(Exe, started.Game.ExePath);
        Assert.Equal("-skipStartScreen", started.Game.Arguments);
        Assert.Equal(GameDir, started.WorkingDirectory); // geerbt
        Assert.True(outcome.Started);
    }

    [Fact]
    public void No_running_instance_starts_game_immediately_without_forwarding()
    {
        _channel.Running = false;

        var outcome = Run();

        Assert.False(outcome.Prepared);
        Assert.Contains("HdrPilot läuft nicht", outcome.PrepareStatus, StringComparison.Ordinal);
        Assert.Empty(_channel.Sent);
        Assert.Empty(_context.Sleeps);
        Assert.Single(_starter.Started);
    }

    [Fact]
    public void Missing_steam_app_id_starts_game_immediately_without_asking_the_instance()
    {
        _context.Environment.Clear();

        var outcome = Run();

        Assert.Contains("keine SteamAppId", outcome.PrepareStatus, StringComparison.Ordinal);
        Assert.Equal(["starten"], _events);
        Assert.Single(_starter.Started);
    }

    [Fact]
    public void Steam_game_id_is_the_fallback()
    {
        _context.Environment.Clear();
        _context.Environment["SteamGameId"] = "480";

        Run();

        Assert.Equal("Steam:480", Assert.Single(_channel.Sent)[1]);
    }

    [Fact]
    public void Unknown_game_is_answered_with_skip_and_starts_without_settle_time()
    {
        _channel.Answer = false;

        var outcome = Run();

        Assert.False(outcome.Prepared);
        Assert.Contains("nichts vorbereitet", outcome.PrepareStatus, StringComparison.Ordinal);
        Assert.Empty(_context.Sleeps);
        Assert.Equal(["laeuft?", "senden", "starten"], _events);
    }

    [Fact]
    public void Without_answer_the_game_starts_after_the_timeout()
    {
        _channel.Answer = null;

        var outcome = Run(options: new WrapOptions { ReadyTimeout = TimeSpan.FromMilliseconds(150) });

        Assert.False(outcome.Prepared);
        Assert.Contains("keine Antwort", outcome.PrepareStatus, StringComparison.Ordinal);
        Assert.True(outcome.Waited >= TimeSpan.FromMilliseconds(100));
        Assert.Empty(_context.Sleeps);
        Assert.Single(_starter.Started);
    }

    [Fact]
    public void Default_timeouts_are_eight_and_one_and_a_half_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(8), WrapOptions.Default.ReadyTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1.5), WrapOptions.Default.SettleTime);
    }

    [Fact]
    public void Rejected_forward_starts_game_immediately()
    {
        _channel.Accept = false;

        var outcome = Run();

        Assert.Contains("nicht angenommen", outcome.PrepareStatus, StringComparison.Ordinal);
        Assert.Empty(_context.Sleeps);
        Assert.Single(_starter.Started);
    }

    [Fact]
    public void Error_while_preparing_still_starts_the_game()
    {
        _channel.Throw = new InvalidOperationException("Fenster weg");

        var outcome = Run();

        Assert.Contains("Fenster weg", outcome.PrepareStatus, StringComparison.Ordinal);
        Assert.True(outcome.Started);
        Assert.Single(_starter.Started);
    }

    [Fact]
    public void Start_failure_is_logged_and_never_thrown()
    {
        _starter.Throw = new System.ComponentModel.Win32Exception(2, "Datei nicht gefunden");

        var outcome = Run();

        Assert.False(outcome.Started);
        Assert.Contains("Spiel gestartet: nein (Datei nicht gefunden)", Assert.Single(LogLines()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\WINDOWS\system32")]
    [InlineData(@"C:\Program Files\HdrPilot")]
    [InlineData("")]
    public void Unusable_inherited_folder_is_replaced_by_the_game_folder(string current)
    {
        _context.CurrentDirectory = current;

        var outcome = Run();

        Assert.Equal(GameDir + @"\bin\x64", outcome.WorkingDirectory);
        Assert.Equal(GameDir + @"\bin\x64", Assert.Single(_starter.Started).WorkingDirectory);
    }

    [Fact]
    public void Writes_exactly_one_line_per_run_with_all_details()
    {
        Run();
        _channel.Running = false;
        Run();

        var lines = LogLines();
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("2026-10-07 18:30:00.000", lines[0], StringComparison.Ordinal);
        Assert.Contains("SteamAppId: 1091500", lines[0], StringComparison.Ordinal);
        Assert.Contains("HDR vorbereitet: ja", lines[0], StringComparison.Ordinal);
        Assert.Matches(@"gewartet: \d+ ms", lines[0]);
        Assert.Contains("Arbeitsordner: " + GameDir, lines[0], StringComparison.Ordinal);
        Assert.Contains("Spiel gestartet: ja", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("Befehlszeile: " + Raw, lines[0], StringComparison.Ordinal);
        Assert.Contains("HDR vorbereitet: nein (HdrPilot läuft nicht)", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_game_command_starts_nothing_but_logs()
    {
        var outcome = Run(@"""C:\HdrPilot\HdrPilot.exe"" --wrap --");

        Assert.False(outcome.Started);
        Assert.Empty(_starter.Started);
        Assert.Empty(_channel.Sent);
        Assert.Contains("Spiel gestartet: nein (kein Spielbefehl)", Assert.Single(LogLines()), StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_option_without_separator_still_starts_the_game_and_notes_it()
    {
        var outcome = Run(@"""C:\HdrPilot\HdrPilot.exe"" --wrap """ + Exe + @""" -x");

        Assert.True(outcome.Started);
        Assert.Equal("-x", Assert.Single(_starter.Started).Game.Arguments);
        Assert.Contains("ohne \"--\"", Assert.Single(LogLines()), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_log_folder_does_not_stop_the_game()
    {
        var blocked = Path.Combine(_logDir, "datei");
        Directory.CreateDirectory(_logDir);
        File.WriteAllText(blocked, "kein Ordner");

        var outcome = new SteamWrapper(_channel, _starter, _context, new WrapLog(blocked)).Run(WrapCommandLine.TryParse(Raw)!);

        Assert.True(outcome.Started);
    }

    private sealed class FakeChannel(List<string> events) : IWrapInstanceChannel
    {
        public bool Running { get; set; } = true;

        public bool Accept { get; set; } = true;

        /// <summary>True: „bereit“, false: „nichts vorbereitet“, null: keine Antwort.</summary>
        public bool? Answer { get; set; } = true;

        public Exception? Throw { get; set; }

        public List<IReadOnlyList<string>> Sent { get; } = [];

        public bool IsInstanceRunning()
        {
            events.Add("laeuft?");
            return Running;
        }

        public bool TrySend(IReadOnlyList<string> args)
        {
            events.Add("senden");
            if (Throw is not null)
            {
                throw Throw;
            }

            Sent.Add(args);
            if (!Accept)
            {
                return false;
            }

            if (Answer is { } prepared)
            {
                // Wie die App: Antwort über das Ereignis aus --ready-event.
                Assert.True(PrepareSignal.TrySignal(args[3], prepared));
            }

            return true;
        }
    }

    private sealed class FakeStarter(List<string> events) : IWrapGameStarter
    {
        public List<(GameCommand Game, string WorkingDirectory)> Started { get; } = [];

        public Exception? Throw { get; set; }

        public void Start(GameCommand game, string workingDirectory)
        {
            events.Add("starten");
            if (Throw is not null)
            {
                throw Throw;
            }

            Started.Add((game, workingDirectory));
        }
    }

    private sealed class FakeContext(List<string> events) : IWrapContext
    {
        public Dictionary<string, string> Environment { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<TimeSpan> Sleeps { get; } = [];

        public string CurrentDirectory { get; set; } = GameDir;

        public string OwnFolder => @"C:\Program Files\HdrPilot\";

        public DateTimeOffset Now => new(2026, 10, 7, 18, 30, 0, TimeSpan.FromHours(2));

        public string? GetEnvironmentVariable(string name) => Environment.GetValueOrDefault(name);

        public bool FileExists(string path) => false;

        public void Sleep(TimeSpan duration)
        {
            events.Add("warten " + duration.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s");
            Sleeps.Add(duration);
        }
    }
}
