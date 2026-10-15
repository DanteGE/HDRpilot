using System.IO;
using System.Text.Json;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Setup;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App;

/// <summary>
/// Spielbilder, SteamGridDB und Helligkeit der Bildschirme (E89). Gerüst: Bilder nur aus dem Steam-Cache, SteamGridDB und
/// DXGI noch ohne Umsetzung (G10.3, G10.4, G10.7).
/// </summary>
public sealed partial class AppServices
{
    private partial IArtworkService CreateArtworkService() => new ArtworkService(
        FileSystem,
        () => SteamLaunchOptionScanner.ReadSteamPath(Registry),
        LoggerFactory.CreateLogger("Bilder"));

    private partial ISteamGridDbService CreateSteamGridDbService() => NullSteamGridDbService.Instance;

    private partial IDisplayLuminanceService CreateDisplayLuminanceService() => NullDisplayLuminanceService.Instance;

    public partial async Task WriteArtworkReportAsync(string path)
    {
        var games = Config.Current.Games;
        var coverage = await Artwork.GetCoverageAsync(games);
        var report = new
        {
            Games = games.Count,
            Rows = coverage.Rows.Select(r => new
            {
                Source = r.Source.ToString(),
                r.Games,
                Hero = new { r.Hero.Used, r.Hero.Found },
                Logo = new { r.Logo.Used, r.Logo.Found },
                Cover = new { r.Cover.Used, r.Cover.Found },
                Icon = new { r.Icon.Used, r.Icon.Found },
            }),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
