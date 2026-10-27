using System.IO;
using System.Windows.Media;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;

namespace HdrPilot.App.Services.Artwork;

/// <summary>Ein geladenes, eingefrorenes Bild samt Herkunft (für die Anzeige „Lokal aus dem Steam-Cache“ usw.).</summary>
/// <param name="SourceLabel">Kurze Herkunft für die Oberfläche (aus ArtworkStrings.resx), etwa „Steam-Cache“.</param>
public sealed record ArtworkImage(ImageSource Image, ArtworkKind Kind, ArtworkOrigin Origin, int PixelWidth, int PixelHeight, string SourceLabel);

/// <summary>Herkunft je Bildart eines Spiels, ohne Bilder (Detailbereich, Einstellungen).</summary>
/// <param name="LogoIsName">Kein Logo: der Name steht in der Titelschrift.</param>
/// <param name="CoverFromSmear">Verschmierte Steam-Kapsel erkannt und durch einen Ausschnitt aus dem Titelbild ersetzt.</param>
/// <param name="Description">Eine Zeile für den Detailbereich, etwa „Lokal aus dem Steam-Cache · nichts heruntergeladen“.</param>
public sealed record ArtworkSummary(
    ArtworkOrigin Hero,
    ArtworkOrigin Logo,
    ArtworkOrigin Cover,
    ArtworkOrigin Icon,
    bool LogoIsName,
    bool CoverFromSmear,
    string Description)
{
    public static ArtworkSummary Empty { get; } = new(ArtworkOrigin.None, ArtworkOrigin.None, ArtworkOrigin.None, ArtworkOrigin.None, LogoIsName: true, CoverFromSmear: false, string.Empty);
}

/// <summary>
/// Alles, was Titelbereich, Kacheln und Detailbereich für ein Spiel brauchen. <see cref="Hero"/>, <see cref="Cover"/>,
/// <see cref="Field"/> und <see cref="Icon"/> sind nie null, sobald das Spiel geladen ist (fehlt ein Launcher-Bild, ist es
/// erzeugt); <see cref="Logo"/> ist null, wenn es kein Logo gibt (dann den Namen in der Titelschrift zeigen);
/// <see cref="HeroBoost"/> ist die vorab berechnete, hellere „HDR“-Fassung des Titelbilds. Der Bilddienst liefert den Satz
/// ohne sie (null); wer sie zeigt (HDR an, HDR-Moment, HDR-Vorschau), holt sie über
/// <see cref="IArtworkService.GetHeroBoostAsync"/> und setzt sie mit <c>with</c> ein.
/// </summary>
public sealed record GameArtwork(
    string GameKey,
    ArtworkImage? Hero,
    ArtworkImage? HeroBoost,
    ArtworkImage? Field,
    ArtworkImage? Logo,
    ArtworkImage? Cover,
    ArtworkImage? Icon,
    AmbientColors Ambient,
    ArtworkSummary Summary);

/// <summary>Eine Wahlmöglichkeit im Dialog „Bild wählen…“.</summary>
/// <param name="Token">Kennzeichen für <see cref="GameArtworkChoice"/> (null: automatisch, siehe <see cref="ArtworkOverride"/>).</param>
/// <param name="Label">Beschriftung aus ArtworkStrings.resx, etwa „Steam-Cache“, „Erzeugt“, „SplashScreen.png (Xbox-Paket)“.</param>
/// <param name="Note">Hinweis, etwa „Schwarzweiß – vermutlich Engine-Logo“; sonst null.</param>
public sealed record ArtworkCandidate(
    string? Token,
    ArtworkKind Kind,
    ArtworkOrigin Origin,
    string Label,
    string? Note,
    ImageSource? Preview,
    int PixelWidth,
    int PixelHeight,
    bool IsCurrent);

/// <summary>Anzahl Spiele einer Quelle mit genutztem und mit gefundenem Bild einer Art („genutzt / gefunden“).</summary>
public readonly record struct ArtworkCount(int Used, int Found);

/// <summary>Eine Zeile der Tabelle „Lokal · Launcher-Caches“ in den Einstellungen.</summary>
public sealed record ArtworkCoverageRow(GameSource Source, int Games, ArtworkCount Hero, ArtworkCount Logo, ArtworkCount Cover, ArtworkCount Icon);

/// <summary>Abdeckung der Bibliothek mit lokalen Bildern (Einstellungen, Messung G10.3).</summary>
public sealed record ArtworkCoverage(IReadOnlyList<ArtworkCoverageRow> Rows)
{
    public static ArtworkCoverage Empty { get; } = new([]);
}

