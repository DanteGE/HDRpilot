using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Eine Wahl im Dialog „Bild wählen…“ (Launcher-Bild, anderes lokales Bild, erzeugt, eigene Datei oder, seit E93, ein Bild
/// von SteamGridDB). Kacheln von SteamGridDB haben kein Kennzeichen (<see cref="Token"/> ist null); ihr volles Bild lädt
/// erst „Übernehmen“ (<see cref="PendingData"/>) und wird dann wie eine eigene Datei gespeichert.
/// </summary>
public sealed class ArtworkChoiceViewModel : ObservableObject
{
    private bool _isSelected;
    private ImageSource? _preview;
    private PreviewState _previewState;

    internal ArtworkChoiceViewModel(string? token, string label, string? note, ImageSource? preview, string sizeText, bool isCurrent, string? pendingFile = null,
        ArtworkKind kind = ArtworkKind.Hero, SteamGridDbAsset? asset = null)
    {
        Token = token;
        Label = label;
        Note = note;
        _preview = preview;
        SizeText = sizeText;
        IsCurrent = isCurrent;
        PendingFile = pendingFile;
        Kind = kind;
        Asset = asset;
        if (asset is not null)
        {
            Author = asset.Author;
            _previewState = asset.ThumbUrl is null ? PreviewState.Missing : PreviewState.None;
        }
    }

    /// <summary>Kennzeichen für die Konfiguration (null: automatisch, siehe <see cref="ArtworkOverride"/>; auch null für SteamGridDB).</summary>
    public string? Token { get; }

    /// <summary>Erste Zeile der Kachel: Beschriftung, bei SteamGridDB „von &lt;Autor&gt;“ bzw. „Autor unbekannt“.</summary>
    public string Label { get; }

    public string? Note { get; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// <summary>Zweite Zeile der Kachel: die Größe, sonst der Hinweis.</summary>
    public string SecondLine => !string.IsNullOrEmpty(SizeText) ? SizeText : Note ?? string.Empty;

    public ImageSource? Preview
    {
        get => _preview;
        private set
        {
            if (SetProperty(ref _preview, value))
            {
                OnPropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => _preview is not null;

    /// <summary>„1920 × 1080“ (leer ohne bekannte Größe; bei SteamGridDB nur für ungewöhnliche Größen, Logos immer).</summary>
    public string SizeText { get; }

    /// <summary>Aktuell gezeigtes Bild (Kennzeichen „AKTUELL“).</summary>
    public bool IsCurrent { get; }

    /// <summary>Eben gewählte eigene Datei, die erst beim Übernehmen gespeichert wird.</summary>
    public string? PendingFile { get; }

    /// <summary>Bildart der Kachel (Form der Kachel).</summary>
    public ArtworkKind Kind { get; }

    /// <summary>Bild von SteamGridDB (E93).</summary>
    public bool IsOnline => Asset is not null;

    public SteamGridDbAsset? Asset { get; }

    /// <summary>Autor bei SteamGridDB (bereinigt) oder null.</summary>
    public string? Author { get; }

    public string AuthorText => Author is { } author ? LibraryText.Format(LibraryStrings.Picker_SgdbAuthor, author) : LibraryStrings.Picker_SgdbAuthorUnknown;

    public PreviewState PreviewState
    {
        get => _previewState;
        private set
        {
            if (SetProperty(ref _previewState, value))
            {
                OnPropertyChanged(nameof(ShowPlaceholderText));
                OnPropertyChanged(nameof(PlaceholderText));
            }
        }
    }

    /// <summary>Platzhalter unter dem Symbol: „Vorschau nicht geladen“ oder „Keine Vorschau“.</summary>
    public string? PlaceholderText => _previewState switch
    {
        PreviewState.Failed => LibraryStrings.Picker_SgdbPreviewFailed,
        PreviewState.Missing => LibraryStrings.Picker_SgdbNoPreview,
        _ => null,
    };

    public bool ShowPlaceholderText => PlaceholderText is not null;

    /// <summary>Speicher der dekodierten Vorschau (Breite × Höhe × 4), 0 ohne.</summary>
    public long DecodedBytes { get; private set; }

    /// <summary>Größe zeigen (bei SteamGridDB nur, wenn sie von der üblichen der Bildart abweicht; Logos immer).</summary>
    public bool ShowSize => !string.IsNullOrEmpty(SizeText);

    /// <summary>Name für Bildschirmleser: lokal die Beschriftung, bei SteamGridDB „Cover von Testautor 03, 600 × 900 Pixel“.</summary>
    public string AutomationName
    {
        get
        {
            if (Asset is not { } asset)
            {
                return Label;
            }

            var kind = Kind switch
            {
                ArtworkKind.Logo => LibraryStrings.Picker_TabLogo,
                ArtworkKind.Cover => LibraryStrings.Picker_TabCover,
                _ => LibraryStrings.Picker_TabHero,
            };
            var name = Author is { } author
                ? LibraryText.Format(LibraryStrings.Picker_SgdbItemName, kind, author)
                : LibraryText.Format(LibraryStrings.Picker_SgdbItemNameNoAuthor, kind);
            return asset.Width > 0 && asset.Height > 0 ? LibraryText.Format(LibraryStrings.Picker_SgdbItemSize, name, asset.Width, asset.Height) : name;
        }
    }

    public string? AutomationHelp => IsOnline ? LibraryStrings.Picker_SgdbItemHelp : Note;

    /// <summary>Volles Bild von SteamGridDB, geladen bei „Übernehmen“, gespeichert danach als eigenes Bild.</summary>
    internal byte[]? PendingData { get; set; }

    internal string? PendingExtension { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Kachel für ein Bild von SteamGridDB.</summary>
    internal static ArtworkChoiceViewModel Online(SteamGridDbAsset asset, ArtworkKind kind)
    {
        var standard = kind switch
        {
            ArtworkKind.Cover => (600, 900),
            ArtworkKind.Hero => (1920, 620),
            _ => (-1, -1),
        };
        var known = asset.Width > 0 && asset.Height > 0;
        var size = known && (asset.Width, asset.Height) != standard ? LibraryText.Format(LibraryStrings.Picker_Size, asset.Width, asset.Height) : string.Empty;
        var label = asset.Author is { } author ? LibraryText.Format(LibraryStrings.Picker_SgdbAuthor, author) : LibraryStrings.Picker_SgdbAuthorUnknown;
        return new ArtworkChoiceViewModel(null, label, null, null, size, isCurrent: false, kind: kind, asset: asset);
    }

    internal void SetPreview(ImageSource image, long decodedBytes)
    {
        DecodedBytes = decodedBytes;
        Preview = image;
        PreviewState = PreviewState.Loaded;
    }

    /// <summary>Vorschau freigeben (verborgener Reiter, Dialog zu); beim nächsten Zeigen wird sie neu angefragt.</summary>
    internal void ReleasePreview()
    {
        if (!IsOnline)
        {
            return;
        }

        DecodedBytes = 0;
        Preview = null;
        PreviewState = Asset!.ThumbUrl is null ? PreviewState.Missing : PreviewState.None;
    }

    internal void SetPreviewState(PreviewState state)
    {
        if (state != PreviewState.Loaded)
        {
            PreviewState = state;
        }
    }

    public override string ToString() => Label;
}

/// <summary>Ergebnis von <see cref="ArtworkPickerViewModel.ApplyWithOutcomeAsync"/>.</summary>
public enum ApplyOutcome
{
    Applied,

    /// <summary>Nichts zu schreiben (Wahl unverändert).</summary>
    Unchanged,

    Failed,
}

/// <summary>
/// Dialog „Bild wählen…“ (E89, G10.3, E93): je Bildart (Titelbild, Logo, Cover) die Möglichkeiten des Bilddienstes, dazu
/// „Eigene Datei…“ und der Abschnitt SteamGridDB (<see cref="Online"/>). Je Reiter gilt genau eine Wahl, aus der lokalen
/// oder der Online-Liste; sie bleibt beim Umschalten der Reiter erhalten. „Übernehmen“ lädt gewählte Bilder von SteamGridDB
/// noch im offenen Dialog (<see cref="PrepareApplyAsync"/>), speichert danach eigene Dateien und Bilder verkleinert im
/// Datenordner (außerhalb des UI-Threads, die Quelle bleibt unverändert), schreibt die Wahl in
/// <see cref="GameConfig.Artwork"/> und löscht ersetzte eigene Dateien dieses Spiels. „Zurücksetzen“ wählt „Automatisch“.
/// Dispose betrifft nur den Online-Teil; die lokale Wahl bleibt danach bedienbar.
/// </summary>
public sealed class ArtworkPickerViewModel : ObservableObject, IDisposable
{
    private static readonly ArtworkKind[] Kinds = [ArtworkKind.Hero, ArtworkKind.Logo, ArtworkKind.Cover];

    private readonly LibraryViewModel _owner;
    private readonly Dictionary<ArtworkKind, ArtworkChoiceViewModel?> _chosen = [];
    private readonly Dictionary<ArtworkKind, IReadOnlyList<ArtworkChoiceViewModel>> _loaded = [];
    private ArtworkKind _kind;
    private bool _isLoading;
    private string? _message;
    private StatusKind _messageKind = StatusKind.Error;
    private int _loadRun;
    private bool _isApplying;
    private string? _busyText;
    private CancellationTokenSource? _applyCancel;
    private bool _onlineClosed;

    internal ArtworkPickerViewModel(LibraryViewModel owner, GameItemViewModel item, ArtworkKind kind)
    {
        _owner = owner;
        Item = item;
        _kind = kind;
        PickFileCommand = new AsyncRelayCommand(PickFileAsync, () => !_isApplying);
        ResetCommand = new RelayCommand(Reset, () => !_isApplying);
        Online = new SteamGridDbSearchViewModel(this, owner.Context, item.Config);
    }

    /// <summary>„Zu den Einstellungen“: der Dialog soll schließen (ohne Änderungen).</summary>
    public event EventHandler? CloseRequested;

    public GameItemViewModel Item { get; }

    /// <summary>„Clive Barker's Hellraiser: Revival · Xbox“.</summary>
    public string Subtitle => LibraryText.Format(LibraryStrings.Picker_Subtitle, Item.Name, Item.SourceName);

    /// <summary>Herkunft der lokalen Bilder (Zeile unter „AUF DIESEM PC“).</summary>
    public string SourceNote => LibraryStrings.Picker_SourceNote;

    /// <summary>Abschnitt „STEAMGRIDDB“ (E93).</summary>
    public SteamGridDbSearchViewModel Online { get; }

    /// <summary>„Übernehmen“ lädt gerade Bilder von SteamGridDB: alles außer „Abbrechen“ ist gesperrt.</summary>
    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            if (SetProperty(ref _isApplying, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                PickFileCommand.RaiseCanExecuteChanged();
                ResetCommand.RaiseCanExecuteChanged();
                Online.RaiseCommands();
            }
        }
    }

    /// <summary>Gegenteil von <see cref="IsApplying"/> (für IsEnabled in der Ansicht).</summary>
    public bool CanEdit => !_isApplying;

    /// <summary>„Lädt das gewählte Bild von SteamGridDB …“ während <see cref="IsApplying"/>.</summary>
    public string? BusyText
    {
        get => _busyText;
        private set
        {
            if (SetProperty(ref _busyText, value))
            {
                OnPropertyChanged(nameof(HasBusyText));
            }
        }
    }

    public bool HasBusyText => !string.IsNullOrEmpty(_busyText);

    /// <summary>Weicht eine Wahl von der gespeicherten ab (auch eine neue Datei oder ein Bild von SteamGridDB)?</summary>
    public bool HasChanges => Kinds.Any(kind => _chosen.GetValueOrDefault(kind) is { } chosen
        && (chosen.IsOnline || chosen.PendingFile is not null || chosen.PendingData is not null
            || !string.Equals(chosen.Token, TokenOf(Item.Config.Artwork, kind), StringComparison.Ordinal)));

    /// <summary>Der Dialog schloss über „Zu den Einstellungen“.</summary>
    public bool SettingsRequested { get; private set; }

    /// <summary>Gewählte Kacheln aller Reiter.</summary>
    internal IEnumerable<ArtworkChoiceViewModel> ChosenItems => _chosen.Values.OfType<ArtworkChoiceViewModel>();

    public ArtworkKind Kind
    {
        get => _kind;
        set
        {
            var previous = _kind;
            if ((value is ArtworkKind.Hero or ArtworkKind.Logo or ArtworkKind.Cover) && SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(IsHero));
                OnPropertyChanged(nameof(IsLogo));
                OnPropertyChanged(nameof(IsCover));

                // Die Online-Liste wechselt mit der lokalen; eine Wahl geht nie in einen anderen Reiter über (E89).
                Online.OnKindChanged(previous);
                _ = LoadAsync();
            }
        }
    }

    public bool IsHero
    {
        get => _kind == ArtworkKind.Hero;
        set
        {
            if (value)
            {
                Kind = ArtworkKind.Hero;
            }
        }
    }

    public bool IsLogo
    {
        get => _kind == ArtworkKind.Logo;
        set
        {
            if (value)
            {
                Kind = ArtworkKind.Logo;
            }
        }
    }

    public bool IsCover
    {
        get => _kind == ArtworkKind.Cover;
        set
        {
            if (value)
            {
                Kind = ArtworkKind.Cover;
            }
        }
    }

    public ObservableCollection<ArtworkChoiceViewModel> Candidates { get; } = [];

    /// <summary>
    /// Gewählte Möglichkeit im aktuellen Reiter, aus der lokalen oder der Online-Liste (genau eine je Reiter). Null wird
    /// übergangen: Die Liste, die die Wahl nicht enthält, meldet null zurück.
    /// </summary>
    public ArtworkChoiceViewModel? Selected
    {
        get => _chosen.GetValueOrDefault(_kind);
        set
        {
            // Nur Möglichkeiten des aktuellen Reiters (während ein anderer Reiter lädt, gehört die alte Liste nicht dazu).
            if (value is null || ReferenceEquals(value, Selected) || _isApplying || !(Candidates.Contains(value) || Online.Results.Contains(value)))
            {
                return;
            }

            _chosen[_kind] = value;
            MarkSelection(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChanges));
            Online.RaiseCommands();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Fehler beim Lesen oder Speichern (etwa „Die Datei ist kein lesbares Bild.“) oder ein Hinweis.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    /// <summary>Ton der Meldung (Fehler, oder Hinweis wie „Deine Wahl … gilt nicht mehr“).</summary>
    public StatusKind MessageKind
    {
        get => _messageKind;
        private set => SetProperty(ref _messageKind, value);
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>„Eigene Datei…“: Bild wählen (PNG, JPG, WebP, BMP); gespeichert wird erst beim Übernehmen.</summary>
    public AsyncRelayCommand PickFileCommand { get; }

    /// <summary>„Zurücksetzen“: im aktuellen Reiter wieder automatisch.</summary>
    public RelayCommand ResetCommand { get; }

    /// <summary>Möglichkeiten des aktuellen Reiters laden (im Hintergrund, Vorschau klein dekodiert).</summary>
    public async Task LoadAsync()
    {
        var kind = _kind;
        var run = ++_loadRun;
        if (_loaded.TryGetValue(kind, out var known))
        {
            Show(known);
            return;
        }

        // Die Liste des vorigen Reiters nicht stehen lassen: eine Wahl darin gälte sonst für die neue Bildart.
        Candidates.Clear();
        IsLoading = true;
        IReadOnlyList<ArtworkCandidate> candidates;
        try
        {
            candidates = await _owner.Context.Artwork.GetCandidatesAsync(Item.Config, kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            candidates = [];
        }

        var current = TokenOf(Item.Config.Artwork, kind);
        var list = candidates.Select(c => new ArtworkChoiceViewModel(c.Token, c.Label, c.Note, c.Preview, SizeOf(c.PixelWidth, c.PixelHeight), c.IsCurrent, kind: kind)).ToList();
        if (!list.Any(c => c.Token is null))
        {
            // Ohne Angebot des Bilddienstes gibt es immer „Automatisch“.
            list.Insert(0, new ArtworkChoiceViewModel(null, LibraryStrings.Picker_Automatic, LibraryStrings.Picker_AutomaticNote, null, string.Empty, current is null, kind: kind));
        }

        // Eine eigene Datei, die schon gewählt wurde, während die Liste noch lud, bleibt in der Liste (und gewählt); sonst
        // ersetzte die fertige Liste sie stillschweigend (Prüfung 2026-10-20).
        if (_loaded.GetValueOrDefault(kind)?.FirstOrDefault(c => c.PendingFile is not null) is { } pending)
        {
            list.Add(pending);
        }

        _loaded[kind] = list;
        if (run == _loadRun)
        {
            IsLoading = false;
            Show(list);
        }
    }

    /// <summary>Gewählte Bilder übernehmen. False, wenn nichts geändert wurde oder Speichern scheiterte (Hinweis in der Bibliothek).</summary>
    public async Task<bool> ApplyAsync() => await ApplyWithOutcomeAsync() == ApplyOutcome.Applied;

    /// <summary>
    /// „Übernehmen“ im offenen Dialog: lädt jedes gewählte Bild von SteamGridDB (nacheinander), solange der Dialog offen ist.
    /// Wirft nie. False bei Abbruch (Dialog schließt) oder Fehler: Dann bleibt der Dialog im betroffenen Reiter offen, mit
    /// Meldung, der Wahl und schon geladenen Bildern.
    /// </summary>
    public async Task<bool> PrepareApplyAsync()
    {
        var picks = Kinds.Select(k => (Kind: k, Choice: _chosen.GetValueOrDefault(k)))
            .Where(p => p.Choice is { IsOnline: true, PendingData: null })
            .ToList();
        if (picks.Count == 0)
        {
            return true;
        }

        if (_isApplying || _onlineClosed)
        {
            return false;
        }

        Message = null;
        BusyText = picks.Count == 1 ? LibraryStrings.Picker_SgdbDownloading : LibraryText.Format(LibraryStrings.Picker_SgdbDownloadingMany, picks.Count);
        IsApplying = true;
        Online.PauseQueue();
        var cancel = _applyCancel = new CancellationTokenSource();
        try
        {
            foreach (var (kind, choice) in picks)
            {
                try
                {
                    var data = await Online.DownloadAsync(choice!.Asset!, kind, cancel.Token);
                    if (cancel.IsCancellationRequested || _onlineClosed)
                    {
                        return false;
                    }

                    choice.PendingData = data.Data;
                    choice.PendingExtension = data.Extension;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    // Jeder andere Fehler (auch ein kaputter Bildkopf als FileFormatException, fehlende Rechte) wird zur
                    // Meldung im Dialog; aus dem async-void-Handler von „Übernehmen“ entkäme er sonst als „Unerwarteter Fehler“.
                    if (_onlineClosed)
                    {
                        return false;
                    }

                    MessageKind = StatusKind.Error;
                    Message = DownloadMessage(ex);
                    BusyText = null;
                    IsApplying = false;
                    Kind = kind;
                    Online.ResumeQueue();
                    Online.RequestTileFocus(choice!);
                    return false;
                }
            }

            return true;
        }
        finally
        {
            if (ReferenceEquals(_applyCancel, cancel))
            {
                _applyCancel = null;
            }

            cancel.Dispose();
            if (_isApplying)
            {
                BusyText = null;
                IsApplying = false;
            }
        }
    }

    /// <summary>
    /// Speichert die Wahl (nach dem Schließen des Dialogs): <see cref="ApplyOutcome.Applied"/>, wo <see cref="ApplyAsync"/>
    /// true lieferte, <see cref="ApplyOutcome.Unchanged"/> ohne etwas zu schreiben, sonst <see cref="ApplyOutcome.Failed"/>.
    /// Bilder von SteamGridDB gehen wie eigene Dateien in art\custom; eine Online-Wahl ohne geladene Daten bleibt unverändert.
    /// </summary>
    internal async Task<ApplyOutcome> ApplyWithOutcomeAsync()
    {
        var config = Item.Config;
        var before = config.Artwork;
        var choice = new GameArtworkChoice { Hero = before?.Hero, Logo = before?.Logo, Cover = before?.Cover };
        var saved = new List<string>();
        var artwork = _owner.Context.Artwork;
        foreach (var (kind, chosen) in _chosen)
        {
            if (chosen is null)
            {
                continue;
            }

            var token = chosen.Token;
            if (chosen.IsOnline && chosen.PendingData is null)
            {
                // Schutz: ein Bild von SteamGridDB ohne geladene Daten wird nie als „Automatisch“ (Kennzeichen null) geschrieben.
                continue;
            }

            if (chosen.PendingFile is not null || chosen.PendingData is not null)
            {
                var file = chosen.PendingFile;
                var data = chosen.PendingData;
                try
                {
                    token = await Task.Run(() => data is not null ? artwork.SaveCustomImageData(config, kind, data) : artwork.SaveCustomImage(config, kind, file!));
                    saved.Add(token);
                }
                catch (ImageTooLargeException)
                {
                    DeleteQuietly(Unreferenced(saved, before));
                    _owner.ShowStatus(LibraryStrings.Picker_TooLarge, StatusKind.Error);
                    return ApplyOutcome.Failed;
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or FormatException or ArgumentException or System.Runtime.InteropServices.ExternalException)
                {
                    DeleteQuietly(Unreferenced(saved, before));
                    _owner.ShowStatus(LibraryStrings.Picker_NotAnImage, StatusKind.Error);
                    return ApplyOutcome.Failed;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    DeleteQuietly(Unreferenced(saved, before));
                    _owner.ShowStatus(LibraryText.Format(LibraryStrings.Picker_SaveFailed, ex.Message), StatusKind.Error);
                    return ApplyOutcome.Failed;
                }
            }

            Set(choice, kind, token);
        }

        var normalized = GameArtworkChoice.Normalize(choice);
        if (SameChoice(before, normalized))
        {
            // Neue eigene Datei für eine Art, die schon eine eigene hatte: Der Bilddienst legt sie unter demselben Namen ab,
            // das Kennzeichen bleibt gleich und das neue Bild gilt schon. Nichts zu schreiben, aber übernommen.
            if (saved.Count == 0)
            {
                return ApplyOutcome.Unchanged;
            }

            _owner.ShowStatus(LibraryText.Format(LibraryStrings.Picker_Applied, Item.Name), StatusKind.Success);
            return ApplyOutcome.Applied;
        }

        var written = await _owner.WriteGameAsync(Item.Key, g => g.Artwork = normalized is null ? null : new GameArtworkChoice { Hero = normalized.Hero, Logo = normalized.Logo, Cover = normalized.Cover });
        if (!written)
        {
            DeleteQuietly(Unreferenced(saved, before));
            return ApplyOutcome.Failed;
        }

        // Ersetzte eigene Dateien dieses Spiels löschen (der Bilddienst löscht nur eigene „custom:“-Dateien).
        foreach (var kind in new[] { ArtworkKind.Hero, ArtworkKind.Logo, ArtworkKind.Cover })
        {
            var old = TokenOf(before, kind);
            if (old is not null && ArtworkOverride.KindOf(old) == ArtworkOverrideKind.Custom && !string.Equals(old, TokenOf(normalized, kind), StringComparison.Ordinal))
            {
                DeleteQuietly([old]);
            }
        }

        _owner.ShowStatus(LibraryText.Format(LibraryStrings.Picker_Applied, Item.Name), StatusKind.Success);
        return ApplyOutcome.Applied;
    }

    /// <summary>
    /// Nach dem Übernehmen: eine vom Nutzer geänderte Zuordnung eines Spiels ohne Steam-App-ID merken (nie bei Steam-Spielen,
    /// nach Abbrechen oder einem Fehler). Auch ohne geändertes Bild; dann meldet die Statuszeile das Merken. Das Merken
    /// (Bilder der alten Zuordnung löschen, Verzeichnis schreiben) läuft im Hintergrund; die Meldung folgt danach.
    /// </summary>
    public async Task CommitMatchAsync(ApplyOutcome outcome)
    {
        if (outcome == ApplyOutcome.Failed || !Online.MatchChangedByUser || Online.IsSteamGame || Online.Match?.Target.GameId is not { } id)
        {
            return;
        }

        var service = _owner.Context.SteamGridDb;
        var game = Item.Config;
        await Task.Run(() => service.RememberMatch(game, id));
        if (outcome == ApplyOutcome.Unchanged)
        {
            _owner.ShowStatus(LibraryText.Format(LibraryStrings.Picker_MatchRemembered, Item.Name), StatusKind.Success);
        }
    }

    /// <summary>
    /// Dialog geschlossen: nur der Online-Teil endet (Sitzung, Vorschauen nicht gewählter Kacheln); gewählte Kacheln behalten
    /// ihre Daten für <see cref="ApplyWithOutcomeAsync"/>. Ein laufendes Laden beim Übernehmen bricht ab.
    /// </summary>
    public void CloseSession()
    {
        if (_onlineClosed)
        {
            return;
        }

        _onlineClosed = true;
        try
        {
            _applyCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Laden eben zu Ende.
        }

        Online.Close(_chosen.Values);
    }

    /// <summary>Gibt den Online-Teil frei (und die geladenen Daten); lokale Funktionen bleiben bedienbar.</summary>
    public void Dispose()
    {
        CloseSession();
        foreach (var chosen in _chosen.Values.OfType<ArtworkChoiceViewModel>())
        {
            chosen.PendingData = null;
            chosen.PendingExtension = null;
        }
    }

    /// <summary>Wahl eines Reiters (auch eines verborgenen).</summary>
    internal ArtworkChoiceViewModel? ChosenFor(ArtworkKind kind) => _chosen.GetValueOrDefault(kind);

    /// <summary>Ein Reiter fällt auf seine Standardwahl zurück (aktuell, sonst automatisch, sonst die erste).</summary>
    internal void ResetToDefault(ArtworkKind kind)
    {
        if (_loaded.GetValueOrDefault(kind) is { } list)
        {
            _chosen[kind] = DefaultChoice(list);
        }
        else
        {
            _chosen.Remove(kind);
        }

        if (kind == _kind)
        {
            MarkSelection(_chosen.GetValueOrDefault(kind));
            OnPropertyChanged(nameof(Selected));
        }

        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>Hinweis in der Meldungszeile (Ton „Info“).</summary>
    internal void ShowInfo(string text)
    {
        MessageKind = StatusKind.Info;
        Message = text;
    }

    /// <summary>„Zu den Einstellungen“: Dialog ohne Änderungen schließen.</summary>
    internal void RequestSettings()
    {
        SettingsRequested = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    internal static string? TokenOf(GameArtworkChoice? choice, ArtworkKind kind) => kind switch
    {
        ArtworkKind.Hero => choice?.Hero,
        ArtworkKind.Logo => choice?.Logo,
        ArtworkKind.Cover => choice?.Cover,
        _ => null,
    };

    private static void Set(GameArtworkChoice choice, ArtworkKind kind, string? token)
    {
        switch (kind)
        {
            case ArtworkKind.Hero:
                choice.Hero = token;
                break;
            case ArtworkKind.Logo:
                choice.Logo = token;
                break;
            case ArtworkKind.Cover:
                choice.Cover = token;
                break;
        }
    }

    /// <summary>
    /// Neu gespeicherte eigene Dateien, auf die die bisherige Wahl nicht zeigt. Der Bilddienst legt eigene Bilder je Spiel und
    /// Art immer unter demselben Namen ab; eine Datei, die die gespeicherte Wahl noch benutzt, darf beim Zurücknehmen nicht
    /// gelöscht werden (sonst zeigte die Konfiguration auf eine fehlende Datei).
    /// </summary>
    private static IEnumerable<string> Unreferenced(IEnumerable<string> saved, GameArtworkChoice? before) =>
        saved.Where(token => !string.Equals(token, before?.Hero, StringComparison.Ordinal)
            && !string.Equals(token, before?.Logo, StringComparison.Ordinal)
            && !string.Equals(token, before?.Cover, StringComparison.Ordinal));

    private static bool SameChoice(GameArtworkChoice? a, GameArtworkChoice? b) =>
        string.Equals(a?.Hero, b?.Hero, StringComparison.Ordinal)
        && string.Equals(a?.Logo, b?.Logo, StringComparison.Ordinal)
        && string.Equals(a?.Cover, b?.Cover, StringComparison.Ordinal);

    private static string SizeOf(int width, int height) =>
        width > 0 && height > 0 ? LibraryText.Format(LibraryStrings.Picker_Size, width, height) : string.Empty;

    private void Show(IReadOnlyList<ArtworkChoiceViewModel> list)
    {
        Candidates.Clear();
        foreach (var c in list)
        {
            Candidates.Add(c);
        }

        var chosen = _chosen.GetValueOrDefault(_kind);

        // Eine Online-Wahl dieses Reiters gilt weiter, auch wenn sie nicht in der lokalen Liste steht.
        if (chosen is null || !(list.Contains(chosen) || (chosen.IsOnline && chosen.Kind == _kind)))
        {
            chosen = DefaultChoice(list);
            if (chosen is not null)
            {
                _chosen[_kind] = chosen;
            }
        }

        MarkSelection(chosen);
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasChanges));
    }

    private static ArtworkChoiceViewModel? DefaultChoice(IReadOnlyList<ArtworkChoiceViewModel> list) =>
        list.FirstOrDefault(c => c.IsCurrent) ?? list.FirstOrDefault(c => c.Token is null) ?? list.FirstOrDefault();

    /// <summary>Text für einen Fehler beim Laden eines gewählten Bildes von SteamGridDB.</summary>
    private static string DownloadMessage(Exception ex) => ex switch
    {
        SteamGridDbException { Kind: SteamGridDbErrorKind.Blocked } => LibraryStrings.Picker_SgdbDownloadBlocked,
        SteamGridDbException { Kind: SteamGridDbErrorKind.RateLimited } => LibraryStrings.Picker_SgdbRateLimited,
        SteamGridDbException { Kind: SteamGridDbErrorKind.RequestLimit } => LibraryStrings.Picker_SgdbBudget,
        SteamGridDbException => LibraryStrings.Picker_SgdbDownloadFailed,
        ImageTooLargeException => LibraryStrings.Picker_TooLarge,
        _ when ex is InvalidDataException || ArtworkImaging.IsDecodeFailure(ex) => LibraryStrings.Picker_SgdbDownloadNotAnImage,
        _ => LibraryStrings.Picker_SgdbDownloadFailed,
    };

    /// <summary>Ring an der Wahl, in beiden Listen; die Liste ohne die Wahl zeigt keine.</summary>
    private void MarkSelection(ArtworkChoiceViewModel? chosen)
    {
        foreach (var c in Candidates)
        {
            c.IsSelected = ReferenceEquals(c, chosen);
        }

        foreach (var c in Online.Results)
        {
            c.IsSelected = ReferenceEquals(c, chosen);
        }
    }

    private void Reset()
    {
        if (Candidates.FirstOrDefault(c => c.Token is null && c.PendingFile is null) is { } automatic)
        {
            Selected = automatic;
        }
    }

    private async Task PickFileAsync()
    {
        if (_isApplying)
        {
            return;
        }

        Message = null;
        var dir = _owner.SafeDirectory(Item.InstallDir);
        var file = _owner.Dialogs.PickArtworkFile(dir);
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        ImageSource? preview;
        int width = 0, height = 0;
        try
        {
            (preview, width, height) = await Task.Run(() => LoadPreview(file));
        }
        catch (ImageTooLargeException)
        {
            MessageKind = StatusKind.Error;
            Message = LibraryStrings.Picker_TooLarge;
            return;
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            MessageKind = StatusKind.Error;
            Message = LibraryStrings.Picker_NotAnImage;
            return;
        }

        var choice = new ArtworkChoiceViewModel(null, LibraryText.Format(LibraryStrings.Picker_OwnFile, Path.GetFileName(file)), LibraryStrings.Picker_OwnFileNote, preview, SizeOf(width, height), isCurrent: false, pendingFile: file, kind: _kind);
        var list = (_loaded.GetValueOrDefault(_kind) ?? []).Where(c => c.PendingFile is null).Append(choice).ToList();
        _loaded[_kind] = list;
        _chosen[_kind] = choice;
        Show(list);
    }

    /// <summary>
    /// Kleine Vorschau einer gewählten Datei (nie größer als 320 px, nur gelesen). Die Größe kommt aus dem Kopf der Datei; zu
    /// große Dateien werden vor dem Dekodieren abgelehnt (<see cref="ImageTooLargeException"/>), alle anderen schon beim
    /// Dekodieren verkleinert.
    /// </summary>
    private static (ImageSource Preview, int Width, int Height) LoadPreview(string path)
    {
        var (width, height) = ArtworkImaging.Measure(path);
        var preview = ArtworkImaging.Decode(path, LibraryArt.PreviewWidth);
        return (preview, width, height);
    }

    private void DeleteQuietly(IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            try
            {
                _owner.Context.Artwork.DeleteCustomImage(token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ohne Eintrag wirkt die Datei nicht; sie wird beim nächsten Speichern ersetzt.
            }
        }
    }
}
