using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Eine Wahl im Dialog „Bild wählen…“ (Launcher-Bild, anderes lokales Bild, erzeugt, eigene Datei).</summary>
public sealed class ArtworkChoiceViewModel : ObservableObject
{
    private bool _isSelected;

    internal ArtworkChoiceViewModel(string? token, string label, string? note, ImageSource? preview, string sizeText, bool isCurrent, string? pendingFile = null)
    {
        Token = token;
        Label = label;
        Note = note;
        Preview = preview;
        SizeText = sizeText;
        IsCurrent = isCurrent;
        PendingFile = pendingFile;
    }

    /// <summary>Kennzeichen für die Konfiguration (null: automatisch, siehe <see cref="ArtworkOverride"/>).</summary>
    public string? Token { get; }

    public string Label { get; }

    public string? Note { get; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public ImageSource? Preview { get; }

    /// <summary>„1920 × 1080“ (leer ohne bekannte Größe).</summary>
    public string SizeText { get; }

    /// <summary>Aktuell gezeigtes Bild (Kennzeichen „AKTUELL“).</summary>
    public bool IsCurrent { get; }

    /// <summary>Eben gewählte eigene Datei, die erst beim Übernehmen gespeichert wird.</summary>
    public string? PendingFile { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public override string ToString() => Label;
}

/// <summary>
/// Dialog „Bild wählen…“ (E89, G10.3): je Bildart (Titelbild, Logo, Cover) die Möglichkeiten des Bilddienstes, dazu
/// „Eigene Datei…“. Die Wahl je Bildart bleibt beim Umschalten der Reiter erhalten; „Übernehmen“ speichert eigene Dateien
/// verkleinert im Datenordner (außerhalb des UI-Threads, die Quelle bleibt unverändert), schreibt die Wahl in
/// <see cref="GameConfig.Artwork"/> und löscht ersetzte eigene Dateien dieses Spiels. „Zurücksetzen“ wählt „Automatisch“.
/// </summary>
public sealed class ArtworkPickerViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;
    private readonly Dictionary<ArtworkKind, ArtworkChoiceViewModel?> _chosen = [];
    private readonly Dictionary<ArtworkKind, IReadOnlyList<ArtworkChoiceViewModel>> _loaded = [];
    private ArtworkKind _kind;
    private bool _isLoading;
    private string? _message;
    private int _loadRun;

    internal ArtworkPickerViewModel(LibraryViewModel owner, GameItemViewModel item, ArtworkKind kind)
    {
        _owner = owner;
        Item = item;
        _kind = kind;
        PickFileCommand = new AsyncRelayCommand(PickFileAsync);
        ResetCommand = new RelayCommand(Reset);
    }

    public GameItemViewModel Item { get; }

    /// <summary>„Clive Barker's Hellraiser: Revival · Xbox“.</summary>
    public string Subtitle => LibraryText.Format(LibraryStrings.Picker_Subtitle, Item.Name, Item.SourceName);

    /// <summary>Hinweis auf die Herkunft der Bilder (nur lokal; SteamGridDB nur, wenn eingeschaltet).</summary>
    public string SourceNote => LibraryStrings.Picker_SourceNote;

    public ArtworkKind Kind
    {
        get => _kind;
        set
        {
            if ((value is ArtworkKind.Hero or ArtworkKind.Logo or ArtworkKind.Cover) && SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(IsHero));
                OnPropertyChanged(nameof(IsLogo));
                OnPropertyChanged(nameof(IsCover));
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

    /// <summary>Gewählte Möglichkeit im aktuellen Reiter.</summary>
    public ArtworkChoiceViewModel? Selected
    {
        get => _chosen.GetValueOrDefault(_kind);
        set
        {
            // Nur Möglichkeiten des aktuellen Reiters (während ein anderer Reiter lädt, gehört die alte Liste nicht dazu).
            if (value is null || ReferenceEquals(value, Selected) || !Candidates.Contains(value))
            {
                return;
            }

            _chosen[_kind] = value;
            foreach (var c in Candidates)
            {
                c.IsSelected = ReferenceEquals(c, value);
            }

            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Fehler beim Lesen oder Speichern (etwa „Die Datei ist kein lesbares Bild.“).</summary>
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
        var list = candidates.Select(c => new ArtworkChoiceViewModel(c.Token, c.Label, c.Note, c.Preview, SizeOf(c.PixelWidth, c.PixelHeight), c.IsCurrent)).ToList();
        if (!list.Any(c => c.Token is null))
        {
            // Ohne Angebot des Bilddienstes gibt es immer „Automatisch“.
            list.Insert(0, new ArtworkChoiceViewModel(null, LibraryStrings.Picker_Automatic, LibraryStrings.Picker_AutomaticNote, null, string.Empty, current is null));
        }

        _loaded[kind] = list;
        if (run == _loadRun)
        {
            IsLoading = false;
            Show(list);
        }
    }

    /// <summary>Gewählte Bilder übernehmen. False, wenn nichts geändert wurde oder Speichern scheiterte (Hinweis in der Bibliothek).</summary>
    public async Task<bool> ApplyAsync()
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
            if (chosen.PendingFile is { } file)
            {
                try
                {
                    token = await Task.Run(() => artwork.SaveCustomImage(config, kind, file));
                    saved.Add(token);
                }
                catch (ImageTooLargeException)
                {
                    DeleteQuietly(Unreferenced(saved, before));
                    _owner.ShowStatus(LibraryStrings.Picker_TooLarge, StatusKind.Error);
                    return false;
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or FormatException or ArgumentException or System.Runtime.InteropServices.ExternalException)
                {
                    DeleteQuietly(Unreferenced(saved, before));
                    _owner.ShowStatus(LibraryStrings.Picker_NotAnImage, StatusKind.Error);
                    return false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    DeleteQuietly(Unreferenced(saved, before));
                    _owner.ShowStatus(LibraryText.Format(LibraryStrings.Picker_SaveFailed, ex.Message), StatusKind.Error);
                    return false;
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
                return false;
            }

            _owner.ShowStatus(LibraryText.Format(LibraryStrings.Picker_Applied, Item.Name), StatusKind.Success);
            return true;
        }

        var written = await _owner.WriteGameAsync(Item.Key, g => g.Artwork = normalized is null ? null : new GameArtworkChoice { Hero = normalized.Hero, Logo = normalized.Logo, Cover = normalized.Cover });
        if (!written)
        {
            DeleteQuietly(Unreferenced(saved, before));
            return false;
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
        return true;
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
        if (chosen is null || !list.Contains(chosen))
        {
            chosen = list.FirstOrDefault(c => c.IsCurrent) ?? list.FirstOrDefault(c => c.Token is null) ?? list.FirstOrDefault();
            if (chosen is not null)
            {
                _chosen[_kind] = chosen;
            }
        }

        foreach (var c in list)
        {
            c.IsSelected = ReferenceEquals(c, chosen);
        }

        OnPropertyChanged(nameof(Selected));
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
            Message = LibraryStrings.Picker_TooLarge;
            return;
        }
        catch (Exception ex) when (ArtworkImaging.IsDecodeFailure(ex))
        {
            Message = LibraryStrings.Picker_NotAnImage;
            return;
        }

        var choice = new ArtworkChoiceViewModel(null, LibraryText.Format(LibraryStrings.Picker_OwnFile, Path.GetFileName(file)), LibraryStrings.Picker_OwnFileNote, preview, SizeOf(width, height), isCurrent: false, pendingFile: file);
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
