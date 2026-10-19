using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Hauptfenster „Spielebibliothek“: Spieleliste mit Suche, Filtern und Sortierung, Kopf mit HDR-Status und
/// Schnellaktionen, Detailbereich des gewählten Spiels, Fehler je Scanner und Vorschläge aus Suchordnern.
/// <para>
/// Schreibvorgänge laufen über <see cref="ILibraryContext.UpdateConfigAsync"/> im Hintergrund; die Liste zeigt die
/// Änderung sofort und gleicht sich danach mit der Konfiguration ab. Gefiltert und sortiert wird über eine
/// CollectionView (Suche entprellt), damit 600+ Spiele flüssig bleiben. Sortierung, Breite des Detailbereichs und Höhe
/// des Titelbereichs der Liste merkt sich die Konfiguration (AppSettings.LibrarySortOrder, AppSettings.LibraryPaneRatio,
/// AppSettings.LibraryHeroRatio). Die HDR-Unterstützung laut PCGamingWiki (Kennzeichen, Sortierung, Filter „HDR-fähig“)
/// wird beim Öffnen und nach jedem Abgleich aus dem Zwischenspeicher geladen und lebt nur, solange das Fenster offen ist.
/// </para>
/// <para>
/// Nicht installierte Spiele (E90): Mit AppSettings.HideUninstalledGames stehen sie nicht in der Grundmenge
/// <see cref="Games"/> und zählen nirgends mit (Kopf, Quellen, Kapsel, Leerzustand); ihre Zeilen bleiben für Automatik und
/// laufende Sitzungen erhalten. „Einstellungen entfernen…“ nimmt ein nicht installiertes Launcher-Spiel nach einer
/// Rückfrage heraus (<see cref="RemoveSettingsAsync"/>). Verlässt das gewählte Spiel die Liste (entfernt oder
/// ausgeblendet), wird sein Nachbar gewählt.
/// </para>
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    /// <summary>„Angesehen“ wird gesammelt gespeichert (Blättern mit den Pfeiltasten schreibt nicht je Spiel).</summary>
    public static readonly TimeSpan SeenDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Spaltenbreite und Höhe des Titelbereichs werden gesammelt gespeichert (Verschieben mit den Pfeiltasten schreibt nicht
    /// je Schritt).
    /// </summary>
    public static readonly TimeSpan LayoutDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Dauert die Prüfung vor der Rückfrage „Einstellungen entfernen…“ länger (Laufwerk, Ordner, Verknüpfungen; E90), sagt die
    /// Hinweiszeile, dass geprüft wird; kürzer bliebe sie nur ein Flackern.
    /// </summary>
    public static readonly TimeSpan CheckingDelay = TimeSpan.FromMilliseconds(250);

    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
    private static readonly CompareInfo SearchCompare = CultureInfo.InvariantCulture.CompareInfo;

    private readonly ILibraryContext _context;
    private readonly ILibraryDialogs _dialogs;
    private readonly Dictionary<string, GameItemViewModel> _byKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zeilen in <see cref="Games"/> (die Grundmenge); ausgeblendete nicht installierte Spiele nur in <see cref="_byKey"/>.</summary>
    private readonly HashSet<GameItemViewModel> _listed = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SourceFilterOption _allSources;
    private readonly HashSet<string> _seenPending = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _seenDelay;
    private IDisposable? _layoutDelay;
    private bool _layoutPending;
    private bool _heroPending;
    private double? _detailsRatio;
    private double? _heroRatio;
    private SortOption _selectedSort;

    private AppConfig? _syncedConfig;
    private int _pendingWrites;
    private bool _syncQueued;
    private bool _refreshing;
    private bool _disposed;
    private bool _hideUninstalled;
    private bool _removingSettings;
    private int _selectionFollows;
    private IDisposable? _searchDelay;
    private IDisposable? _statusDelay;
    private string _candidateSignature = string.Empty;
    private int _candidateRun;

    private string _searchText = string.Empty;
    private string[] _searchTokens = [];
    private SourceFilterOption _selectedSource;
    private bool _onlySelected;
    private bool _onlyNew;
    private bool _onlyHdrCapable;
    private IReadOnlyDictionary<string, HdrSupportStatus> _hdrSupport = new Dictionary<string, HdrSupportStatus>();
    private int _hdrSupportRun;
    private GameItemViewModel? _selectedGame;
    private GameItemViewModel? _displacedGame;
    private GameDetailsViewModel? _details;

    private int _totalCount;
    private int _hdrAutoCount;
    private int _newCount;
    private int _visibleCount;
    private bool _isHdrOn;
    private bool _canToggleHdr;
    private string _hdrStatusText = string.Empty;
    private string? _activeSessionText;
    private bool _isScanning;
    private bool _hasScanErrors;
    private string _scanErrorsHeader = string.Empty;
    private bool _hasCandidates;
    private string? _statusMessage;
    private StatusKind _statusKind;

    public LibraryViewModel(ILibraryContext context, ILibraryDialogs dialogs)
    {
        _context = context;
        _dialogs = dialogs;
        _allSources = new SourceFilterOption(null, 0);
        _selectedSource = _allSources;
        SourceOptions = [_allSources];

        // Sortierung, Spaltenbreite und Höhe des Titelbereichs aus der Konfiguration (unbekannte oder fehlende Werte: Standard).
        var settings = _context.Config.Settings;
        SortOptions = GameSorting.CreateOptions();
        var order = GameSorting.Parse(settings.LibrarySortOrder);
        _selectedSort = SortOptions.First(o => o.Order == order);
        _detailsRatio = settings.LibraryPaneRatio;
        _heroRatio = settings.LibraryHeroRatio;

        GamesView = new ListCollectionView(Games) { Filter = Matches };
        ApplySortToView();

        ToggleHdrCommand = new AsyncRelayCommand(ToggleHdrAsync, () => CanToggleHdr);
        OpenSettingsCommand = new RelayCommand(() => _context.ShowSettings());
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsScanning);
        AddGameCommand = new AsyncRelayCommand(AddGameAsync);
        MarkAllSeenCommand = new AsyncRelayCommand(MarkAllSeenAsync, () => NewCount > 0);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        AddCandidateCommand = new AsyncRelayCommand(p => AddCandidateAsync(p as CandidateItemViewModel));
        DismissStatusCommand = new RelayCommand(() => StatusMessage = null);
        ChangeIconCommand = new AsyncRelayCommand(ChangeIconAsync, () => _selectedGame is not null);
        ResetIconCommand = new AsyncRelayCommand(ResetIconAsync, () => _selectedGame is { HasCustomIcon: true });
        RemoveSettingsCommand = new AsyncRelayCommand(
            () => _selectedGame is { } item ? RemoveSettingsAsync(item) : Task.CompletedTask,
            () => _selectedGame is { IsRemovable: true, IsRunning: false });

        _context.ConfigChanged += OnConfigChanged;
        _context.HdrStateChanged += OnHdrStateChanged;
        _context.ScanStateChanged += OnScanStateChanged;
        _context.HdrSupportChanged += OnHdrSupportChanged;

        _isScanning = _context.IsScanning;
        InitializeShowcase(settings);
        SyncFromConfig(); // startet auch die Suche nach Vorschlägen
        RefreshHdrState();
        RefreshScanErrors();
        _ = RefreshHdrSupportAsync();
    }

    /// <summary>Das Fenster soll dieses Spiel in den sichtbaren Bereich holen (nach dem Hinzufügen).</summary>
    public event EventHandler<GameItemViewModel>? RevealRequested;

    /// <summary>
    /// Die Auswahl ist der Liste gefolgt: Suche oder Filtern (erster Treffer, keiner oder die vorherige zurück, siehe
    /// <see cref="SelectedGame"/>) oder das gewählte Spiel hat die Liste verlassen (entfernt oder ausgeblendet, dann der
    /// Nachbar, E90). Das Fenster holt sie in den sichtbaren Bereich, und lag der Fokus auf der verschwundenen Zeile, folgt
    /// er ihr.
    /// </summary>
    public event EventHandler? SelectionFollowedFilter;

    public BulkObservableCollection<GameItemViewModel> Games { get; } = [];

    /// <summary>Gefilterte Sicht auf <see cref="Games"/> (an die Liste gebunden).</summary>
    public ListCollectionView GamesView { get; }

    public ObservableCollection<SourceFilterOption> SourceOptions { get; }

    public BulkObservableCollection<ScanErrorItem> ScanErrors { get; } = [];

    public BulkObservableCollection<CandidateItemViewModel> Candidates { get; } = [];

    public AsyncRelayCommand ToggleHdrCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public AsyncRelayCommand RescanCommand { get; }

    public AsyncRelayCommand AddGameCommand { get; }

    public AsyncRelayCommand MarkAllSeenCommand { get; }

    public RelayCommand ClearFiltersCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    public AsyncRelayCommand AddCandidateCommand { get; }

    public RelayCommand DismissStatusCommand { get; }

    /// <summary>„Symbol ändern…“ für das gewählte Spiel (Rechtsklick in der Liste oder Klick auf das Symbol im Detailbereich).</summary>
    public AsyncRelayCommand ChangeIconCommand { get; }

    /// <summary>„Symbol zurücksetzen“: eigenes Symbol des gewählten Spiels löschen, wieder automatisch.</summary>
    public AsyncRelayCommand ResetIconCommand { get; }

    /// <summary>
    /// „Einstellungen entfernen…“ für das gewählte Spiel (Kontextmenü, Taste Entf; E90): nur nicht installierte
    /// Launcher-Spiele ohne laufende oder vorbereitete Sitzung (<see cref="RemoveSettingsAsync"/>).
    /// </summary>
    public AsyncRelayCommand RemoveSettingsCommand { get; }

    internal ILibraryContext Context => _context;

    internal ILibraryDialogs Dialogs => _dialogs;

    internal IReadOnlyList<DisplayInfo> Displays { get; private set; } = [];

    internal IReadOnlyList<DisplayInfo> Targets { get; private set; } = [];

    // ---------- Filter ----------

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasSearchText));
                _searchDelay?.Dispose();
                _searchDelay = _context.Ui.Delay(SearchDelay, ApplySearch);
            }
        }
    }

    public bool HasSearchText => _searchText.Length > 0;

    public SourceFilterOption? SelectedSource
    {
        get => _selectedSource;
        set
        {
            var next = value ?? _allSources;
            if (!ReferenceEquals(next, _selectedSource))
            {
                _selectedSource = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFiltered));
                RefreshView();
            }
        }
    }

    public bool OnlySelected
    {
        get => _onlySelected;
        set
        {
            if (SetProperty(ref _onlySelected, value))
            {
                OnPropertyChanged(nameof(IsFiltered));
                RefreshView();
            }
        }
    }

    public bool OnlyNew
    {
        get => _onlyNew;
        set
        {
            if (SetProperty(ref _onlyNew, value))
            {
                OnPropertyChanged(nameof(IsFiltered));
                RefreshView();
            }
        }
    }

    /// <summary>Nur Spiele mit HDR laut PCGamingWiki (auch eingeschränkt).</summary>
    public bool OnlyHdrCapable
    {
        get => _onlyHdrCapable;
        set
        {
            if (SetProperty(ref _onlyHdrCapable, value))
            {
                OnPropertyChanged(nameof(IsFiltered));
                RefreshView();
            }
        }
    }

    public bool IsFiltered => _searchTokens.Length > 0 || _selectedSource.Source is not null || _onlySelected || _onlyNew || _onlyHdrCapable;

    // ---------- Sortierung ----------

    public IReadOnlyList<SortOption> SortOptions { get; }

    /// <summary>Gewählte Sortierung; wird sofort angewandt und in AppSettings.LibrarySortOrder gespeichert.</summary>
    public SortOption SelectedSortOption
    {
        get => _selectedSort;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedSort))
            {
                return; // Die Auswahlliste meldet beim Neuaufbau kurz null.
            }

            _selectedSort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SortOrder));
            OnSortChanged();
            ApplySortToView();

            // Immer schreiben: Ein Vergleich mit der gespeicherten Konfiguration könnte während eines noch laufenden
            // Schreibvorgangs veraltet sein (schnelles Hin- und Zurückschalten).
            var stored = GameSorting.ToConfig(value.Order);
            _ = WriteAsync(c => c.Settings.LibrarySortOrder = stored);
        }
    }

    public GameSortOrder SortOrder
    {
        get => _selectedSort.Order;
        set => SelectedSortOption = SortOptions.First(o => o.Order == value);
    }

    // ---------- Spaltenbreite, Höhe des Titelbereichs ----------

    /// <summary>Gemerkter Anteil des Detailbereichs (null: Standard). Das Fenster begrenzt ihn auf seine Größe.</summary>
    public double? DetailsRatio => _detailsRatio;

    /// <summary>
    /// Neuer Anteil des Detailbereichs nach dem Verschieben des Trenners (null: zurück zum Standard). Gespeichert
    /// wird gesammelt nach <see cref="LayoutDelay"/> bzw. beim Schließen.
    /// </summary>
    public void SetDetailsRatio(double? ratio)
    {
        double? value = ratio is { } r && double.IsFinite(r) ? Math.Round(Math.Clamp(r, 0, 1), 3) : null;
        if (_disposed || Nullable.Equals(value, _detailsRatio))
        {
            return;
        }

        _detailsRatio = value;
        _layoutPending = true;
        _layoutDelay?.Dispose();
        _layoutDelay = _context.Ui.Delay(LayoutDelay, FlushLayout);
    }

    /// <summary>Gemerkte Höhe des Titelbereichs der Liste als Anteil der Fensterhöhe (null: Standard, ein Drittel).</summary>
    public double? HeroRatio => _heroRatio;

    /// <summary>
    /// Neue Höhe des Titelbereichs der Liste nach dem Ziehen am Griff oder per Tastatur (null: zurück zum Standard). Auf
    /// vier Stellen gerundet (bei 1440 px Fensterhöhe unter 0,1 px), gespeichert wie <see cref="SetDetailsRatio"/>.
    /// </summary>
    public void SetHeroRatio(double? ratio)
    {
        double? value = ratio is { } r && double.IsFinite(r) ? Math.Round(Math.Clamp(r, 0.01, 0.99), 4) : null;
        if (_disposed || Nullable.Equals(value, _heroRatio))
        {
            return;
        }

        _heroRatio = value;
        _heroPending = true;
        _layoutDelay?.Dispose();
        _layoutDelay = _context.Ui.Delay(LayoutDelay, FlushLayout);
    }

    // ---------- Auswahl und Details ----------

    /// <summary>
    /// Gewähltes Spiel (Titelbereich und Details). Selbst gewählt gilt es als angesehen. Fällt es durch Suche oder Filter
    /// aus der Liste, folgt die Auswahl leise der Liste (Entscheidung des Nutzers 2026-10-19, ersetzt die Regel aus Phase 4,
    /// nach der Neufiltern die Auswahl nie verschob): erster Treffer, ohne Treffer keiner; steht das verdrängte Spiel
    /// wieder in der Liste, kehrt es zurück (<see cref="FollowFilter"/>).
    /// </summary>
    public GameItemViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            // Beim Neufiltern ändert die Liste die Auswahl nur als Nebenwirkung (null, oder bei einer mit dem
            // aktuellen Element verbundenen Liste das erste Element): Das ist keine Wahl, welches Spiel dann gilt, legt
            // FollowFilter fest, und nichts gilt als angesehen.
            if (_refreshing || ReferenceEquals(value, _selectedGame))
            {
                return;
            }

            _displacedGame = null; // selbst gewählt: Das vom Filter verdrängte Spiel kehrt nicht mehr zurück.
            ShowSelection(value);
            if (value is { IsNew: true })
            {
                MarkSeen(value);
            }
        }
    }

    public GameDetailsViewModel? Details
    {
        get => _details;
        private set
        {
            var previous = _details;
            if (SetProperty(ref _details, value))
            {
                previous?.Detach(); // hört sonst weiter auf seine Zeile
                OnPropertyChanged(nameof(HasDetails));
            }
        }
    }

    public bool HasDetails => _details is not null;

    // ---------- Kennzahlen ----------

    public int TotalCount
    {
        get => _totalCount;
        private set => SetProperty(ref _totalCount, value);
    }

    public int HdrAutoCount
    {
        get => _hdrAutoCount;
        private set => SetProperty(ref _hdrAutoCount, value);
    }

    public int NewCount
    {
        get => _newCount;
        private set
        {
            if (SetProperty(ref _newCount, value))
            {
                MarkAllSeenCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int VisibleCount
    {
        get => _visibleCount;
        private set => SetProperty(ref _visibleCount, value);
    }

    public string TotalText => LibraryText.Games(_totalCount);

    public string HdrAutoText => LibraryText.Format(LibraryStrings.Stats_HdrAuto, _hdrAutoCount);

    public string NewText => LibraryText.Format(LibraryStrings.Stats_New, _newCount);

    public string ResultText => IsFiltered
        ? LibraryText.Format(LibraryStrings.List_ResultFiltered, _visibleCount, _totalCount)
        : LibraryText.Games(_totalCount);

    public bool IsLibraryEmpty => _totalCount == 0;

    public bool IsFilterEmpty => _totalCount > 0 && _visibleCount == 0;

    /// <summary>
    /// Ausgeblendete nicht installierte Spiele (E90, AppSettings.HideUninstalledGames): Sie zählen nicht mit, nur der Tooltip
    /// von „Zurücksetzen“ und der Leerzustand nennen sie. 0 ohne Ausblenden.
    /// </summary>
    public int HiddenCount => _byKey.Count - _listed.Count;

    /// <summary>Leere Liste: „Noch keine Spiele“, sind alle Spiele ausgeblendet „Nur nicht installierte Spiele“.</summary>
    public string EmptyTitle => HiddenCount > 0 ? LibraryStrings.List_EmptyHiddenTitle : LibraryStrings.List_EmptyTitle;

    /// <summary>Erklärung der leeren Liste; mit ausgeblendeten Spielen, wie viele es sind und wo sie sich einblenden lassen.</summary>
    public string EmptyText => HiddenCount switch
    {
        0 => LibraryStrings.List_EmptyText,
        1 => LibraryStrings.List_EmptyHiddenTextOne,
        var hidden => LibraryText.Format(LibraryStrings.List_EmptyHiddenTextMany, hidden),
    };

    /// <summary>
    /// Erklärung zu „Keine Spiele für diese Auswahl“: Würden ausgeblendete nicht installierte Spiele zu Suche, Quelle und
    /// Filtern passen (E90), nennt sie, wie viele und wo sie sich einblenden lassen; „Filter zurücksetzen“ holt sie nicht.
    /// </summary>
    public string NoMatchText => HiddenCount == 0 ? LibraryStrings.List_NoMatchText : _byKey.Values.Count(i => !_listed.Contains(i) && Matches(i)) switch
    {
        0 => LibraryStrings.List_NoMatchText,
        1 => LibraryStrings.List_NoMatchHiddenOne,
        var matching => LibraryText.Format(LibraryStrings.List_NoMatchHiddenMany, matching),
    };

    // ---------- HDR-Status und Schnellaktionen ----------

    public bool IsHdrOn
    {
        get => _isHdrOn;
        private set => SetProperty(ref _isHdrOn, value);
    }

    public bool CanToggleHdr
    {
        get => _canToggleHdr;
        private set
        {
            if (SetProperty(ref _canToggleHdr, value))
            {
                ToggleHdrCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>„HDR an · MAG 341C OLED“ (AutomationId HdrStatus).</summary>
    public string HdrStatusText
    {
        get => _hdrStatusText;
        private set => SetProperty(ref _hdrStatusText, value);
    }

    /// <summary>„HDR einschalten“ / „HDR ausschalten“; folgt wie die Kapsel auch der HDR-Vorschau der Bildschirmfotos.</summary>
    public string HdrToggleText => ShowsHdrOn ? LibraryStrings.Hdr_TurnOff : LibraryStrings.Hdr_TurnOn;

    public string? ActiveSessionText
    {
        get => _activeSessionText;
        private set
        {
            if (SetProperty(ref _activeSessionText, value))
            {
                OnPropertyChanged(nameof(HasActiveSession));
            }
        }
    }

    public bool HasActiveSession => !string.IsNullOrEmpty(_activeSessionText);

    /// <summary>Automatik pausiert (Schalter im Kopf).</summary>
    public bool IsPaused
    {
        get => _context.IsPaused;
        set
        {
            if (value != _context.IsPaused)
            {
                _context.SetPaused(value);
            }

            OnPropertyChanged();
        }
    }

    // ---------- Einlesen ----------

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                RescanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasScanErrors
    {
        get => _hasScanErrors;
        private set => SetProperty(ref _hasScanErrors, value);
    }

    public string ScanErrorsHeader
    {
        get => _scanErrorsHeader;
        private set => SetProperty(ref _scanErrorsHeader, value);
    }

    public bool HasCandidates
    {
        get => _hasCandidates;
        private set
        {
            if (SetProperty(ref _hasCandidates, value))
            {
                OnPropertyChanged(nameof(CandidatesCountText));
            }
        }
    }

    public string CandidatesCountText => Candidates.Count.ToString(CultureInfo.CurrentCulture);

    // ---------- Hinweiszeile ----------

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public StatusKind StatusKind
    {
        get => _statusKind;
        private set => SetProperty(ref _statusKind, value);
    }

    public bool HasStatus => !string.IsNullOrEmpty(_statusMessage);

    /// <summary>Wendet eine ausstehende Suche sofort an (Enter-Taste, Tests).</summary>
    public void ApplySearch()
    {
        _searchDelay?.Dispose();
        _searchDelay = null;
        var tokens = _searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.SequenceEqual(_searchTokens, StringComparer.Ordinal))
        {
            return;
        }

        _searchTokens = tokens;
        OnPropertyChanged(nameof(IsFiltered));
        RefreshView();
    }

    public void ClearFilters()
    {
        _searchDelay?.Dispose();
        _searchDelay = null;
        _searchText = string.Empty;
        _searchTokens = [];
        _selectedSource = _allSources;
        _onlySelected = false;
        _onlyNew = false;
        _onlyHdrCapable = false;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(OnlySelected));
        OnPropertyChanged(nameof(OnlyNew));
        OnPropertyChanged(nameof(OnlyHdrCapable));
        OnPropertyChanged(nameof(IsFiltered));
        RefreshView();
    }

    /// <summary>Wählt ein Spiel aus und holt es in den sichtbaren Bereich (setzt bei Bedarf die Filter zurück).</summary>
    public void SelectGame(string key)
    {
        if (!_byKey.TryGetValue(key, out var item) || !_listed.Contains(item))
        {
            return; // unbekannt oder ausgeblendet (E90)
        }

        if (!GamesView.Contains(item))
        {
            ClearFilters();
        }

        SelectedGame = item;
        RevealRequested?.Invoke(this, item);
    }

    /// <summary>Gleicht die Liste mit der aktuellen Konfiguration ab (nur wenn sie sich geändert hat).</summary>
    public void SyncFromConfig()
    {
        var cfg = _context.Config;
        if (_disposed || ReferenceEquals(cfg, _syncedConfig))
        {
            return;
        }

        var targetsChanged = _syncedConfig is { } previous
            && !previous.Settings.TargetDisplays.SequenceEqual(cfg.Settings.TargetDisplays, StringComparer.OrdinalIgnoreCase);
        _syncedConfig = cfg;
        _hideUninstalled = cfg.Settings.HideUninstalledGames;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entering = new List<GameItemViewModel>();
        var renamed = false;
        var lastPlayedChanged = false;
        foreach (var game in cfg.Games)
        {
            if (!seen.Add(game.Key))
            {
                continue;
            }

            if (_byKey.TryGetValue(game.Key, out var item))
            {
                renamed |= _listed.Contains(item) && !string.Equals(item.Name, game.Name, StringComparison.Ordinal);
                lastPlayedChanged |= item.Config.LastPlayed != game.LastPlayed;
                item.Apply(game);
            }
            else
            {
                item = new GameItemViewModel(this, game) { HdrSupport = _hdrSupport.GetValueOrDefault(game.Key) };
                _byKey[game.Key] = item;
            }

            if (_seenPending.Contains(game.Key))
            {
                item.IsNew = false; // „Angesehen“ ist schon vorgemerkt, nur noch nicht gespeichert.
            }

            if (IsListed(item) && !_listed.Contains(item))
            {
                entering.Add(item); // neu, wieder installiert oder nicht mehr ausgeblendet
            }
        }

        var removed = _byKey.Values.Where(i => !seen.Contains(i.Key)).ToList();
        foreach (var item in removed)
        {
            _byKey.Remove(item.Key);
        }

        // Verlassen die Liste: aus der Bibliothek entfernt oder (nicht installiert) ausgeblendet (E90).
        var leaving = _listed.Where(i => !_byKey.ContainsKey(i.Key) || !IsListed(i)).ToHashSet();

        // Verlässt das gewählte Spiel die Liste, gilt vor dem Neufiltern sein Nachbar (sonst gälte es als vom Filter
        // verdrängt); leise wie beim Filtern, ohne „angesehen“.
        var follows = _selectionFollows;
        GameItemViewModel? gone = null;
        if (_selectedGame is { } selected && (leaving.Contains(selected) || !_byKey.ContainsKey(selected.Key)))
        {
            gone = selected;
            ShowSelection(Neighbour(selected, leaving));
        }

        if (_displacedGame is not null && (!_byKey.ContainsKey(_displacedGame.Key) || leaving.Contains(_displacedGame)))
        {
            _displacedGame = null;
        }

        _listed.ExceptWith(leaving);
        _listed.UnionWith(entering);

        // Nach „Zuletzt gespielt“ sortiert: ein geändertes Datum (Spiel beendet, Steam eingelesen) verschiebt die Zeile.
        var resort = lastPlayedChanged && SortOrder == GameSortOrder.LastPlayed;
        var followed = false;
        if (renamed || entering.Count + leaving.Count > 12)
        {
            _refreshing = true;
            try
            {
                Games.ReplaceAll(_listed.OrderBy(g => g.Name, NameOrder).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase));
            }
            finally
            {
                _refreshing = false;
            }

            followed = RefreshView();
        }
        else if (entering.Count + leaving.Count > 0 || resort)
        {
            _refreshing = true;
            try
            {
                foreach (var item in leaving)
                {
                    Games.Remove(item);
                }

                foreach (var item in entering)
                {
                    Games.Insert(InsertIndex(item), item);
                }

                followed = !resort && FollowFilter(); // etwa ein neues Spiel, das zu Suche und Filtern passt, ohne andere Treffer
            }
            finally
            {
                _refreshing = false;
            }

            if (resort)
            {
                followed = RefreshView(); // Die Sicht ordnet geänderte Zeilen nicht von selbst neu ein.
            }
            else
            {
                UpdateVisibleCount();
                if (followed)
                {
                    RaiseSelectionFollowed();
                }
            }
        }

        if (_selectedGame is not null)
        {
            _details?.Apply(_selectedGame.Config);
            _hero?.Refresh(IsPaused);
        }

        ChangeIconCommand.RaiseCanExecuteChanged();
        ResetIconCommand.RaiseCanExecuteChanged();
        RemoveSettingsCommand.RaiseCanExecuteChanged();

        if (targetsChanged)
        {
            RefreshHdrState(); // Zielbildschirme in den Einstellungen geändert: Status und „Globale Einstellung (…)“ nachziehen
        }
        else
        {
            ApplyRunningFlags();
        }

        UpdateCounts();

        if (gone is not null)
        {
            // Hatte das verschwundene Spiel in der alten Ansicht keinen bleibenden Nachbarn, zeigt die Liste jetzt aber Spiele
            // (seine Quelle fiel als letzte weg und es gilt wieder „Alle“, oder ein Einlesen brachte zugleich ein passendes
            // Spiel): sein Nachbar in der Ansicht, wie sie jetzt ist, statt eines leeren Titelbereichs.
            if (_selectedGame is null && GamesView.Count > 0)
            {
                ShowSelection(NeighbourInView(gone));
            }

            if (_selectionFollows == follows)
            {
                RaiseSelectionFollowed(); // Fokus der verschwundenen Zeile zum Nachbarn
            }
        }

        var signature = CandidateSignature(cfg);
        if (!string.Equals(signature, _candidateSignature, StringComparison.Ordinal))
        {
            _ = RefreshCandidatesAsync();
        }
    }

    /// <summary>Lädt Kandidaten aus den Suchordnern im Hintergrund neu.</summary>
    public async Task RefreshCandidatesAsync()
    {
        var cfg = _context.Config;
        _candidateSignature = CandidateSignature(cfg);
        var run = ++_candidateRun;
        var folders = cfg.Settings.SearchFolders.ToList();
        if (folders.Count == 0)
        {
            ApplyCandidates([]);
            return;
        }

        var installDirs = cfg.Games.Select(g => g.InstallDir).ToList();
        var fileSystem = _context.FileSystem;
        var token = _lifetime.Token;
        IReadOnlyList<GameCandidate> found;
        try
        {
            found = await Task.Run(() => new SearchFolderCandidateFinder(fileSystem).FindCandidates(folders, installDirs, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            found = [];
        }

        if (!_disposed && run == _candidateRun)
        {
            ApplyCandidates(found);
        }
    }

    /// <summary>
    /// Lädt die HDR-Unterstützung laut PCGamingWiki aus dem Zwischenspeicher des Abgleichs (beim Öffnen und nach
    /// jedem Abgleich) und überträgt sie auf die Zeilen. Nach HDR sortiert oder gefiltert: Sicht neu ordnen.
    /// </summary>
    public async Task RefreshHdrSupportAsync()
    {
        var run = ++_hdrSupportRun;
        IReadOnlyDictionary<string, HdrSupportStatus> statuses;
        try
        {
            statuses = await _context.LoadHdrSupportAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return; // Kennzeichen sind nur Zusatz: ohne Zwischenspeicher bleibt die Liste wie sie ist.
        }

        if (_disposed || run != _hdrSupportRun)
        {
            return;
        }

        _hdrSupport = statuses;
        var changed = false;
        foreach (var item in _byKey.Values)
        {
            var status = statuses.GetValueOrDefault(item.Key);
            if (item.HdrSupport != status)
            {
                item.HdrSupport = status;
                changed = true;
            }
        }

        if (changed)
        {
            HdrCapableCount = Games.Count(i => HdrSupportValues.IsHdrCapable(i.HdrSupport)); // ausgeblendete zählen nicht (E90)
            _hero?.Refresh(IsPaused);
        }

        if (changed && (_onlyHdrCapable || SortOrder == GameSortOrder.HdrSupport))
        {
            RefreshView();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        FlushSeen(); // Vorgemerktes „angesehen“ nicht verlieren.
        FlushLayout(); // ebenso eine gerade verschobene Spaltenbreite bzw. Höhe des Titelbereichs
        _disposed = true;
        _lifetime.Cancel();
        _searchDelay?.Dispose();
        _statusDelay?.Dispose();
        _context.ConfigChanged -= OnConfigChanged;
        _context.HdrStateChanged -= OnHdrStateChanged;
        _context.ScanStateChanged -= OnScanStateChanged;
        _context.HdrSupportChanged -= OnHdrSupportChanged;
        _details?.Detach();
        DisposeShowcase();
        RevealRequested = null;
        SelectionFollowedFilter = null;
        _context.Dispose();
    }

    // ---------- Aufrufe aus Zeilen und Details ----------

    internal ImageSource? RequestIcon(GameItemViewModel item)
    {
        var cached = _context.Icons.GetCachedIcon(item.Config);
        if (cached is not null)
        {
            return cached;
        }

        _ = LoadIconAsync(item);
        return null;
    }

    internal void OnHdrAutoChangedByUser(GameItemViewModel item)
    {
        var value = item.HdrAuto;
        item.IsNew = false;
        UpdateCounts();
        _ = WriteGameAsync(item.Key, g =>
        {
            g.HdrAuto = value;
            g.IsNew = false;
        });
    }

    /// <summary>Ändert ein Spiel in der Konfiguration. False bei Fehler (Hinweis wird gezeigt, Liste zurückgesetzt).</summary>
    internal Task<bool> WriteGameAsync(string key, Action<GameConfig> mutate) => WriteAsync(c =>
    {
        if (c.FindGame(key) is { } g)
        {
            mutate(g);
        }
    });

    internal async Task AddManualGameAsync(GameEntry entry)
    {
        var now = _context.Clock.UtcNow;
        var ok = await WriteAsync(c => c.Games.Add(new GameConfig
        {
            Source = entry.Source,
            SourceId = entry.SourceId,
            Name = entry.Name,
            InstallDir = entry.InstallDir,
            KnownExecutables = [.. entry.Executables],
            LaunchCommand = entry.LaunchCommand,
            LaunchArguments = entry.LaunchArguments,
            LaunchWorkingDirectory = entry.LaunchWorkingDirectory,
            // Bewusst hinzugefügt: HDR automatisch an, nicht als „neu“ markiert.
            HdrAuto = true,
            IsNew = false,
            IsInstalled = true,
            RestoreAfter = true,
            FirstSeen = now,
            LastSeen = now,
        }));
        if (ok)
        {
            SelectGame(entry.Key);
            ShowStatus(LibraryText.Format(LibraryStrings.Game_Added, entry.Name), StatusKind.Success);
        }
    }

    internal async Task RemoveGameAsync(GameItemViewModel item)
    {
        var key = item.Key;
        var customIcon = item.Config.CustomIcon;
        var ok = await WriteAsync(c => c.Games.RemoveAll(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase)));
        if (ok)
        {
            DeleteCustomIconQuietly(key, customIcon); // keine verwaiste PNG im Datenordner
            ShowStatus(LibraryText.Format(LibraryStrings.Game_Removed, item.Name), StatusKind.Info);
        }
    }

    /// <summary>
    /// „Einstellungen entfernen…“ (E90; Kontextmenü, Taste Entf, Knopf ganz unten im Detailbereich): nur für nicht installierte
    /// Launcher-Spiele ohne laufende oder vorbereitete Sitzung. Vor der Rückfrage sucht HDRpilot im Hintergrund die
    /// HDRpilot-Verknüpfungen und prüft, ob das Spiel vielleicht noch installiert ist (Laufwerk, Ordner); dauert das länger als
    /// <see cref="CheckingDelay"/>, sagt die Hinweiszeile es. Die Rückfrage nennt, was verloren geht, warnt bei fehlendem
    /// Laufwerk oder noch vorhandenem Ordner und bietet die Verknüpfungen per Häkchen an. Entfernt wird erst nach „Entfernen“
    /// und nur, wenn das Spiel beim Schreiben noch entfernbar ist (<see cref="LibraryCleanup.RemoveNotInstalled"/>); danach
    /// räumt <see cref="UninstalledGameCleanup"/> Symbol, eigene Bilder und gewählte Verknüpfungen auf, und die Liste wählt
    /// den Nachbarn.
    /// </summary>
    internal async Task RemoveSettingsAsync(GameItemViewModel item)
    {
        if (_disposed || _removingSettings)
        {
            return; // eine Rückfrage zur Zeit (sie pumpt Nachrichten)
        }

        var key = item.Key;
        if (_context.Config.FindGame(key) is not { } game)
        {
            return;
        }

        if (!LibraryCleanup.IsRemovable(game))
        {
            ShowStatus(LibraryText.Format(LibraryStrings.RemoveSettings_Kept, item.Name), StatusKind.Warning);
            return;
        }

        if (HasSession(key))
        {
            ShowStatus(LibraryText.Format(LibraryStrings.RemoveSettings_Running, item.Name), StatusKind.Warning);
            return;
        }

        _removingSettings = true;
        try
        {
            var cleanup = _context.Cleanup;
            var shortcutSearch = Task.Run(() => cleanup.FindShortcuts(key));
            var installCheck = cleanup.CheckInstallAsync(game.InstallDir);
            var checking = LibraryText.Format(LibraryStrings.RemoveSettings_Checking, item.Name);
            var prepared = Task.WhenAll(shortcutSearch, installCheck);
            if (await Task.WhenAny(prepared, Task.Delay(CheckingDelay)) != prepared)
            {
                ShowStatus(checking, StatusKind.Info); // etwa ein getrenntes Netzlaufwerk: bis zu 2 s
            }

            var trace = await installCheck;
            var shortcuts = await shortcutSearch;
            if (StatusMessage == checking)
            {
                StatusMessage = null;
            }

            if (_disposed)
            {
                return;
            }

            var answer = _dialogs.Confirm(RemoveSettingsText.ForGame(game, trace, shortcuts.Count));
            if (!answer.Confirmed || _disposed)
            {
                return;
            }

            // Geprüft wird beim Schreiben: Hat ein Einlesen das Spiel inzwischen wiedergefunden oder läuft es, bleibt alles.
            IReadOnlyList<GameConfig> removed = [];
            if (!await WriteAsync(c => removed = LibraryCleanup.RemoveNotInstalled(c, [key], HasSession)))
            {
                return; // Fehler steht schon in der Hinweiszeile
            }

            if (removed.Count == 0)
            {
                ShowStatus(LibraryText.Format(LibraryStrings.RemoveSettings_Kept, item.Name), StatusKind.Warning);
                return;
            }

            var gone = removed[0];
            IReadOnlyCollection<GameShortcut> chosen = answer.OptionChecked ? shortcuts : [];
            var remaining = _context.Config;
            var result = await Task.Run(() => cleanup.DeleteData(gone, chosen, remaining));
            var done = result.DeletedShortcuts switch
            {
                0 => LibraryText.Format(LibraryStrings.RemoveSettings_Done, gone.Name),
                1 => LibraryText.Format(LibraryStrings.RemoveSettings_DoneShortcutOne, gone.Name),
                var count => LibraryText.Format(LibraryStrings.RemoveSettings_DoneShortcutsMany, gone.Name, count),
            };
            ShowStatus(
                result.Failed.Count == 0 ? done : done + " " + LibraryText.FilesFailed(result.Failed.Count),
                result.Failed.Count == 0 ? StatusKind.Success : StatusKind.Warning);
        }
        finally
        {
            _removingSettings = false;
        }
    }

    /// <summary>Läuft für das Spiel eine Sitzung oder ist eine vorbereitet (Start über HDRpilot, Steam-Startoption, Frühwarnung)?</summary>
    private bool HasSession(string key) => _context.Sessions.Any(s => string.Equals(s.GameKey, key, StringComparison.OrdinalIgnoreCase));

    internal void ShowStatus(string message, StatusKind kind)
    {
        if (_disposed)
        {
            return; // Fenster zu: kein Zeitgeber mehr anlegen.
        }

        StatusKind = kind;
        StatusMessage = message;
        _statusDelay?.Dispose();
        _statusDelay = _context.Ui.Delay(StatusDuration, () => StatusMessage = null);
    }

    // ---------- intern ----------

    private static string CandidateSignature(AppConfig cfg) =>
        string.Join("|", cfg.Settings.SearchFolders) + "#" + cfg.Games.Count.ToString(CultureInfo.InvariantCulture);

    private bool Matches(object obj)
    {
        if (obj is not GameItemViewModel g)
        {
            return false;
        }

        if ((_selectedSource.Source is { } source && g.Source != source)
            || (_onlySelected && !g.HdrAuto)
            || (_onlyNew && !g.IsNew)
            || (_onlyHdrCapable && !HdrSupportValues.IsHdrCapable(g.HdrSupport)))
        {
            return false;
        }

        foreach (var token in _searchTokens)
        {
            if (SearchCompare.IndexOf(g.SearchText, token, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Sicht neu filtern und ordnen; die Auswahl folgt (<see cref="FollowFilter"/>). True, wenn sie gefolgt ist.</summary>
    private bool RefreshView()
    {
        bool followed;
        _refreshing = true;
        try
        {
            GamesView.Refresh();
            followed = FollowFilter();
            OnPropertyChanged(nameof(SelectedGame)); // die Liste wieder auf die Auswahl setzen (Neufiltern hat ihre geändert)
        }
        finally
        {
            _refreshing = false;
        }

        UpdateVisibleCount();
        if (followed)
        {
            RaiseSelectionFollowed();
        }

        return followed;
    }

    /// <summary>Meldet dem Fenster, dass die Auswahl der Liste gefolgt ist (<see cref="SelectionFollowedFilter"/>), und zählt mit.</summary>
    private void RaiseSelectionFollowed()
    {
        _selectionFollows++;
        SelectionFollowedFilter?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gehört das Spiel in die Grundmenge <see cref="Games"/>? Nicht, wenn es nicht installiert und ausgeblendet ist (E90).</summary>
    private bool IsListed(GameItemViewModel item) => !_hideUninstalled || item.IsInstalled;

    /// <summary>
    /// Nachbar eines Spiels, das die Liste verlässt (entfernt oder ausgeblendet, E90): das nächste bleibende in der
    /// Reihenfolge der Ansicht, am Ende das vorige; ohne bleibendes (oder nicht in der Ansicht) null.
    /// </summary>
    private GameItemViewModel? Neighbour(GameItemViewModel item, HashSet<GameItemViewModel> leaving)
    {
        var index = GamesView.IndexOf(item);
        if (index < 0)
        {
            return null;
        }

        for (var i = index + 1; i < GamesView.Count; i++)
        {
            if (GamesView.GetItemAt(i) is GameItemViewModel next && !leaving.Contains(next))
            {
                return next;
            }
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (GamesView.GetItemAt(i) is GameItemViewModel previous && !leaving.Contains(previous))
            {
                return previous;
            }
        }

        return null;
    }

    /// <summary>
    /// Nachbar eines Spiels, das nicht mehr in der Ansicht steht, nach seinem Platz in deren Reihenfolge: das erste Spiel, das
    /// danach käme, sonst das letzte; null bei leerer Ansicht.
    /// </summary>
    private GameItemViewModel? NeighbourInView(GameItemViewModel gone)
    {
        var order = GameSorting.Comparer(SortOrder);
        GameItemViewModel? last = null;
        foreach (var item in GamesView.OfType<GameItemViewModel>())
        {
            if (order.Compare(item, gone) > 0)
            {
                return item;
            }

            last = item;
        }

        return last;
    }

    /// <summary>
    /// Auswahl nach dem Neufiltern (Entscheidung des Nutzers 2026-10-19): Steht das vom Filter verdrängte Spiel wieder in der
    /// Liste, kehrt es zurück. Sonst bleibt ein noch sichtbares gewähltes Spiel; fällt es heraus, gilt der erste Treffer in
    /// der Reihenfolge der Liste, ohne Treffer keiner, und das Spiel wird als verdrängt gemerkt (ein schon verdrängtes bleibt
    /// es). Leise: nichts gilt als angesehen, nichts wird gespeichert. Ohne Auswahl und ohne verdrängtes Spiel bleibt es so.
    /// </summary>
    /// <returns>true, wenn sich die Auswahl geändert hat.</returns>
    private bool FollowFilter()
    {
        var current = _selectedGame;
        GameItemViewModel? next;
        if (_displacedGame is { } displaced && GamesView.Contains(displaced))
        {
            next = displaced;
            _displacedGame = null;
        }
        else if (current is not null ? GamesView.Contains(current) : _displacedGame is null)
        {
            return false; // noch zu sehen, oder nichts gewählt und nichts verdrängt: bleibt
        }
        else
        {
            _displacedGame ??= current;
            next = GamesView.Count > 0 ? GamesView.GetItemAt(0) as GameItemViewModel : null;
        }

        if (ReferenceEquals(next, current))
        {
            return false;
        }

        ShowSelection(next);
        return true;
    }

    /// <summary>Spiel zeigen (Liste, Titelbereich, Details, Befehle), ohne es als angesehen zu markieren; null: keins.</summary>
    private void ShowSelection(GameItemViewModel? item)
    {
        _selectedGame = item;
        OnPropertyChanged(nameof(SelectedGame));
        ChangeIconCommand.RaiseCanExecuteChanged();
        ResetIconCommand.RaiseCanExecuteChanged();
        RemoveSettingsCommand.RaiseCanExecuteChanged();
        Details = item is null ? null : new GameDetailsViewModel(this, item);
        OnSelectionChanged(item);
    }

    /// <summary>
    /// Sortierung der Sicht setzen (baut sie neu auf). Nach Name ist schon <see cref="Games"/> geordnet, dann sortiert
    /// die Sicht nicht selbst (schneller beim Filtern); andere Reihenfolgen sortiert sie mit einem eigenen Vergleich.
    /// </summary>
    private void ApplySortToView()
    {
        _refreshing = true;
        try
        {
            GamesView.CustomSort = _selectedSort.Order == GameSortOrder.Name ? null : GameSorting.Comparer(_selectedSort.Order);
            OnPropertyChanged(nameof(SelectedGame));
        }
        finally
        {
            _refreshing = false;
        }

        UpdateVisibleCount();
    }

    private void FlushLayout()
    {
        _layoutDelay?.Dispose();
        _layoutDelay = null;
        if (!_layoutPending && !_heroPending)
        {
            return;
        }

        // Nur, was sich geändert hat: Die andere Angabe bleibt, wie sie in der Konfiguration steht.
        var pane = _layoutPending;
        var hero = _heroPending;
        _layoutPending = false;
        _heroPending = false;
        var ratio = _detailsRatio;
        var heroRatio = _heroRatio;
        _ = WriteAsync(c =>
        {
            if (pane)
            {
                c.Settings.LibraryPaneRatio = ratio;
            }

            if (hero)
            {
                c.Settings.LibraryHeroRatio = heroRatio;
            }
        });
    }

    private void UpdateVisibleCount()
    {
        VisibleCount = GamesView.Count;
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(IsFilterEmpty));
        OnPropertyChanged(nameof(NoMatchText));
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(ViewSummaryText));
        OnPropertyChanged(nameof(ListHeaderText));
        RaiseToolbarTexts();
    }

    private int InsertIndex(GameItemViewModel item)
    {
        int lo = 0, hi = Games.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (NameOrder.Compare(Games[mid].Name, item.Name) <= 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void UpdateCounts()
    {
        int auto = 0, fresh = 0;
        var perSource = new Dictionary<GameSource, int>();
        foreach (var g in Games)
        {
            auto += g.HdrAuto ? 1 : 0;
            fresh += g.IsNew ? 1 : 0;
            perSource[g.Source] = perSource.GetValueOrDefault(g.Source) + 1;
        }

        TotalCount = Games.Count;
        HdrAutoCount = auto;
        NewCount = fresh;
        HdrCapableCount = Games.Count(g => HdrSupportValues.IsHdrCapable(g.HdrSupport));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(HdrAutoText));
        OnPropertyChanged(nameof(NewText));
        OnPropertyChanged(nameof(ViewSummaryText));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(IsFilterEmpty));
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(NoMatchText));

        _allSources.Count = Games.Count;
        var index = 1;
        foreach (var source in Enum.GetValues<GameSource>())
        {
            var count = perSource.GetValueOrDefault(source);
            var existing = SourceOptions.Skip(1).FirstOrDefault(o => o.Source == source);
            if (count == 0)
            {
                if (existing is not null)
                {
                    SourceOptions.Remove(existing);
                    if (ReferenceEquals(existing, _selectedSource))
                    {
                        SelectedSource = _allSources;
                    }
                }

                continue;
            }

            if (existing is null)
            {
                SourceOptions.Insert(index, new SourceFilterOption(source, count));
            }
            else
            {
                existing.Count = count;
            }

            index++;
        }

        RaiseToolbarTexts();
    }

    private void ApplyRunningFlags()
    {
        var running = new HashSet<string>(_context.Sessions.Select(s => s.GameKey), StringComparer.OrdinalIgnoreCase);
        foreach (var item in _byKey.Values)
        {
            item.IsRunning = running.Contains(item.Key);
        }

        ActiveSessionText = running.Count == 0
            ? null
            : LibraryText.Format(LibraryStrings.Hdr_ActiveSessions, string.Join(", ", _context.Sessions.Select(s => s.GameName)));
        IsGameRunning = running.Count > 0;
        RemoveSettingsCommand.RaiseCanExecuteChanged(); // während einer Sitzung gesperrt (E90)
        UpdateAutomationState();
    }

    private void RefreshHdrState()
    {
        Displays = _context.GetDisplays();
        Targets = _context.TargetDisplays();
        var wasOn = _isHdrOn;
        IsHdrOn = Targets.Any(d => d.HdrEnabled);
        CanToggleHdr = Targets.Any(d => d.HdrSupported);
        var names = string.Join(", ", Targets.Select(d => d.FriendlyName));
        HdrStatusText = Targets.Count == 0
            ? LibraryStrings.Hdr_StatusNoDisplay
            : IsHdrOn
                ? LibraryText.Format(LibraryStrings.Hdr_StatusOn, names)
                : CanToggleHdr
                    ? LibraryText.Format(LibraryStrings.Hdr_StatusOff, names)
                    : LibraryText.Format(LibraryStrings.Hdr_StatusUnavailable, names);
        OnPropertyChanged(nameof(HdrToggleText));
        OnPropertyChanged(nameof(IsPaused));
        ApplyRunningFlags();
        _details?.RefreshDisplays();
        _hero?.Refresh(IsPaused);
        RefreshNits();
        OnHdrStateRefreshed(wasOn);
    }

    private void RefreshScanErrors()
    {
        var report = _context.LastScan;
        var items = report is null
            ? []
            : report.Scanners
                .SelectMany(s => s.Result.Errors.Select(e => new ScanErrorItem(LibraryText.SourceName(s.Result.Source), e.Message, e.Path)))
                .ToList();
        ScanErrors.ReplaceAll(items);
        ScanErrorsHeader = items.Count == 1
            ? LibraryStrings.ScanErrors_HeaderOne
            : LibraryText.Format(LibraryStrings.ScanErrors_HeaderMany, items.Count);
        HasScanErrors = items.Count > 0;
    }

    private void ApplyCandidates(IReadOnlyList<GameCandidate> found)
    {
        Candidates.ReplaceAll(found.Select(c => new CandidateItemViewModel(c)));
        OnPropertyChanged(nameof(CandidatesCountText));
        HasCandidates = Candidates.Count > 0;
    }

    /// <summary>
    /// Ansehen setzt „neu“ sofort in der Liste zurück; gespeichert wird gesammelt nach <see cref="SeenDelay"/>
    /// (oder vor dem nächsten Schreibvorgang bzw. beim Schließen). Bis dahin zählt es als ausstehender
    /// Schreibvorgang, damit ein Abgleich die Markierung nicht kurz zurückholt.
    /// </summary>
    private void MarkSeen(GameItemViewModel item)
    {
        item.IsNew = false;
        UpdateCounts();
        if (_seenPending.Add(item.Key) && _seenPending.Count == 1)
        {
            _pendingWrites++;
        }

        _seenDelay?.Dispose();
        _seenDelay = _context.Ui.Delay(SeenDelay, FlushSeen);
    }

    private void FlushSeen()
    {
        _seenDelay?.Dispose();
        _seenDelay = null;
        if (_seenPending.Count == 0)
        {
            return;
        }

        var keys = _seenPending.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _seenPending.Clear();
        _pendingWrites--;
        _ = WriteAsync(c =>
        {
            foreach (var g in c.Games)
            {
                if (keys.Contains(g.Key))
                {
                    g.IsNew = false;
                }
            }
        });
    }

    private async Task<bool> WriteAsync(Action<AppConfig> mutate)
    {
        if (_seenPending.Count > 0)
        {
            FlushSeen(); // in Reihenfolge vor dieser Änderung speichern
        }

        _pendingWrites++;
        var ok = true;
        try
        {
            await _context.UpdateConfigAsync(mutate);
        }
        catch (Exception ex)
        {
            ok = false;
            _syncedConfig = null; // Liste auf den gespeicherten Stand zurücksetzen.
            ShowStatus(LibraryText.Format(LibraryStrings.Error_SaveFailed, ex.Message), StatusKind.Error);
        }
        finally
        {
            _pendingWrites--;
        }

        if (_pendingWrites == 0)
        {
            SyncFromConfig();
        }

        return ok;
    }

    private async Task LoadIconAsync(GameItemViewModel item)
    {
        // Nummer der Anfrage: Fragt die Zeile inzwischen neu an (eigenes Symbol, neue Hinweise), gilt dieses Ergebnis nicht mehr.
        var request = item.IconRequest;
        try
        {
            var icon = await _context.Icons.GetIconAsync(item.Config, _lifetime.Token);
            if (!_disposed)
            {
                item.SetIcon(icon, request);
            }
        }
        catch (OperationCanceledException)
        {
            // Fenster geschlossen.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            // Symbol ist nur Zierde: Ersatzsymbol bleibt.
        }
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        if (_disposed || _pendingWrites > 0 || _syncQueued)
        {
            return; // Nach dem letzten eigenen Schreibvorgang wird ohnehin abgeglichen.
        }

        _syncQueued = true;
        _context.Ui.Post(() =>
        {
            _syncQueued = false;
            if (_pendingWrites == 0)
            {
                SyncFromConfig();
            }
        });
    }

    private void OnHdrStateChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            RefreshHdrState();
        }
    }

    private void OnHdrSupportChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _ = RefreshHdrSupportAsync();
        }
    }

    private void OnScanStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        IsScanning = _context.IsScanning;
        if (!IsScanning)
        {
            RefreshScanErrors();
            _ = RefreshCandidatesAsync();
        }
    }

    private async Task ToggleHdrAsync()
    {
        try
        {
            await _context.ToggleHdrNowAsync();
        }
        catch (Exception ex)
        {
            ShowStatus(LibraryText.Format(LibraryStrings.Error_Generic, ex.Message), StatusKind.Error);
        }

        RefreshHdrState();
    }

    private async Task RescanAsync()
    {
        IsScanning = true;
        try
        {
            await _context.RescanAsync();
            SyncFromConfig();
            ShowStatus(LibraryText.Format(LibraryStrings.Rescan_Done, LibraryText.Games(TotalCount)), StatusKind.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(LibraryText.Format(LibraryStrings.Error_Generic, ex.Message), StatusKind.Error);
        }
        finally
        {
            IsScanning = _context.IsScanning;
            RefreshScanErrors();
        }
    }

    private async Task AddGameAsync()
    {
        var dialog = new AddGameViewModel(_context.FileSystem, _context.Environment, _dialogs);
        if (_dialogs.ShowAddGame(dialog) && dialog.Result is { } entry)
        {
            await AddManualGameAsync(entry);
        }
    }

    /// <summary>
    /// „Symbol ändern…“ (E86): Bild oder EXE wählen, als PNG mit 96 px im Datenordner speichern (außerhalb des
    /// UI-Threads), in der Konfiguration eintragen und sofort zeigen. Eine nicht lesbare Datei ändert nichts.
    /// </summary>
    private async Task ChangeIconAsync()
    {
        if (_selectedGame is not { } item)
        {
            return;
        }

        var file = _dialogs.PickIconFile(SafeDirectory(item.InstallDir));
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        var icons = _context.Icons;
        var config = item.Config;
        var previous = config.CustomIcon;
        string name;
        try
        {
            name = await Task.Run(() => icons.SaveCustomIcon(config, file));
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or FormatException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            ShowStatus(LibraryStrings.Icon_NotAnImage, StatusKind.Error);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowStatus(LibraryText.Format(LibraryStrings.Icon_SaveFailed, ex.Message), StatusKind.Error);
            return;
        }

        // Vor dem Eintragen vergessen: Beim erneuten Ändern bleibt der Dateiname gleich, nur das Bild ist neu.
        icons.Forget(item.Key);
        var written = await WriteGameAsync(item.Key, g => g.CustomIcon = name);
        if (written && string.Equals(_context.Config.FindGame(item.Key)?.CustomIcon, name, StringComparison.Ordinal))
        {
            RefreshIcon(item);
            ShowStatus(LibraryText.Format(LibraryStrings.Icon_Changed, item.Name), StatusKind.Success);
        }
        else if (!string.Equals(previous, name, StringComparison.OrdinalIgnoreCase))
        {
            DeleteCustomIconQuietly(item.Key, name); // nicht eingetragen (Schreibfehler, Spiel entfernt): keine verwaiste Datei
        }

        // Auch die Bilder: Symbol im Titelbereich und in „Bilder“, daraus erzeugte Titelbilder, Cover und Vorschaubilder.
        _context.Artwork.Forget(item.Key);
    }

    /// <summary>
    /// „Symbol zurücksetzen“: Eintrag entfernen, dann die PNG löschen, aber nur die eigene des Spiels; das automatische
    /// Symbol gilt sofort.
    /// </summary>
    private async Task ResetIconAsync()
    {
        if (_selectedGame is not { HasCustomIcon: true } item)
        {
            return;
        }

        var name = item.Config.CustomIcon;
        if (!await WriteGameAsync(item.Key, g => g.CustomIcon = null))
        {
            return;
        }

        DeleteCustomIconQuietly(item.Key, name);
        RefreshIcon(item);
        _context.Artwork.Forget(item.Key);
        ShowStatus(LibraryText.Format(LibraryStrings.Icon_ResetDone, item.Name), StatusKind.Success);
    }

    /// <summary>Symbol eines Spiels sofort neu laden (aktuellen Stand übernehmen, falls der Abgleich noch aussteht).</summary>
    private void RefreshIcon(GameItemViewModel item)
    {
        if (_context.Config.FindGame(item.Key) is { } current)
        {
            item.Apply(current);
        }

        item.ReloadIcon();
        ResetIconCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Löscht die PNG eines Spiels im Symbolordner, nur wenn der Name zu diesem Spiel gehört (<see cref="CustomIcons.BelongsTo"/>):
    /// Nennt eine von Hand geänderte config.json die Datei eines anderen Spiels, fällt nur der Eintrag weg. Scheitert das
    /// Löschen, wirkt die Datei ohne Eintrag nicht mehr und wird beim nächsten Ändern ersetzt.
    /// </summary>
    private void DeleteCustomIconQuietly(string gameKey, string? name)
    {
        if (!CustomIcons.BelongsTo(name, gameKey))
        {
            return;
        }

        try
        {
            _context.Icons.DeleteCustomIcon(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Siehe oben: ohne Eintrag ohne Wirkung.
        }
    }

    internal string? SafeDirectory(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && _context.FileSystem.DirectoryExists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private async Task MarkAllSeenAsync()
    {
        var fresh = Games.Where(g => g.IsNew).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        foreach (var item in fresh)
        {
            item.IsNew = false;
        }

        UpdateCounts();
        RefreshView();
        if (await WriteAsync(c => c.Games.ForEach(g => g.IsNew = false)))
        {
            ShowStatus(LibraryText.Format(LibraryStrings.MarkAllSeen_Done, LibraryText.Games(fresh.Count)), StatusKind.Success);
        }
    }

    private async Task AddCandidateAsync(CandidateItemViewModel? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        if (!AddGameViewModel.ConfirmFolder(candidate.Folder, _context.Environment, _dialogs, out var refusal))
        {
            if (refusal is not null)
            {
                ShowStatus(refusal, StatusKind.Error);
            }

            return;
        }

        GameEntry entry;
        try
        {
            if (!_context.FileSystem.DirectoryExists(candidate.Folder))
            {
                ShowStatus(LibraryStrings.AddGame_ErrorNotFound, StatusKind.Error); // inzwischen gelöscht oder verschoben
                return;
            }

            entry = ManualGames.Create(_context.FileSystem, candidate.Folder, candidate.Name);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(LibraryText.Format(LibraryStrings.Error_Generic, ex.Message), StatusKind.Error);
            return;
        }

        // Der Suchordner kennt auch tiefer liegende EXEs (etwa Binaries\Win64): als bekannte EXEs übernehmen.
        var exes = candidate.Candidate.Executables;
        entry = entry with
        {
            Executables = entry.Executables.Concat(exes).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            LaunchCommand = entry.LaunchCommand ?? (exes.Count == 1 ? exes[0] : null),
            LaunchWorkingDirectory = entry.LaunchWorkingDirectory ?? (exes.Count == 1 ? Path.GetDirectoryName(exes[0]) : null),
        };

        Candidates.Remove(candidate);
        OnPropertyChanged(nameof(CandidatesCountText));
        HasCandidates = Candidates.Count > 0;
        await AddManualGameAsync(entry);
    }
}
