using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Bilddienst für Tests (E89): kleine, erzeugte Bilder (nie Spielbilder), Auswahl je Art aus festen Kandidaten, eigene
/// Dateien nur als Einträge. Zählt Anfragen und gelöschte eigene Bilder.
/// </summary>
public sealed class FakeArtworkService : IArtworkService
{
    private EventHandler<ArtworkChangedEventArgs>? _changed;

    public event EventHandler<ArtworkChangedEventArgs>? Changed
    {
        add
        {
            _changed += value;
            Subscribers++;
        }

        remove
        {
            _changed -= value;
            Subscribers--;
        }
    }

    public int Subscribers { get; private set; }

    /// <summary>Spiele mit Titelbild aus einer Quelle (sonst: Ersatzbild).</summary>
    public HashSet<string> WithHero { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Spiele mit Logo (sonst steht der Name in der Titelschrift).</summary>
    public HashSet<string> WithLogo { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Angefragte helle Fassungen (Spielschlüssel).</summary>
    public List<string> BoostRequests { get; } = [];

    /// <summary>Vorab angefragte helle Fassungen (niedrige Priorität, Spielschlüssel).</summary>
    public List<string> PrefetchRequests { get; } = [];

    /// <summary>Gesetzt: Anfragen nach der hellen Fassung warten auf diese Quelle (Tests: sie lädt noch).</summary>
    public TaskCompletionSource<ArtworkImage?>? PendingBoost { get; set; }

    /// <summary>Vergessene Spiele (<see cref="Forget"/>).</summary>
    public List<string> Forgotten { get; } = [];

    /// <summary>Kandidaten je Art (ohne Eintrag: keine; „Automatisch“ ergänzt der Dialog selbst).</summary>
    public Dictionary<ArtworkKind, List<ArtworkCandidate>> Candidates { get; } = [];

    public List<string> ArtworkRequests { get; } = [];

    public List<string?> Deleted { get; } = [];

    public List<(string Key, ArtworkKind Kind, string Path)> Saved { get; } = [];

    public int Leases { get; private set; }

    /// <summary>Kleines einfarbiges Bild (eingefroren).</summary>
    public static ImageSource Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public void Raise(string? key) => _changed?.Invoke(this, new ArtworkChangedEventArgs(key));

    public ImageSource? GetCachedImage(GameConfig game, ArtworkKind kind, int decodeWidth) => null;

    public Task<ImageSource?> GetImageAsync(GameConfig game, ArtworkKind kind, int decodeWidth, CancellationToken cancellationToken = default) =>
        Task.FromResult<ImageSource?>(kind == ArtworkKind.Cover ? Solid(8, 12, Colors.SteelBlue) : Solid(16, 9, Colors.DarkSlateBlue));

    public GameArtwork? GetCachedArtwork(GameConfig game) => null;

    public Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default)
    {
        ArtworkRequests.Add(game.Key);
        var hero = WithHero.Contains(game.Key)
            ? new ArtworkImage(Solid(32, 9, Colors.DarkOrange), ArtworkKind.Hero, ArtworkOrigin.Launcher, 3840, 1240, "Steam")
            : new ArtworkImage(Solid(32, 9, Colors.DimGray), ArtworkKind.Hero, ArtworkOrigin.Generated, 32, 9, "erzeugt");
        var logo = WithLogo.Contains(game.Key)
            ? new ArtworkImage(Solid(40, 12, Colors.Gold), ArtworkKind.Logo, ArtworkOrigin.Launcher, 40, 12, "Steam")
            : null;
        var summary = new ArtworkSummary(hero.Origin, logo?.Origin ?? ArtworkOrigin.None, ArtworkOrigin.Generated, ArtworkOrigin.None, LogoIsName: logo is null, CoverFromSmear: false, "Testbilder");
        return Task.FromResult(new GameArtwork(game.Key, hero, null, null, logo, null, null, AmbientColors.FromName(game.Name), summary));
    }

    /// <summary>Helle Fassung (nur für Spiele mit Titelbild aus einer Quelle), je Anfrage ein neues Bild.</summary>
    public Task<ArtworkImage?> GetHeroBoostAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default)
    {
        BoostRequests.Add(game.Key);
        return Boost(game);
    }

    /// <summary>Wie <see cref="GetHeroBoostAsync"/>, gezählt in <see cref="PrefetchRequests"/>.</summary>
    public Task<ArtworkImage?> PrefetchHeroBoostAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default)
    {
        PrefetchRequests.Add(game.Key);
        return Boost(game);
    }

    private Task<ArtworkImage?> Boost(GameConfig game) => PendingBoost?.Task ?? Task.FromResult(WithHero.Contains(game.Key)
        ? new ArtworkImage(Solid(32, 9, Colors.Moccasin), ArtworkKind.Hero, ArtworkOrigin.Launcher, 3840, 1240, "Steam")
        : null);

    public void Forget(string gameKey)
    {
        Forgotten.Add(gameKey);
        Raise(gameKey);
    }

    /// <summary>Gesetzt: Die Kandidaten kommen erst, wenn diese Quelle fertig ist (Tests: die Liste lädt noch).</summary>
    public TaskCompletionSource? PendingCandidates { get; set; }

    public async Task<IReadOnlyList<ArtworkCandidate>> GetCandidatesAsync(GameConfig game, ArtworkKind kind, CancellationToken cancellationToken = default)
    {
        if (PendingCandidates is { } pending)
        {
            await pending.Task.WaitAsync(cancellationToken);
        }

        return Candidates.TryGetValue(kind, out var list) ? list : [];
    }

    public string SaveCustomImage(GameConfig game, ArtworkKind kind, string sourcePath)
    {
        Saved.Add((game.Key, kind, sourcePath));
        return ArtworkOverride.Custom("bild-" + Saved.Count + ".png");
    }

    public void DeleteCustomImage(string? token) => Deleted.Add(token);

    public Task<ArtworkCoverage> GetCoverageAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) =>
        Task.FromResult(ArtworkCoverage.Empty);

    public Task<IReadOnlyList<FallbackExample>> GetFallbackExamplesAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FallbackExample>>([]);

    public IDisposable Acquire()
    {
        Leases++;
        return new Lease(this);
    }

    public void ClearCache()
    {
    }

    public void Dispose()
    {
    }

    private sealed class Lease(FakeArtworkService owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                owner.Leases--;
            }
        }
    }
}

/// <summary>Helligkeitswerte für Tests (nie DXGI): feste Bildschirme, Änderungen von Hand.</summary>
public sealed class FakeLuminanceService : IDisplayLuminanceService
{
    public event EventHandler? Changed;

    public DisplayNits? Primary => Displays.Count > 0 ? Displays[0] : null;

    public List<DisplayNits> List { get; } = [];

    public IReadOnlyList<DisplayNits> Displays => List;

    public int Invalidations { get; private set; }

    public void Invalidate() => Invalidations++;

    public IDisposable Acquire() => new NoLease();

    public void Set(params DisplayNits[] displays)
    {
        List.Clear();
        List.AddRange(displays);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }

    private sealed class NoLease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
