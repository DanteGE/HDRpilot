using System.Text.Json;
using HdrPilot.Cli;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.Scanners;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Games;

/// <summary>
/// Quiver Launcher im Rest von HDRpilot (E91): Übernahme in die Konfiguration, Cover über den Bildfinder, kein Abgleich mit
/// PCGamingWiki, kein Vorschlag aus Suchordnern, Kommandozeile „scan --source Quiver“ und geteiltes Lesen am echten Dateisystem.
/// </summary>
public sealed class QuiverIntegrationTests
{
    private const string Root = @"D:\Spielordner\QuiverLauncher-win-Portable";
    private const string BlackDir = Root + @"\Apps\Black-BlackXBERecomp";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryFileSystem _fs = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Quiver_merger_stores_cover_hints_and_install_kind_only_where_there_are_some()
    {
        var config = new AppConfig();
        var quiver = new GameEntry
        {
            Source = GameSource.Quiver,
            SourceId = "Black-BlackXBERecomp",
            Name = "Black",
            InstallDir = BlackDir,
            LaunchCommand = BlackDir + @"\BLACK.exe",
            CoverCandidates = [Root + @"\Cache\Images\8C8FC6677FB84678288E4615E4E470A5"],
            InstallKind = InstallKinds.WindowsInstaller,
        };
        var steam = new GameEntry { Source = GameSource.Steam, SourceId = "570", Name = "Dota 2", InstallDir = @"D:\S\570" };

        LibraryMerger.Apply(config, Report(GameSource.Quiver, quiver), _fs, Now);
        LibraryMerger.Apply(config, Report(GameSource.Steam, steam), _fs, Now);

        var game = config.FindGame("Quiver:Black-BlackXBERecomp")!;
        Assert.Equal([Root + @"\Cache\Images\8C8FC6677FB84678288E4615E4E470A5"], game.CoverCandidates);
        Assert.Equal(InstallKinds.WindowsInstaller, game.InstallKind);
        Assert.True(game.IsNew);
        Assert.False(game.HdrAuto);
        Assert.Null(config.FindGame("Steam:570")!.CoverCandidates);

        // Gespeichert: nur das Quiver-Spiel bekommt die neuen Felder, die übrigen Einträge bleiben wie bisher.
        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        using var document = JsonDocument.Parse(json);
        var games = document.RootElement.GetProperty("games").EnumerateArray().ToList();
        Assert.True(games[0].TryGetProperty("coverCandidates", out _));
        Assert.False(games[1].TryGetProperty("coverCandidates", out _));
        Assert.False(games[1].TryGetProperty("installKind", out _));
        Assert.False(document.RootElement.GetProperty("settings").TryGetProperty("quiverFolders", out _));

        // Erneutes Einlesen ohne Cover: das Feld fällt weg.
        LibraryMerger.Apply(config, Report(GameSource.Quiver, quiver with { CoverCandidates = [], InstallKind = null }), _fs, Now);
        Assert.Null(config.FindGame("Quiver:Black-BlackXBERecomp")!.CoverCandidates);
        Assert.Null(config.FindGame("Quiver:Black-BlackXBERecomp")!.InstallKind);
    }

    [Fact]
    public void Quiver_artwork_locator_takes_the_first_existing_cover_hint()
    {
        var gone = Root + @"\Cache\Images\00000000000000000000000000000000";
        var first = Root + @"\Cache\Images\8C8FC6677FB84678288E4615E4E470A5";
        var second = Root + @"\Cache\Icons\Black-BlackXBERecomp_0123456789abcdef.png";
        _fs.AddFile(first, "webp").AddFile(second, "png");
        var game = new GameConfig { Source = GameSource.Quiver, SourceId = "Black-BlackXBERecomp", Name = "Black", InstallDir = BlackDir, CoverCandidates = [gone, first, second, @"\\server\freigabe\cover.png"] };

        var located = new ArtworkLocator(new ReadOnlyFileSystem(_fs), ArtworkRoots.None).Locate(game);

        Assert.Equal(first, located.Cover?.Path);
        Assert.Equal(LauncherCache.Quiver, located.Cover?.Cache);
        Assert.Null(located.Hero);
        Assert.Null(located.Logo);
        Assert.Equal([second], located.Alternatives.Select(a => a.Path));
        Assert.Equal(ArtworkKind.Cover, Assert.Single(located.Alternatives).Kind);
        Assert.Same(LocatedArtwork.None, new ArtworkLocator(_fs, ArtworkRoots.None).Locate(new GameConfig { Source = GameSource.Quiver, SourceId = "x", Name = "x" }));
    }

