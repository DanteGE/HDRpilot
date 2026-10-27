using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Zustand des Abschnitts „STEAMGRIDDB“ in einem Reiter von „Bild wählen…“ (E93).</summary>
public enum SteamGridDbSearchState
{
    NoKey,
    KeyUnreadable,
    Ready,
    Resolving,
    Loading,
    Results,
    NoMatch,
    NoHits,
    NoImages,
    Unauthorized,
    RateLimited,
    Offline,
    ServerError,
    BudgetExhausted,
}

/// <summary>Vorschau einer Kachel von SteamGridDB.</summary>
public enum PreviewState
{
    None,
    Loading,
    Loaded,
    Failed,

    /// <summary>Keine Vorschau-Adresse: „Keine Vorschau“, die Kachel bleibt wählbar.</summary>
    Missing,
}

/// <summary>Wohin die Ansicht den Tastaturfokus legen soll.</summary>
public enum SteamGridDbFocusTarget
{
    FirstResult,
    FirstNewResult,
    Retry,
    SettingsLink,
    MatchSearch,
    MatchList,
    Tile,
}

/// <summary>Bitte an die Ansicht, den Fokus zu setzen (bei <see cref="SteamGridDbFocusTarget.Tile"/> auf <see cref="Item"/>).</summary>
public sealed class SteamGridDbFocusRequestEventArgs(SteamGridDbFocusTarget target, ArtworkChoiceViewModel? item = null) : EventArgs
{
    public SteamGridDbFocusTarget Target { get; } = target;

    public ArtworkChoiceViewModel? Item { get; } = item;
}

/// <summary>Eintrag der Spielwahl („Spiel: … ▾“): der Eintrag ohne Netz (Steam bzw. gemerkt) oder ein Treffer der Suche.</summary>
public sealed class SteamGridDbMatchEntry : ObservableObject
{
    private bool _isCurrent;

    internal SteamGridDbMatchEntry(string text, string automationName, bool verified, SteamGridDbGameHit? hit, SteamGridDbTarget target)
    {
        Text = text;
        AutomationName = automationName;
        IsVerified = verified;
        Hit = hit;
        Target = target;
    }

    /// <summary>„Kupferwald (2019)“ oder „Kupferwald (Steam, aus deiner Bibliothek)“.</summary>
    public string Text { get; }

    /// <summary>„Kupferwald, 2019, verifiziert“.</summary>
    public string AutomationName { get; }

    public bool IsVerified { get; }

    /// <summary>Treffer der Suche; null für den Eintrag ohne Netz.</summary>
    public SteamGridDbGameHit? Hit { get; }

    internal SteamGridDbTarget Target { get; }

    /// <summary>Gilt gerade (Häkchen, Status „gewählt“).</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        internal set
        {
            if (SetProperty(ref _isCurrent, value))
            {
                OnPropertyChanged(nameof(ItemStatus));
            }
        }
    }

    public string? ItemStatus => _isCurrent ? LibraryStrings.Picker_SgdbEntryCurrent : null;

    public override string ToString() => Text;
}

/// <summary>
/// Abschnitt „STEAMGRIDDB“ von „Bild wählen…“ (E93): Zustand, Ergebnisse und Spielwahl je Reiter, gezeigt wird immer der
/// aktuelle Reiter. Ins Netz geht nur ein Klick (Suchen, Mehr laden, Erneut versuchen, Suche der Spielwahl, Wahl eines
/// Treffers, Übernehmen); die Sitzung (und mit ihr der entschlüsselte Schlüssel) entsteht erst mit dem ersten solchen Klick,
/// bis dahin zeigt die Spielwahl die Zuordnung ohne Netz (<see cref="ISteamGridDbService.PeekMatch"/>). Je Seite 24 Kacheln
/// aus dem Speicher (die Schnittstelle liefert 50), höchstens 240 je Reiter;
/// Vorschauen nur des aktuellen Reiters, in der Reihenfolge der Kacheln, gehalten höchstens 96 MB (verborgene Reiter geben
/// ihre zuerst frei). Gehört dem <see cref="ArtworkPickerViewModel"/>; nach <see cref="Close"/> bleiben späte Ergebnisse
/// ohne Wirkung.
/// </summary>
public sealed class SteamGridDbSearchViewModel : ObservableObject
{
    /// <summary>Kacheln je „Seite“ (aus dem Speicher).</summary>
    public const int PageSize = 24;

    /// <summary>Höchstens so viele Kacheln je Reiter.</summary>
    public const int MaxTiles = 240;

    /// <summary>Höchstens so viel dekodierte Vorschau im Speicher (alle Reiter).</summary>
    public const long MaxHeldBytes = 96L * 1024 * 1024;

    /// <summary>Höchste Breite einer dekodierten Vorschau.</summary>
    public const int MaxDecodeWidth = 512;

    /// <summary>
    /// Höchstens so viele Seiten holt ein Klick zusätzlich, solange eine Seite nur verworfene Einträge brachte (bewegte Bilder,
    /// Markierungen) und die Liste weitergeht.
    /// </summary>
    public const int MaxFilteredPages = 3;

    /// <summary>Nach so langer Zeit ohne Antwort einmal „antwortet langsam“.</summary>
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(4);

    private static readonly ArtworkKind[] Kinds = [ArtworkKind.Hero, ArtworkKind.Logo, ArtworkKind.Cover];

    private readonly ArtworkPickerViewModel _picker;
    private readonly ILibraryContext _context;
    private readonly GameConfig _game;
    private readonly Dictionary<ArtworkKind, KindState> _states = [];
    private ISteamGridDbSearchSession? _session;
    private SteamGridDbMatch? _initialMatch;
    private SteamGridDbMatch? _match;
    private IReadOnlyList<SteamGridDbGameHit> _hits = [];
    private SteamGridDbGameHit? _described;
    private CancellationTokenSource? _chooserSearch;
    private string? _announcement;
    private string _matchSearchText;
    private bool _isMatchOpen;
    private bool _isMatchSearching;
    private bool _closed;
    private bool _keyReady;
    private long _shownCounter;
    private double _previewScale = 1;

    internal SteamGridDbSearchViewModel(ArtworkPickerViewModel picker, ILibraryContext context, GameConfig game)
    {
        _picker = picker;
        _context = context;
        _game = game;
        _matchSearchText = game.Name;
        var start = StateFor(SafeKeyState());
        foreach (var kind in Kinds)
        {
            _states[kind] = new KindState(kind) { State = start };
        }

        if (start == SteamGridDbSearchState.Ready)
        {
            // Nur die Zuordnung ohne Netz; die Sitzung entsteht erst mit dem ersten Klick (EnsureSession).
            _keyReady = true;
            _initialMatch = _match = _context.SteamGridDb.PeekMatch(game);
        }

        SearchCommand = new RelayCommand(() => _ = SearchAsync(Kind), () => !_picker.IsApplying);
        MoreCommand = new RelayCommand(() => _ = MoreAsync(Kind), () => !_picker.IsApplying && !Current.IsBusy);
        RetryCommand = new RelayCommand(() => _ = RetryAsync(Kind), () => CanRetry);
        OpenSettingsCommand = new RelayCommand(() => _picker.RequestSettings(), () => !_picker.IsApplying);
        SearchGamesCommand = new RelayCommand(() => _ = SearchGamesAsync(), () => !_picker.IsApplying && CanUseSearch);
        ChooseMatchCommand = new RelayCommand(p => _ = ChooseMatchAsync(p as SteamGridDbMatchEntry), p => p is SteamGridDbMatchEntry && !_picker.IsApplying);
        ToggleMatchCommand = new RelayCommand(() => IsMatchOpen = !IsMatchOpen, () => !_picker.IsApplying && CanUseSearch);
        context.SteamGridDb.Changed += OnServiceChanged;
        RebuildEntries();
    }

