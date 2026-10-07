using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>„Zuletzt verwendet“ beim Einlesen: der spätere Wert aus Konfiguration und Launcher gewinnt.</summary>
public sealed class LibraryMergerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Early = new(2024, 1, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Late = new(2026, 9, 30, 21, 0, 0, TimeSpan.Zero);

    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public void New_game_takes_last_played_from_the_launcher()
    {
        var config = new AppConfig();

        LibraryMerger.Apply(config, Report(Entry("1", Late), Entry("2", null)), _fs, Now);

        Assert.Equal(Late, config.FindGame("Steam:1")!.LastPlayed);
        Assert.Null(config.FindGame("Steam:2")!.LastPlayed);
    }

    [Theory]
    [InlineData(null, "late", "late")]
    [InlineData("late", "early", "late")]
    [InlineData("early", "late", "late")]
    [InlineData("late", null, "late")]
    [InlineData(null, null, null)]
    public void Existing_game_keeps_the_later_value(string? existing, string? scanned, string? expected)
    {
        var config = new AppConfig();
        config.Games.Add(new GameConfig { Source = GameSource.Steam, SourceId = "1", Name = "Alt", InstallDir = @"D:\S\1", LastPlayed = Pick(existing) });

        LibraryMerger.Apply(config, Report(Entry("1", Pick(scanned))), _fs, Now);

        Assert.Equal(Pick(expected), config.FindGame("Steam:1")!.LastPlayed);
    }

    [Fact]
    public void Later_compares_points_in_time_not_offsets()
    {
        var sameInstantOtherZone = Late.ToOffset(TimeSpan.FromHours(2));

        Assert.Equal(Late, LibraryMerger.Later(Late, Early));
        Assert.Equal(Late, LibraryMerger.Later(Early, sameInstantOtherZone).GetValueOrDefault().ToUniversalTime());
    }

    private static DateTimeOffset? Pick(string? name) => name switch
    {
        "early" => Early,
        "late" => Late,
        _ => null,
    };

    private static GameEntry Entry(string id, DateTimeOffset? lastPlayed) => new()
    {
        Source = GameSource.Steam,
        SourceId = id,
        Name = "Spiel " + id,
        InstallDir = @"D:\S\" + id,
        LaunchCommand = "steam://rungameid/" + id,
        LastPlayed = lastPlayed,
    };

    private static LibraryScanReport Report(params GameEntry[] games) => new(
    [
        new ScannerReport(new ScanResult { Source = GameSource.Steam, ScannerName = "Steam", LauncherDetected = true, Games = games }, TimeSpan.Zero),
    ]);
}
