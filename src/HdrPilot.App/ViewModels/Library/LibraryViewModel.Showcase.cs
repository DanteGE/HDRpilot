using System.IO;
using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Services.Display;
using HdrPilot.App.Theming;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Zustand der Automatik für die Statuszeile (Farbe des Punkts).</summary>
public enum AutomationState
{
    /// <summary>Aktiv, wartet auf einen Spielstart.</summary>
    Waiting,

    /// <summary>Aktiv, HDR ist an.</summary>
    HdrOn,

    /// <summary>Pausiert: ein erkannter Spielstart schaltet nichts.</summary>
    Paused,

    /// <summary>Ein Spiel läuft.</summary>
    Running,
}

/// <summary>
/// Neugestaltung „Showcase Hero“ (E89): Ansichten Liste/Raster/Regal (gemerkt in AppSettings.LibraryView), Vorschaubild
/// der Liste (AppSettings.LibraryThumbnail), Sortieren über die Spaltenköpfe, Titelbereich und Bilder des gewählten
/// Spiels, Bildschirm-Kapsel mit echten Nit-Werten, Statuszeile, Regal-Schublade, „Bild wählen…“ und die Befehle des
/// Kontextmenüs. Bilder lädt der Bilddienst im Hintergrund; diese Klasse hält nur, was gerade zu sehen ist.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>Bei schnellem Wechsel (gehaltene Pfeiltaste) wird das Titelbild erst so lange nach dem letzten Wechsel geladen.</summary>
    public static readonly TimeSpan HeroDelay = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// So lange nach dem letzten Wechsel der Auswahl (Titelbild geladen) wird die helle Fassung vorab geladen, mit niedriger
    /// Priorität und nur bei aktivem Fenster ohne laufendes Spiel: So ist sie da, wenn HDR angeht.
    /// </summary>
    public static readonly TimeSpan BoostPrefetchDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>So lange wartet der HDR-Moment höchstens auf eine noch ladende helle Fassung; kommt sie später, blendet sie über.</summary>
    public static readonly TimeSpan MomentBoostWait = TimeSpan.FromMilliseconds(300);

    private readonly LinkedList<GameItemViewModel> _coverOrder = new();
    private readonly Dictionary<GameItemViewModel, LinkedListNode<GameItemViewModel>> _coverNodes = [];
    private LibraryViewMode _viewMode;
    private LibraryThumbnail _thumbnail;
    private bool _drawerOpen;
    private bool _isGameRunning;
    private int _hdrCapableCount;
    private HeroViewModel? _hero;
    private IDisposable? _heroDelay;
    private long _lastSelectionTick;
    private int _artworkRun;
    private int _heroDecodeWidth = 1920;
    private EffectLevel _effectLevel;
    private MotionPause _motionPause;
    private string _effectsText = string.Empty;
    private AutomationState _automationState;
    private string _automationStateText = string.Empty;
    private bool _showcaseReady;
    private bool _windowActive;
    private int _launching;
    private IDisposable? _boostPrefetch;
    private CancellationTokenSource? _boostCancel;
    private Task _boostLoad = Task.CompletedTask;

    /// <summary>HDR wurde bei offenem Fenster eingeschaltet: das Fenster spielt den HDR-Moment.</summary>
    public event EventHandler? HdrMomentRequested;

    /// <summary>Nach einem Start über die Bibliothek soll das Fenster schließen („Nach dem Start in den Infobereich“).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Jetzt (für „vor 2 Tagen“).</summary>
    internal DateTimeOffset Now => _context.Clock.UtcNow;

    // ---------- Ansicht ----------

    /// <summary>Gewählte Ansicht; Setzen schaltet um und speichert sie (AppSettings.LibraryView).</summary>
    public LibraryViewMode ViewMode
    {
        get => _viewMode;
        set => SetViewMode(value, persist: true);
    }

    public bool IsListView
    {
        get => _viewMode == LibraryViewMode.List;
        set
        {
            if (value)
            {
                ViewMode = LibraryViewMode.List;
            }
        }
    }

    public bool IsGridView
    {
        get => _viewMode == LibraryViewMode.Grid;
        set
        {
            if (value)
            {
                ViewMode = LibraryViewMode.Grid;
            }
        }
    }

    public bool IsShelfView
    {
        get => _viewMode == LibraryViewMode.Shelf;
        set
        {
            if (value)
            {
                ViewMode = LibraryViewMode.Shelf;
            }
        }
    }

    /// <summary>Strg+1/2/3 und Menüs: Ansicht nach Namen ("List", "Grid", "Shelf") oder Wert.</summary>
    public RelayCommand ShowViewCommand { get; private set; } = null!;

    /// <summary>
    /// Ansicht wechseln. <paramref name="persist"/> false nur für Bildschirmfotos und Prüfungen ohne Schreiben. Beim
    /// Wechsel schließt die Schublade des Regals.
    /// </summary>
    public void SetViewMode(LibraryViewMode mode, bool persist)
    {
        if (!Enum.IsDefined(mode))
        {
            mode = LibraryViewMode.List;
        }

        if (mode == _viewMode)
        {
            return;
        }

        _viewMode = mode;
        IsDetailsDrawerOpen = false;
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsShelfView));
        OnPropertyChanged(nameof(ViewSummaryText));
        OnPropertyChanged(nameof(ViewName));
        if (persist && !_disposed)
        {
            var stored = LibraryViewModes.ToConfig(mode);
            _ = WriteAsync(c => c.Settings.LibraryView = stored);
        }
    }

    /// <summary>„Liste“, „Raster“, „Regal“.</summary>
    public string ViewName => _viewMode switch
    {
        LibraryViewMode.Grid => LibraryStrings.View_Grid,
        LibraryViewMode.Shelf => LibraryStrings.View_Shelf,
        _ => LibraryStrings.View_List,
    };

    // ---------- Vorschaubild der Liste ----------

    public LibraryThumbnail Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (value == _thumbnail || !Enum.IsDefined(value))
            {
                return;
            }

            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsThumbnailImage));
            OnPropertyChanged(nameof(IsThumbnailIcon));
            if (!_disposed)
            {
                var stored = LibraryViewModes.ToConfig(value);
                _ = WriteAsync(c => c.Settings.LibraryThumbnail = stored);
            }
        }
    }

    public bool IsThumbnailImage
    {
        get => _thumbnail == LibraryThumbnail.Image;
        set
        {
            if (value)
            {
                Thumbnail = LibraryThumbnail.Image;
            }
        }
    }

    public bool IsThumbnailIcon
    {
        get => _thumbnail == LibraryThumbnail.Icon;
        set
        {
            if (value)
            {
                Thumbnail = LibraryThumbnail.Icon;
            }
        }
    }

    // ---------- Sortieren über die Spaltenköpfe ----------

    /// <summary>Spaltenkopf geklickt: nach Name, Zuletzt gespielt oder HDR sortieren (wie die Auswahlliste, gespeichert).</summary>
    public RelayCommand SortByCommand { get; private set; } = null!;

    public bool IsSortedByName => SortOrder == GameSortOrder.Name;

    public bool IsSortedByLastPlayed => SortOrder == GameSortOrder.LastPlayed;

    public bool IsSortedByHdr => SortOrder == GameSortOrder.HdrSupport;

    // ---------- Kennzahlen ----------

    /// <summary>Spiele mit HDR laut PCGamingWiki (Chip „HDR-fähig“).</summary>
    public int HdrCapableCount
    {
        get => _hdrCapableCount;
        private set => SetProperty(ref _hdrCapableCount, value);
    }

    /// <summary>Kopf der Spalte „Spiel“: „SPIEL · 651“.</summary>
    public string ListHeaderText => LibraryText.Format(LibraryStrings.List_ColumnGame, VisibleCount).ToUpper(System.Globalization.CultureInfo.CurrentCulture);

    // ---------- Statuszeile ----------

    /// <summary>„Liste · 651 von 651 · sortiert nach Zuletzt gespielt“.</summary>
    public string ViewSummaryText => LibraryText.Format(LibraryStrings.Status_ViewSummary, ViewName, VisibleCount, TotalCount, _selectedSort.Label);

    public AutomationState AutomationState
    {
        get => _automationState;
        private set => SetProperty(ref _automationState, value);
    }

    /// <summary>„Automatik aktiv · wartet auf Spielstart“, „Spiel läuft · … · Effekte pausiert“ …</summary>
    public string AutomationStateText
    {
        get => _automationStateText;
        private set => SetProperty(ref _automationStateText, value);
    }

    /// <summary>Rechts in der Statuszeile: Effektstufe und wann sie anhalten.</summary>
    public string EffectsText
    {
        get => _effectsText;
        private set => SetProperty(ref _effectsText, value);
    }

    /// <summary>Effekte stehen gerade (im Hintergrund, minimiert, Spiel läuft): Blitz-Symbol in Warnfarbe.</summary>
    public bool EffectsPaused => _effectLevel == EffectLevel.Full && _motionPause != MotionPause.None;

    public EffectLevel EffectLevel => _effectLevel;

    /// <summary>Ein Spiel läuft (laufende Sitzung): Dauer-Effekte stehen, Statuszeile sagt es.</summary>
    public bool IsGameRunning
    {
        get => _isGameRunning;
        private set => SetProperty(ref _isGameRunning, value);
    }

    // ---------- Titelbereich, Kapsel ----------

    /// <summary>Titelbereich des gewählten Spiels; null ohne Auswahl.</summary>
    public HeroViewModel? Hero
    {
        get => _hero;
        private set => SetProperty(ref _hero, value);
    }

    public bool HasHero => _hero is not null;

    /// <summary>Bildschirm-Kapsel mit den echten Werten (DXGI) des ersten Zielbildschirms.</summary>
    public NitsReadout Nits { get; } = new();

    /// <summary>Breite, in der das Titelbild geladen wird (Fensterbreite in Pixeln, höchstens 3840); setzt das Fenster.</summary>
    public int HeroDecodeWidth
    {
        get => _heroDecodeWidth;
        set => _heroDecodeWidth = Math.Clamp(value, 640, LibraryArt.MaxHeroWidth);
    }

    // ---------- Regal ----------

    /// <summary>Regal: voller Detailbereich als Schublade von rechts (Taste I, „Details“).</summary>
    public bool IsDetailsDrawerOpen
    {
        get => _drawerOpen;
        set => SetProperty(ref _drawerOpen, value && _viewMode == LibraryViewMode.Shelf);
    }

    public RelayCommand ToggleDetailsDrawerCommand { get; private set; } = null!;

    // ---------- Kontextmenü ----------

    /// <summary>„Installationsordner öffnen“ für das gewählte Spiel.</summary>
    public RelayCommand OpenInstallFolderCommand { get; private set; } = null!;

    /// <summary>„Bild wählen…“ (Parameter: Titelbild, Logo oder Cover als <see cref="ArtworkKind"/>; ohne: Titelbild).</summary>
    public AsyncRelayCommand ChooseArtworkCommand { get; private set; } = null!;

    /// <summary>Für die Ansicht: Bildschirmfoto oder Prüfung, Ansicht ohne Speichern (siehe <see cref="SetViewMode"/>).</summary>
    internal bool ShowcaseReady => _showcaseReady;

    /// <summary>
    /// Effektstufe und Grund des Anhaltens vom Fenster (MotionController); daraus der Text rechts in der Statuszeile.
    /// </summary>
    public void SetEffectsState(EffectLevel level, MotionPause pause, bool reducedByWindows)
    {
        _effectLevel = level;
        _motionPause = pause;
        var appearance = _context.Appearance.Current;
        EffectsText = level switch
        {
            EffectLevel.Off => LibraryStrings.Effects_Off,
            EffectLevel.Reduced => reducedByWindows ? LibraryStrings.Effects_ReducedByWindows : LibraryStrings.Effects_Reduced,
            _ => pause switch
            {
                MotionPause.GameRunning => LibraryStrings.Effects_PausedGame,
                MotionPause.Background => LibraryStrings.Effects_PausedBackground,
                MotionPause.Hidden => LibraryStrings.Effects_PausedMinimized,
                _ => (appearance.PauseInBackground, appearance.PauseWhilePlaying) switch
                {
                    (true, true) => LibraryStrings.Effects_FullPauseBoth,
                    (true, false) => LibraryStrings.Effects_FullPauseBackground,
                    (false, true) => LibraryStrings.Effects_FullPausePlaying,
                    _ => LibraryStrings.Effects_Full,
                },
            },
        };
        OnPropertyChanged(nameof(EffectsPaused));
        OnPropertyChanged(nameof(EffectLevel));
        UpdateAutomationState();
    }

    /// <summary>Spiel ohne Merken als gesehen wählen (beim Öffnen: das erste der Liste, ohne „neu“ zurückzusetzen).</summary>
    public void SelectInitial()
    {
        if (_selectedGame is not null || GamesView.Count == 0 || GamesView.GetItemAt(0) is not GameItemViewModel first)
        {
            return;
        }

        _selectedGame = first;
        OnPropertyChanged(nameof(SelectedGame));
        ChangeIconCommand.RaiseCanExecuteChanged();
        ResetIconCommand.RaiseCanExecuteChanged();
        Details = new GameDetailsViewModel(this, first);
        OnSelectionChanged(first);
    }

    /// <summary>Spiel wählen, ohne es als gesehen zu speichern (Bildschirmfotos, Prüfungen: keine Schreibvorgänge).</summary>
    public void SelectQuietly(GameItemViewModel item)
    {
        if (ReferenceEquals(item, _selectedGame) || !_byKey.ContainsKey(item.Key))
        {
            return;
        }

        _selectedGame = item;
        OnPropertyChanged(nameof(SelectedGame));
        ChangeIconCommand.RaiseCanExecuteChanged();
        ResetIconCommand.RaiseCanExecuteChanged();
        Details = new GameDetailsViewModel(this, item);
        OnSelectionChanged(item);
        RevealRequested?.Invoke(this, item);
    }

    /// <summary>Darstellung von HDR ohne Schaltung (Bildschirmfotos): Kapsel „HDR EIN“, Titelbild hell, Statuszeile „HDR ein“.</summary>
    public bool PreviewHdr { get; private set; }

    internal void SetPreviewHdr(bool on)
    {
        PreviewHdr = on;
        OnPropertyChanged(nameof(PreviewHdr));
        OnPropertyChanged(nameof(ShowsHdrOn));
        OnPropertyChanged(nameof(HdrShortText));
        OnPropertyChanged(nameof(HdrToggleText));
        UpdateAutomationState();
        UpdateBoostWork(restart: false);
    }

    /// <summary>
    /// Helle Fassung des gewählten Titelbilds holen, wenn sie zu sehen ist (HDR an oder in der Vorschau) und sie berechnet
    /// werden darf (<see cref="BoostAllowed"/>), oder <paramref name="force"/> (Prüfung und Bildschirmfotos: der HDR-Moment
    /// spielt gleich, auch ohne HDR). Sie kostet Rechenzeit und Speicher. Fertig, sobald sie im Titelbereich steht oder es
    /// keine gibt.
    /// </summary>
    public Task EnsureHeroBoostAsync(bool force)
    {
        if (_disposed || (!force && (!ShowsHdrOn || !BoostAllowed)) || _hero is not { Artwork: { Hero: not null, HeroBoost: null } artwork } hero)
        {
            return Task.CompletedTask;
        }

        return StartHeroBoost(hero, artwork, prefetch: false);
    }

    /// <summary>
    /// Darf die helle Fassung jetzt berechnet werden (dekodieren, aufhellen, als JPEG schreiben)? Nur bei sichtbarem, aktivem
    /// Fenster und wenn kein Spiel läuft oder über die Bibliothek startet; im Hintergrund, minimiert und geschlossen nie.
    /// </summary>
    public bool BoostAllowed => !_disposed && _windowActive && _launching == 0 && _context.Sessions.Count == 0;

    /// <summary>
    /// Fenster sichtbar, nicht minimiert und aktiv (setzt das Fenster). Aktiv: die helle Fassung des gewählten Spiels vorab
    /// laden (bei HDR an gleich); sonst wartendes Vorab-Laden und noch nicht begonnene Anfragen abbrechen.
    /// </summary>
    public void SetWindowActive(bool active)
    {
        if (_windowActive == active)
        {
            return;
        }

        _windowActive = active;
        UpdateBoostWork(restart: false);
    }

    /// <summary>
    /// HDR-Moment: kurz (höchstens <paramref name="max"/>) auf eine noch ladende helle Fassung warten. Kommt sie später,
    /// blendet der Titelbereich sie über. Sofort fertig, wenn nichts lädt.
    /// </summary>
    public async Task WaitForHeroBoostAsync(TimeSpan max)
    {
        var load = _boostLoad;
        if (load.IsCompleted)
        {
            return;
        }

        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timer = _context.Ui.Delay(max, () => expired.TrySetResult());
        await Task.WhenAny(load, expired.Task);
    }

    /// <summary>HDR an oder in der Vorschau an (Kapsel, Titelbild, Kacheln).</summary>
    public bool ShowsHdrOn => IsHdrOn || PreviewHdr;

    /// <summary>„HDR EIN“ / „HDR AUS“ in der Kapsel.</summary>
    public string HdrShortText => ShowsHdrOn ? LibraryStrings.Nits_HdrOn : LibraryStrings.Nits_HdrOff;

    // ---------- Aufrufe aus Zeilen, Kacheln und Details ----------

    /// <summary>Bild für eine Zeile oder Kachel: aus dem Speicher des Bilddienstes oder im Hintergrund nachgeladen.</summary>
    internal ImageSource? RequestArtwork(GameItemViewModel item, ArtworkKind kind, int width, int request)
    {
        if (_disposed)
        {
            return null;
        }

        var cached = _context.Artwork.GetCachedImage(item.Config, kind, width);
        if (cached is not null)
        {
            return cached;
        }

        _ = LoadArtworkAsync(item, kind, width, request);
        return null;
    }

    /// <summary>Cover zuletzt gebraucht; die ältesten über <see cref="LibraryArt.CoverCacheLimit"/> gibt die Bibliothek frei.</summary>
    internal void TouchCover(GameItemViewModel item)
    {
        if (_coverNodes.TryGetValue(item, out var node))
        {
            _coverOrder.Remove(node);
            _coverOrder.AddFirst(node);
            return;
        }

        _coverNodes[item] = _coverOrder.AddFirst(item);
        while (_coverOrder.Count > LibraryArt.CoverCacheLimit && _coverOrder.Last is { } oldest)
        {
            _coverOrder.RemoveLast();
            _coverNodes.Remove(oldest.Value);
            oldest.Value.ReleaseCover();
        }
    }

    /// <summary>Cover, die die Bibliothek gerade hält (Tests: Obergrenze).</summary>
    internal int HeldCovers => _coverOrder.Count;

    /// <summary>Start über die Bibliothek beginnt: bis er fertig ist (HDR an, Spiel gestartet), wird keine helle Fassung berechnet.</summary>
    internal void OnLaunchStarting()
    {
        _launching++;
        UpdateBoostWork(restart: false);
    }

    /// <summary>Start über die Bibliothek ist fertig (gelungen oder nicht); danach entscheidet die laufende Sitzung.</summary>
    internal void OnLaunchFinished()
    {
        _launching = Math.Max(0, _launching - 1);
        UpdateBoostWork(restart: false);
    }

    /// <summary>Start über die Bibliothek ist gelungen: auf Wunsch schließt das Fenster (HDRpilot läuft im Infobereich weiter).</summary>
    internal void OnLaunchSucceeded()
    {
        if (!_disposed && _context.Appearance.Current.MinimizeToTrayAfterLaunch)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---------- intern ----------

    private void InitializeShowcase(AppSettings settings)
    {
        _viewMode = LibraryViewModes.Parse(settings.LibraryView);
        _thumbnail = LibraryViewModes.ParseThumbnail(settings.LibraryThumbnail);
        _effectLevel = _context.Appearance.Current.EffectLevel;
        ShowViewCommand = new RelayCommand(p => ViewMode = p switch
        {
            LibraryViewMode mode => mode,
            string text => LibraryViewModes.Parse(text),
            _ => _viewMode,
        });
        SortByCommand = new RelayCommand(p =>
        {
            if (p is GameSortOrder order)
            {
                SortOrder = order;
            }
            else if (p is string text)
            {
                SortOrder = GameSorting.Parse(text);
            }
        });
        ToggleDetailsDrawerCommand = new RelayCommand(() => IsDetailsDrawerOpen = !IsDetailsDrawerOpen);
        OpenInstallFolderCommand = new RelayCommand(OpenInstallFolder, () => _selectedGame is not null);
        ChooseArtworkCommand = new AsyncRelayCommand(
            p => ChooseArtworkAsync(p switch
            {
                ArtworkKind kind => kind,
                string text when Enum.TryParse<ArtworkKind>(text, ignoreCase: true, out var parsed) => parsed,
                _ => ArtworkKind.Hero,
            }),
            _ => _selectedGame is not null);
        _context.Artwork.Changed += OnArtworkChanged;
        _context.Luminance.Changed += OnLuminanceChanged;
        _context.Appearance.Changed += OnAppearanceChanged;
        SetEffectsState(_effectLevel, MotionPause.None, _context.Appearance.Current.ReducedByWindows);
        _showcaseReady = true;
    }

    private void DisposeShowcase()
    {
        _heroDelay?.Dispose();
        _heroDelay = null;
        StopBoostWork();
        _artworkRun++;
        _context.Artwork.Changed -= OnArtworkChanged;
        _context.Luminance.Changed -= OnLuminanceChanged;
        _context.Appearance.Changed -= OnAppearanceChanged;
        foreach (var item in _coverOrder)
        {
            item.ReleaseCover();
        }

        _coverOrder.Clear();
        _coverNodes.Clear();
        HdrMomentRequested = null;
        CloseRequested = null;
    }

    private void OnSortChanged()
    {
        OnPropertyChanged(nameof(IsSortedByName));
        OnPropertyChanged(nameof(IsSortedByLastPlayed));
        OnPropertyChanged(nameof(IsSortedByHdr));
        OnPropertyChanged(nameof(ViewSummaryText));
    }

    /// <summary>Auswahl gewechselt: Titelbereich sofort mit Texten, Bilder sofort oder bei schnellem Wechsel kurz danach.</summary>
    private void OnSelectionChanged(GameItemViewModel? item)
    {
        OpenInstallFolderCommand?.RaiseCanExecuteChanged();
        ChooseArtworkCommand?.RaiseCanExecuteChanged();
        _heroDelay?.Dispose();
        _heroDelay = null;
        StopBoostWork(); // die helle Fassung des vorigen Spiels wird nicht mehr gebraucht
        _artworkRun++;
        Hero = item is null ? null : new HeroViewModel(item, () => Now, IsPaused);
        OnPropertyChanged(nameof(HasHero));
        if (item is null)
        {
            return;
        }

        var tick = Environment.TickCount64;
        var rapid = tick - _lastSelectionTick < HeroDelay.TotalMilliseconds;
        _lastSelectionTick = tick;
        if (_context.Artwork.GetCachedArtwork(item.Config) is { } cached)
        {
            ApplySelectedArtwork(item, cached);
        }
        else if (rapid)
        {
            _heroDelay = _context.Ui.Delay(HeroDelay, () => _ = LoadSelectedArtworkAsync(item));
        }
        else
        {
            _ = LoadSelectedArtworkAsync(item);
        }
    }

    private async Task LoadSelectedArtworkAsync(GameItemViewModel item)
    {
        var run = _artworkRun;
        GameArtwork artwork;
        try
        {
            artwork = await _context.Artwork.GetArtworkAsync(item.Config, _heroDecodeWidth, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            return; // Bilder sind Zierde: der Titelbereich bleibt beim Ersatzbild.
        }

        if (!_disposed && run == _artworkRun && ReferenceEquals(item, _selectedGame))
        {
            ApplySelectedArtwork(item, artwork);
        }
    }

    private void ApplySelectedArtwork(GameItemViewModel item, GameArtwork artwork)
    {
        if (_hero is { } hero && ReferenceEquals(hero.Item, item))
        {
            // Dasselbe Titelbild neu geliefert: eine schon geholte helle Fassung bleibt.
            if (artwork.HeroBoost is null && hero.Artwork is { HeroBoost: { } boost } shown && ReferenceEquals(shown.Hero?.Image, artwork.Hero?.Image))
            {
                artwork = artwork with { HeroBoost = boost };
            }

            hero.Artwork = artwork;
        }

        if (_details is { } details && ReferenceEquals(details.Item, item))
        {
            details.SetArtwork(artwork);
        }

        // Auswahl samt Titelbild steht: helle Fassung bei HDR an gleich, sonst nach kurzer Ruhe vorab (nur wenn erlaubt).
        UpdateBoostWork(restart: true);
    }

    /// <summary>
    /// Fenster, Spiel, HDR oder Auswahl haben sich geändert: Darf die helle Fassung berechnet werden, wird sie bei HDR an gleich
    /// geholt, sonst nach <see cref="BoostPrefetchDelay"/> Ruhe vorab geladen (<paramref name="restart"/>: die Ruhe beginnt
    /// neu). Darf sie nicht, entfallen wartendes Vorab-Laden und noch nicht begonnene Anfragen.
    /// </summary>
    private void UpdateBoostWork(bool restart)
    {
        if (!_showcaseReady || !BoostAllowed)
        {
            StopBoostWork();
            return;
        }

        if (ShowsHdrOn)
        {
            _boostPrefetch?.Dispose(); // zu sehen: gleich mit normaler Priorität statt vorab
            _boostPrefetch = null;
            _ = EnsureHeroBoostAsync(force: false);
            return;
        }

        if (_hero is { Artwork: { Hero: not null, HeroBoost: null } } && (restart || _boostPrefetch is null))
        {
            _boostPrefetch?.Dispose();
            _boostPrefetch = _context.Ui.Delay(BoostPrefetchDelay, PrefetchHeroBoost);
        }
    }

    /// <summary>Auswahl ist zur Ruhe gekommen: helle Fassung mit niedriger Priorität vorab laden (sichtbare Bilder gehen vor).</summary>
    private void PrefetchHeroBoost()
    {
        _boostPrefetch = null;
        if (BoostAllowed && _hero is { Artwork: { Hero: not null, HeroBoost: null } artwork } hero)
        {
            _ = StartHeroBoost(hero, artwork, prefetch: true);
        }
    }

    /// <summary>Wartendes Vorab-Laden abbrechen und noch nicht begonnene Anfragen nach der hellen Fassung zurückziehen.</summary>
    private void StopBoostWork()
    {
        _boostPrefetch?.Dispose();
        _boostPrefetch = null;
        if (_boostCancel is { } cancel)
        {
            _boostCancel = null;
            cancel.Cancel();
            cancel.Dispose();
        }
    }

    private Task StartHeroBoost(HeroViewModel hero, GameArtwork artwork, bool prefetch)
    {
        _boostCancel ??= CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var load = LoadHeroBoostAsync(hero, artwork, prefetch, _boostCancel.Token);
        _boostLoad = load;
        return load;
    }

    private async Task LoadHeroBoostAsync(HeroViewModel hero, GameArtwork artwork, bool prefetch, CancellationToken cancellationToken)
    {
        ArtworkImage? boost;
        try
        {
            boost = prefetch
                ? await _context.Artwork.PrefetchHeroBoostAsync(hero.Item.Config, _heroDecodeWidth, cancellationToken)
                : await _context.Artwork.GetHeroBoostAsync(hero.Item.Config, _heroDecodeWidth, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            return; // ohne helle Fassung legt der Titelbereich warmes Licht über das Bild
        }

        // Nur für den Satz, der noch zu sehen ist (inzwischen gewechselt oder neu geladen: nichts).
        if (boost is not null && !_disposed && ReferenceEquals(_hero, hero) && ReferenceEquals(hero.Artwork, artwork))
        {
            hero.Artwork = artwork with { HeroBoost = boost };
        }
    }

    private async Task LoadArtworkAsync(GameItemViewModel item, ArtworkKind kind, int width, int request)
    {
        try
        {
            var image = await _context.Artwork.GetImageAsync(item.Config, kind, width, _lifetime.Token);
            if (!_disposed)
            {
                item.SetArtwork(kind, image, request);
            }
        }
        catch (OperationCanceledException)
        {
            // Fenster geschlossen.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            // Bild ist Zierde: die Ersatzfläche bleibt.
        }
    }

    private void OnArtworkChanged(object? sender, ArtworkChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        foreach (var item in _byKey.Values.Where(i => e.GameKey is null || string.Equals(i.Key, e.GameKey, StringComparison.OrdinalIgnoreCase)))
        {
            item.ReloadArtwork();
        }

        if (_selectedGame is { } selected && (e.GameKey is null || string.Equals(selected.Key, e.GameKey, StringComparison.OrdinalIgnoreCase)))
        {
            _artworkRun++;
            _ = LoadSelectedArtworkAsync(selected);
        }
    }

    private void OnLuminanceChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            RefreshNits();
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            UpdateAutomationState();
        }
    }

    private void RefreshNits()
    {
        var luminance = _context.Luminance;
        var target = Targets.FirstOrDefault();
        var display = luminance.Primary;
        if (target is not null)
        {
            display = luminance.Displays.FirstOrDefault(d => string.Equals(d.MonitorDevicePath, target.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)) ?? display;
        }

        Nits.Update(display, target?.FriendlyName ?? string.Empty);
        OnPropertyChanged(nameof(HdrShortText));
        OnPropertyChanged(nameof(ShowsHdrOn));
    }

    /// <summary>Nach jedem Abgleich des HDR-Zustands: eingeschaltet bei offenem Fenster → HDR-Moment und Hinweis.</summary>
    private void OnHdrStateRefreshed(bool wasOn)
    {
        UpdateAutomationState();

        // HDR an: die helle Fassung des Titelbilds wird jetzt gebraucht (meist liegt sie schon vorab bereit; der Moment wartet
        // kurz auf sie oder sie blendet über). Aber nie, solange ein Spiel läuft oder startet oder das Fenster nicht aktiv ist:
        // Dann fallen auch Vorab-Laden und wartende Anfragen weg.
        UpdateBoostWork(restart: false);
        if (!_showcaseReady || _disposed || wasOn || !_isHdrOn)
        {
            return;
        }

        HdrMomentRequested?.Invoke(this, EventArgs.Empty);
        var name = Targets.FirstOrDefault()?.FriendlyName ?? Nits.DisplayName;
        ShowStatus(
            Nits.HasValues
                ? LibraryText.Format(LibraryStrings.Status_HdrOnWithNits, name, Nits.PeakText, Nits.FullFrameText)
                : LibraryText.Format(LibraryStrings.Status_HdrOn, name),
            StatusKind.Success);
    }

    private void UpdateAutomationState()
    {
        if (!_showcaseReady)
        {
            return;
        }

        var sessions = _context.Sessions;
        if (sessions.Count > 0)
        {
            AutomationState = AutomationState.Running;
            var names = string.Join(", ", sessions.Select(s => s.GameName));
            AutomationStateText = _context.Appearance.Current.PauseWhilePlaying && _effectLevel == EffectLevel.Full
                ? LibraryText.Format(LibraryStrings.Status_RunningPaused, names)
                : LibraryText.Format(LibraryStrings.Status_Running, names);
        }
        else if (IsPaused)
        {
            AutomationState = AutomationState.Paused;
            AutomationStateText = LibraryStrings.Status_Paused;
        }
        else if (_isHdrOn || PreviewHdr)
        {
            AutomationState = AutomationState.HdrOn;
            AutomationStateText = LibraryStrings.Status_ActiveHdrOn;
        }
        else
        {
            AutomationState = AutomationState.Waiting;
            AutomationStateText = LibraryStrings.Status_ActiveWaiting;
        }

        OnPropertyChanged(nameof(HdrShortText));
        OnPropertyChanged(nameof(ShowsHdrOn));
    }

    private void OpenInstallFolder()
    {
        if (_selectedGame is not { } item)
        {
            return;
        }

        var dir = item.InstallDir;
        bool exists;
        try
        {
            exists = !string.IsNullOrWhiteSpace(dir) && _context.FileSystem.DirectoryExists(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            exists = false;
        }

        if (!exists)
        {
            ShowStatus(LibraryText.Format(LibraryStrings.Folder_NotFound, item.Name), StatusKind.Warning);
            return;
        }

        try
        {
            _context.OpenFolder(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowStatus(LibraryText.Format(LibraryStrings.Error_Generic, ex.Message), StatusKind.Error);
        }
    }

    /// <summary>„Bild wählen…“: Dialog mit den Möglichkeiten des Bilddienstes; Übernehmen schreibt die Wahl in die Konfiguration.</summary>
    private async Task ChooseArtworkAsync(ArtworkKind kind)
    {
        if (_selectedGame is not { } item)
        {
            return;
        }

        var picker = new ArtworkPickerViewModel(this, item, kind is ArtworkKind.Logo or ArtworkKind.Cover ? kind : ArtworkKind.Hero);
        var load = picker.LoadAsync();
        if (_dialogs.ShowArtworkPicker(picker))
        {
            await load;
            await picker.ApplyAsync();
        }
    }
}