    /// <summary>Bitte an die Ansicht, den Fokus zu setzen; sie folgt nur, solange der Fokus im Abschnitt liegt.</summary>
    public event EventHandler<SteamGridDbFocusRequestEventArgs>? FocusRequest;

    public SteamGridDbSearchState State => Current.State;

    /// <summary>Kacheln des aktuellen Reiters.</summary>
    public ObservableCollection<ArtworkChoiceViewModel> Results { get; } = [];

    public string? StatusText => Status(Current);

    public StatusKind StatusTone => Current.State switch
    {
        SteamGridDbSearchState.Unauthorized => StatusKind.Error,
        SteamGridDbSearchState.KeyUnreadable or SteamGridDbSearchState.RateLimited or SteamGridDbSearchState.Offline or SteamGridDbSearchState.ServerError => StatusKind.Warning,
        _ => StatusKind.Info,
    };

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>Text der Live-Ansage: ändert sich nur bei einem Zustandswechsel und einmal, wenn eine Seite Vorschauen fertig ist.</summary>
    public string? Announcement
    {
        get => _announcement;
        private set => SetProperty(ref _announcement, value);
    }

    public bool ShowResults => Current.Shown.Count > 0;

    public bool ShowSkeleton => Current.Shown.Count == 0 && Current.State is SteamGridDbSearchState.Resolving or SteamGridDbSearchState.Loading;

    /// <summary>Der Suchknopf bleibt beim Laden (fokussierbar, „Sucht …“), bis Ergebnisse ihn ersetzen.</summary>
    public bool ShowSearchButton => Current.Shown.Count == 0 && Current.State is SteamGridDbSearchState.Ready or SteamGridDbSearchState.Resolving or SteamGridDbSearchState.Loading;

    public bool IsSearchBusy => Current.State is SteamGridDbSearchState.Resolving or SteamGridDbSearchState.Loading;

    public string SearchButtonText => IsSearchBusy ? LibraryStrings.Picker_SgdbSearching : LibraryStrings.Picker_SgdbSearch;

    public string? SearchItemStatus => IsSearchBusy ? LibraryStrings.Picker_SgdbSearching : null;

    public string SearchAutomationName => LibraryText.Format(LibraryStrings.Picker_SgdbSearchName, Plural(Kind));

    public string MoreAutomationName => LibraryText.Format(LibraryStrings.Picker_SgdbMoreName, Plural(Kind));

    /// <summary>Kopfzeile mit Spielwahl (nicht ohne oder mit unlesbarem Schlüssel).</summary>
    public bool ShowHeader => Current.State is not (SteamGridDbSearchState.NoKey or SteamGridDbSearchState.KeyUnreadable);

    public bool ShowReadyNote => Current.State == SteamGridDbSearchState.Ready && Current.Shown.Count == 0;

    public bool ShowSettingsLink => Current.State is SteamGridDbSearchState.NoKey or SteamGridDbSearchState.KeyUnreadable or SteamGridDbSearchState.Unauthorized;

    public string SettingsLinkText => _picker.HasChanges ? LibraryStrings.Picker_SgdbOpenSettingsDiscard : LibraryStrings.Picker_SgdbOpenSettings;

    public bool ShowRetry => Current.State is SteamGridDbSearchState.RateLimited or SteamGridDbSearchState.Offline or SteamGridDbSearchState.ServerError;

    public bool CanRetry => !_picker.IsApplying && ShowRetry && (Current.State != SteamGridDbSearchState.RateLimited || _session?.BlockedUntil is null);

    public string? RetryToolTip => Current.State == SteamGridDbSearchState.RateLimited ? LibraryStrings.Picker_SgdbRateLimited : null;

    public bool ShowMore => Current.Shown.Count > 0 && Current.State == SteamGridDbSearchState.Results && !Current.LimitReached
        && (Current.Buffer.Count > 0 || Current.ServerHasMore);

    public bool IsMoreBusy => Current.IsBusy && Current.Shown.Count > 0;

    /// <summary>„24 Cover von SteamGridDB“ (keine Live-Ansage).</summary>
    public string? CountText => Current.Shown.Count switch
    {
        0 => null,
        1 => LibraryText.Format(LibraryStrings.Picker_SgdbResultsOne, Singular(Kind)),
        var n => LibraryText.Format(LibraryStrings.Picker_SgdbResults, n, Plural(Kind)),
    };

    /// <summary>„Vorschauen 12 von 24“, solange eine Seite Vorschauen lädt (keine Live-Ansage).</summary>
    public string? ProgressText => Current.BatchTotal > 0 && Current.BatchDone < Current.BatchTotal
        ? LibraryText.Format(LibraryStrings.Picker_SgdbPreviewProgress, Current.BatchDone, Current.BatchTotal)
        : null;

    public string MatchText => LibraryText.Format(LibraryStrings.Picker_SgdbMatch, MatchLabel);

    public string MatchToolTip => (_match?.Kind ?? SteamGridDbMatchKind.ExactName) switch
    {
        SteamGridDbMatchKind.SteamAppId => LibraryStrings.Picker_SgdbMatchHowSteam,
        SteamGridDbMatchKind.Remembered => LibraryStrings.Picker_SgdbMatchHowRemembered,
        SteamGridDbMatchKind.Chosen => LibraryStrings.Picker_SgdbMatchHowChosen,
        _ => LibraryStrings.Picker_SgdbMatchHowName,
    };

    public string MatchAutomationName => LibraryText.Format(LibraryStrings.Picker_SgdbMatchName, MatchLabel);

    public bool IsMatchAmbiguous => _match?.Ambiguous == true;

    public string MatchAmbiguousText => LibraryStrings.Picker_SgdbMatchAmbiguous;

