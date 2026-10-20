using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Eine Zeile der Spieleliste. Hält den letzten Konfigurationsstand des Spiels (nur lesen). Das Symbol wird erst
/// geladen, wenn eine sichtbare Zeile es anfragt (virtualisierte Liste).
/// </summary>
public sealed class GameItemViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;
    private GameConfig _config;
    private string _name = string.Empty;
    private string _installDir = string.Empty;
    private bool _hdrAuto;
    private bool _autoHdr;
    private bool _isNew;
    private bool _isInstalled;
    private bool _isRunning;
    private HdrSupportStatus _hdrSupport;
    private bool _iconRequested;
    private int _iconRequest;
    private ImageSource? _icon;
    private bool _thumbnailRequested;
    private ImageSource? _thumbnail;
    private bool _coverRequested;
    private ImageSource? _cover;
    private int _artworkRequest;
    private Brush? _nameField;

    internal GameItemViewModel(LibraryViewModel owner, GameConfig config)
    {
        _owner = owner;
        _config = config;
        Key = config.Key;
        Source = config.Source;
        SourceName = LibraryText.SourceName(config.Source);
        Apply(config);
    }

    public string Key { get; }

    public GameSource Source { get; }

    public string SourceName { get; }

    public bool IsManual => Source == GameSource.Manual;

    /// <summary>Letzter bekannter Konfigurationsstand dieses Spiels.</summary>
    public GameConfig Config => _config;

    public string Name
    {
        get => _name;
        private set
        {
            if (SetProperty(ref _name, value))
            {
                SearchText = value + " " + SourceName;
            }
        }
    }

    /// <summary>Durchsuchter Text: Name und Quelle.</summary>
    public string SearchText { get; private set; } = string.Empty;

    public string InstallDir
    {
        get => _installDir;
        private set => SetProperty(ref _installDir, value);
    }

    /// <summary>Schalter „HDR automatisch“. Setzen schreibt die Konfiguration und gilt als „angesehen“.</summary>
    public bool HdrAuto
    {
        get => _hdrAuto;
        set
        {
            if (SetProperty(ref _hdrAuto, value))
            {
                OnPropertyChanged(nameof(HdrAutoStateText));
                _owner.OnHdrAutoChangedByUser(this);
            }
        }
    }

    /// <summary>
    /// „Windows Auto HDR“ (E88) ist für dieses Spiel an: nur das Kennzeichen „AUTO HDR“ in der Liste, geschaltet wird im
    /// Detailbereich (Abschnitt „BILD“). Stand aus der Konfiguration.
    /// </summary>
    public bool AutoHdr
    {
        get => _autoHdr;
        private set => SetProperty(ref _autoHdr, value);
    }

    public bool IsNew
    {
        get => _isNew;
        internal set => SetProperty(ref _isNew, value);
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(IsRemovable));
            }
        }
    }

    /// <summary>
    /// Nicht installiertes Spiel eines Launchers: „Einstellungen entfernen…“ wird angeboten (E90). Selbst hinzugefügte Spiele
    /// haben „Aus der Bibliothek entfernen“.
    /// </summary>
    public bool IsRemovable => !IsManual && !_isInstalled;

    /// <summary>Gerade eine Sitzung aktiv (HDR für dieses Spiel geschaltet).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        internal set => SetProperty(ref _isRunning, value);
    }

    /// <summary>HDR-Unterstützung laut PCGamingWiki (aus dem Zwischenspeicher des Abgleichs).</summary>
    public HdrSupportStatus HdrSupport
    {
        get => _hdrSupport;
        internal set
        {
            if (SetProperty(ref _hdrSupport, value))
            {
                OnPropertyChanged(nameof(HasHdrBadge));
                OnPropertyChanged(nameof(HdrBadgeText));
                OnPropertyChanged(nameof(HdrBadgeTooltip));
                OnPropertyChanged(nameof(ShowHdrPill));
                OnPropertyChanged(nameof(HdrPillText));
                OnPropertyChanged(nameof(HdrKindLabel));
                OnPropertyChanged(nameof(HdrTagText));
            }
        }
    }

    /// <summary>HDR-Angabe als Kennzeichen: „NATIVES HDR“, „HDR TEILWEISE“, „HDR UNBEKANNT“ …</summary>
    public string HdrTagText => LibraryText.HdrTag(_hdrSupport);

    /// <summary>Spalte „HDR“ und Kachel: Kennzeichen „Nativ“, „Teilweise“ oder „Mod“ (sonst ein Strich).</summary>
    public bool ShowHdrPill => _hdrSupport is HdrSupportStatus.Native or HdrSupportStatus.Limited or HdrSupportStatus.ModOnly;

    /// <summary>Text des Kennzeichens in der Spalte „HDR“; leer ohne Kennzeichen.</summary>
    public string HdrPillText => LibraryText.HdrPill(_hdrSupport);

    /// <summary>HDR-Angabe in Worten für Titelbereich, Karte und Detailbereich: „Natives HDR“, „HDR teilweise“ … (nie „SDR“).</summary>
    public string HdrKindLabel => LibraryText.HdrKindLabel(_hdrSupport);

    /// <summary>„Ein“/„Aus“ neben dem Schalter in der Liste.</summary>
    public string HdrAutoStateText => _hdrAuto ? LibraryStrings.List_SwitchOn : LibraryStrings.List_SwitchOff;

    /// <summary>„vor 2 Tagen“, „gestern, 21:15“, „nie gespielt“ (Spalte „Zuletzt gespielt“).</summary>
    public string LastPlayedText => RelativeTime.Format(_config.LastPlayed, _owner.Now);

    public bool HasLastPlayed => _config.LastPlayed is not null;

    /// <summary>Zwei Buchstaben für Ersatzbilder ohne Symbol („Baldur's Gate 3“ → „BG“).</summary>
    public string Monogram => LibraryText.Monogram(_name);

    /// <summary>Farbfeld aus dem Namen (fester Farbton je Spiel) für erzeugte Cover und Vorschaubilder, solange kein Bild da ist.</summary>
    public Brush NameField => _nameField ??= LibraryArt.NameField(_name);

    /// <summary>
    /// Querformat-Vorschau der Listenzeile (Steam header bzw. Ersatzbild), klein dekodiert (128 px); null bis geladen. Wird
    /// erst angefragt, wenn eine sichtbare Zeile es zeigt.
    /// </summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested)
            {
                _thumbnailRequested = true;
                _thumbnail = _owner.RequestArtwork(this, ArtworkKind.Header, LibraryArt.ThumbnailWidth, _artworkRequest);
            }

            return _thumbnail;
        }
    }

    public bool HasThumbnail => _thumbnail is not null;

    /// <summary>Hochformat-Cover der Kacheln (Raster, Regal), dekodiert in doppelter Kachelbreite; null bis geladen.</summary>
    public ImageSource? Cover
    {
        get
        {
            if (!_coverRequested)
            {
                _coverRequested = true;
                _cover = _owner.RequestArtwork(this, ArtworkKind.Cover, LibraryArt.CoverWidth, _artworkRequest);
                if (_cover is not null)
                {
                    _owner.TouchCover(this);
                }
            }

            return _cover;
        }
    }

    public bool HasCover => _coverRequested && _cover is not null;

    /// <summary>Ergebnis einer Bildanfrage; eines aus einer älteren Anfrage (vor <see cref="ReloadArtwork"/>) zählt nicht mehr.</summary>
    internal void SetArtwork(ArtworkKind kind, ImageSource? image, int request)
    {
        if (request != _artworkRequest || image is null)
        {
            return;
        }

        if (kind == ArtworkKind.Header && !ReferenceEquals(image, _thumbnail))
        {
            _thumbnail = image;
            OnPropertyChanged(nameof(Thumbnail));
            OnPropertyChanged(nameof(HasThumbnail));
        }
        else if (kind == ArtworkKind.Cover && !ReferenceEquals(image, _cover))
        {
            _cover = image;
            _owner.TouchCover(this);
            OnPropertyChanged(nameof(Cover));
            OnPropertyChanged(nameof(HasCover));
        }
    }

    /// <summary>Bilder neu anfragen (neue Datei im Cache, eigene Wahl); bis dahin das Ersatzbild.</summary>
    internal void ReloadArtwork()
    {
        _artworkRequest++;
        var hadThumbnail = _thumbnailRequested;
        var hadCover = _coverRequested;
        _thumbnailRequested = false;
        _coverRequested = false;
        _thumbnail = null;
        _cover = null;
        if (hadThumbnail)
        {
            OnPropertyChanged(nameof(Thumbnail));
            OnPropertyChanged(nameof(HasThumbnail));
        }

        if (hadCover)
        {
            OnPropertyChanged(nameof(Cover));
            OnPropertyChanged(nameof(HasCover));
        }
    }

    /// <summary>Cover aus dem Speicher geben (zu viele geladen); eine sichtbare Kachel fragt es beim nächsten Zeigen neu an.</summary>
    internal void ReleaseCover()
    {
        if (_coverRequested)
        {
            _coverRequested = false;
            _cover = null;
        }
    }

    /// <summary>Zeit hat sich weitergedreht (Fenster neu geöffnet, Mitternacht): „Zuletzt gespielt“ neu formulieren.</summary>
    internal void RefreshTimeTexts() => OnPropertyChanged(nameof(LastPlayedText));

    /// <summary>Kennzeichen „HDR“ (eigenes HDR) oder „HDR*“ (eingeschränkt); sonst keines.</summary>
    public bool HasHdrBadge => HdrSupportValues.IsHdrCapable(_hdrSupport);

    /// <summary>Text des Kennzeichens; leer ohne Kennzeichen (AutomationProperties.Name erlaubt kein null).</summary>
    public string HdrBadgeText => _hdrSupport switch
    {
        HdrSupportStatus.Native => LibraryStrings.Badge_Hdr,
        HdrSupportStatus.Limited => LibraryStrings.Badge_HdrLimited,
        _ => string.Empty,
    };

    public string HdrBadgeTooltip => _hdrSupport switch
    {
        HdrSupportStatus.Native => LibraryStrings.Badge_HdrTooltip,
        HdrSupportStatus.Limited => LibraryStrings.Badge_HdrLimitedTooltip,
        _ => string.Empty,
    };

    /// <summary>Symbol des Spiels (eigenes, vom Launcher oder aus der EXE); null bis geladen (dann Ersatzsymbol der Quelle).</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _icon = _owner.RequestIcon(this);
            }

            return _icon;
        }
    }

    /// <summary>Ein eigenes Symbol ist gesetzt („Symbol zurücksetzen“ möglich).</summary>
    public bool HasCustomIcon => !string.IsNullOrEmpty(_config.CustomIcon);

    /// <summary>Nummer der laufenden Symbolanfrage; <see cref="ReloadIcon"/> zählt weiter.</summary>
    internal int IconRequest => _iconRequest;

    /// <summary>
    /// Ergebnis einer Symbolanfrage. Eines aus einer älteren Anfrage (vor <see cref="ReloadIcon"/>) wird verworfen: Die neue
    /// Anfrage ist oft zuerst fertig (zuletzt angefragte zuerst), und das alte Symbol darf das neue nicht überschreiben.
    /// </summary>
    internal void SetIcon(ImageSource? icon, int request)
    {
        if (request == _iconRequest && icon is not null && !ReferenceEquals(icon, _icon))
        {
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
        }
    }

    /// <summary>Symbol neu anfragen (eigenes Symbol geändert oder zurückgesetzt); bis dahin das Ersatzsymbol.</summary>
    internal void ReloadIcon()
    {
        if (!_iconRequested)
        {
            return; // Noch nie sichtbar oder schon neu angefragt: wird beim nächsten Anzeigen ohnehin geladen.
        }

        _iconRequested = false;
        _iconRequest++;
        _icon = null;
        OnPropertyChanged(nameof(Icon));
    }

    /// <summary>Übernimmt einen neuen Stand aus der Konfiguration, ohne Schreibvorgänge auszulösen.</summary>
    internal void Apply(GameConfig config)
    {
        var previous = _config;
        _config = config;
        if (!string.Equals(previous.CustomIcon, config.CustomIcon, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(HasCustomIcon));
        }

        if (!ReferenceEquals(previous, config) && !SameIconInputs(previous, config))
        {
            ReloadIcon(); // etwa neue Symbol-Hinweise nach dem Einlesen oder ein eigenes Symbol
        }

        if (!ReferenceEquals(previous, config)
            && (!SameArtworkChoice(previous.Artwork, config.Artwork) || !SameIconInputs(previous, config) || !string.Equals(previous.Name, config.Name, StringComparison.Ordinal)))
        {
            // „Bild wählen…“ (eigenes Bild, erzeugtes oder wieder automatisch), anderes Symbol oder anderer Name: Vorschaubild
            // und Cover neu anfragen (ohne Titelbild entstehen sie aus Symbol und Name).
            ReloadArtwork();
        }

        if (!string.Equals(_name, config.Name, StringComparison.Ordinal))
        {
            _nameField = null;
            Name = config.Name;
            OnPropertyChanged(nameof(Monogram));
            OnPropertyChanged(nameof(NameField));
        }

        if (previous.LastPlayed != config.LastPlayed)
        {
            OnPropertyChanged(nameof(LastPlayedText));
            OnPropertyChanged(nameof(HasLastPlayed));
        }

        InstallDir = config.InstallDir;
        if (_hdrAuto != config.HdrAuto)
        {
            _hdrAuto = config.HdrAuto;
            OnPropertyChanged(nameof(HdrAuto));
            OnPropertyChanged(nameof(HdrAutoStateText));
        }

        AutoHdr = config.AutoHdr;
        IsNew = config.IsNew;
        IsInstalled = config.IsInstalled;
    }

    /// <summary>Gleiche Eingaben für das Symbol (eigenes Symbol, Hinweise, Ordner, installiert, EXEs): dann nicht neu laden.</summary>
    private static bool SameIconInputs(GameConfig a, GameConfig b) =>
        string.Equals(a.CustomIcon, b.CustomIcon, StringComparison.Ordinal)
        && string.Equals(a.InstallDir, b.InstallDir, StringComparison.OrdinalIgnoreCase)
        && a.IsInstalled == b.IsInstalled
        && a.IconCandidates.SequenceEqual(b.IconCandidates, StringComparer.OrdinalIgnoreCase)
        && a.KnownExecutables.SequenceEqual(b.KnownExecutables, StringComparer.OrdinalIgnoreCase)
        && a.DetectedExecutables.SequenceEqual(b.DetectedExecutables, StringComparer.OrdinalIgnoreCase)
        && a.ExcludedExecutables.SequenceEqual(b.ExcludedExecutables, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ändert den Schalter ohne Rückmeldung an die Liste (Rückfall bei Schreibfehler).</summary>
    internal void SetHdrAutoSilently(bool value)
    {
        if (_hdrAuto != value)
        {
            _hdrAuto = value;
            OnPropertyChanged(nameof(HdrAuto));
            OnPropertyChanged(nameof(HdrAutoStateText));
        }
    }

    /// <summary>Gleiche eigene Bildwahl (Titelbild, Logo, Cover): dann die Bilder nicht neu laden.</summary>
    private static bool SameArtworkChoice(GameArtworkChoice? a, GameArtworkChoice? b) =>
        string.Equals(a?.Hero, b?.Hero, StringComparison.Ordinal)
        && string.Equals(a?.Logo, b?.Logo, StringComparison.Ordinal)
        && string.Equals(a?.Cover, b?.Cover, StringComparison.Ordinal);

    public override string ToString() => Name;
}
