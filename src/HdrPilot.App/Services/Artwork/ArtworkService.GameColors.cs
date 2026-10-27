using System.Collections.Concurrent;
using System.IO;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Spielfarben (E94) auf Anfrage: einmal je Spiel und Bildstand aus kleinen Fassungen von Logo, Cover und Titelbild gelesen
/// (<see cref="GameColorAnalysis"/>), im Speicher gemerkt und als <c>colors</c> in index.json gespeichert. Nur lesend: es
/// entstehen keine Zuschnitte und keine erzeugten Bilder; erzeugte Bilder und Farben aus dem Namen sind nie eine Quelle.
/// <see cref="LoadArtwork"/> bleibt unberührt, wer den Schalter nicht nutzt, zahlt nichts. Ein Fehler ergibt null
/// (unbekannt, gezeigte Farben bleiben), nie <see cref="GameColors.None"/> (grau, das Schema).
/// </summary>
public sealed partial class ArtworkService : IGameColorsSource
{
    private readonly ConcurrentDictionary<string, (string Signature, GameColors Colors)> _gameColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _forgotten = new(StringComparer.OrdinalIgnoreCase);
    private int _gameColorsComputed;

    /// <summary>Wie oft Spielfarben berechnet (nicht aus Speicher oder Verzeichnis gelesen) wurden; für Tests.</summary>
    public int GameColorsComputed => Volatile.Read(ref _gameColorsComputed);

    /// <summary>Nur für Tests: läuft nach der Berechnung und vor dem Speichern (Wettlauf mit einem neuen Bildstand, Fehler).</summary>
    public Action<string>? GameColorsComputedHook { get; set; }

    public bool TryPeek(GameConfig game, out GameColors colors)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (_states.TryGetValue(game.Key, out var state) && state.ConfigStamp == ConfigStamp(game)
            && _gameColors.TryGetValue(game.Key, out var known) && known.Signature == state.Signature)
        {
            colors = known.Colors;
            return true;
        }