    public bool IsMatchOpen
    {
        get => _isMatchOpen;
        set
        {
            if (value && (!CanUseSearch || _picker.IsApplying))
            {
                // Abgelehnt: Die Ansicht (TwoWay) hat schon umgeschaltet und muss zurück auf zu.
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref _isMatchOpen, value) && value)
            {
                // Aufklappen fragt nichts ab; der Fokus geht ins Suchfeld.
                RaiseFocus(SteamGridDbFocusTarget.MatchSearch);
            }
        }
    }

    public string MatchSearchText
    {
        get => _matchSearchText;
        set => SetProperty(ref _matchSearchText, value ?? string.Empty);
    }

    public ObservableCollection<SteamGridDbMatchEntry> MatchEntries { get; } = [];

    public bool IsMatchSearching
    {
        get => _isMatchSearching;
        private set
        {
            if (SetProperty(ref _isMatchSearching, value))
            {
                OnPropertyChanged(nameof(MatchHint));
            }
        }
    }

    public string MatchHint => _isMatchSearching ? LibraryStrings.Picker_SgdbMatchSearching : LibraryStrings.Picker_SgdbMatchHint;

    /// <summary>Hinweis, dass HDRpilot die Wahl beim Übernehmen merkt (nur ohne Steam-App-ID).</summary>
    public bool ShowCommitHint => CanUseSearch && !IsSteamGame;

    /// <summary>Vom Nutzer auf ein anderes Spiel als das anfangs zugeordnete geändert.</summary>
    public bool MatchChangedByUser { get; private set; }

    public bool IsSteamGame => _session?.IsSteamGame ?? (_initialMatch?.Kind == SteamGridDbMatchKind.SteamAppId
        || (_game.Source == Core.Games.GameSource.Steam && SteamGridDbTarget.IsSteamAppId(_game.SourceId)));

    internal SteamGridDbMatch? Match => _match;

    /// <summary>DPI-Faktor der Ansicht (Breite der dekodierten Vorschau).</summary>
    public double PreviewScale
    {
        get => _previewScale;
        set => _previewScale = value > 0 && double.IsFinite(value) ? value : 1;
    }

    public RelayCommand SearchCommand { get; }

    public RelayCommand MoreCommand { get; }

    public RelayCommand RetryCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand SearchGamesCommand { get; }

    public RelayCommand ChooseMatchCommand { get; }

    public RelayCommand ToggleMatchCommand { get; }

    /// <summary>Dekodierte Vorschauen aller Reiter im Speicher.</summary>
    internal long HeldBytes => _states.Values.SelectMany(s => s.Shown).Where(c => c.Preview is not null).Sum(c => c.DecodedBytes);

    internal bool IsClosed => _closed;

    /// <summary>Die Sitzung, sobald ein Klick sie geöffnet hat (Tests).</summary>
    internal ISteamGridDbSearchSession? Session => _session;

    private ArtworkKind Kind => _picker.Kind;

    private KindState Current => _states[Kind];

    /// <summary>Ein lesbarer Schlüssel ist gespeichert und der Dialog offen: Suche und Spielwahl sind bedienbar.</summary>
    private bool CanUseSearch => _keyReady && !_closed;

    private string MatchLabel => _match is { } match && (match.Kind is not (SteamGridDbMatchKind.SteamAppId or SteamGridDbMatchKind.Remembered) || _described is not null)
        ? Label(match.DisplayName, match.Year)
        : _game.Name;

    /// <summary>Breite einer Kachel je Bildart (§8.3 der Spezifikation zu E93).</summary>
    public static double TileWidth(ArtworkKind kind) => kind == ArtworkKind.Cover ? 130 : 200;

    /// <summary>Breite, in der eine Vorschau dekodiert wird.</summary>
    internal int DecodeWidth(ArtworkKind kind) => (int)Math.Min(MaxDecodeWidth, Math.Ceiling(TileWidth(kind) * _previewScale));

    /// <summary>Reiter gewechselt: wartende Vorschauen des alten Reiters fallen weg, die des neuen kommen wieder.</summary>
    internal void OnKindChanged(ArtworkKind previous)
    {
        if (_states.TryGetValue(previous, out var old))
        {
            old.CancelQueue();
            old.LastShown = ++_shownCounter;
        }

        Results.Clear();
        foreach (var tile in Current.Shown)
        {
            Results.Add(tile);
        }

        Current.LastShown = ++_shownCounter;
        if (!_closed)
        {
            // Platz für die Vorschauen, die dieser Reiter neu anfragt.
            var pending = Current.Shown.Count(t => t.PreviewState == PreviewState.None);
            ReleaseHidden(EstimateBytes(Kind, DecodeWidth(Kind)) * pending);
            QueuePreviews(Current, Current.Shown);
        }

        RaiseAll();
    }

    /// <summary>„Übernehmen“ lädt: wartende Vorschauen anhalten.</summary>
    internal void PauseQueue()
    {
        foreach (var state in _states.Values)
        {
            state.CancelQueue();
        }

        RaiseCommands();
    }

    /// <summary>Nach gescheitertem Übernehmen: fehlende Vorschauen des aktuellen Reiters wieder anfragen.</summary>
    internal void ResumeQueue()
    {
        if (!_closed)
        {
            QueuePreviews(Current, Current.Shown);
        }

        RaiseCommands();
    }

    /// <summary>Volles Bild für „Übernehmen“.</summary>
    internal Task<SteamGridDbImageData> DownloadAsync(SteamGridDbAsset asset, ArtworkKind kind, CancellationToken cancellationToken) =>
        _session is { } session && !_closed
            ? session.DownloadAsync(asset, kind, cancellationToken)
            : Task.FromCanceled<SteamGridDbImageData>(new CancellationToken(true));

    internal void RequestTileFocus(ArtworkChoiceViewModel item) => RaiseFocus(SteamGridDbFocusTarget.Tile, item);

    /// <summary>Picker hat <c>IsApplying</c> oder Änderungen umgeschaltet.</summary>
    internal void RaiseCommands()
    {
        SearchCommand.RaiseCanExecuteChanged();
        MoreCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        OpenSettingsCommand.RaiseCanExecuteChanged();
        SearchGamesCommand.RaiseCanExecuteChanged();
        ChooseMatchCommand.RaiseCanExecuteChanged();
        ToggleMatchCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(SettingsLinkText));
    }

    /// <summary>
    /// Dialog geschlossen: Sitzung freigeben (bricht alles ab), Ergebnisse und Puffer leeren, Vorschauen nicht gewählter
    /// Kacheln freigeben. Gewählte Kacheln behalten ihre Daten für das Übernehmen.
    /// </summary>
    internal void Close(IEnumerable<ArtworkChoiceViewModel?> chosen)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _context.SteamGridDb.Changed -= OnServiceChanged;
        _chooserSearch?.Cancel();
        foreach (var state in _states.Values)
        {
            state.CancelOp();
            state.CancelQueue();
        }

        _session?.Dispose();
        _session = null;
        var keep = chosen.OfType<ArtworkChoiceViewModel>().ToHashSet();
        foreach (var state in _states.Values)
        {
            foreach (var tile in state.Shown.Where(t => !keep.Contains(t)))
            {
                tile.ReleasePreview();
            }

            state.Shown.Clear();
            state.Buffer.Clear();
        }

        Results.Clear();
        RaiseAll();
    }

    /// <summary>Wahl in der Spielwahl: neue Zuordnung für alle Reiter, der aktuelle lädt sofort.</summary>
    internal async Task ChooseMatchAsync(SteamGridDbMatchEntry? entry)
    {
        if (entry is null || _closed || _picker.IsApplying)
        {
            return;
        }

        SteamGridDbMatch match;
        if (entry.Hit is { } hit)
        {
            match = new SteamGridDbMatch(SteamGridDbMatchKind.Chosen, SteamGridDbTarget.Game(hit.Id), hit.Name, hit.ReleaseYear, hit.Verified);
        }
        else if (_initialMatch is { } initial)
        {
            match = _described is { } described && initial.Kind == SteamGridDbMatchKind.Remembered
                ? initial with { DisplayName = described.Name, Year = described.ReleaseYear }
                : initial;
        }
        else
        {
            return;
        }

        var changed = _match?.Target != match.Target;
        _match = match;
        MatchChangedByUser = _initialMatch?.Target != match.Target;
        IsMatchOpen = false;
        RebuildEntries();
        RaiseMatch();
        if (changed)
        {
            ResetForNewMatch();
        }

        // Die Wahl ist ein Klick: Der aktuelle Reiter lädt sofort, die anderen erst nach ihrem eigenen.
        await SearchAsync(Kind);
    }

    /// <summary>„Bei SteamGridDB suchen“ für einen Reiter (oder Erneut versuchen nach einem Fehler vor den Ergebnissen).</summary>
    internal async Task SearchAsync(ArtworkKind kind)
    {
        var st = _states[kind];
        if (_closed || _picker.IsApplying || st.IsBusy || st.Shown.Count > 0 || EnsureSession() is not { } session)
        {
            return;
        }

        var token = st.StartOp();
        using var slow = StartSlow(st, token);
        try
        {
            if (_match is null)
            {
                SetState(st, SteamGridDbSearchState.Resolving);
                var resolved = await session.ResolveAsync(token);
                if (Stale(token))
                {
                    return;
                }

                _hits = resolved.Hits;
                RebuildEntries();
                if (resolved.Match is not { } found)
                {
                    ShowNoMatch(st, resolved.SteamAppUnknown);
                    return;
                }

                _match = found;
                RaiseMatch();
            }

            SetState(st, SteamGridDbSearchState.Loading);
            var match = _match;
            var page = await session.GetAssetsAsync(match, kind, 0, token);
            if (Stale(token))
            {
                return;
            }

            if (page.GameUnknown || (match.Kind == SteamGridDbMatchKind.Remembered && _described is null && !await DescribeAsync(session, match, token)))
            {
                // Unbekannte Steam-App oder veraltete gemerkte ID: einmal über den Namen.
                SetState(st, SteamGridDbSearchState.Resolving);
                var fallback = await session.ResolveByNameAsync(token);
                if (Stale(token))
                {
                    return;
                }

                _hits = fallback.Hits;
                RebuildEntries();
                if (fallback.Match is not { } byName)
                {
                    ShowNoMatch(st, fallback.SteamAppUnknown);
                    return;
                }

                _match = byName;
                RaiseMatch();
                SetState(st, SteamGridDbSearchState.Loading);
                page = await session.GetAssetsAsync(byName, kind, 0, token);
                if (Stale(token))
                {
                    return;
                }
            }

            if (Stale(token))
            {
                return;
            }

            st.NextServerPage = 1;
            st.ServerHasMore = page.HasMore && !page.GameUnknown;
            AddToBuffer(st, page);
            if (st.Pinned is null)
            {
                // Seite 0 nur mit verworfenen Einträgen: Die nächste kann Bilder haben (begrenzt).
                await FetchPastFilteredAsync(st, session, _match!, kind, token);
                if (Stale(token))
                {
                    return;
                }
            }

            if (st.Buffer.Count == 0 && st.Pinned is null)
            {
                st.Ended = !st.ServerHasMore;
                SetState(st, SteamGridDbSearchState.NoImages);
                return;
            }

            ShowNext(st, focus: SteamGridDbFocusTarget.FirstResult);
        }
        catch (OperationCanceledException)
        {
            // Reiter zurückgesetzt, Dialog geschlossen oder Zuordnung geändert: nichts anzeigen.
        }
        catch (SteamGridDbException ex)
        {
            if (!Stale(token))
            {
                st.Retry = RetryAction.Search;
                SetError(st, ex);
            }
        }
        finally
        {
            st.EndOp(token);
            RaiseIfCurrent(st);
        }
    }

    /// <summary>„Mehr laden“: die nächsten 24 aus dem Speicher, erst wenn der zu knapp wird, die nächste Seite.</summary>
    internal async Task MoreAsync(ArtworkKind kind)
    {
        var st = _states[kind];
        if (_closed || _picker.IsApplying || st.IsBusy || _session is not { } session || _match is not { } match || st.LimitReached)
        {
            return;
        }

        var token = st.StartOp();
        try
        {
            if (st.Buffer.Count < PageSize && st.ServerHasMore)
            {
                RaiseIfCurrent(st);
                var page = await session.GetAssetsAsync(match, kind, st.NextServerPage, token);
                if (Stale(token))
                {
                    return;
                }

                st.NextServerPage++;
                st.ServerHasMore = page.HasMore;
                AddToBuffer(st, page);
                await FetchPastFilteredAsync(st, session, match, kind, token);
                if (Stale(token))
                {
                    return;
                }
            }

            if (st.Buffer.Count == 0)
            {
                // Zu Ende nur, wenn die Liste endet; nach lauter verworfenen Seiten bleibt „Mehr laden“ stehen.
                st.Ended = !st.ServerHasMore;
                SetState(st, SteamGridDbSearchState.Results, force: true);
                return;
            }

            ShowNext(st, focus: SteamGridDbFocusTarget.FirstNewResult);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SteamGridDbException ex)
        {
            if (!Stale(token))
            {
                // Die Kacheln bleiben; der Fehler steht darunter, „Erneut versuchen“ wiederholt diese Seite.
                st.Retry = RetryAction.More;
                SetError(st, ex);
            }
        }
        finally
        {
            st.EndOp(token);
            RaiseIfCurrent(st);
        }
    }

    /// <summary>Suche der Spielwahl (Enter oder „Suchen“); eine neue bricht die vorige ab.</summary>
    internal async Task SearchGamesAsync()
    {
        if (_closed || _picker.IsApplying || EnsureSession() is not { } session)
        {
            return;
        }

        var term = MatchSearchText.Trim();
        _chooserSearch?.Cancel();
        var cancel = _chooserSearch = new CancellationTokenSource();
        IsMatchSearching = true;
        var st = Current;
        try
        {
            var hits = await session.SearchGamesAsync(term, cancel.Token);
            if (_closed || cancel.IsCancellationRequested)
            {
                return;
            }

            _hits = hits;
            RebuildEntries();
            if (hits.Count == 0)
            {
                st.SearchedTerm = term;
                if (st.Shown.Count == 0)
                {
                    SetState(st, SteamGridDbSearchState.NoHits);
                }
            }
            else if (st.State is SteamGridDbSearchState.NoHits or SteamGridDbSearchState.NoMatch)
            {
                SetState(st, SteamGridDbSearchState.NoMatch);
            }

            RaiseFocus(hits.Count > 0 ? SteamGridDbFocusTarget.MatchList : SteamGridDbFocusTarget.MatchSearch);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SteamGridDbException ex)
        {
            if (!_closed && !cancel.IsCancellationRequested)
            {
                st.Retry = RetryAction.ChooserSearch;
                SetError(st, ex);
            }
        }
        finally
        {
            if (ReferenceEquals(_chooserSearch, cancel))
            {
                IsMatchSearching = false;
                _chooserSearch = null;
            }

            cancel.Dispose();
        }
    }

    /// <summary>„Erneut versuchen“: wiederholt, was scheiterte, und fragt gescheiterte Vorschauen neu an.</summary>
    internal async Task RetryAsync(ArtworkKind kind)
    {
        var st = _states[kind];
        if (!CanRetry || st.IsBusy)
        {
            return;
        }

        foreach (var tile in st.Shown.Where(t => t.PreviewState == PreviewState.Failed))
        {
            tile.SetPreviewState(PreviewState.None);
        }

        var action = st.Retry;
        var hadResults = st.Shown.Count > 0;
        st.Error = null;
        st.Timeout = false;
        if (hadResults)
        {
            SetState(st, SteamGridDbSearchState.Results, force: true);
            QueuePreviews(st, st.Shown);
        }
        else
        {
            SetState(st, SteamGridDbSearchState.Ready);
        }

        switch (action)
        {
            case RetryAction.More when hadResults:
                await MoreAsync(kind);
                break;
            case RetryAction.ChooserSearch:
                await SearchGamesAsync();
                break;
            default:
                if (!hadResults)
                {
                    await SearchAsync(kind);
                }

                break;
        }
    }

    private static SteamGridDbSearchState StateFor(SteamGridDbKeyState key) => key switch
    {
        SteamGridDbKeyState.Stored => SteamGridDbSearchState.Ready,
        SteamGridDbKeyState.Unreadable => SteamGridDbSearchState.KeyUnreadable,
        _ => SteamGridDbSearchState.NoKey,
    };

    private static string Plural(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Hero => LibraryStrings.Picker_KindPluralHero,
        ArtworkKind.Logo => LibraryStrings.Picker_KindPluralLogo,
        _ => LibraryStrings.Picker_KindPluralCover,
    };

    private static string Singular(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Hero => LibraryStrings.Picker_KindSingularHero,
        ArtworkKind.Logo => LibraryStrings.Picker_KindSingularLogo,
        _ => LibraryStrings.Picker_KindSingularCover,
    };

    private static string Label(string name, int? year) => year is { } y ? LibraryText.Format(LibraryStrings.Picker_SgdbMatchYear, name, y) : name;

    /// <summary>Speicher einer Vorschau nach dem Seitenverhältnis der Bildart (zum Schätzen vor dem Laden).</summary>
    private static long EstimateBytes(ArtworkKind kind, int width) =>
        (long)width * (long)Math.Ceiling(width * (kind switch
        {
            ArtworkKind.Cover => 1.5,
            ArtworkKind.Hero => 0.33,
            _ => 0.6,
        })) * 4;

    private SteamGridDbKeyState SafeKeyState()
    {
        try
        {
            return _context.SteamGridDb.KeyState;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return SteamGridDbKeyState.Unreadable;
        }
    }

    /// <summary>
    /// Öffnet die Sitzung beim ersten Klick, der ins Netz geht (kein Handler, keine Anfrage; erst jetzt wird der Schlüssel
    /// entschlüsselt). Ohne lesbaren Schlüssel bleiben die Reiter beim Hinweis; null.
    /// </summary>
    private ISteamGridDbSearchSession? EnsureSession()
    {
        if (_session is not null || !CanUseSearch)
        {
            return _session;
        }

        _session = _context.SteamGridDb.OpenSearch(_game);
        if (_session is null)
        {
            var state = StateFor(SafeKeyState());
            _keyReady = false;
            foreach (var st in _states.Values)
            {
                st.State = state == SteamGridDbSearchState.Ready ? SteamGridDbSearchState.NoKey : state;
            }

            IsMatchOpen = false;
            Announcement = Status(Current);
            RaiseAll();
            return null;
        }

        // Die Sitzung kennt die Zuordnung ohne Netz ebenso; eine vom Nutzer gewählte bleibt.
        var initial = _session.InitialMatch;
        if (_match == _initialMatch)
        {
            _match = initial;
        }

        _initialMatch = initial;
        RebuildEntries();
        RaiseMatch();
        return _session;
    }

    private string? Status(KindState st) => st.State switch
    {
        SteamGridDbSearchState.NoKey => LibraryStrings.Picker_SgdbNoKey,
        SteamGridDbSearchState.KeyUnreadable => LibraryStrings.Picker_SgdbKeyUnreadable,
        SteamGridDbSearchState.Resolving when st.Slow => LibraryStrings.Picker_SgdbSlow,
        SteamGridDbSearchState.Loading when st.Slow => LibraryStrings.Picker_SgdbSlow,
        SteamGridDbSearchState.Resolving => LibraryStrings.Picker_SgdbResolving,
        SteamGridDbSearchState.Loading => LibraryText.Format(LibraryStrings.Picker_SgdbLoading, Plural(st.Kind)),
        SteamGridDbSearchState.Results when st.LimitReached => LibraryText.Format(LibraryStrings.Picker_SgdbTabLimit, Plural(st.Kind)),
        SteamGridDbSearchState.Results when st.Ended && st.Buffer.Count == 0 => LibraryText.Format(LibraryStrings.Picker_SgdbEnd, Plural(st.Kind)),
        SteamGridDbSearchState.NoMatch when st.SteamUnknown => LibraryStrings.Picker_SgdbSteamUnknown,
        SteamGridDbSearchState.NoMatch => LibraryText.Format(LibraryStrings.Picker_SgdbNoMatch, _session?.GameName ?? _game.Name),
        SteamGridDbSearchState.NoHits => LibraryText.Format(LibraryStrings.Picker_SgdbNoHits, st.SearchedTerm ?? MatchSearchText),
        SteamGridDbSearchState.NoImages => LibraryText.Format(LibraryStrings.Picker_SgdbNoImages, Plural(st.Kind)),
        SteamGridDbSearchState.Unauthorized => LibraryStrings.Picker_SgdbUnauthorized,
        SteamGridDbSearchState.RateLimited => LibraryStrings.Picker_SgdbRateLimited,
        SteamGridDbSearchState.Offline => st.Timeout ? LibraryStrings.Picker_SgdbTimeout : LibraryStrings.Picker_SgdbOffline,
        SteamGridDbSearchState.ServerError => LibraryStrings.Picker_SgdbServerError,
        SteamGridDbSearchState.BudgetExhausted => LibraryStrings.Picker_SgdbBudget,
        _ => null,
    };

    /// <summary>Text der Live-Ansage für einen Zustand.</summary>
    private string? AnnouncementFor(KindState st) => st.State == SteamGridDbSearchState.Results ? Status(st) ?? CountTextFor(st) : Status(st);

    private string? CountTextFor(KindState st) => st.Shown.Count switch
    {
        0 => null,
        1 => LibraryText.Format(LibraryStrings.Picker_SgdbResultsOne, Singular(st.Kind)),
        var n => LibraryText.Format(LibraryStrings.Picker_SgdbResults, n, Plural(st.Kind)),
    };

    private void SetState(KindState st, SteamGridDbSearchState state, bool force = false)
    {
        if (st.State == state && !force)
        {
            RaiseIfCurrent(st);
            return;
        }

        st.State = state;
        if (ReferenceEquals(st, Current))
        {
            Announcement = AnnouncementFor(st);
        }

        RaiseIfCurrent(st);
    }

    /// <summary>Fehlerart → Zustand (§7.2): die Kacheln bleiben, der Fehler steht darunter.</summary>
    private void SetError(KindState st, SteamGridDbException ex)
    {
        st.Error = ex.Kind;
        st.Timeout = ex.IsTimeout;
        var state = ex.Kind switch
        {
            SteamGridDbErrorKind.Unauthorized => SteamGridDbSearchState.Unauthorized,
            SteamGridDbErrorKind.RateLimited => SteamGridDbSearchState.RateLimited,
            SteamGridDbErrorKind.Network => SteamGridDbSearchState.Offline,
            SteamGridDbErrorKind.RequestLimit => SteamGridDbSearchState.BudgetExhausted,
            _ => SteamGridDbSearchState.ServerError,
        };
        SetState(st, state, force: true);
        if (state == SteamGridDbSearchState.RateLimited && _session?.BlockedUntil is { } until)
        {
            // „Erneut versuchen“ bleibt bis zum Ende der Pause gesperrt (ein Zeitgeber, kein Ticken).
            var wait = until - _context.Clock.UtcNow;
            st.RetryTimer?.Dispose();
            st.RetryTimer = _context.Ui.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, () =>
            {
                st.RetryTimer = null;
                RaiseIfCurrent(st);
                RetryCommand.RaiseCanExecuteChanged();
            });
        }

        RetryCommand.RaiseCanExecuteChanged();
        RaiseFocus(ShowRetry ? SteamGridDbFocusTarget.Retry : ShowSettingsLink ? SteamGridDbFocusTarget.SettingsLink : SteamGridDbFocusTarget.FirstResult);
    }

    private void ShowNoMatch(KindState st, bool steamUnknown)
    {
        st.SteamUnknown = steamUnknown;
        st.SearchedTerm = _game.Name;
        SetState(st, _hits.Count > 0 ? SteamGridDbSearchState.NoMatch : SteamGridDbSearchState.NoHits, force: true);
        IsMatchOpen = true;
        RaiseFocus(_hits.Count > 0 ? SteamGridDbFocusTarget.MatchList : SteamGridDbFocusTarget.MatchSearch);
    }

    /// <summary>Name und Jahr einer gemerkten Zuordnung, einmal beim ersten Klick. False: veraltete ID (404).</summary>
    private async Task<bool> DescribeAsync(ISteamGridDbSearchSession session, SteamGridDbMatch match, CancellationToken token)
    {
        if (match.Target.GameId is not { } id)
        {
            return true;
        }

        var hit = await session.DescribeAsync(id, token);
        if (hit is null)
        {
            return false;
        }

        _described = hit;
        if (_match is { Kind: SteamGridDbMatchKind.Remembered } current && current.Target == match.Target)
        {
            _match = current with { DisplayName = hit.Name, Year = hit.ReleaseYear, Verified = hit.Verified };
        }

        RebuildEntries();
        RaiseMatch();
        return true;
    }

    private bool Stale(CancellationToken token) => _closed || token.IsCancellationRequested;

    /// <summary>Einmal „antwortet langsam“ nach 4 s, solange die Anfrage läuft.</summary>
    private IDisposable StartSlow(KindState st, CancellationToken token) => _context.Ui.Delay(SlowAfter, () =>
    {
        if (!Stale(token) && st.State is SteamGridDbSearchState.Resolving or SteamGridDbSearchState.Loading && !st.Slow)
        {
            st.Slow = true;
            SetState(st, st.State, force: true);
        }
    });

    private void AddToBuffer(KindState st, SteamGridDbAssetPage page)
    {
        foreach (var asset in page.Items)
        {
            if (st.Seen.Add(asset.Id))
            {
                st.Buffer.Enqueue(asset);
            }
        }
    }

    /// <summary>
    /// Holt weitere Seiten, solange der Speicher leer ist und die Liste weitergeht: Eine Seite aus lauter verworfenen Einträgen
    /// heißt nicht, dass keine Bilder mehr kommen. Höchstens <see cref="MaxFilteredPages"/> je Klick (Budget der Suche).
    /// </summary>
    private async Task FetchPastFilteredAsync(KindState st, ISteamGridDbSearchSession session, SteamGridDbMatch match, ArtworkKind kind, CancellationToken token)
    {
        for (var i = 0; i < MaxFilteredPages && st.Buffer.Count == 0 && st.ServerHasMore && !Stale(token); i++)
        {
            var page = await session.GetAssetsAsync(match, kind, st.NextServerPage, token);
            if (Stale(token))
            {
                return;
            }

            st.NextServerPage++;
            st.ServerHasMore = page.HasMore;
            AddToBuffer(st, page);
        }
    }

    /// <summary>Zeigt die nächsten bis zu 24 Kacheln (Grenzen beachtet) und fragt ihre Vorschauen an.</summary>
    private void ShowNext(KindState st, SteamGridDbFocusTarget focus)
    {
        var current = ReferenceEquals(st, Current);
        var added = new List<ArtworkChoiceViewModel>();
        if (st.Pinned is { } pinned)
        {
            // Gewählt vor dem Spielwechsel: bleibt vorn in der neuen Liste und behält seine Vorschau.
            st.Pinned = null;
            st.Seen.Add(pinned.Asset!.Id);
            st.Shown.Insert(0, pinned);
            if (current)
            {
                Results.Insert(0, pinned);
            }
        }

        var width = DecodeWidth(st.Kind);
        if (current)
        {
            ReleaseHidden(EstimateBytes(st.Kind, width) * PageSize);
        }

        var room = Math.Min(PageSize, MaxTiles - st.Shown.Count);
        var fits = HeldBytes + (EstimateBytes(st.Kind, width) * Math.Min(room, st.Buffer.Count)) <= MaxHeldBytes;
        for (var i = 0; i < room && st.Buffer.Count > 0 && fits; i++)
        {
            var tile = ArtworkChoiceViewModel.Online(st.Buffer.Dequeue(), st.Kind);
            st.Shown.Add(tile);
            added.Add(tile);
            if (current)
            {
                Results.Add(tile);
            }
        }

        st.LimitReached = st.Shown.Count >= MaxTiles || !fits;
        st.Ended = st.Buffer.Count == 0 && !st.ServerHasMore;
        SetState(st, SteamGridDbSearchState.Results, force: true);
        QueuePreviews(st, st.Shown);
        var target = focus == SteamGridDbFocusTarget.FirstResult ? st.Shown.FirstOrDefault() : added.FirstOrDefault();
        if (current && target is not null)
        {
            RaiseFocus(focus, target);
        }
    }

    /// <summary>Vorschauen der Kacheln anfragen (nur im aktuellen Reiter), in sichtbarer Reihenfolge.</summary>
    private void QueuePreviews(KindState st, IEnumerable<ArtworkChoiceViewModel> tiles)
    {
        if (_closed || _session is not { } session || !ReferenceEquals(st, Current) || _picker.IsApplying)
        {
            return;
        }

        var queue = st.QueueToken();
        var width = DecodeWidth(st.Kind);
        var batch = 0;
        foreach (var tile in tiles)
        {
            if (tile.Asset is not { } asset || tile.PreviewState is PreviewState.Loaded or PreviewState.Loading or PreviewState.Failed)
            {
                continue;
            }

            if (asset.ThumbUrl is null)
            {
                tile.SetPreviewState(PreviewState.Missing);
                continue;
            }

            tile.SetPreviewState(PreviewState.Loading);
            batch++;
            _ = ObservePreviewAsync(st, tile, session.LoadPreviewAsync(asset, width, queue));
        }

        if (batch > 0)
        {
            if (st.BatchDone >= st.BatchTotal)
            {
                st.BatchTotal = 0;
                st.BatchDone = 0;
            }

            st.BatchTotal += batch;
            RaiseIfCurrent(st);
        }
    }

    /// <summary>Wartet auf eine Vorschau und setzt sie auf dem UI-Thread (der Aufrufer ist dort).</summary>
    private async Task ObservePreviewAsync(KindState st, ArtworkChoiceViewModel tile, Task<SteamGridDbPreview> task)
    {
        // WhenAny wirft nie; Fehler und Abbruch wertet OnPreview aus.
        await Task.WhenAny(task);
        OnPreview(st, tile, task);
    }

    private void OnPreview(KindState st, ArtworkChoiceViewModel tile, Task<SteamGridDbPreview> task)
    {
        if (_closed)
        {
            return; // späte Ergebnisse nach dem Schließen gelten nicht
        }

        if (!st.Shown.Contains(tile))
        {
            // Veraltet: Der Reiter wurde inzwischen zurückgesetzt (neue Zuordnung, Schlüssel entfernt); Zähler, Zustand und
            // Fehler gehören der neuen Liste. Eine mitgenommene Wahl behält ihre Vorschau, sonst fragt sie sie neu an.
            if (ReferenceEquals(tile, st.Pinned) && task.IsCompletedSuccessfully)
            {
                tile.SetPreview(task.Result.Image, task.Result.DecodedBytes);
            }
            else if (tile.PreviewState == PreviewState.Loading)
            {
                tile.SetPreviewState(PreviewState.None);
            }

            return;
        }

        if (task.IsCanceled)
        {
            // Reiter gewechselt: beim nächsten Zeigen neu anfragen.
            tile.SetPreviewState(PreviewState.None);
            st.BatchTotal = Math.Max(st.BatchDone, st.BatchTotal - 1);
        }
        else
        {
            st.BatchDone++;
            if (task.IsFaulted)
            {
                var limit = task.Exception?.InnerException is SteamGridDbException { Kind: SteamGridDbErrorKind.RateLimited or SteamGridDbErrorKind.RequestLimit } error ? error : null;
                if (limit is not null && !ReferenceEquals(st, Current))
                {
                    // Verborgener Reiter: kein Fehler dort (die Bitte um Fokus gälte dem sichtbaren); beim Zeigen neu angefragt.
                    tile.SetPreviewState(PreviewState.None);
                }
                else
                {
                    tile.SetPreviewState(PreviewState.Failed);
                    if (limit is not null && st.State == SteamGridDbSearchState.Results)
                    {
                        // Die Kacheln bleiben; Pause oder Grenze steht darunter, die übrigen Vorschauen scheitern sofort.
                        st.Retry = RetryAction.Previews;
                        SetError(st, limit);
                    }
                }
            }
            else if (st.Shown.Contains(tile))
            {
                tile.SetPreview(task.Result.Image, task.Result.DecodedBytes);
            }
        }

        if (st.BatchTotal > 0 && st.BatchDone >= st.BatchTotal && ReferenceEquals(st, Current))
        {
            // Einmal je Seite: die Vorschauen sind da (nie je Vorschau).
            Announcement = LibraryText.Format(LibraryStrings.Picker_SgdbPreviewProgress, st.BatchDone, st.BatchTotal);
            st.BatchTotal = 0;
            st.BatchDone = 0;
        }

        RaiseIfCurrent(st);
    }

    /// <summary>
    /// Hält der Speicher (mit <paramref name="extra"/> für die nächste Seite) nicht, geben verborgene Reiter ihre Vorschauen
    /// frei, der am längsten nicht gezeigte zuerst; gewählte Kacheln nie.
    /// </summary>
    private void ReleaseHidden(long extra)
    {
        var chosen = _picker.ChosenItems.ToHashSet();
        foreach (var hidden in _states.Values.Where(s => !ReferenceEquals(s, Current)).OrderBy(s => s.LastShown))
        {
            if (HeldBytes + extra <= MaxHeldBytes)
            {
                return;
            }

            foreach (var tile in hidden.Shown.Where(t => t.Preview is not null && !chosen.Contains(t)))
            {
                tile.ReleasePreview();
            }
        }
    }

    /// <summary>Neue Zuordnung: alle Reiter zurück; die Wahl des aktuellen Reiters bleibt, die anderen fallen auf ihre Standardwahl.</summary>
    private void ResetForNewMatch()
    {
        var reset = false;
        foreach (var st in _states.Values)
        {
            st.CancelOp();
            st.CancelQueue();
            var chosen = _picker.ChosenFor(st.Kind);
            st.Pinned = null;
            if (chosen is { IsOnline: true })
            {
                if (st.Kind == Kind)
                {
                    st.Pinned = chosen;
                }
                else
                {
                    _picker.ResetToDefault(st.Kind);
                    reset = true;
                }
            }

            foreach (var tile in st.Shown.Where(t => !ReferenceEquals(t, st.Pinned)))
            {
                tile.ReleasePreview();
            }

            st.Shown.Clear();
            st.Buffer.Clear();
            st.Seen.Clear();
            st.NextServerPage = 0;
            st.ServerHasMore = true;
            st.Ended = false;
            st.LimitReached = false;
            st.Error = null;
            st.Timeout = false;
            st.BatchDone = 0;
            st.BatchTotal = 0;
            if (st.State is not (SteamGridDbSearchState.NoKey or SteamGridDbSearchState.KeyUnreadable))
            {
                st.State = SteamGridDbSearchState.Ready;
            }
        }

        Results.Clear();
        if (reset)
        {
            _picker.ShowInfo(LibraryStrings.Picker_SgdbPicksReset);
        }

        RaiseAll();
    }

    private void RebuildEntries()
    {
        MatchEntries.Clear();
        if (_initialMatch is { } initial && initial.Kind is SteamGridDbMatchKind.SteamAppId or SteamGridDbMatchKind.Remembered)
        {
            var name = initial.Kind == SteamGridDbMatchKind.Remembered && _described is { } described ? described.Name : _game.Name;
            var text = initial.Kind == SteamGridDbMatchKind.SteamAppId
                ? LibraryText.Format(LibraryStrings.Picker_SgdbSteamEntry, name)
                : LibraryText.Format(LibraryStrings.Picker_SgdbRememberedEntry, name);
            MatchEntries.Add(new SteamGridDbMatchEntry(text, text, false, null, initial.Target));
        }

        foreach (var hit in _hits.Take(SteamGridDbClient.MaxHits))
        {
            if (_initialMatch?.Target.GameId == hit.Id)
            {
                continue;
            }

            var spoken = hit.ReleaseYear is { } year ? LibraryText.Format(LibraryStrings.Picker_SgdbHitName, hit.Name, year) : hit.Name;
            if (hit.Verified)
            {
                spoken = LibraryText.Format(LibraryStrings.Picker_SgdbHitNameVerified, spoken);
            }

            MatchEntries.Add(new SteamGridDbMatchEntry(Label(hit.Name, hit.ReleaseYear), spoken, hit.Verified, hit, SteamGridDbTarget.Game(hit.Id)));
        }

        foreach (var entry in MatchEntries)
        {
            entry.IsCurrent = _match is { } match && entry.Target == match.Target;
        }
    }

    private void OnServiceChanged(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        var key = SafeKeyState();
        if (key != SteamGridDbKeyState.Stored)
        {
            // Schlüssel anderswo entfernt (Einstellungen): der Dienst hat die Sitzung schon abgebrochen.
            if (_session is not null)
            {
                _session.Dispose();
                _session = null;
            }

            // Eine Wahl von SteamGridDB ließe sich ohne Schlüssel nicht mehr laden („Übernehmen“ täte still nichts): Die
            // Reiter fallen mit einem Hinweis auf ihre Standardwahl zurück.
            var reset = false;
            foreach (var kind in Kinds)
            {
                if (_picker.ChosenFor(kind) is { IsOnline: true })
                {
                    _picker.ResetToDefault(kind);
                    reset = true;
                }
            }

            _keyReady = false;
            var state = StateFor(key);
            foreach (var st in _states.Values)
            {
                st.CancelOp();
                st.CancelQueue();
                st.Pinned = null;
                if (st.State != state)
                {
                    st.State = state;
                    st.Shown.Clear();
                    st.Buffer.Clear();
                    st.Seen.Clear();
                }
            }

            Results.Clear();
            IsMatchOpen = false;
            if (reset)
            {
                _picker.ShowInfo(LibraryStrings.Picker_SgdbPicksResetNoKey);
            }

            Announcement = Status(Current);
            RaiseAll();
            return;
        }

        if (_states.Values.Any(s => s.State is SteamGridDbSearchState.NoKey or SteamGridDbSearchState.KeyUnreadable))
        {
            // Schlüssel gespeichert: bereit; die Sitzung entsteht erst mit dem nächsten Klick.
            _keyReady = true;
            if (_session is null)
            {
                _initialMatch = _context.SteamGridDb.PeekMatch(_game);
                _match ??= _initialMatch;
            }

            foreach (var st in _states.Values.Where(s => s.State is SteamGridDbSearchState.NoKey or SteamGridDbSearchState.KeyUnreadable))
            {
                st.State = SteamGridDbSearchState.Ready;
            }

            RebuildEntries();
            Announcement = Status(Current);
            RaiseAll();
        }
    }

    private void RaiseFocus(SteamGridDbFocusTarget target, ArtworkChoiceViewModel? item = null)
    {
        if (!_closed)
        {
            FocusRequest?.Invoke(this, new SteamGridDbFocusRequestEventArgs(target, item));
        }
    }

    private void RaiseIfCurrent(KindState st)
    {
        if (ReferenceEquals(st, Current))
        {
            RaiseAll();
        }
    }

    private void RaiseMatch()
    {
        OnPropertyChanged(nameof(MatchText));
        OnPropertyChanged(nameof(MatchToolTip));
        OnPropertyChanged(nameof(MatchAutomationName));
        OnPropertyChanged(nameof(IsMatchAmbiguous));
        foreach (var entry in MatchEntries)
        {
            entry.IsCurrent = _match is { } match && entry.Target == match.Target;
        }
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(State), nameof(StatusText), nameof(StatusTone), nameof(HasStatus), nameof(ShowResults), nameof(ShowSkeleton),
            nameof(ShowSearchButton), nameof(IsSearchBusy), nameof(SearchButtonText), nameof(SearchItemStatus), nameof(SearchAutomationName),
            nameof(MoreAutomationName), nameof(ShowHeader), nameof(ShowReadyNote), nameof(ShowSettingsLink), nameof(SettingsLinkText),
            nameof(ShowRetry), nameof(CanRetry), nameof(RetryToolTip), nameof(ShowMore), nameof(IsMoreBusy), nameof(CountText),
            nameof(ProgressText), nameof(ShowCommitHint),
        })
        {
            OnPropertyChanged(name);
        }

        RaiseMatch();
        MoreCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        SearchGamesCommand.RaiseCanExecuteChanged();
        ToggleMatchCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Was „Erneut versuchen“ wiederholt.</summary>
    private enum RetryAction
    {
        Search,
        More,
        ChooserSearch,
        Previews,
    }

    /// <summary>Stand eines Reiters.</summary>
    private sealed class KindState(ArtworkKind kind)
    {
        private CancellationTokenSource? _op;
        private CancellationTokenSource? _queue;

        public ArtworkKind Kind { get; } = kind;

        public SteamGridDbSearchState State { get; set; }

        public SteamGridDbErrorKind? Error { get; set; }

        public bool Timeout { get; set; }

        public bool Slow { get; set; }

        public bool SteamUnknown { get; set; }

        public string? SearchedTerm { get; set; }

        public RetryAction Retry { get; set; }

        public IDisposable? RetryTimer { get; set; }

        public List<ArtworkChoiceViewModel> Shown { get; } = [];

        public Queue<SteamGridDbAsset> Buffer { get; } = new();

        public HashSet<long> Seen { get; } = [];

        public ArtworkChoiceViewModel? Pinned { get; set; }

        public int NextServerPage { get; set; }

        public bool ServerHasMore { get; set; } = true;

        public bool Ended { get; set; }

        public bool LimitReached { get; set; }

        public long LastShown { get; set; }

        public int BatchTotal { get; set; }

        public int BatchDone { get; set; }

        /// <summary>Eine Seite der Schnittstelle lädt (sperrt „Mehr laden“).</summary>
        public bool IsBusy => _op is not null;

        public CancellationToken StartOp()
        {
            _op = new CancellationTokenSource();
            Slow = false;
            return _op.Token;
        }

        public void EndOp(CancellationToken token)
        {
            if (_op is { } op && op.Token == token)
            {
                _op = null;
                op.Dispose();
            }

            Slow = false;
        }

        public void CancelOp()
        {
            var op = _op;
            _op = null;
            op?.Cancel();
            op?.Dispose();
            Slow = false;
        }

        public CancellationToken QueueToken() => (_queue ??= new CancellationTokenSource()).Token;

        public void CancelQueue()
        {
            var queue = _queue;
            _queue = null;
            queue?.Cancel();
            queue?.Dispose();
        }
    }
}