    [Fact]
    public void Quiver_games_are_skipped_by_the_hdr_list_and_their_names_never_sent()
    {
        var wiki = new FakeWiki().Page("Black", "1200", "true").Page("Dota 2", "570", "false");
        var handlers = new List<FakeHttpHandler>();
        var clock = new FakeClock();
        var cache = new HdrSupportCache(_fs, AppPaths.InDirectory(@"C:\Daten"));
        cache.Save(new HdrSupportData { Games = { ["Quiver:Black-BlackXBERecomp"] = new HdrSupportEntry { Status = HdrSupportStatus.Native, Name = "Black", CheckedAt = clock.UtcNow } } });
        HdrListGame[] games =
        [
            new("Quiver:Black-BlackXBERecomp", GameSource.Quiver, "Black-BlackXBERecomp", "Black"),
            new("Steam:570", GameSource.Steam, "570", "Dota 2"),
        ];

        var result = new HdrListSync(cache, clock).Run(games, null, () =>
        {
            var handler = wiki.CreateHandler();
            handlers.Add(handler);
            return new PcgwClient(handler, clock, "HdrPilot/1.0.0 (Windows desktop app; HDR list)", TimeSpan.FromSeconds(30));
        }, force: true, Ct);

        Assert.Equal((1, 1), (result.Checked, result.Found));
        Assert.DoesNotContain(handlers.SelectMany(h => h.Requests), r => r.Raw.Contains("Black", StringComparison.Ordinal));
        Assert.Contains(handlers.SelectMany(h => h.Requests), r => r.Raw.Contains("Dota", StringComparison.Ordinal));
        var statuses = cache.LoadStatuses();
        Assert.False(statuses.ContainsKey("Quiver:Black-BlackXBERecomp")); // früheres Ergebnis verworfen: „unbekannt“
        Assert.True(HdrListSync.Skips(GameSource.Quiver));
        Assert.False(HdrListSync.Skips(GameSource.Manual));
    }

    [Fact]
    public void Quiver_games_do_not_count_in_the_hdr_list_status()
    {
        var config = new AppConfig();
        config.Games.Add(new GameConfig { Source = GameSource.Quiver, SourceId = "Black-BlackXBERecomp", Name = "Black" });
        config.Games.Add(new GameConfig { Source = GameSource.Steam, SourceId = "570", Name = "Dota 2" });
        using var service = new HdrListService(AppPaths.InDirectory(@"C:\Daten"), _fs, new FakeClock(), () => config);

        var status = service.GetStatus();

        Assert.Equal((0, 1), (status.CheckedGames, status.TotalGames));
    }

    [Fact]
    public void Quiver_folder_and_everything_in_it_is_never_suggested_from_search_folders()
    {
        _fs.AddFixture(@"Quiver\sq.version", Root + @"\current\sq.version");
        _fs.AddFile(Root + @"\current\QuiverLauncher.exe", "MZ").AddFile(Root + @"\QuiverLauncher.exe", "MZ").AddFile(Root + @"\Update.exe", "MZ");
        _fs.AddFile(BlackDir + @"\BLACK.exe", "MZ");
        _fs.AddFile(@"D:\Spielordner\Hades\Hades.exe", "MZ");
        var finder = new SearchFolderCandidateFinder(new ReadOnlyFileSystem(_fs));

        var fromParent = finder.FindCandidates([@"D:\Spielordner"], [], Ct);
        var fromRoot = finder.FindCandidates([Root], [], Ct);
        var fromApps = finder.FindCandidates([Root + @"\Apps"], [], Ct);

        Assert.Equal(["Hades"], fromParent.Select(c => c.Name));
        Assert.Empty(fromRoot);
        Assert.Empty(fromApps);
    }

