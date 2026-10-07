using System.Text.Json;
using HdrPilot.Cli;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Games;

public sealed class LibraryScannerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Throwing_scanner_does_not_affect_others()
    {
        var scanner = new LibraryScanner([new StubScanner(GameSource.Steam, "A"), new ThrowingScanner(), new StubScanner(GameSource.Gog, "B")]);

        var report = await scanner.ScanAsync(ScanOptions.Default, Ct);

        Assert.Equal(3, report.Scanners.Count);
        Assert.Equal(["A", "B"], report.Games.Select(g => g.Name));
        var broken = report.Scanners.Single(s => s.Result.Source == GameSource.Epic);
        Assert.Empty(broken.Result.Games);
        var error = Assert.Single(broken.Result.Errors);
        Assert.Contains("Scanner abgestürzt", error.Message, StringComparison.Ordinal);
        Assert.Contains("defekt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_of_one_scanner_are_kept_with_its_games()
    {
        var withError = new StubScanner(GameSource.Epic, "C") { Errors = [new ScanError("Manifest kaputt", @"C:\x.item")] };
        var report = await new LibraryScanner([withError]).ScanAsync(ScanOptions.Default, Ct);

        Assert.Single(report.Games);
        Assert.Single(report.AllErrors);
        Assert.Equal(@"C:\x.item", report.AllErrors[0].Path);
    }

    [Fact]
    public async Task Duplicate_keys_are_merged_and_results_sorted_by_source()
    {
        var report = await new LibraryScanner([new StubScanner(GameSource.Gog, "Z"), new StubScanner(GameSource.Steam, "Y"), new StubScanner(GameSource.Steam, "Y")])
            .ScanAsync(ScanOptions.Default, Ct);

        Assert.Equal([GameSource.Steam, GameSource.Steam, GameSource.Gog], report.Scanners.Select(s => s.Result.Source));
        Assert.Equal(2, report.Games.Count);
    }

    [Fact]
    public async Task Search_folders_are_passed_to_scanners()
    {
        var stub = new StubScanner(GameSource.EA, "E");
        await new LibraryScanner([stub]).ScanAsync(new ScanOptions { SearchFolders = [@"D:\Spielordner"] }, Ct);
        Assert.Equal([@"D:\Spielordner"], stub.LastOptions!.SearchFolders);
    }

    [Fact]
    public void Catalog_only_accepts_scanners_with_contract_constructor()
    {
        Assert.All(ScannerCatalog.ScannerTypes, t => Assert.True(typeof(IGameScanner).IsAssignableFrom(t)));
        var created = ScannerCatalog.CreateAll(new InMemoryFileSystem(), new FakeRegistry(), new FakeEnvironment());
        Assert.Equal(ScannerCatalog.ScannerTypes.Count, created.Count);
    }

    [Fact]
    public async Task Cli_scan_outputs_games_and_scanner_errors_as_json()
    {
        var services = new Services([new StubScanner(GameSource.Steam, "Spiel™ Ä") { Errors = [new ScanError("kaputt")] }, new ThrowingScanner()]);
        var stdout = new StringWriter();
        var code = await new CliApp(services, stdout, new StringWriter()).RunAsync(["scan"], Ct);

        Assert.Equal(ExitCodes.Ok, code);
        using var doc = JsonDocument.Parse(stdout.ToString());
        var game = Assert.Single(doc.RootElement.GetProperty("games").EnumerateArray());
        Assert.Equal("Steam:A-Spiel™ Ä", game.GetProperty("key").GetString());
        Assert.Equal("Spiel™ Ä", game.GetProperty("name").GetString());
        Assert.Equal("Steam", game.GetProperty("source").GetString());
        var scanners = doc.RootElement.GetProperty("scanners").EnumerateArray().ToList();
        Assert.Equal(2, scanners.Count);
        Assert.All(scanners, s => Assert.Equal(1, s.GetProperty("errors").GetArrayLength()));
        Assert.DoesNotContain("™", stdout.ToString(), StringComparison.Ordinal); // ASCII-sicher (E9)
    }

    [Fact]
    public async Task Cli_scan_filters_by_source_and_rejects_unknown_source()
    {
        var services = new Services([new StubScanner(GameSource.Steam, "S"), new StubScanner(GameSource.Gog, "G")]);
        var stdout = new StringWriter();
        Assert.Equal(ExitCodes.Ok, await new CliApp(services, stdout, new StringWriter()).RunAsync(["scan", "--source", "gog"], Ct));
        using var doc = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("G", Assert.Single(doc.RootElement.GetProperty("games").EnumerateArray()).GetProperty("name").GetString());

        Assert.Equal(ExitCodes.Usage, await new CliApp(services, new StringWriter(), new StringWriter()).RunAsync(["scan", "--source", "origin"], Ct));
        Assert.Equal(ExitCodes.Usage, await new CliApp(services, new StringWriter(), new StringWriter()).RunAsync(["scan", "--source", "99"], Ct));
    }

    private sealed class Services(IReadOnlyList<IGameScanner> scanners) : ICliServices
    {
        public IHdrController CreateHdrController(HdrControllerOptions options) => throw new NotSupportedException();

        public IReadOnlyList<IGameScanner> CreateScanners() => scanners;
    }

    private sealed class StubScanner(GameSource source, string name) : IGameScanner
    {
        public GameSource Source => source;

        public string Name => source.ToString();

        public IReadOnlyList<ScanError> Errors { get; init; } = [];

        public ScanOptions? LastOptions { get; private set; }

        public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return new ScanResult
            {
                Source = source,
                ScannerName = Name,
                LauncherDetected = true,
                Games = [new GameEntry { Source = source, SourceId = "A-" + name, Name = name, InstallDir = @"C:\Games\" + name }],
                Errors = Errors,
            };
        }
    }

    private sealed class ThrowingScanner : IGameScanner
    {
        public GameSource Source => GameSource.Epic;

        public string Name => "Epic";

        public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("defekt");
    }
}
