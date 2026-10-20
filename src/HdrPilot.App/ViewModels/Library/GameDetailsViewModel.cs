using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Core.Util;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Wo die Rückmeldung der letzten Aktion steht: beim Starten oder bei den EXE-Dateien.</summary>
public enum DetailMessageArea
{
    /// <summary>Starten, Verknüpfungen, Steam-Startoption.</summary>
    Launch,

    /// <summary>EXE hinzufügen, entfernen, wieder aufnehmen, neu lernen.</summary>
    Executables,

    /// <summary>Bild: Steam-Startoption, angeboten beim Einschalten von „Windows Auto HDR“ (G9.7).</summary>
    Picture,
}

/// <summary>
/// Detailbereich des gewählten Spiels: Start über HdrPilot, Verknüpfungen, Steam-Startoption (nur Steam), Automatik,
/// Bildschirm, Zurückstellen, Bild („Windows Auto HDR“, E88), EXE-Dateien (mit Herkunft) und ganz unten Entfernen (manuelle
/// Spiele) bzw. „Einstellungen entfernen…“ (nicht installierte Launcher-Spiele, E90). Änderungen gehen sofort in die
/// Konfiguration. Die Rückmeldung einer Aktion steht bei ihrem Abschnitt.
/// </summary>
public sealed class GameDetailsViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;
    private GameConfig _config;
    private bool _restoreAfter;
    private bool _autoHdr;
    private bool _detached;
    private DisplayOptionViewModel? _selectedDisplay;
    private ExecutableItemViewModel? _selectedExecutable;
    private string? _message;
    private StatusKind _messageKind;
    private DetailMessageArea _messageArea;
    private bool _isLaunching;
    private bool _suppressWrites;
    private bool _steamOfferOpen;
    private GameArtwork? _artwork;
    private IReadOnlyList<ArtworkTileViewModel> _artworkTiles;

    internal GameDetailsViewModel(LibraryViewModel owner, GameItemViewModel item)
    {
        _owner = owner;
        Item = item;
        _config = item.Config;
        _restoreAfter = _config.RestoreAfter;
        _autoHdr = _config.AutoHdr;

        AddExeCommand = new AsyncRelayCommand(AddExecutableAsync);
        RemoveExeCommand = new AsyncRelayCommand(RemoveExecutableAsync, () => _selectedExecutable is { IsExcluded: false });
        IncludeExeCommand = new AsyncRelayCommand(IncludeExecutableAsync, () => _selectedExecutable is { IsExcluded: true });
        RelearnExeCommand = new AsyncRelayCommand(RelearnAsync, () => _config.DetectedExecutables.Count > 0);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => !_isLaunching && Item.IsInstalled && CanLaunchGame);
        CreateDesktopShortcutCommand = new AsyncRelayCommand(() => CreateShortcutAsync(desktop: true), () => CanLaunchGame);
        CreateStartMenuShortcutCommand = new AsyncRelayCommand(() => CreateShortcutAsync(desktop: false), () => CanLaunchGame);
        CopySteamLaunchOptionCommand = new RelayCommand(CopySteamLaunchOption, () => IsSteam);
        RemoveGameCommand = new AsyncRelayCommand(RemoveGameAsync, () => Item.IsManual);
        RemoveSettingsCommand = new AsyncRelayCommand(() => _owner.RemoveSettingsAsync(Item), () => Item.IsRemovable && !Item.IsRunning);

        Executables.ReplaceAll(BuildExecutables(_config, HelperPatterns));
        _artworkTiles = ArtworkTileViewModel.For(item, null);
        RefreshDisplays();

        // „HDR automatisch“ (Liste oder Details) und die HDR-Angabe laut PCGamingWiki steuern die Hinweise unter
        // „Windows Auto HDR“; abgemeldet in Detach, wenn ein anderes Spiel gewählt wird.
        Item.PropertyChanged += OnItemChanged;
    }

    public GameItemViewModel Item { get; }

    // ---------- Spielprofil und Bilder (E89) ----------

    /// <summary>„Steam · GoWEDay-Steam.exe“ (Haupt-EXE: Hilfs- und Startprogramme nach hinten) bzw. „Steam · keine EXE erkannt“.</summary>
    public string ProfileSubtitle => MainExecutable is { } exe
        ? LibraryText.Format(LibraryStrings.Profile_Subtitle, Item.SourceName, exe)
        : LibraryText.Format(LibraryStrings.Profile_SubtitleNoExe, Item.SourceName);

    /// <summary>Dateiname der Haupt-EXE (null ohne bekannte EXE).</summary>
    public string? MainExecutable => Executables
        .Where(e => !e.IsExcluded)
        .OrderBy(e => e.IsHelper || LauncherName(e.FileName) ? 1 : 0)
        .ThenByDescending(e => GameBinary(e.Path) ? 1 : 0)
        .Select(e => e.FileName)
        .FirstOrDefault();

    /// <summary>Startbefehl, wie HDRpilot ihn ausführt (Adresse oder Pfad mit Argumenten); leer ohne Befehl.</summary>
    public string LaunchCommandText => string.IsNullOrWhiteSpace(_config.LaunchCommand)
        ? string.Empty
        : string.IsNullOrWhiteSpace(_config.LaunchArguments) ? _config.LaunchCommand! : _config.LaunchCommand + " " + _config.LaunchArguments;

    public bool HasLaunchCommandText => LaunchCommandText.Length > 0;

    /// <summary>„EXE-DATEIEN · 2“.</summary>
    public string ExecutablesHeader => LibraryText.Format(LibraryStrings.Details_ExecutablesCount, LibraryStrings.Details_SectionExecutables, Executables.Count);

    /// <summary>Vorschau-Kacheln Titelbild, Logo, Cover und Symbol (Abschnitt „BILDER“).</summary>
    public IReadOnlyList<ArtworkTileViewModel> ArtworkTiles => _artworkTiles;

    /// <summary>Cover klein im Kopf des Detailbereichs (null: Ersatzfläche aus dem Namen).</summary>
    public System.Windows.Media.ImageSource? CoverPreview => _artwork?.Cover?.Image;

    /// <summary>„Lokal aus dem Steam-Cache · nichts heruntergeladen“ (vom Bilddienst); leer, solange die Bilder laden.</summary>
    public string ArtworkSummary => _artwork?.Summary.Description ?? string.Empty;

    public bool HasArtworkSummary => ArtworkSummary.Length > 0;

    /// <summary>„Titelbild erzeugt · Name statt Logo · Cover erzeugt“; leer, wenn alles aus einer Quelle kommt.</summary>
    public string GeneratedSummary
    {
        get
        {
            if (_artwork is not { } art)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (art.Summary.Hero is ArtworkOrigin.Generated or ArtworkOrigin.None)
            {
                parts.Add(LibraryStrings.Artwork_HeroGenerated);
            }

            if (art.Summary.LogoIsName)
            {
                parts.Add(LibraryStrings.Artwork_NameInsteadOfLogo);
            }

            if (art.Summary.CoverFromSmear)
            {
                parts.Add(LibraryStrings.Artwork_CoverFromSmear);
            }
            else if (art.Summary.Cover is ArtworkOrigin.Generated or ArtworkOrigin.None)
            {
                parts.Add(LibraryStrings.Artwork_CoverGenerated);
            }

            if (art.Summary.Icon == ArtworkOrigin.Generated)
            {
                parts.Add(LibraryStrings.Artwork_Monogram);
            }

            return string.Join(LibraryStrings.Separator, parts);
        }
    }

    public bool HasGeneratedSummary => GeneratedSummary.Length > 0;

    /// <summary>Bildsatz des Spiels ist geladen (aus dem Titelbereich übernommen).</summary>
    internal void SetArtwork(GameArtwork artwork)
    {
        _artwork = artwork;
        _artworkTiles = ArtworkTileViewModel.For(Item, artwork);
        OnPropertyChanged(nameof(ArtworkTiles));
        OnPropertyChanged(nameof(CoverPreview));
        OnPropertyChanged(nameof(ArtworkSummary));
        OnPropertyChanged(nameof(HasArtworkSummary));
        OnPropertyChanged(nameof(GeneratedSummary));
        OnPropertyChanged(nameof(HasGeneratedSummary));
    }

    private static bool LauncherName(string file) =>
        System.Text.RegularExpressions.Regex.IsMatch(file, @"(start_protected_game|launcher|prelauncher|anticheat|helper|crash|unins|setup|redist|battleye|easyanti|gamelaunchhelper|^play[a-z0-9]+\.exe$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool GameBinary(string path) =>
        System.Text.RegularExpressions.Regex.IsMatch(path, @"shipping|win64|x64|\\bin\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public string InstallDir => _config.InstallDir;

    public bool HasInstallDir => !string.IsNullOrWhiteSpace(_config.InstallDir);

    public bool IsManual => Item.IsManual;

    /// <summary>Spiel aus Steam: Die Steam-Startoption („HdrPilot.exe --wrap -- %command%“) wird angeboten.</summary>
    public bool IsSteam => Item.Source == GameSource.Steam;

    /// <summary>
    /// Ohne Start-Befehl (eigener Eintrag als Ordner mit keiner oder mehreren EXEs) kann HdrPilot das Spiel weder starten
    /// noch eine Verknüpfung anlegen; die Erkennung beim Spielen wirkt trotzdem.
    /// </summary>
    public bool CanLaunchGame => !string.IsNullOrWhiteSpace(_config.LaunchCommand);

    /// <summary>
    /// Hinweis ohne Start-Befehl (unter „Mit HDR starten“): bei Quiver-Spielen der Weg über Quiver (mehrere EXE und keine
    /// gemerkte Wahl, E91), sonst der allgemeine Hinweis für eigene Einträge.
    /// </summary>
    public string NoLaunchText => Item.Source == GameSource.Quiver ? LibraryStrings.Launch_NoCommandQuiver : LibraryStrings.Launch_NoCommand;

    /// <summary>Quiver-Spiel, das per Windows Installer installiert wurde (E91): Hinweis, welcher Ordner als Installationsordner gilt.</summary>
    public bool ShowWindowsInstallerHint => Item.Source == GameSource.Quiver && string.Equals(_config.InstallKind, InstallKinds.WindowsInstaller, StringComparison.Ordinal);

    public string? LastPlayedText => _config.LastPlayed is { } t
        ? LibraryText.Format(LibraryStrings.Details_LastPlayed, t.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : null;

    public bool HasLastPlayed => _config.LastPlayed is not null;

    /// <summary>Nach dem Spiel den vorherigen HDR-Zustand wiederherstellen.</summary>
    public bool RestoreAfter
    {
        get => _restoreAfter;
        set
        {
            if (SetProperty(ref _restoreAfter, value) && !_suppressWrites)
            {
                _ = _owner.WriteGameAsync(Item.Key, g => g.RestoreAfter = value);
            }
        }
    }

    /// <summary>
    /// Schalter „Windows Auto HDR“ (E88, Abschnitt „BILD“): Windows rechnet das SDR-Bild in HDR um. HDRpilot schaltet dafür
    /// den globalen Auto-HDR-Schalter von Windows direkt vor jedem Start ein, den es kennt (Starten, Verknüpfung,
    /// Steam-Startoption, Frühwarnung), und danach zurück; beim Schalten selbst ändert sich in Windows nichts. Unabhängig
    /// von „HDR automatisch“ (das bleibt beim Schalten unberührt). Scheitert das Speichern, setzt der Abgleich der Liste den
    /// Schalter zurück (<see cref="Apply"/>). Beim Einschalten für ein Steam-Spiel ohne Steam-Startoption bietet HDRpilot
    /// sie an (<see cref="OfferSteamLaunchOptionAsync"/>).
    /// </summary>
    public bool AutoHdr
    {
        get => _autoHdr;
        set
        {
            if (SetProperty(ref _autoHdr, value) && !_suppressWrites)
            {
                var saved = _owner.WriteGameAsync(Item.Key, g => g.AutoHdr = value);
                if (value && IsSteam)
                {
                    _ = OfferSteamLaunchOptionAsync(saved);
                }
            }
        }
    }

    /// <summary>
    /// Spiel hat laut PCGamingWiki eigenes (auch eingeschränktes) HDR: Zeile „Windows Auto HDR“ zurückgenommen dargestellt,
    /// bleibt aber bedienbar (Hinweis darunter).
    /// </summary>
    public bool IsAutoHdrSubdued => HdrSupportValues.IsHdrCapable(Item.HdrSupport);

    public bool ShowAutoHdrNativeHint => IsAutoHdrSubdued;

    /// <summary>„… eigenes HDR – besser im Spiel einschalten“ (eingeschränkt: „meist besser“); leer ohne eigenes HDR.</summary>
    public string AutoHdrNativeHint => Item.HdrSupport switch
    {
        HdrSupportStatus.Native => LibraryStrings.Details_AutoHdrNative,
        HdrSupportStatus.Limited => LibraryStrings.Details_AutoHdrNativeLimited,
        _ => string.Empty,
    };

    /// <summary>
    /// „HDR automatisch“ ist aus: Auto HDR wirkt nur bei eingeschaltetem Windows-HDR (Hinweis unter dem Schalter, auch bei
    /// ausgeschaltetem Auto HDR, damit die Abhängigkeit vor dem Einschalten klar ist).
    /// </summary>
    public bool ShowAutoHdrNeedsHdrHint => !Item.HdrAuto;

    public ObservableCollection<DisplayOptionViewModel> Displays { get; } = [];

    /// <summary>Globale Einstellung oder ein bestimmter Bildschirm.</summary>
    public DisplayOptionViewModel? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedDisplay))
            {
                return; // Beim Neuaufbau der Liste meldet die Auswahl kurz null.
            }

            _selectedDisplay = value;
            OnPropertyChanged();
            if (!_suppressWrites)
            {
                var path = value.DevicePath;
                _ = _owner.WriteGameAsync(Item.Key, g => g.Display = path);
            }
        }
    }

    public BulkObservableCollection<ExecutableItemViewModel> Executables { get; } = [];

    public bool HasExecutables => Executables.Count > 0;

    public ExecutableItemViewModel? SelectedExecutable
    {
        get => _selectedExecutable;
        set
        {
            if (SetProperty(ref _selectedExecutable, value))
            {
                OnPropertyChanged(nameof(IsSelectedExecutableExcluded));
                RemoveExeCommand.RaiseCanExecuteChanged();
                IncludeExeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSelectedExecutableExcluded => _selectedExecutable is { IsExcluded: true };

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

    public StatusKind MessageKind
    {
        get => _messageKind;
        private set => SetProperty(ref _messageKind, value);
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>Abschnitt, in dem die Rückmeldung steht (in der Nähe der Schaltfläche, die sie ausgelöst hat).</summary>
    public DetailMessageArea MessageArea => _messageArea;

    public bool HasLaunchMessage => HasMessage && _messageArea == DetailMessageArea.Launch;

    public bool HasExecutablesMessage => HasMessage && _messageArea == DetailMessageArea.Executables;

    public bool HasPictureMessage => HasMessage && _messageArea == DetailMessageArea.Picture;

    public bool IsLaunching
    {
        get => _isLaunching;
        private set
        {
            if (SetProperty(ref _isLaunching, value))
            {
                LaunchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand AddExeCommand { get; }

    public AsyncRelayCommand RemoveExeCommand { get; }

    public AsyncRelayCommand IncludeExeCommand { get; }

    public AsyncRelayCommand RelearnExeCommand { get; }

    public AsyncRelayCommand LaunchCommand { get; }

    public AsyncRelayCommand CreateDesktopShortcutCommand { get; }

    public AsyncRelayCommand CreateStartMenuShortcutCommand { get; }

    /// <summary>Legt die Steam-Startoption in die Zwischenablage (nur Steam-Spiele).</summary>
    public RelayCommand CopySteamLaunchOptionCommand { get; }

    public AsyncRelayCommand RemoveGameCommand { get; }

    /// <summary>
    /// „Einstellungen entfernen…“ ganz unten (E90): nur nicht installierte Launcher-Spiele, gesperrt während einer Sitzung
    /// (<see cref="LibraryViewModel.RemoveSettingsAsync"/>).
    /// </summary>
    public AsyncRelayCommand RemoveSettingsCommand { get; }

    /// <summary>Der Knopf „Einstellungen entfernen…“ erscheint (nicht installiertes Launcher-Spiel).</summary>
    public bool CanRemoveSettings => Item.IsRemovable;

    /// <summary>Hinweis „Nicht mehr gefunden“; bei Launcher-Spielen mit dem Hinweis, dass sich die Einstellungen ganz unten entfernen lassen.</summary>
    public string NotInstalledText => Item.IsRemovable ? LibraryStrings.Details_NotInstalledRemovable : LibraryStrings.Details_NotInstalled;

    /// <summary>Ausschlussliste für Hilfsprozesse aus der Konfiguration (null: Standardliste).</summary>
    private IReadOnlyList<string>? HelperPatterns => _owner.Context.Config.Settings.HelperExecutablePatterns;

    /// <summary>
    /// EXE-Liste: gemerkte, dann vom Launcher genannte, dann vom Nutzer ergänzte EXEs (ohne Doppelte). Ausgeschlossene
    /// sind markiert; reine Ausschluss-Einträge (etwa nur ein Dateiname) stehen am Ende.
    /// </summary>
    public static IReadOnlyList<ExecutableItemViewModel> BuildExecutables(GameConfig game, IReadOnlyList<string>? helperPatterns = null)
    {
        var list = new List<ExecutableItemViewModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<string> entries, ExecutableOrigin origin)
        {
            foreach (var entry in entries)
            {
                var path = NormalizeEntry(entry);
                if (path.Length > 0 && seen.Add(path))
                {
                    list.Add(new ExecutableItemViewModel(
                        path,
                        origin,
                        game.ExcludedExecutables.Any(x => ExcludeMatches(x, path)),
                        HelperExecutables.IsHelper(path, helperPatterns))
                    {
                        ShortFolder = ExecutableItemViewModel.Shorten(System.IO.Path.GetDirectoryName(path) ?? string.Empty, game.InstallDir),
                    });
                }
            }
        }

        Add(game.DetectedExecutables, ExecutableOrigin.Detected);
        Add(game.KnownExecutables, ExecutableOrigin.Known);
        Add(game.ExtraExecutables, ExecutableOrigin.Extra);
        foreach (var entry in game.ExcludedExecutables)
        {
            var path = NormalizeEntry(entry);
            if (path.Length > 0 && !list.Any(i => ExcludeMatches(path, i.Path)) && seen.Add(path))
            {
                list.Add(new ExecutableItemViewModel(path, ExecutableOrigin.ExcludedOnly, isExcluded: true));
            }
        }

        return list;
    }

    /// <summary>Ausschluss-Eintrag mit Pfadtrenner: Pfadvergleich, sonst Dateinamenvergleich (wie die Prozesszuordnung).</summary>
    public static bool ExcludeMatches(string entry, string path)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        return entry.Contains('\\') || entry.Contains('/')
            ? PathUtil.PathEquals(entry, path)
            : string.Equals(entry.Trim(), System.IO.Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Übernimmt einen neuen Konfigurationsstand (nach dem Abgleich der Liste).</summary>
    internal void Apply(GameConfig config)
    {
        _config = config;
        _suppressWrites = true;
        try
        {
            if (_restoreAfter != config.RestoreAfter)
            {
                _restoreAfter = config.RestoreAfter;
                OnPropertyChanged(nameof(RestoreAfter));
            }

            AutoHdr = config.AutoHdr; // ohne Schreiben: auch der Rückfall nach einem Speicherfehler

            var display = Displays.FirstOrDefault(o => SameDisplay(o.DevicePath, config.Display));
            if (display is null)
            {
                RefreshDisplays();
            }
            else if (!ReferenceEquals(display, _selectedDisplay))
            {
                _selectedDisplay = display;
                OnPropertyChanged(nameof(SelectedDisplay));
            }

            var exes = BuildExecutables(config, HelperPatterns);
            if (!exes.Select(Signature).SequenceEqual(Executables.Select(Signature), StringComparer.OrdinalIgnoreCase))
            {
                var selected = _selectedExecutable?.Path;
                Executables.ReplaceAll(exes);
                SelectedExecutable = Executables.FirstOrDefault(e => string.Equals(e.Path, selected, StringComparison.OrdinalIgnoreCase));
                OnPropertyChanged(nameof(HasExecutables));
            }
        }
        finally
        {
            _suppressWrites = false;
        }

        OnPropertyChanged(nameof(InstallDir));
        OnPropertyChanged(nameof(HasInstallDir));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(HasLastPlayed));
        OnPropertyChanged(nameof(ProfileSubtitle));
        OnPropertyChanged(nameof(MainExecutable));
        OnPropertyChanged(nameof(ExecutablesHeader));
        OnPropertyChanged(nameof(LaunchCommandText));
        OnPropertyChanged(nameof(HasLaunchCommandText));
        OnPropertyChanged(nameof(CanLaunchGame));
        OnPropertyChanged(nameof(ShowWindowsInstallerHint));
        RelearnExeCommand.RaiseCanExecuteChanged();
        LaunchCommand.RaiseCanExecuteChanged();
        CreateDesktopShortcutCommand.RaiseCanExecuteChanged();
        CreateStartMenuShortcutCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Ein anderes Spiel ist gewählt oder das Fenster geschlossen: nicht mehr auf die Zeile hören.</summary>
    internal void Detach()
    {
        if (!_detached)
        {
            _detached = true;
            Item.PropertyChanged -= OnItemChanged;
        }
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GameItemViewModel.HdrAuto):
                OnPropertyChanged(nameof(ShowAutoHdrNeedsHdrHint));
                break;
            case nameof(GameItemViewModel.HdrSupport):
                OnPropertyChanged(nameof(IsAutoHdrSubdued));
                OnPropertyChanged(nameof(ShowAutoHdrNativeHint));
                OnPropertyChanged(nameof(AutoHdrNativeHint));
                break;
            case nameof(GameItemViewModel.IsRemovable):
                OnPropertyChanged(nameof(CanRemoveSettings));
                OnPropertyChanged(nameof(NotInstalledText));
                RemoveSettingsCommand.RaiseCanExecuteChanged();
                break;
            case nameof(GameItemViewModel.IsRunning):
                RemoveSettingsCommand.RaiseCanExecuteChanged();
                break;
        }
    }

    /// <summary>Bildschirmliste neu aufbauen (Bildschirme können kommen und gehen).</summary>
    internal void RefreshDisplays()
    {
        var displays = _owner.Displays;
        var targets = _owner.Targets;
        _suppressWrites = true;
        try
        {
            var options = new List<DisplayOptionViewModel>();
            var globalNames = string.Join(", ", targets.Select(t => t.FriendlyName));
            options.Add(new DisplayOptionViewModel(
                null,
                globalNames.Length == 0 ? LibraryStrings.Display_Global : LibraryText.Format(LibraryStrings.Display_GlobalWith, globalNames),
                null,
                targets.Any(t => t.HdrSupported),
                isConnected: true));

            foreach (var d in displays)
            {
                var label = displays.Count(x => string.Equals(x.FriendlyName, d.FriendlyName, StringComparison.OrdinalIgnoreCase)) > 1 && d.GdiDeviceName is { } gdi
                    ? LibraryText.Format(LibraryStrings.Display_NameWithDevice, d.FriendlyName, gdi.TrimStart('\\', '.'))
                    : d.FriendlyName;
                var detail = d.HdrSupported ? LibraryStrings.Display_HdrCapable : LibraryStrings.Display_NoHdr;
                if (d.IsPrimary)
                {
                    detail = LibraryText.Format(LibraryStrings.Display_LabelWithDetail, LibraryStrings.Display_Primary, detail);
                }

                options.Add(new DisplayOptionViewModel(d.MonitorDevicePath, label, detail, d.HdrSupported, isConnected: true));
            }

            if (!string.IsNullOrWhiteSpace(_config.Display) && !options.Any(o => SameDisplay(o.DevicePath, _config.Display)))
            {
                options.Add(new DisplayOptionViewModel(_config.Display, LibraryStrings.Display_NotConnected, null, isHdrCapable: false, isConnected: false));
            }

            Displays.Clear();
            foreach (var option in options)
            {
                Displays.Add(option);
            }

            _selectedDisplay = Displays.FirstOrDefault(o => SameDisplay(o.DevicePath, _config.Display)) ?? Displays[0];
            OnPropertyChanged(nameof(SelectedDisplay));
        }
        finally
        {
            _suppressWrites = false;
        }
    }

    private static string Signature(ExecutableItemViewModel e) => e.Path + "|" + e.Origin + "|" + e.IsExcluded + "|" + e.IsHelper;

    private static bool SameDisplay(string? a, string? b) =>
        string.IsNullOrWhiteSpace(a) ? string.IsNullOrWhiteSpace(b) : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEntry(string entry) =>
        string.IsNullOrWhiteSpace(entry) ? string.Empty
        : entry.Contains('\\') || entry.Contains('/') ? PathUtil.Normalize(entry) : entry.Trim();

    private static bool SameEntry(string a, string b) =>
        string.Equals(NormalizeEntry(a), NormalizeEntry(b), StringComparison.OrdinalIgnoreCase);

    private void ShowMessage(string text, StatusKind kind, DetailMessageArea area)
    {
        _messageArea = area;
        MessageKind = kind;
        Message = text;
        OnPropertyChanged(nameof(MessageArea));
        OnPropertyChanged(nameof(HasLaunchMessage));
        OnPropertyChanged(nameof(HasExecutablesMessage));
        OnPropertyChanged(nameof(HasPictureMessage));
    }

    private void CopySteamLaunchOption() => CopySteamLaunchOption(DetailMessageArea.Launch, LibraryStrings.Steam_LaunchOptionCopied);

    /// <summary>„Steam-Startoption kopieren“; die Rückmeldung steht im Abschnitt, der es ausgelöst hat.</summary>
    private void CopySteamLaunchOption(DetailMessageArea area, string copiedText)
    {
        if (!IsSteam)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_owner.Context.ExecutablePath))
        {
            ShowMessage(LibraryText.Format(LibraryStrings.Steam_LaunchOptionFailed, LibraryStrings.Shortcut_NoTarget), StatusKind.Error, area);
            return;
        }

        try
        {
            _owner.Context.Clipboard.SetText(SteamLaunchOption.Build(_owner.Context.ExecutablePath));
            ShowMessage(copiedText, StatusKind.Success, area);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException)
        {
            // Zwischenablage gerade von einem anderen Programm belegt.
            ShowMessage(LibraryText.Format(LibraryStrings.Steam_LaunchOptionFailed, ex.Message), StatusKind.Error, area);
        }
    }

    /// <summary>
    /// G9.7: Auto HDR für ein Steam-Spiel eingeschaltet. Startet der Nutzer es direkt in Steam, kennt HDRpilot den Start
    /// vorher nur über die Steam-Startoption. Fehlt sie, fragt HDRpilot, ob es sie kopieren soll: dieselbe Funktion wie
    /// „Steam-Startoption kopieren“ mit denselben Bedingungen (Programmpfad bekannt, Zwischenablage frei); eintragen kann
    /// sie nur der Nutzer in Steam. Ablehnen ändert nichts. Auto HDR bleibt in jedem Fall an. Nicht gefragt wird nach einem
    /// gescheiterten Speichern, wenn die Startoption schon gesetzt ist, und wenn der Schalter inzwischen wieder aus oder ein
    /// anderes Spiel gewählt ist.
    /// </summary>
    private async Task OfferSteamLaunchOptionAsync(Task<bool> saved)
    {
        var appId = _config.SourceId;
        if (!await saved || string.IsNullOrWhiteSpace(appId) || await _owner.Context.HasSteamLaunchOptionAsync(appId))
        {
            return;
        }

        // Die Rückfrage pumpt Nachrichten: Schnelles Aus- und Einschalten darf keine zweite darüber öffnen.
        if (_detached || !_autoHdr || _steamOfferOpen)
        {
            return;
        }

        _steamOfferOpen = true;
        try
        {
            if (_owner.Dialogs.Confirm(
                LibraryStrings.AutoHdrSteam_OfferTitle,
                LibraryText.Format(LibraryStrings.AutoHdrSteam_OfferText, Item.Name),
                LibraryStrings.AutoHdrSteam_OfferConfirm,
                isDanger: false))
            {
                CopySteamLaunchOption(DetailMessageArea.Picture, LibraryStrings.AutoHdrSteam_Copied);
            }
        }
        finally
        {
            _steamOfferOpen = false;
        }
    }

    private async Task AddExecutableAsync()
    {
        var initial = HasInstallDir && _owner.Context.FileSystem.DirectoryExists(_config.InstallDir) ? _config.InstallDir : null;
        var picked = _owner.Dialogs.PickExecutable(initial);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        var path = PathUtil.Normalize(picked);
        if (await _owner.WriteGameAsync(Item.Key, g =>
        {
            g.ExcludedExecutables.RemoveAll(x => ExcludeMatches(x, path));
            var known = g.DetectedExecutables.Concat(g.KnownExecutables).Concat(g.ExtraExecutables).Any(x => SameEntry(x, path));
            if (!known)
            {
                g.ExtraExecutables.Add(path);
            }
        }))
        {
            SelectedExecutable = Executables.FirstOrDefault(e => SameEntry(e.Path, path));
            ShowMessage(LibraryText.Format(LibraryStrings.Exe_Added, System.IO.Path.GetFileName(path)), StatusKind.Success, DetailMessageArea.Executables);
        }
    }

    private async Task RemoveExecutableAsync()
    {
        if (_selectedExecutable is not { IsExcluded: false } exe)
        {
            return;
        }

        var path = exe.Path;
        if (await _owner.WriteGameAsync(Item.Key, g =>
        {
            g.ExtraExecutables.RemoveAll(x => SameEntry(x, path));
            var listed = g.DetectedExecutables.Concat(g.KnownExecutables).Any(x => SameEntry(x, path));
            if (listed && !g.ExcludedExecutables.Any(x => ExcludeMatches(x, path)))
            {
                g.ExcludedExecutables.Add(path);
            }
        }))
        {
            ShowMessage(LibraryText.Format(LibraryStrings.Exe_Removed, exe.FileName), StatusKind.Info, DetailMessageArea.Executables);
        }
    }

    private async Task IncludeExecutableAsync()
    {
        if (_selectedExecutable is not { IsExcluded: true } exe)
        {
            return;
        }

        var path = exe.Path;
        if (await _owner.WriteGameAsync(Item.Key, g => g.ExcludedExecutables.RemoveAll(x => ExcludeMatches(x, path) || SameEntry(x, path))))
        {
            ShowMessage(LibraryText.Format(LibraryStrings.Exe_Included, exe.FileName), StatusKind.Success, DetailMessageArea.Executables);
        }
    }

    private async Task RelearnAsync()
    {
        if (await _owner.WriteGameAsync(Item.Key, g => g.DetectedExecutables.Clear()))
        {
            ShowMessage(LibraryStrings.Exe_RelearnDone, StatusKind.Info, DetailMessageArea.Executables);
        }
    }

    private async Task LaunchAsync()
    {
        IsLaunching = true;
        ShowMessage(LibraryStrings.Launch_Running, StatusKind.Info, DetailMessageArea.Launch);
        _owner.OnLaunchStarting(); // solange der Start läuft, rechnet die Bibliothek keine helle Fassung
        try
        {
            var outcome = await _owner.Context.LaunchAsync(Item.Key);
            if (outcome.Started)
            {
                ShowMessage(
                    outcome.HdrConfirmedAt is { } t
                        ? LibraryText.Format(LibraryStrings.Launch_Started, t.ToLocalTime().ToString("T", CultureInfo.CurrentCulture))
                        : LibraryStrings.Launch_StartedPlain,
                    StatusKind.Success,
                    DetailMessageArea.Launch);
                _owner.OnLaunchSucceeded(); // auf Wunsch schließt das Fenster („Nach dem Start in den Infobereich“)
            }
            else
            {
                ShowMessage(LibraryText.Format(LibraryStrings.Launch_Failed, outcome.Message), StatusKind.Error, DetailMessageArea.Launch);
            }
        }
        catch (Exception ex)
        {
            ShowMessage(LibraryText.Format(LibraryStrings.Launch_Failed, ex.Message), StatusKind.Error, DetailMessageArea.Launch);
        }
        finally
        {
            IsLaunching = false;
            _owner.OnLaunchFinished();
        }
    }

    private async Task CreateShortcutAsync(bool desktop)
    {
        try
        {
            // Abseits des UI-Threads: Hat die Liste für das Spiel nichts gemerkt, liest die Wahl des Symbols die Hinweise des
            // Launchers und bis zu zweimal 24 EXE-Symbole, auf einem kalten Laufwerk mehrere Sekunden lang.
            var shortcuts = _owner.Context.Shortcuts;
            var config = _config;
            var path = await Task.Run(() => desktop ? shortcuts.CreateDesktopShortcut(config) : shortcuts.CreateStartMenuShortcut(config));
            ShowMessage(LibraryText.Format(LibraryStrings.Shortcut_Created, path), StatusKind.Success, DetailMessageArea.Launch);
        }
        catch (Exception ex)
        {
            ShowMessage(LibraryText.Format(LibraryStrings.Shortcut_Failed, ex.Message), StatusKind.Error, DetailMessageArea.Launch);
        }
    }

    private async Task RemoveGameAsync()
    {
        if (!Item.IsManual)
        {
            return;
        }

        if (_owner.Dialogs.Confirm(
            LibraryStrings.RemoveGame_ConfirmTitle,
            LibraryText.Format(LibraryStrings.RemoveGame_ConfirmText, Item.Name),
            LibraryStrings.RemoveGame_Confirm,
            isDanger: true))
        {
            await _owner.RemoveGameAsync(Item);
        }
    }
}
