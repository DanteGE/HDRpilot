using HdrPilot.Cli;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners.Common;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Resources;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Localization;

/// <summary>
/// Englische Ausgaben von Kern und Kommandozeile (E95): Die Satellitenassemblies werden gefunden, Meldungen aus dem Code
/// folgen der Sprache, und keine Logik hängt mehr am deutschen Wortlaut einer Meldung.
/// </summary>
public sealed class EnglishOutputTests
{
    private const string ConfigFile = @"D:\Ablage\HDRpilot\config.json";
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Core_texts_are_english_in_english_and_german_otherwise()
    {
        using (TestCulture.Use("en-US"))
        {
            Assert.Equal("HDR turned on", CoreStrings.NotifyHdrEnabledTitle);
            Assert.Equal("on", CoreStrings.WordOn);
            Assert.Equal("Steam manifest damaged: {0}", ScanStrings.Steam_ManifestDamaged);
        }

        using (TestCulture.Use("en-GB"))
        {
            Assert.Equal("HDR turned on", CoreStrings.NotifyHdrEnabledTitle); // fällt auf „en“ zurück
        }

        using (TestCulture.Use("fr-FR"))
        {
            Assert.Equal("HDR eingeschaltet", CoreStrings.NotifyHdrEnabledTitle); // nur de und en: sonst neutral (Deutsch)
        }

        Assert.Equal("HDR eingeschaltet", CoreStrings.NotifyHdrEnabledTitle);
        Assert.Equal("an", CoreStrings.WordOn);
    }

    [Fact]
    public void Scanner_messages_follow_the_language()
    {
        using (TestCulture.Use("en-US"))
        {
            Assert.Null(InstallPaths.NormalizeInstallDir("Spiele\\relativ", out var problem));
            Assert.Equal("Install folder \"Spiele\\relativ\" isn't an absolute path.", problem);
        }

        Assert.Null(InstallPaths.NormalizeInstallDir("Spiele\\relativ", out var german));
        Assert.Equal("Installationsordner \"Spiele\\relativ\" ist kein absoluter Pfad.", german);
    }

