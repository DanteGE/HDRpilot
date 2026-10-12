using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
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
}

/// <summary>
/// Detailbereich des gewählten Spiels: Start über HdrPilot, Verknüpfungen, Steam-Startoption (nur Steam), Automatik,
/// Bildschirm, Zurückstellen, Bild („Windows Auto HDR“, E88), EXE-Dateien (mit Herkunft) und Entfernen (nur manuelle
/// Spiele). Änderungen gehen sofort in die Konfiguration. Die Rückmeldung einer Aktion steht bei ihrem Abschnitt.
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

        Executables.ReplaceAll(BuildExecutables(_config, HelperPatterns));
        RefreshDisplays();

        // „HDR automatisch“ (Liste oder Details) und die HDR-Angabe laut PCGamingWiki steuern die Hinweise unter
        // „Windows Auto HDR“; abgemeldet in Detach, wenn ein anderes Spiel gewählt wird.
        Item.PropertyChanged += OnItemChanged;
    }

    public GameItemViewModel Item { get; }

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
    /// Schalter zurück (<see cref="Apply"/>).
    /// </summary>
    public bool AutoHdr
    {
        get => _autoHdr;
        set
        {
            if (SetProperty(ref _autoHdr, value) && !_suppressWrites)
            {
                _ = _owner.WriteGameAsync(Item.Key, g => g.AutoHdr = value);
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
                        HelperExecutables.IsHelper(path, helperPatterns)));
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
        RelearnExeCommand.RaiseCanExecuteChanged();
        LaunchCommand.RaiseCanExecuteChanged();
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
    }

    private void CopySteamLaunchOption()
    {
        if (!IsSteam)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_owner.Context.ExecutablePath))
        {
            ShowMessage(LibraryText.Format(LibraryStrings.Steam_LaunchOptionFailed, LibraryStrings.Shortcut_NoTarget), StatusKind.Error, DetailMessageArea.Launch);
            return;
        }

        try
        {
            _owner.Context.Clipboard.SetText(SteamLaunchOption.Build(_owner.Context.ExecutablePath));
            ShowMessage(LibraryStrings.Steam_LaunchOptionCopied, StatusKind.Success, DetailMessageArea.Launch);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException)
        {
            // Zwischenablage gerade von einem anderen Programm belegt.
            ShowMessage(LibraryText.Format(LibraryStrings.Steam_LaunchOptionFailed, ex.Message), StatusKind.Error, DetailMessageArea.Launch);
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