/// <summary>Ersatzbild-Arten für die Beispiele in den Einstellungen („Erzeugte Bilder“).</summary>
public enum FallbackKind
{
    HeroFromIcon,
    NameInsteadOfLogo,
    CoverGenerated,
    CoverFromSmearedCapsule,
    Monogram,
}

/// <summary>Ein echtes Beispiel aus der Bibliothek für eine Ersatzbild-Art.</summary>
/// <param name="Detail">Kurze Erklärung aus ArtworkStrings.resx, etwa „EA app · aus bf6.exe · 256 px“.</param>
public sealed record FallbackExample(FallbackKind Kind, string GameKey, string GameName, string Detail);

/// <summary>Bilder eines Spiels haben sich geändert (neue Datei im Cache, eigene Wahl); null: alle Spiele.</summary>
public sealed class ArtworkChangedEventArgs(string? gameKey) : EventArgs
{
    public string? GameKey { get; } = gameKey;
}

/// <summary>
/// Spielbilder aus lokalen Quellen (E89, G10.3): Launcher-Caches nur lesend, Ersatzbilder, eigene Bilder, Umgebungsfarben,
/// helle HDR-Fassung, Zwischenspeicher unter <c>%LOCALAPPDATA%\HDRpilot\art</c>. Alle Bilder sind eingefroren und in der
/// angefragten Breite dekodiert; geladen wird im Hintergrund. Ereignisse kommen auf dem UI-Thread an.
/// <para>
/// Lebensdauer: Speicher und Beobachtung der Cache-Ordner nur, solange mindestens ein Fenster eine
/// <see cref="Acquire"/>-Freigabe hält; ohne offenes Fenster läuft nichts (keine Last im Leerlauf).
/// </para>
/// Änderungen an dieser Schnittstelle nur hinzufügend und mit Standardumsetzung (andere Teile und Attrappen bauen darauf).
/// </summary>
public interface IArtworkService : IDisposable
{
    /// <summary>Bilder eines Spiels haben sich geändert (UI-Thread).</summary>
    event EventHandler<ArtworkChangedEventArgs>? Changed;

    /// <summary>Schon geladenes Bild in dieser Breite (UI-Thread, ohne Plattenzugriff); sonst null.</summary>
    ImageSource? GetCachedImage(GameConfig game, ArtworkKind kind, int decodeWidth);

    /// <summary>
    /// Bild in höchstens dieser Breite (Pixel). Für <see cref="ArtworkKind.Hero"/>, <see cref="ArtworkKind.Cover"/>,
    /// <see cref="ArtworkKind.Header"/>, <see cref="ArtworkKind.Field"/> und <see cref="ArtworkKind.Icon"/> nie null (Ersatzbild),
    /// für <see cref="ArtworkKind.Logo"/> null ohne Logo. Zuletzt angefragte zuerst (sichtbare Zeilen).
    /// </summary>
    Task<ImageSource?> GetImageAsync(GameConfig game, ArtworkKind kind, int decodeWidth, CancellationToken cancellationToken = default);

    /// <summary>Schon geladener Satz für den Titelbereich; sonst null.</summary>
    GameArtwork? GetCachedArtwork(GameConfig game);

