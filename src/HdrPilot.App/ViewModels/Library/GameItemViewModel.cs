using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;

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
    private bool _isNew;
    private bool _isInstalled;
    private bool _isRunning;
    private bool _iconRequested;
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

    /// <summary>Symbol aus der EXE; null bis geladen (dann Ersatzsymbol der Quelle).</summary>
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

    internal void SetIcon(ImageSource? icon)
    {
        if (icon is not null && !ReferenceEquals(icon, _icon))
        {
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
        }
    }

    /// <summary>Übernimmt einen neuen Stand aus der Konfiguration, ohne Schreibvorgänge auszulösen.</summary>
    internal void Apply(GameConfig config)
    {
        _config = config;
        Name = config.Name;
        InstallDir = config.InstallDir;
        if (_hdrAuto != config.HdrAuto)
        {
            _hdrAuto = config.HdrAuto;
            OnPropertyChanged(nameof(HdrAuto));
        }

        IsNew = config.IsNew;
        IsInstalled = config.IsInstalled;
    }

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