        colors = GameColors.None;
        return false;
    }

    public Task<GameColors?> GetGameColorsAsync(GameConfig game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (TryPeek(game, out var known))
        {
            return Task.FromResult<GameColors?>(known);
        }

        var snapshot = Snapshot(game);
        return Typed(
            Enqueue(snapshot.Key + "|" + ConfigStamp(snapshot) + "|" + ArtVersion(snapshot) + "|colors", () => ReadGameColors(snapshot), cancellationToken),
            value => value as GameColors);
    }

    /// <summary>
    /// Bildstand im Schlüssel der laufenden Arbeit: die bekannte Kennung der Bilder und wie oft das Spiel vergessen wurde
    /// (neue Datei, „Bild wählen…“). Eine Anfrage nach neuen Bildern hängt sich so nie an eine Rechnung für die alten.
    /// </summary>
    private string ArtVersion(GameConfig game)
    {
        var signature = _states.TryGetValue(game.Key, out var state) && state.ConfigStamp == ConfigStamp(game) ? state.Signature : "?";
        return signature + "|" + _forgotten.GetValueOrDefault(game.Key);
    }

    /// <summary>
    /// Arbeit auf dem Bilder-Thread: Verzeichnis oder Berechnung. Ein unerwarteter Fehler ergibt null (nicht gelesen, nichts
    /// gemerkt), nie <see cref="GameColors.None"/>: Das hieße „graue Bilder“ und blendete ein buntes Spiel zum Schema.
    /// </summary>
    private GameColors? ReadGameColors(GameConfig game)
    {
        try
        {
            var state = Resolve(game, full: false);
            var stored = Entry(state.Key, e => e.Signature == state.Signature ? e.Colors : null);
            if (stored?.ToColors() is { } fromIndex)
            {
                _gameColors[state.Key] = (state.Signature, fromIndex);
                return fromIndex;
            }

            var (logo, cover, hero, transient) = GameColorInputs(state);
            var result = GameColorAnalysis.Derive(logo, cover, hero);
            Interlocked.Increment(ref _gameColorsComputed);
            GameColorsComputedHook?.Invoke(state.Key);
            if (!transient)
            {
                // Im Speicher nur, solange dieser Bildstand gilt (sonst überschriebe eine späte Rechnung eine neuere).
                if (_states.TryGetValue(state.Key, out var current) && current.Signature == state.Signature && current.ConfigStamp == state.ConfigStamp)
                {
                    _gameColors[state.Key] = (state.Signature, result);
                }

                // Nur in den Eintrag desselben Bildstands: Hat sich ein Bild während der Rechnung geändert, bleibt er leer.
                var written = WithEntry(state.Key, e =>
                {
                    if (e.Signature != state.Signature)
                    {
                        return false;
                    }

                    e.Colors = IndexedGameColors.From(result);
                    return true;
                });
                if (written)
                {
                    MarkDirty();
                }
            }

            _logger.LogDebug("Spielfarben: Akzent aus {Source}, Sicherheit {Confidence:0.00}", result.AccentSource, result.Confidence);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug("Spielfarben: nicht gelesen ({Error}).", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Die drei Quellen als kleine Fassungen (nur echte Dateien: Launcher, anderes lokales Bild, eigene Datei, SteamGridDB).
    /// Eine kaputte, zu große oder fremde Datei fehlt als Quelle, das Ergebnis gilt aber für diesen Bildstand (eine ersetzte
    /// Datei ändert die Kennung). <c>transient</c>: eine Datei war nur gerade nicht lesbar (gesperrt, kein Zugriff, halb
    /// geschrieben, kein Speicher); das Ergebnis wird dann weder gemerkt noch gespeichert, die nächste Anfrage liest neu.
    /// </summary>
    private (SourceResult? Logo, SourceResult? Cover, SourceResult? Hero, bool Transient) GameColorInputs(GameState state)
    {
        var transient = false;
        SourceResult? Read(ArtSpec spec, Func<string, SourceResult?> analyze)
        {
            if (spec.File is not { } file || !IsColorSource(spec))
            {
                return null;
            }

            try
            {
                return analyze(file);
            }
            catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
            {
                _logger.LogDebug("Spielfarben: {Kind} nicht lesbar: {Error}", spec.Kind, ex.GetType().Name);
                transient |= IsPassingReadFailure(ex);
                return null;
            }
        }

        var logo = Read(state.Logo, file => AnalyzeLogo(state, file));
        var cover = Read(state.Cover, file => AnalyzeCover(file, state.Cover.Generate == Generator.CoverFromSquare));
        var hero = Read(state.Hero, AnalyzeHero);
        return (logo, cover, hero, transient);
    }

    /// <summary>
    /// Vorübergehender Lesefehler (Datei gesperrt, kein Zugriff, abgeschnitten, kein Speicher). Formatfehler des Decoders
    /// (<see cref="NotSupportedException"/>, <see cref="FileFormatException"/>, COM), fehlende Pixel und zu große Bilder
    /// bleiben, solange die Datei bleibt.
    /// </summary>
    public static bool IsPassingReadFailure(Exception ex) =>
        ex is UnauthorizedAccessException or OutOfMemoryException || (ex is IOException && ex is not ImageTooLargeException);

    /// <summary>Eine Bildart zählt nur, wenn sie eine echte Datei zeigt (beim quadratischen Cover die Datei des Launchers).</summary>
    private static bool IsColorSource(ArtSpec spec) =>
        spec.File is not null
        && (spec.Generate == Generator.None || (spec.Kind == ArtworkKind.Cover && spec.Generate == Generator.CoverFromSquare))
        && spec.Origin is ArtworkOrigin.Launcher or ArtworkOrigin.Alternative or ArtworkOrigin.Custom or ArtworkOrigin.SteamGridDb;

    /// <summary>
    /// Logo: ein vorhandener Zuschnitt in 96 px Breite, sonst das Original in höchstens 384 px, im Speicher auf den sichtbaren
    /// Teil zugeschnitten (nie über <see cref="TrimmedLogo"/>, das Dateien schreibt).
    /// </summary>
    private SourceResult? AnalyzeLogo(GameState state, string file)
    {
        var trimmed = Entry(state.Key, e => e.Signature == state.Signature ? e.Logo : null);
        var trimmedPath = trimmed is null ? null : Path.Combine(_options.ArtDirectory, ArtworkFiles.LogoFolder, trimmed);
        var image = trimmedPath is not null && _fileSystem.FileExists(trimmedPath)
            ? ArtworkImaging.ToBgra(ArtworkImaging.Decode(trimmedPath, GameColorAnalysis.LogoLongSide))
            : ArtworkImaging.ToBgra(ArtworkImaging.Decode(file, GameColorAnalysis.LogoDecodeWidth));
        return GameColorAnalysis.PrepareLogo(image.Pixels, image.Width, image.Height) is { } small
            ? GameColorAnalysis.Analyze(small.Pixels, small.Width, small.Height, GameColorKind.Logo)
            : null;
    }

    /// <summary>Cover: in 96 px dekodiert und auf 32×48, 40×40 oder 48×32 verkleinert (ein quadratisches Bild immer 40×40).</summary>
    private static SourceResult? AnalyzeCover(string file, bool square)
    {
        var image = ArtworkImaging.ToBgra(ArtworkImaging.Decode(file, GameColorAnalysis.DecodeWidth));
        if (square)
        {
            var pixels = PixelAnalysis.Resample(image.Pixels, image.Width, image.Height, 0, 0, image.Width, image.Height, 40, 40);
            return GameColorAnalysis.Analyze(pixels, 40, 40, GameColorKind.Cover);
        }

        return GameColorAnalysis.PrepareCover(image.Pixels, image.Width, image.Height) is { } small
            ? GameColorAnalysis.Analyze(small.Pixels, small.Width, small.Height, GameColorKind.Cover)
            : null;
    }

    /// <summary>Titelbild: dieselbe kleine Fassung wie für die Umgebungsfarben (48 px breit, gestreckt).</summary>
    private static SourceResult? AnalyzeHero(string file)
    {
        var (width, height) = ArtworkImaging.Measure(file);
        var small = ArtworkImaging.DecodeStretched(file, PixelAnalysis.AmbientWidth, PixelAnalysis.AmbientHeight(width, height));
        return GameColorAnalysis.Analyze(small.Pixels, small.Width, small.Height, GameColorKind.Hero);
    }
}
