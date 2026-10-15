using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Titelbereich des gewählten Spiels (E89): Überschrift („ZULETZT GESPIELT · VOR 2 TAGEN“), Logo oder Name, Quelle,
/// HDR-Angabe, Kennzeichen und „Beim Start …“. Die Bilder (<see cref="Artwork"/>) lädt die Bibliothek im Hintergrund; bis
/// dahin und ohne Titelbild zeigt die Ansicht ein Ersatzbild aus Farbfeld, Symbol und Name.
/// </summary>
public sealed class HeroViewModel : ObservableObject
{
    private readonly Func<DateTimeOffset> _now;
    private GameArtwork? _artwork;
    private string _chipText = string.Empty;
    private string _overline = string.Empty;

    internal HeroViewModel(GameItemViewModel item, Func<DateTimeOffset> now, bool paused)
    {
        Item = item;
        _now = now;
        Refresh(paused);
    }

    public GameItemViewModel Item { get; }

    public string Key => Item.Key;

    public string Name => Item.Name;

    public GameSource Source => Item.Source;

    public string SourceName => Item.SourceName;

    /// <summary>Kleine Überschrift in Großbuchstaben.</summary>
    public string Overline
    {
        get => _overline;
        private set => SetProperty(ref _overline, value);
    }

    /// <summary>„NATIVES HDR“, „HDR TEILWEISE“, „HDR PER MOD“, „HDR UNBEKANNT“ (nie „SDR“).</summary>
    public string HdrKindLabel => Item.HdrTagText;

    public HdrSupportStatus HdrSupport => Item.HdrSupport;

    public bool IsHdrNative => Item.HdrSupport == HdrSupportStatus.Native;

    /// <summary>„HDR ein · danach zurück“, „HDR bleibt unverändert“, „Automatik pausiert“ (Regal, unter „Beim Start“).</summary>
    public string ChipText
    {
        get => _chipText;
        private set => SetProperty(ref _chipText, value);
    }

    /// <summary>Titelbild, Farbfeld, Logo, Cover, Symbol und Umgebungsfarben; null, solange sie laden.</summary>
    public GameArtwork? Artwork
    {
        get => _artwork;
        internal set
        {
            if (SetProperty(ref _artwork, value))
            {
                OnPropertyChanged(nameof(Logo));
                OnPropertyChanged(nameof(LogoWidth));
                OnPropertyChanged(nameof(LogoHeight));
                OnPropertyChanged(nameof(HasLogo));
                OnPropertyChanged(nameof(ShowName));
                OnPropertyChanged(nameof(IsGenerated));
                OnPropertyChanged(nameof(ShowIconTile));
                OnPropertyChanged(nameof(Ambient));
                OnPropertyChanged(nameof(IconImage));
            }
        }
    }

    /// <summary>Logo ohne durchsichtigen Rand (<see cref="LibraryArt.TrimTransparent"/>).</summary>
    public ImageSource? Logo => LibraryArt.TrimTransparent(_artwork?.Logo?.Image);

    /// <summary>
    /// Größe des zugeschnittenen Logos in Bildpunkten (nicht in WPF-Einheiten): Manche Logos tragen 300 dpi und mehr in den
    /// Metadaten und stünden sonst winzig da. Das Fenster skaliert das Logo damit wie der Prototyp nur verkleinernd.
    /// </summary>
    public double LogoWidth => Logo switch
    {
        BitmapSource bitmap => bitmap.PixelWidth,
        { } image => image.Width,
        _ => 0,
    };

    public double LogoHeight => Logo switch
    {
        BitmapSource bitmap => bitmap.PixelHeight,
        { } image => image.Height,
        _ => 0,
    };

    public bool HasLogo => Logo is not null;

    /// <summary>Kein Logo: der Name steht in der Titelschrift.</summary>
    public bool ShowName => Logo is null;

    /// <summary>Kein Titelbild aus einer Quelle (erzeugt oder noch nicht geladen): Symbol-Kachel links vom Namen.</summary>
    public bool IsGenerated => _artwork?.Hero is not { } hero || hero.Origin == ArtworkOrigin.Generated;

    public bool ShowIconTile => IsGenerated && ShowName;

    /// <summary>Symbol für die Kachel neben dem Namen: aus dem Bildsatz, sonst das Symbol der Liste.</summary>
    public ImageSource? IconImage => _artwork?.Icon?.Image ?? Item.Icon;

    public string Monogram => Item.Monogram;

    /// <summary>Umgebungsfarben des Spiels (Ersatz: aus dem Namen).</summary>
    public AmbientColors Ambient => _artwork?.Ambient ?? AmbientColors.FromName(Item.Name);

    /// <summary>Texte nach einer Änderung neu bilden (Konfiguration, Pause, HDR-Angabe).</summary>
    internal void Refresh(bool paused)
    {
        var config = Item.Config;
        Overline = RelativeTime.Overline(Item.IsNew, config.FirstSeen, config.LastPlayed, _now()).ToUpper(CultureInfo.CurrentCulture);
        ChipText = paused
            ? LibraryStrings.Hero_ChipPaused
            : !Item.HdrAuto
                ? LibraryStrings.Hero_ChipUnchanged
                : config.RestoreAfter ? LibraryStrings.Hero_ChipRestore : LibraryStrings.Hero_ChipStay;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HdrKindLabel));
        OnPropertyChanged(nameof(HdrSupport));
        OnPropertyChanged(nameof(IsHdrNative));
        OnPropertyChanged(nameof(IconImage));
    }
}