    [Fact]
    public async Task Quiver_cli_scan_takes_the_source_and_reads_learned_folders_without_writing()
    {
        _fs.AddFixture(@"Quiver\sq.version", Root + @"\current\sq.version");
        _fs.AddFixture(@"Quiver\apps.json", Root + @"\apps.json");
        _fs.AddFixture(@"Quiver\settings.json", Root + @"\settings.json");
        _fs.AddFile(BlackDir + @"\BLACK.exe", "MZ");
        _fs.AddFile(@"C:\Ablage\config.json", "{ \"version\": 1, \"settings\": { \"quiverFolders\": [ \"" + Root.Replace(@"\", @"\\") + "\" ] }, \"games\": [] }");
        var output = new StringWriter();
        var errors = new StringWriter();
        var cli = new CliApp(new QuiverCliServices(new ReadOnlyFileSystem(_fs)), output, errors);

        var withoutFolder = await cli.RunAsync(["scan", "--source", "Quiver"], Ct);
        var withoutText = output.ToString();
        output.GetStringBuilder().Clear();
        var code = await cli.RunAsync(["scan", "--source", "quiver", "--config-dir", @"C:\Ablage"], Ct);

        Assert.Equal((ExitCodes.Ok, ExitCodes.Ok), (withoutFolder, code));
        Assert.Equal(string.Empty, errors.ToString());
        using (var none = JsonDocument.Parse(withoutText))
        {
            Assert.Empty(none.RootElement.GetProperty("games").EnumerateArray());
        }

        using var document = JsonDocument.Parse(output.ToString());
        var game = Assert.Single(document.RootElement.GetProperty("games").EnumerateArray());
        Assert.Equal("Quiver:Black-BlackXBERecomp", game.GetProperty("key").GetString());
        Assert.Equal("Quiver", game.GetProperty("source").GetString());
        Assert.Equal(BlackDir + @"\BLACK.exe", game.GetProperty("launchCommand").GetString());
        var scanner = Assert.Single(document.RootElement.GetProperty("scanners").EnumerateArray());
        Assert.True(scanner.GetProperty("launcherDetected").GetBoolean());
        Assert.Empty(scanner.GetProperty("errors").EnumerateArray());
        Assert.Contains("BattleNet, Quiver", CliApp.HelpText, StringComparison.Ordinal);
        Assert.Contains("hdrpilot scan [--source <quelle>] [--search \"<ordner>;<ordner>\"] [--config-dir <ordner>]", CliApp.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void Quiver_files_are_read_shared_while_another_program_writes_them()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hdrpilot-quiver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "velopack_QuiverLauncher.log");
        try
        {
            File.WriteAllText(path, "Anfang\n" + new string('x', 100) + "\nEnde");
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                Assert.ThrowsAny<IOException>(() => File.ReadAllBytes(path)); // ohne Freigabe zum Schreiben: scheitert
                var head = PhysicalFileSystem.Instance.ReadShared(path, 6);
                var tail = PhysicalFileSystem.Instance.ReadShared(path, 4, fromEnd: true);
                var all = PhysicalFileSystem.Instance.ReadShared(path, 4096);

                Assert.Equal("Anfang", System.Text.Encoding.UTF8.GetString(head.Bytes));
                Assert.Equal("Ende", System.Text.Encoding.UTF8.GetString(tail.Bytes));
                Assert.False(head.IsComplete);
                Assert.True(all.IsComplete);
                Assert.Equal(writer.Length, all.Length);
            }

            Assert.Equal(112, PhysicalFileSystem.Instance.ReadShared(path, 0).Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static LibraryScanReport Report(GameSource source, params GameEntry[] games) => new(
    [
        new ScannerReport(new ScanResult { Source = source, ScannerName = source.ToString(), LauncherDetected = true, Games = games }, TimeSpan.Zero),
    ]);

    /// <summary>Kommandozeile über der Ablage im Speicher (nur lesend) mit dem Quiver-Scanner; schaltet nie HDR.</summary>
    private sealed class QuiverCliServices(IFileSystem fileSystem) : ICliServices
    {
        private readonly FakeEnvironment _environment = new();

        public IFileSystem FileSystem => fileSystem;

        public IAppEnvironment Environment => _environment;

        public IClock Clock { get; } = new FakeClock();

        public IHdrController CreateHdrController(HdrControllerOptions options) => throw new InvalidOperationException("scan schaltet kein HDR");

        public IReadOnlyList<IGameScanner> CreateScanners() => [new QuiverScanner(fileSystem, new ReadOnlyRegistry(new FakeRegistry()), _environment)];
    }
}
