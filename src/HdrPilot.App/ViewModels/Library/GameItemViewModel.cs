using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
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
        private set => SetProperty(ref _isInstalled, value);
    }

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
            }
        }
    }

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

        Name = config.Name;
        InstallDir = config.InstallDir;
        if (_hdrAuto != config.HdrAuto)
        {
            _hdrAuto = config.HdrAuto;
            OnPropertyChanged(nameof(HdrAuto));
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
        }
    }

    public override string ToString() => Name;
}
