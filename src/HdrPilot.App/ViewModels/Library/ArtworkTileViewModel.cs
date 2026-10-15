using System.Windows.Media;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Eine Vorschau-Kachel im Abschnitt „BILDER“ des Detailbereichs (Titelbild, Logo, Cover, Symbol): kleines Bild, grüner
/// Punkt für ein lokales Bild aus dem Launcher oder eine eigene Wahl, oranger Punkt für ein erzeugtes. Ohne Bild zeigt die
/// Ansicht das Farbfeld des Namens mit Symbol bzw. „Aa“ (Logo) oder Monogramm.
/// </summary>
public sealed class ArtworkTileViewModel
{
    private ArtworkTileViewModel(ArtworkKind kind, string label, ImageSource? preview, ArtworkOrigin origin, string sourceLabel, GameItemViewModel item)
    {
        Kind = kind;
        Label = label;
        Preview = preview;
        Origin = origin;
        Item = item;
        Tooltip = string.IsNullOrEmpty(sourceLabel) ? label : LibraryText.Format(LibraryStrings.Artwork_TileTooltip, label, sourceLabel);
    }

    public ArtworkKind Kind { get; }

    /// <summary>„Titelbild“, „Logo“, „Cover“, „Symbol“.</summary>
    public string Label { get; }

    public ImageSource? Preview { get; }

    public bool HasPreview => Preview is not null;

    public ArtworkOrigin Origin { get; }

    public GameItemViewModel Item { get; }

    /// <summary>Lokales Bild (Launcher, andere lokale Datei, eigene Wahl, SteamGridDB): grüner Punkt.</summary>
    public bool IsLocal => Origin is ArtworkOrigin.Launcher or ArtworkOrigin.Alternative or ArtworkOrigin.Custom or ArtworkOrigin.SteamGridDb;

    /// <summary>Logo und Symbol werden eingepasst gezeigt, Titelbild und Cover füllen die Kachel.</summary>
    public bool Contain => Kind is ArtworkKind.Logo or ArtworkKind.Icon;

    /// <summary>Ersatz ohne Bild: beim Logo „Aa“, sonst das Monogramm.</summary>
    public string FallbackText => Kind == ArtworkKind.Logo ? LibraryStrings.Artwork_LogoPlaceholder : Item.Monogram;

    public string Tooltip { get; }

    /// <summary>„Bild wählen…“ öffnet sich mit diesem Reiter (Symbol: „Symbol ändern…“ über das Menü).</summary>
    public bool OpensPicker => Kind != ArtworkKind.Icon;

    internal static IReadOnlyList<ArtworkTileViewModel> For(GameItemViewModel item, GameArtwork? artwork)
    {
        var summary = artwork?.Summary ?? ArtworkSummary.Empty;
        return
        [
            Tile(ArtworkKind.Hero, LibraryStrings.Artwork_TileHero, artwork?.Hero, summary.Hero, item),
            Tile(ArtworkKind.Logo, LibraryStrings.Artwork_TileLogo, artwork?.Logo is { } logo ? logo with { Image = LibraryArt.TrimTransparent(logo.Image)! } : null, summary.Logo, item),
            Tile(ArtworkKind.Cover, LibraryStrings.Artwork_TileCover, artwork?.Cover, summary.Cover, item),
            new ArtworkTileViewModel(
                ArtworkKind.Icon,
                LibraryStrings.Artwork_TileIcon,
                artwork?.Icon?.Image ?? item.Icon,
                artwork?.Icon?.Origin ?? (item.Icon is null ? ArtworkOrigin.None : item.HasCustomIcon ? ArtworkOrigin.Custom : ArtworkOrigin.Launcher),
                artwork?.Icon?.SourceLabel ?? string.Empty,
                item),
        ];
    }

    private static ArtworkTileViewModel Tile(ArtworkKind kind, string label, ArtworkImage? image, ArtworkOrigin origin, GameItemViewModel item) =>
        new(kind, label, image?.Image, image?.Origin ?? origin, image?.SourceLabel ?? string.Empty, item);
}
