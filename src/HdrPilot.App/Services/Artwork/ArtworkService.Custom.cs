using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Resources;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Services.Artwork;

/// <summary>„Bild wählen…“: Wahlmöglichkeiten und eigene Bilder je Spiel (dauerhaft wie die eigenen Symbole, E86).</summary>
public sealed partial class ArtworkService
{
    /// <summary>Höchste Breite eines eigenen Titelbilds.</summary>
    public const int CustomHeroWidth = 3840;

    /// <summary>Höchste Breite eines eigenen Logos (Durchsichtigkeit bleibt).</summary>
    public const int CustomLogoWidth = 1280;

    /// <summary>Vorschaubilder der Wahlmöglichkeiten: höchstens so breit (Cover: so hoch).</summary>
    public const int PreviewSize = 320;

    /// <summary>Mittlere Farbigkeit, unter der ein Splash als Schwarzweiß (vermutlich Engine-Logo) gilt.</summary>
    private const double GrayscaleChroma = 6;

    public Task<IReadOnlyList<ArtworkCandidate>> GetCandidatesAsync(GameConfig game, ArtworkKind kind, CancellationToken cancellationToken = default)
    {
        if (kind is not (ArtworkKind.Hero or ArtworkKind.Logo or ArtworkKind.Cover))
        {
            return Task.FromResult<IReadOnlyList<ArtworkCandidate>>([]);
        }

        var snapshot = Snapshot(game);
        return Enqueue(snapshot.Key + "|" + ConfigStamp(snapshot) + "|candidates|" + kind, () => Candidates(snapshot, kind), cancellationToken)
            .ContinueWith(t => (IReadOnlyList<ArtworkCandidate>?)t.Result ?? [], cancellationToken, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    public string SaveCustomImage(GameConfig game, ArtworkKind kind, string sourcePath)
    {
        if (kind is not (ArtworkKind.Hero or ArtworkKind.Logo or ArtworkKind.Cover))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Eigene Bilder gibt es für Titelbild, Logo und Cover.");
        }

        // Große Fotos (48 MP und mehr) werden schon beim Dekodieren verkleinert; zu groß ist nur, was die Grenzen von
        // ArtworkImaging überschreitet (eigene Meldung, nicht „kein lesbares Bild“).
        BitmapSource source;
        try
        {
            source = ArtworkImaging.Decode(sourcePath, kind switch
            {
                ArtworkKind.Hero => CustomHeroWidth,
                ArtworkKind.Logo => CustomLogoWidth,
                _ => ArtworkRenderer.CoverWidth * 3,
            });
        }
        catch (ImageTooLargeException)
        {
            throw;
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            throw new InvalidDataException(ArtworkStrings.Error_NotAnImage, ex);
        }

        var image = kind == ArtworkKind.Cover ? ArtworkImaging.CoverFill(source, ArtworkRenderer.CoverWidth, ArtworkRenderer.CoverHeight) : source;
        var file = ArtworkFiles.CustomFileName(game.Key, kind);
        ArtworkImaging.Save(image, Path.Combine(_options.ArtDirectory, ArtworkFiles.CustomFolder, file), 92);
        _logger.LogInformation("Bilder: eigenes {Kind} für {Game} gespeichert ({File}).", kind, game.Key, file);
        ForgetInMemory(game.Key);
        Raise(game.Key);
        return ArtworkOverride.Custom(file);
    }

    public void DeleteCustomImage(string? token)
    {
        if (ArtworkOverride.KindOf(token) != ArtworkOverrideKind.Custom || ArtworkOverride.FileOf(token) is not { } file || !ArtworkFiles.IsOwnCustomFile(file))
        {
            return;
        }

        try
        {
            _fileSystem.DeleteFile(Path.Combine(_options.ArtDirectory, ArtworkFiles.CustomFolder, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Bilder: eigene Datei {File} nicht gelöscht: {Message}", file, ex.Message);
        }
    }

    /// <summary>„Automatisch“, jedes andere lokale Bild, „Erzeugt“ (beim Logo: Name als Schrift) und die aktuelle eigene Datei.</summary>
    private IReadOnlyList<ArtworkCandidate> Candidates(GameConfig game, ArtworkKind kind)
    {
        var state = Resolve(game, full: true);
        var token = kind switch
        {
            ArtworkKind.Hero => game.Artwork?.Hero,
            ArtworkKind.Logo => game.Artwork?.Logo,
            _ => game.Artwork?.Cover,
        };
        var list = new List<ArtworkCandidate>();
        var previewWidth = kind == ArtworkKind.Cover ? (PreviewSize * 2) / 3 : PreviewSize;

        // Automatisch: was ohne eigene Wahl gälte.
        var automaticState = token is null ? state : Hypothetical(state, WithoutChoice(game.Artwork, kind));
        var automatic = automaticState.Spec(kind);
        var note = kind == ArtworkKind.Cover && automaticState.Summary.CoverFromSmear ? ArtworkStrings.Note_SmearedCapsule : automatic.Label;
        list.Add(Candidate(automaticState, automatic, null, ArtworkStrings.Candidate_Automatic, note, previewWidth, token is null));

        foreach (var alternative in state.Located.Alternatives.Where(a => a.Kind == kind))
        {
            var altToken = ArtworkOverride.IsPlainImageName(alternative.FileName) ? ArtworkOverride.Alternative(alternative.FileName) : null;
            if (altToken is null)
            {
                continue;
            }

            var spec = new ArtSpec(kind, alternative.Path, ArtworkOrigin.Alternative, CacheLabel(alternative.Cache), Generator.None);
            var label = Format(ArtworkStrings.Candidate_Alternative, alternative.FileName, CacheLabel(alternative.Cache));
            list.Add(Candidate(state, spec, altToken, label, AlternativeNote(state, alternative), previewWidth, string.Equals(token, altToken, StringComparison.OrdinalIgnoreCase)));
        }

        if (kind == ArtworkKind.Logo)
        {
            list.Add(new ArtworkCandidate(ArtworkOverride.Generated, kind, ArtworkOrigin.Generated, ArtworkStrings.Candidate_NameInsteadOfLogo, null, null, 0, 0, token == ArtworkOverride.Generated));
        }
        else
        {
            var generatedState = token == ArtworkOverride.Generated ? state : Hypothetical(state, WithChoice(game.Artwork, kind, ArtworkOverride.Generated));
            list.Add(Candidate(generatedState, generatedState.Spec(kind), ArtworkOverride.Generated, ArtworkStrings.Candidate_Generated, null, previewWidth, token == ArtworkOverride.Generated));
        }

        if (ArtworkOverride.KindOf(token) == ArtworkOverrideKind.Custom && state.Spec(kind) is { Origin: ArtworkOrigin.Custom } custom)
        {
            list.Add(Candidate(state, custom, token, ArtworkStrings.Candidate_CustomFile, null, previewWidth, true));
        }

        return list;
    }

    private ArtworkCandidate Candidate(GameState state, ArtSpec spec, string? token, string label, string? note, int previewWidth, bool isCurrent)
    {
        var preview = Preview(state, spec, previewWidth);

        var (width, height) = (0, 0);
        if (GeneratedSize(spec.Generate) is { } generated)
        {
            // Erzeugte Bilder (auch das Hochformat aus einem quadratischen Bild): die Größe der gespeicherten Fassung, nicht die
            // der kleinen Vorschau oder der Vorlage.
            (width, height) = generated;
        }
        else if (spec.File is { } file)
        {
            try
            {
                (width, height) = ArtworkImaging.Measure(file);
            }
            catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
            {
                // Größe unbekannt.
            }
        }
        else if (preview is BitmapSource bitmap)
        {
            (width, height) = (bitmap.PixelWidth, bitmap.PixelHeight);
        }

        return new ArtworkCandidate(token, spec.Kind, spec.Origin, label, note, preview, width, height, isCurrent);
    }

    /// <summary>Pixelgröße, in der ein Erzeuger sein Bild in art\gen ablegt (null: kein erzeugtes Bild mit fester Größe).</summary>
    private static (int Width, int Height)? GeneratedSize(Generator generator) => generator switch
    {
        Generator.Hero => (ArtworkRenderer.HeroWidth, ArtworkRenderer.HeroHeight),
        Generator.CoverFromHero or Generator.CoverFromField or Generator.CoverFromSquare => (ArtworkRenderer.CoverWidth, ArtworkRenderer.CoverHeight),
        Generator.Header or Generator.HeaderFromSquare => (ArtworkRenderer.HeaderWidth, ArtworkRenderer.HeaderHeight),
        Generator.Monogram => (ArtworkRenderer.IconSize, ArtworkRenderer.IconSize),
        _ => null,
    };

    /// <summary>Hinweis zu einem anderen lokalen Bild: SplashScreen eines Xbox-Pakets ist oft Engine-Logo oder Titelkarte.</summary>
    private string? AlternativeNote(GameState state, LocatedImage alternative)
    {
        if (alternative.Cache != LauncherCache.Xbox)
        {
            return null;
        }

        var stored = Entry(state.Key, e => e.Alternatives?.FirstOrDefault(a => string.Equals(a.Path, alternative.Path, StringComparison.OrdinalIgnoreCase))?.Note);
        if (stored is null)
        {
            try
            {
                var chroma = PixelAnalysis.MeanChroma(ArtworkImaging.DecodeStretched(alternative.Path, 48, 27).Pixels);
                stored = chroma < GrayscaleChroma ? "grayscale" : "titlecard";
                var note = stored;
                WithEntry(state.Key, e =>
                {
                    if (e.Alternatives?.FirstOrDefault(a => string.Equals(a.Path, alternative.Path, StringComparison.OrdinalIgnoreCase)) is { } indexed)
                    {
                        indexed.Note = note;
                    }
                });
            }
            catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
            {
                return null;
            }
        }

        return stored == "grayscale" ? ArtworkStrings.Note_Grayscale : ArtworkStrings.Note_TitleCard;
    }

    /// <summary>
    /// Vorschau einer Wahlmöglichkeit (klein, nicht im Speicher des Fensters, weil sie vom geltenden Bild abweichen kann).
    /// Erzeugte Bilder der geltenden Wahl kommen aus art\gen; die einer anderen Wahl werden nur im Speicher gezeichnet.
    /// </summary>
    private BitmapSource? Preview(GameState state, ArtSpec spec, int width)
    {
        try
        {
            switch (spec.Generate)
            {
                case Generator.None when spec.File is null:
                    return null;
                case Generator.None when spec.Kind == ArtworkKind.Logo:
                    return TrimInMemory(spec.File!, width);
                case Generator.None:
                    return ArtworkImaging.Decode(spec.File!, width);
                case Generator.Field:
                    return FieldImage(state).ToBitmap();
                default:
                    if (!state.IsHypothetical)
                    {
                        return ArtworkImaging.Decode(EnsureGenerated(state, spec.Generate), width);
                    }

                    var rendered = Sta.Run(() => Render(state, spec.Generate));
                    return width < rendered.PixelWidth
                        ? ArtworkImaging.Scale(rendered, width, (int)Math.Round(rendered.PixelHeight * (width / (double)rendered.PixelWidth)))
                        : rendered;
            }
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            _logger.LogDebug("Bilder: Vorschau {Kind} von {Game} nicht lesbar: {Error}", spec.Kind, state.Key, ex.GetType().Name);
            return null;
        }
    }

    private static GameArtworkChoice? WithoutChoice(GameArtworkChoice? choice, ArtworkKind kind) => WithChoice(choice, kind, null);

    private static GameArtworkChoice? WithChoice(GameArtworkChoice? choice, ArtworkKind kind, string? token)
    {
        var copy = new GameArtworkChoice { Hero = choice?.Hero, Logo = choice?.Logo, Cover = choice?.Cover };
        switch (kind)
        {
            case ArtworkKind.Hero:
                copy.Hero = token;
                break;
            case ArtworkKind.Logo:
                copy.Logo = token;
                break;
            default:
                copy.Cover = token;
                break;
        }

        return copy.IsEmpty ? null : copy;
    }
}