    /// <summary>Titelbild (in <paramref name="heroDecodeWidth"/>), Farbfeld, Logo, Cover, Symbol und Umgebungsfarben (ohne helle Fassung).</summary>
    Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default);

    /// <summary>
    /// Helle „HDR“-Fassung des Titelbilds für denselben Satz (einmal berechnet und auf der Platte gemerkt). Nur anfragen, wenn
    /// sie zu sehen ist: HDR an, HDR-Moment oder HDR-Vorschau; null, wenn sie sich nicht berechnen lässt oder gerade ein Spiel
    /// läuft. Standard: null.
    /// </summary>
    Task<ArtworkImage?> GetHeroBoostAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default) =>
        Task.FromResult<ArtworkImage?>(null);

    /// <summary>
    /// Helle Fassung vorab laden, mit niedriger Priorität: erst, wenn keine andere Anfrage mehr ansteht (sichtbare Bilder
    /// gehen vor). Für das gewählte Spiel, wenn die Auswahl zur Ruhe gekommen ist, das Fenster aktiv ist und kein Spiel läuft;
    /// so ist sie da, wenn HDR angeht. Eine spätere normale Anfrage zieht sie vor. Standard: wie <see cref="GetHeroBoostAsync"/>.
    /// </summary>
    Task<ArtworkImage?> PrefetchHeroBoostAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default) =>
        GetHeroBoostAsync(game, heroDecodeWidth, cancellationToken);

    /// <summary>
    /// Fertig, sobald nach dem Ende der letzten Freigabe aufgeräumt ist: laufende Arbeit beendet, Speicher geleert, Bilder
    /// entfernter Spiele gelöscht, Verzeichnis geschrieben. Solange eine Freigabe besteht oder nie eine bestand: sofort.
    /// Standard: sofort.
    /// </summary>
    Task WhenReleasedAsync() => Task.CompletedTask;

    /// <summary>Wahlmöglichkeiten für „Bild wählen…“ (Launcher-Bild, andere lokale Bilder, erzeugt, aktuelle eigene Datei).</summary>
    Task<IReadOnlyList<ArtworkCandidate>> GetCandidatesAsync(GameConfig game, ArtworkKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Eigene Datei für eine Bildart übernehmen (verkleinert in art\custom gespeichert, Quelle bleibt unverändert). Liefert das
    /// Kennzeichen für <see cref="GameArtworkChoice"/>; der Aufrufer schreibt die Konfiguration. Wirft InvalidDataException,
    /// wenn die Datei kein lesbares Bild ist, und IO-Ausnahmen beim Schreiben. Nicht auf dem UI-Thread aufrufen.
    /// </summary>
    string SaveCustomImage(GameConfig game, ArtworkKind kind, string sourcePath);

    /// <summary>Eigene Datei zu einem Kennzeichen löschen (andere Kennzeichen: nichts).</summary>
    void DeleteCustomImage(string? token);

    /// <summary>
    /// Wie <see cref="SaveCustomImage"/>, aus Bytes (ein von SteamGridDB übernommenes Bild, E93). Nicht auf dem UI-Thread.
    /// Standard: nicht unterstützt (keine Temp-Dateien mit fremden Bytes).
    /// </summary>
    string SaveCustomImageData(GameConfig game, ArtworkKind kind, byte[] data) =>
        throw new NotSupportedException("Eigene Bilder aus Daten speichert nur der Bilddienst.");

    /// <summary>Abdeckung der Bibliothek mit lokalen Bildern je Launcher (im Hintergrund, nur lesend).</summary>
    Task<ArtworkCoverage> GetCoverageAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default);

    /// <summary>Je Ersatzbild-Art ein echtes Beispiel aus der Bibliothek (Einstellungen „Erzeugte Bilder“).</summary>
    Task<IReadOnlyList<FallbackExample>> GetFallbackExamplesAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default);

    /// <summary>
    /// Alles zu einem Spiel im Speicher vergessen und <see cref="Changed"/> melden, etwa nach „Symbol ändern…“ (die Datei
    /// behält beim erneuten Ändern ihren Namen). Standard: nichts.
    /// </summary>
    void Forget(string gameKey)
    {
    }

    /// <summary>Ein Fenster zeigt Bilder: Speicher und Beobachtung der Cache-Ordner bis zum Freigeben der Rückgabe.</summary>
    IDisposable Acquire();

    /// <summary>Bilder im Speicher freigeben (Fenster geschlossen); Zwischenspeicher auf der Platte bleibt.</summary>
    void ClearCache();
}

/// <summary>Ohne Bilder (Tests, Gerüst): alles leer, nichts wird gelesen.</summary>
public sealed class NullArtworkService : IArtworkService
{
    public static NullArtworkService Instance { get; } = new();

    public event EventHandler<ArtworkChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public ImageSource? GetCachedImage(GameConfig game, ArtworkKind kind, int decodeWidth) => null;

    public Task<ImageSource?> GetImageAsync(GameConfig game, ArtworkKind kind, int decodeWidth, CancellationToken cancellationToken = default) =>
        Task.FromResult<ImageSource?>(null);

    public GameArtwork? GetCachedArtwork(GameConfig game) => null;

    public Task<GameArtwork> GetArtworkAsync(GameConfig game, int heroDecodeWidth, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GameArtwork(game.Key, null, null, null, null, null, null, AmbientColors.FromName(game.Name), ArtworkSummary.Empty));

    public Task<IReadOnlyList<ArtworkCandidate>> GetCandidatesAsync(GameConfig game, ArtworkKind kind, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArtworkCandidate>>([]);

    public string SaveCustomImage(GameConfig game, ArtworkKind kind, string sourcePath) =>
        throw new InvalidDataException("Keine Bilder verfügbar.");

    public void DeleteCustomImage(string? token)
    {
    }

    public Task<ArtworkCoverage> GetCoverageAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) =>
        Task.FromResult(ArtworkCoverage.Empty);

    public Task<IReadOnlyList<FallbackExample>> GetFallbackExamplesAsync(IReadOnlyList<GameConfig> games, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FallbackExample>>([]);

    public IDisposable Acquire() => NoLease.Instance;

    public void ClearCache()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>Freigabe ohne Wirkung.</summary>
public sealed class NoLease : IDisposable
{
    public static NoLease Instance { get; } = new();

    public void Dispose()
    {
    }
}