    [Fact]
    public void Adding_a_game_explains_problems_in_english()
    {
        var fs = new InMemoryFileSystem();
        using (TestCulture.Use("en-US"))
        {
            Assert.StartsWith("No path was given.", Assert.Throws<ArgumentException>(() => ManualGames.Create(fs, " ")).Message, StringComparison.Ordinal);
            var error = Assert.Throws<ArgumentException>(() => ManualGames.Create(fs, @"Spielordner\Testspiel"));
            Assert.StartsWith("The path must be complete (with drive), not relative: \"Spielordner\\Testspiel\".", error.Message, StringComparison.Ordinal);
        }

        var deutsch = Assert.Throws<ArgumentException>(() => ManualGames.Create(fs, @"Spielordner\Testspiel"));
        Assert.StartsWith("Der Pfad muss vollständig sein (mit Laufwerk), nicht relativ: \"Spielordner\\Testspiel\".", deutsch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Crashed_scanner_keeps_its_games_whatever_the_language()
    {
        // Früher erkannte das Einlesen einen Absturz am deutschen Text „Scanner abgestürzt“; auf Englisch wären die Spiele
        // dieser Quelle sonst als nicht installiert markiert worden.
        var fs = new InMemoryFileSystem();
        var clock = new FakeClock();
        var config = new ConfigService(new ConfigStore(fs, ConfigFile, clock, newConfigLanguage: () => "en"), clock);
        config.Update(c => c.Games.Add(new GameConfig { Source = GameSource.Epic, SourceId = "a1", Name = "Testspiel", InstallDir = @"D:\Spielordner\A", HdrAuto = true }));

        using (TestCulture.Use("en-US"))
        {
            var report = await new LibraryScanner([new CrashingScanner()]).ScanAsync(ScanOptions.Default, Ct);
            var error = Assert.Single(report.AllErrors);
            Assert.StartsWith("Scanner crashed: InvalidDataException", error.Message, StringComparison.Ordinal);
            Assert.True(error.ScannerCrashed);

            config.ApplyScan(report, fs);
        }

        Assert.True(config.Current.FindGame("Epic:a1")!.IsInstalled);
    }

    [Fact]
    public async Task Command_line_speaks_english()
    {
        using (TestCulture.Use("en-US"))
        {
            var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
            Assert.Equal(ExitCodes.Ok, await Run(api, "hdr", "status"));
            var text = _out.ToString();
            Assert.Contains("HDR-capable: yes", text, StringComparison.Ordinal);
            Assert.Contains("HDR: off", text, StringComparison.Ordinal);
            Assert.Contains("(primary display)", text, StringComparison.Ordinal);

            Assert.Equal(ExitCodes.Usage, await Run(api, "fliegen"));
            Assert.Contains("Unknown command \"fliegen\".", _err.ToString(), StringComparison.Ordinal);
            Assert.Contains("Help: hdrpilot help", _err.ToString(), StringComparison.Ordinal);

            _out.GetStringBuilder().Clear();
            Assert.Equal(ExitCodes.Ok, await Run(api, "--help"));
            Assert.StartsWith("HDRpilot command line", _out.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Ambiguous_display_is_a_usage_error_in_english_too()
    {
        // Früher am Wort „Mehrdeutig“ erkannt: Auf Englisch wäre das ein „Bildschirm nicht gefunden“ geworden.
        var api = FakeDisplayConfigApi.WithSingleHdrMonitor(hdrOn: false);
        api.Monitors.Add(new FakeMonitor { Name = "MAG 341C OLED", DevicePath = @"\\?\DISPLAY#MSI#2", Target = new DisplayTarget(1, 0, 8) });
        using (TestCulture.Use("en-US"))
        {
            Assert.Equal(ExitCodes.Usage, await Run(api, "hdr", "on", "--display", "MAG"));
            Assert.Contains("Ambiguous: \"MAG\" matches 2 displays.", _err.ToString(), StringComparison.Ordinal);
            Assert.Equal(ExitCodes.DisplayNotFound, await Run(api, "hdr", "on", "--display", "Projektor"));
            Assert.Contains("No display matches \"Projektor\".", _err.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Command_line_language_comes_from_the_storage_or_windows()
    {
        var fs = new InMemoryFileSystem();
        var environment = new FakeEnvironment();
        string[] args = ["games", "list", "--config-dir", @"D:\Ablage\HDRpilot"];
        var configFile = AppPaths.InDirectory(Path.GetFullPath(@"D:\Ablage\HDRpilot")).ConfigFile; // wie die Kommandozeile (unter Linux anders)

        // Ohne config.json: wie eine neue Konfiguration nach Windows, ohne etwas anzulegen.
        Assert.Equal("en", CliApp.LanguageFor(args, fs, environment, () => "en"));
        Assert.Equal("de", CliApp.LanguageFor(args, fs, environment, () => "de"));
        Assert.False(fs.FileExists(configFile));

        fs.AddFile(configFile, """{ "settings": { "language": "en" } }""");
        Assert.Equal("en", CliApp.LanguageFor(args, fs, environment, () => "de"));
        Assert.Equal("en", CliApp.LanguageFor(["games", "list", @"--config-dir=D:\Ablage\HDRpilot"], fs, environment, () => "de"));

        // Normale Ablage: %APPDATA%\HDRpilot\config.json ohne Sprache (ältere Version) gilt als Deutsch.
        fs.AddFile(Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDRpilot", "config.json"), """{ "settings": {} }""");
        Assert.Equal("de", CliApp.LanguageFor(["hdr", "status"], fs, environment, () => "en"));
    }

    private Task<int> Run(FakeDisplayConfigApi api, params string[] args) =>
        new CliApp(new TestServices(api), _out, _err).RunAsync(args, Ct);

    private sealed class TestServices(FakeDisplayConfigApi api) : ICliServices
    {
        public IHdrController CreateHdrController(HdrControllerOptions options) =>
            new DisplayConfigHdrController(api, new FakeEnvironment(), new FakeClock(), options);

        public IReadOnlyList<IGameScanner> CreateScanners() => [];
    }

    private sealed class CrashingScanner : IGameScanner
    {
        public GameSource Source => GameSource.Epic;

        public string Name => "Epic";

        public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default) => throw new InvalidDataException("defekt");
    }
}
