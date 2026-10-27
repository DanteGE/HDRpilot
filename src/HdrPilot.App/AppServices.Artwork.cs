using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Hdr;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App;

/// <summary>
/// Spielbilder, SteamGridDB und Helligkeit der Bildschirme (E89): lokale Launcher-Caches nur lesend samt Ersatzbildern und
/// eigenen Bildern (G10.3), SteamGridDB optional mit eigenem Schlüssel (G10.4), echte Nit-Werte über DXGI (G10.7).
/// </summary>
public sealed partial class AppServices
{
    private partial IArtworkService CreateArtworkService() => new ArtworkService(new ArtworkServiceOptions
    {
        ArtDirectory = Paths.ArtDirectory,
        IconDirectory = Paths.IconDirectory,
        FileSystem = FileSystem,
        Roots = () => ArtworkRoots.Resolve(FileSystem, Registry, Environment, LoggerFactory.CreateLogger("Bilder")),
        HelperPatterns = () => Config.Current.Settings.HelperExecutablePatterns,
        SteamGridDbEnabled = () => Config.Current.Settings.Artwork?.SteamGridDb == true,
        LibraryKeys = () => Config.Current.AllGameKeys(),
        GameRunning = () => Sessions.Sessions.Count > 0,
        PostToUi = action => Dispatcher.BeginInvoke(DispatcherPriority.Background, action),
        Logger = LoggerFactory.CreateLogger("Bilder"),
    });

    private partial ISteamGridDbService CreateSteamGridDbService() => Artwork is ArtworkService artwork
        ? new SteamGridDbService(new SteamGridDbServiceOptions
        {
            Artwork = artwork,
            Current = () => Config.Current,
            Update = mutate => Config.Update(mutate),
            FileSystem = FileSystem,
            KeyFile = Paths.SteamGridDbKeyFile,
            Clock = Host.Clock,
            GameRunning = () => Sessions.Sessions.Count > 0,
            PostToUi = action => Dispatcher.BeginInvoke(DispatcherPriority.Background, action),
            Logger = LoggerFactory.CreateLogger("SteamGridDB"),

            // Prüfläufe (nur mit --config-dir): erzeugte Antworten statt des Netzes und Zeilen „Prüfung: …“ (E93).
            HandlerFactory = SteamGridDbFixture.HandlerFactoryFor(
                ConfigDirArgument() is null ? null : Paths.ConfigDirectory,
                System.Environment.GetEnvironmentVariable,
                Path.Combine(Paths.ConfigDirectory, "sgdb-fixture.log")),
            TestLog = TestLog(),
        })
        : NullSteamGridDbService.Instance;

    private partial IDisplayLuminanceService CreateDisplayLuminanceService() => new DisplayLuminanceService(new DisplayLuminanceServiceOptions
    {
        Reader = new DxgiLuminanceReader(LoggerFactory.CreateLogger("DXGI")),
        Displays = () => Hdr.GetDisplays(),
        TargetDisplays = () => Config.Current.Settings.TargetDisplays,
        Store = new DisplayLuminanceStore(FileSystem, Paths.DisplayLuminanceFile, LoggerFactory.CreateLogger("Helligkeit")),
        PostToUi = action => Dispatcher.BeginInvoke(DispatcherPriority.Background, action),
        Logger = LoggerFactory.CreateLogger("Helligkeit"),
    });

    /// <summary>
    /// Entwicklung (G10.3): Messung der lokalen Bilder über die ganze Bibliothek als JSON (Abdeckung je Launcher „genutzt /
    /// gefunden“, verschmierte Kapseln, Symbole aus EXE-Dateien, Zahl der Ersatzbilder, Beispiele, Zeit kalt und warm) und
    /// daneben ein Kontaktbogen der Ersatzbilder (<c>&lt;Bericht&gt;.kontakt.png</c>). Liest die Launcher-Caches nur.
    /// </summary>
    public partial async Task WriteArtworkReportAsync(string path)
    {
        var games = Config.Current.Games;
        using var lease = Artwork.Acquire();
        if (Artwork is not ArtworkService artwork)
        {
            return;
        }

        var cold = await artwork.BuildReportAsync(games);
        artwork.ClearCache();
        var warm = await artwork.BuildReportAsync(games);
        artwork.FlushIndex();
        var report = new
        {
            Games = games.Count,
            Roots = artwork.Roots,
            TimingColdMs = (int)cold.Elapsed.TotalMilliseconds,
            TimingWarmMs = (int)warm.Elapsed.TotalMilliseconds,
            Rows = cold.Coverage.Rows.Select(r => new
            {
                Source = r.Source.ToString(),
                r.Games,
                Hero = new { r.Hero.Used, r.Hero.Found },
                Logo = new { r.Logo.Used, r.Logo.Found },
                Cover = new { r.Cover.Used, r.Cover.Found },
                Icon = new { r.Icon.Used, r.Icon.Found },
            }),
            Generated = new
            {
                Heroes = cold.GeneratedHeroes,
                Covers = cold.GeneratedCovers,
                CoversFromSmear = cold.CoversFromSmear,
                Headers = cold.GeneratedHeaders,
                Monograms = cold.Monograms,
                NameInsteadOfLogo = cold.NameInsteadOfLogo,
            },
            cold.Smeared,
            cold.ExeIcons,
            Examples = cold.Examples.Select(e => new { Kind = e.Kind.ToString(), e.GameKey, e.GameName, e.Detail }),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        await ArtworkContactSheet.WriteAsync(artwork, games, cold.Examples, Path.ChangeExtension(path, ".kontakt.png"));
    }
}
