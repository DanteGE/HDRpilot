using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Sessions;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>
/// Einstellungen: lädt den aktuellen Stand, prüft und begrenzt Eingaben und schreibt beim Speichern über
/// <see cref="ConfigService.Update"/> (sofort auf der Platte). Abbrechen verwirft alles.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    public const int PollIntervalMin = 250;
    public const int PollIntervalMax = 5000;
    public const int GracePeriodMin = 0;
    public const int GracePeriodMax = 120;
    public const int LaunchTimeoutMin = 30;
    public const int LaunchTimeoutMax = 600;
    public const int RestartBufferMinutesMax = AppSettings.RestartBufferMaxSeconds / 60;
    public const int RestartBufferSecondsMax = 59;

    private readonly IShellContext _context;
    private readonly IFolderPicker _folderPicker;
    private readonly RelayCommand _removeSearchFolderCommand;

    /// <summary>restartBufferSeconds fehlt in config.json: Der Standard bleibt ungeschrieben, solange er nicht geändert wird.</summary>
    private readonly bool _restartBufferUnset;
    private Snapshot _original;
    private int _pollIntervalMs;
    private int _gracePeriodSeconds;
    private int _launchTimeoutSeconds;
    private int _restartBufferTotalSeconds;
    private bool _startWithWindows;
    private bool _notifications;
    private bool _hotkeyFallback;
    private bool _hdrListSync;
    private string? _selectedSearchFolder;
    private LogLevel _logLevel;
    private string _helperPatternsText = string.Empty;
    private HelperPatternParseResult _helperPatterns = new([], null);
    private string? _errorMessage;
    private bool _patternErrorShown;
    private bool _hasChanges;
    private bool _disposed;

    public SettingsViewModel(IShellContext context, IFolderPicker folderPicker)
    {
        _context = context;
        _folderPicker = folderPicker;
        _removeSearchFolderCommand = new RelayCommand(RemoveSearchFolder, () => SelectedSearchFolder is not null);
        SaveCommand = new RelayCommand(() => Save());
        CancelCommand = new RelayCommand(Cancel);
        AddSearchFolderCommand = new RelayCommand(AddSearchFolder);
        ResetHelperPatternsCommand = new RelayCommand(ResetHelperPatterns);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);

        var s = context.Config.Current.Settings;
        _pollIntervalMs = Math.Clamp(s.PollIntervalMs, PollIntervalMin, PollIntervalMax);
        _gracePeriodSeconds = Math.Clamp(s.GracePeriodSeconds, GracePeriodMin, GracePeriodMax);
        _launchTimeoutSeconds = Math.Clamp(s.LaunchTimeoutSeconds, LaunchTimeoutMin, LaunchTimeoutMax);
        _restartBufferUnset = s.RestartBufferSeconds is null;
        _restartBufferTotalSeconds = Math.Clamp(s.RestartBufferSeconds ?? RestartBufferDefaultSeconds, 0, AppSettings.RestartBufferMaxSeconds);
        _startWithWindows = ReadAutostart(s.StartWithWindows);
        _notifications = s.Notifications;
        _hotkeyFallback = s.HotkeyFallback;
        _hdrListSync = s.HdrListSync;
        _logLevel = LogLevelOption.Clamp(s.LogLevel);
        foreach (var folder in s.SearchFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            SearchFolders.Add(folder);
        }

        _helperPatternsText = s.HelperExecutablePatterns is { } custom ? HelperPatternList.ToText(custom) : HelperPatternList.DefaultText;
        _helperPatterns = HelperPatternList.Parse(_helperPatternsText);
        LoadDisplays(s.TargetDisplays);

        SearchFolders.CollectionChanged += (_, _) => OnSearchFoldersChanged();
        _original = Capture();
        context.HdrStateChanged += OnHdrStateChanged;
        HdrList = new HdrListViewModel(context.HdrList, context.OpenWebPage);
    }

    /// <summary>Speichern oder Abbrechen: Fenster soll sich schließen.</summary>
    public event EventHandler? CloseRequested;

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand AddSearchFolderCommand { get; }

    public ICommand RemoveSearchFolderCommand => _removeSearchFolderCommand;

    public ICommand ResetHelperPatternsCommand { get; }

    public ICommand OpenLogFolderCommand { get; }

    public ObservableCollection<DisplayOptionViewModel> Displays { get; } = [];

    public bool HasDisplays => Displays.Count > 0;

    /// <summary>„Keiner ausgewählt: Hauptbildschirm (…)“ oder „1 von 2 ausgewählt“.</summary>
    public string DisplaysSummary
    {
        get
        {
            var selected = Displays.Count(d => d.IsSelected);
            if (selected > 0)
            {
                return string.Format(CultureInfo.CurrentCulture, ShellStrings.DisplaysSelected, selected, Displays.Count);
            }

            var primary = Displays.FirstOrDefault(d => d.IsPrimary && d.IsConnected) ?? Displays.FirstOrDefault(d => d.IsConnected);
            return primary is null
                ? ShellStrings.DisplaysPrimaryFallbackUnknown
                : string.Format(CultureInfo.CurrentCulture, ShellStrings.DisplaysPrimaryFallback, primary.Name);
        }
    }

    public int PollIntervalMs
    {
        get => _pollIntervalMs;
        set
        {
            if (SetProperty(ref _pollIntervalMs, Math.Clamp(value, PollIntervalMin, PollIntervalMax)))
            {
                OnPropertyChanged(nameof(PollIntervalText));
                UpdateChanges();
            }
        }
    }

    public string PollIntervalText => string.Format(CultureInfo.CurrentCulture, ShellStrings.UnitMilliseconds, _pollIntervalMs);

    public int GracePeriodSeconds
    {
        get => _gracePeriodSeconds;
        set
        {
            if (SetProperty(ref _gracePeriodSeconds, Math.Clamp(value, GracePeriodMin, GracePeriodMax)))
            {
                OnPropertyChanged(nameof(GracePeriodText));
                UpdateChanges();
            }
        }
    }

    public string GracePeriodText => string.Format(CultureInfo.CurrentCulture, ShellStrings.UnitSeconds, _gracePeriodSeconds);

    public int LaunchTimeoutSeconds
    {
        get => _launchTimeoutSeconds;
        set
        {
            if (SetProperty(ref _launchTimeoutSeconds, Math.Clamp(value, LaunchTimeoutMin, LaunchTimeoutMax)))
            {
                OnPropertyChanged(nameof(LaunchTimeoutText));
                UpdateChanges();
            }
        }
    }

    public string LaunchTimeoutText => string.Format(CultureInfo.CurrentCulture, ShellStrings.UnitSeconds, _launchTimeoutSeconds);

    /// <summary>Standard des Neustart-Puffers, wenn restartBufferSeconds nicht gesetzt ist (60 s, E85).</summary>
    public static int RestartBufferDefaultSeconds => (int)SessionManagerOptions.Default.RestartBuffer.TotalSeconds;

    /// <summary>Neustart-Puffer, Minuten (0–30). Bei 30 Minuten werden die Sekunden auf 0 begrenzt (höchstens 30:00).</summary>
    public int RestartBufferMinutes
    {
        get => _restartBufferTotalSeconds / 60;
        set => SetRestartBuffer((Math.Clamp(value, 0, RestartBufferMinutesMax) * 60) + RestartBufferSeconds);
    }

    /// <summary>Neustart-Puffer, Sekunden (0–59); zusammen mit den Minuten höchstens 30:00.</summary>
    public int RestartBufferSeconds
    {
        get => _restartBufferTotalSeconds % 60;
        set => SetRestartBuffer((RestartBufferMinutes * 60) + Math.Clamp(value, 0, RestartBufferSecondsMax));
    }

    /// <summary>Gespeicherter Wert (restartBufferSeconds): 0 bis 1800, 0 = aus.</summary>
    public int RestartBufferTotalSeconds => _restartBufferTotalSeconds;

    /// <summary>„1:00 min“ oder „aus“.</summary>
    public string RestartBufferText => _restartBufferTotalSeconds == 0
        ? ShellStrings.RestartBufferOff
        : string.Format(CultureInfo.CurrentCulture, ShellStrings.UnitMinutesSeconds, RestartBufferMinutes, RestartBufferSeconds);

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                UpdateChanges();
            }
        }
    }

    public bool Notifications
    {
        get => _notifications;
        set
        {
            if (SetProperty(ref _notifications, value))
            {
                UpdateChanges();
            }
        }
    }

    public bool HotkeyFallback
    {
        get => _hotkeyFallback;
        set
        {
            if (SetProperty(ref _hotkeyFallback, value))
            {
                UpdateChanges();
            }
        }
    }

    /// <summary>„Bei jedem Start abgleichen“ (AppSettings.HdrListSync), wird mit „Speichern“ übernommen.</summary>
    public bool HdrListSync
    {
        get => _hdrListSync;
        set
        {
            if (SetProperty(ref _hdrListSync, value))
            {
                UpdateChanges();
            }
        }
    }

    /// <summary>Abschnitt „HDR-Liste (PCGamingWiki)“: Stand, „Jetzt abgleichen“, Konto (wirkt sofort).</summary>
    public HdrListViewModel HdrList { get; }

    public ObservableCollection<string> SearchFolders { get; } = [];

    public bool HasSearchFolders => SearchFolders.Count > 0;

    public string? SelectedSearchFolder
    {
        get => _selectedSearchFolder;
        set
        {
            if (SetProperty(ref _selectedSearchFolder, value))
            {
                _removeSearchFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<LogLevelOption> LogLevels => LogLevelOption.All;

    public LogLevel LogLevel
    {
        get => _logLevel;
        set
        {
            if (SetProperty(ref _logLevel, LogLevelOption.Clamp(value)))
            {
                UpdateChanges();
            }
        }
    }

    /// <summary>Editor der Ausschlussliste: ein Muster je Zeile.</summary>
    public string HelperPatternsText
    {
        get => _helperPatternsText;
        set
        {
            if (SetProperty(ref _helperPatternsText, value ?? string.Empty))
            {
                _helperPatterns = HelperPatternList.Parse(_helperPatternsText);
                OnPropertyChanged(nameof(HelperPatternCountText));
                OnPropertyChanged(nameof(HelperPatternsAreDefault));
                OnPropertyChanged(nameof(HelperPatternsKind));

                // Nur die eigene Prüfmeldung setzen oder löschen; ein Autostart- oder Ordnerfehler bleibt stehen.
                if (_helperPatterns.Error is { } error)
                {
                    ErrorMessage = error;
                }
                else if (_patternErrorShown)
                {
                    ErrorMessage = null;
                }

                _patternErrorShown = _helperPatterns.Error is not null;
                UpdateChanges();
            }
        }
    }

    public string HelperPatternCountText => string.Format(CultureInfo.CurrentCulture, ShellStrings.HelperPatternsCount, _helperPatterns.Patterns.Count);

    public bool HelperPatternsAreDefault => _helperPatterns.IsValid && HelperPatternList.IsDefault(_helperPatterns.Patterns);

    public string HelperPatternsKind => HelperPatternsAreDefault ? ShellStrings.HelperPatternsDefault : ShellStrings.HelperPatternsCustom;

    /// <summary>Prüf- oder Speicherfehler; null, wenn alles in Ordnung ist.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorMessage is not null;

    public bool HasChanges
    {
        get => _hasChanges;
        private set
        {
            if (SetProperty(ref _hasChanges, value))
            {
                OnPropertyChanged(nameof(ChangeStatus));
            }
        }
    }

    public string ChangeStatus => _hasChanges ? ShellStrings.UnsavedChanges : ShellStrings.NoChanges;

    /// <summary>Prüft, setzt den Autostart und schreibt die Konfiguration. False bei Fehlern (Fenster bleibt offen).</summary>
    public bool Save()
    {
        var parsed = HelperPatternList.Parse(_helperPatternsText);
        if (!parsed.IsValid)
        {
            ErrorMessage = parsed.Error;
            _patternErrorShown = true;
            return false;
        }

        // Autostart zuerst: Scheitert die Registry, bleibt alles unverändert.
        try
        {
            if (_startWithWindows != _context.Autostart.IsEnabled)
            {
                _context.Autostart.SetEnabled(_startWithWindows);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, ShellStrings.AutostartFailed, ex.Message);
            return false;
        }

        var targets = Displays.Where(d => d.IsSelected).Select(d => d.Path).ToList();
        var folders = SearchFolders.ToList();
        var patterns = HelperPatternList.ToConfigValue(parsed.Patterns);
        var foldersChanged = !string.Equals(_original.Folders, JoinFolders(folders), StringComparison.OrdinalIgnoreCase);
        try
        {
            _context.Config.Update(c =>
            {
                var s = c.Settings;
                s.TargetDisplays = targets;
                s.PollIntervalMs = _pollIntervalMs;
                s.GracePeriodSeconds = _gracePeriodSeconds;
                s.LaunchTimeoutSeconds = _launchTimeoutSeconds;
                s.RestartBufferSeconds = _restartBufferUnset && _restartBufferTotalSeconds == RestartBufferDefaultSeconds ? null : _restartBufferTotalSeconds;
                s.StartWithWindows = _startWithWindows;
                s.Notifications = _notifications;
                s.HotkeyFallback = _hotkeyFallback;
                s.HdrListSync = _hdrListSync;
                s.SearchFolders = folders;
                s.LogLevel = _logLevel;
                s.HelperExecutablePatterns = patterns;
            });
        }
        catch (IOException ex)
        {
            // Speichern gescheitert (Platte voll, Datei gesperrt): nichts übernommen, Fenster bleibt offen.
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, ShellStrings.SaveFailed, ex.Message);
            return false;
        }

        ErrorMessage = null;
        _patternErrorShown = false;
        _original = Capture();
        HasChanges = false;
        _context.OnSettingsSaved(foldersChanged);
        CloseRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Verwirft alle Änderungen.</summary>
    public void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Fügt einen Ordner hinzu (vereinheitlicht, ohne Doppelte). False, wenn er ungültig oder schon da ist.</summary>
    public bool TryAddSearchFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var normalized = PathUtil.Normalize(folder);
        if (!Path.IsPathFullyQualified(normalized))
        {
            return false;
        }

        var existing = SearchFolders.FirstOrDefault(f => PathUtil.PathEquals(f, normalized));
        if (existing is not null)
        {
            SelectedSearchFolder = existing;
            return false;
        }

        SearchFolders.Add(normalized);
        SelectedSearchFolder = normalized;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context.HdrStateChanged -= OnHdrStateChanged;
        HdrList.Dispose();
        foreach (var d in Displays)
        {
            d.PropertyChanged -= OnDisplayChanged;
        }

        CloseRequested = null;
    }

    private static string JoinFolders(IEnumerable<string> folders) => string.Join("\n", folders);

    private bool ReadAutostart(bool configured)
    {
        try
        {
            return _context.Autostart.IsEnabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return configured;
        }
    }

    private void AddSearchFolder()
    {
        var picked = _folderPicker.PickFolder(SelectedSearchFolder ?? SearchFolders.LastOrDefault());
        TryAddSearchFolder(picked);
    }

    private void RemoveSearchFolder()
    {
        if (SelectedSearchFolder is not { } selected)
        {
            return;
        }

        var index = SearchFolders.IndexOf(selected);
        SearchFolders.Remove(selected);
        SelectedSearchFolder = SearchFolders.Count == 0 ? null : SearchFolders[Math.Clamp(index, 0, SearchFolders.Count - 1)];
    }

    private void ResetHelperPatterns() => HelperPatternsText = HelperPatternList.DefaultText;

    private void OpenLogFolder()
    {
        try
        {
            _context.OpenLogFolder();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, ShellStrings.OpenLogFolderFailed, ex.Message);
        }
    }

    private void OnSearchFoldersChanged()
    {
        OnPropertyChanged(nameof(HasSearchFolders));
        UpdateChanges();
    }

    private void LoadDisplays(IReadOnlyList<string> targets)
    {
        var connected = QueryDisplays();
        foreach (var d in connected.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase))
        {
            AddDisplay(new DisplayOptionViewModel(
                d.MonitorDevicePath, DisplayName(d), d.IsPrimary, d.HdrSupported, d.HdrEnabled, isConnected: true,
                isSelected: targets.Contains(d.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)));
        }

        // Gewählte, aber gerade nicht verbundene Bildschirme bleiben sichtbar und gewählt.
        foreach (var path in targets.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Displays.Any(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                AddDisplay(new DisplayOptionViewModel(
                    path, DisplayOptionViewModel.NameFromPath(path), isPrimary: false, hdrSupported: false, hdrEnabled: false, isConnected: false, isSelected: true));
            }
        }
    }

    private void AddDisplay(DisplayOptionViewModel display)
    {
        display.PropertyChanged += OnDisplayChanged;
        Displays.Add(display);
        OnPropertyChanged(nameof(HasDisplays));
    }

    private IReadOnlyList<DisplayInfo> QueryDisplays()
    {
        try
        {
            return _context.GetDisplays();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }

    private static string DisplayName(DisplayInfo d) =>
        string.IsNullOrWhiteSpace(d.FriendlyName) ? DisplayOptionViewModel.NameFromPath(d.MonitorDevicePath) : d.FriendlyName;

    /// <summary>Bildschirm angesteckt, abgezogen oder HDR umgeschaltet: Kennzeichen auffrischen, Auswahl behalten.</summary>
    private void OnHdrStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var current = QueryDisplays();
        foreach (var option in Displays)
        {
            var info = current.FirstOrDefault(d => string.Equals(d.MonitorDevicePath, option.Path, StringComparison.OrdinalIgnoreCase));
            option.IsConnected = info is not null;
            if (info is not null)
            {
                option.Name = DisplayName(info);
                option.IsPrimary = info.IsPrimary;
                option.HdrSupported = info.HdrSupported;
                option.HdrEnabled = info.HdrEnabled;
            }
            else
            {
                option.IsPrimary = false;
                option.HdrEnabled = false;
            }
        }

        foreach (var info in current.Where(d => !Displays.Any(o => string.Equals(o.Path, d.MonitorDevicePath, StringComparison.OrdinalIgnoreCase))))
        {
            AddDisplay(new DisplayOptionViewModel(info.MonitorDevicePath, DisplayName(info), info.IsPrimary, info.HdrSupported, info.HdrEnabled, isConnected: true, isSelected: false));
        }

        OnPropertyChanged(nameof(DisplaysSummary));
    }

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplayOptionViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(DisplaysSummary));
            UpdateChanges();
        }
    }

    /// <summary>
    /// Begrenzt auf 0 bis 30:00 und meldet beide Regler immer: Bei 30 Minuten springt ein Sekunden-Regler auf 5 wieder
    /// auf 0 zurück, obwohl sich der Wert nicht ändert.
    /// </summary>
    private void SetRestartBuffer(int totalSeconds)
    {
        var clamped = Math.Clamp(totalSeconds, 0, AppSettings.RestartBufferMaxSeconds);
        var changed = clamped != _restartBufferTotalSeconds;
        _restartBufferTotalSeconds = clamped;
        OnPropertyChanged(nameof(RestartBufferMinutes));
        OnPropertyChanged(nameof(RestartBufferSeconds));
        if (changed)
        {
            OnPropertyChanged(nameof(RestartBufferTotalSeconds));
            OnPropertyChanged(nameof(RestartBufferText));
            UpdateChanges();
        }
    }

    private void UpdateChanges() => HasChanges = Capture() != _original;

    private Snapshot Capture() => new(
        string.Join("\n", Displays.Where(d => d.IsSelected).Select(d => d.Path)),
        _pollIntervalMs,
        _gracePeriodSeconds,
        _launchTimeoutSeconds,
        _restartBufferTotalSeconds,
        _startWithWindows,
        _notifications,
        _hotkeyFallback,
        _hdrListSync,
        JoinFolders(SearchFolders),
        _logLevel,
        _helperPatterns.IsValid ? string.Join("\n", _helperPatterns.Patterns) : "\u0000" + _helperPatternsText);

    private sealed record Snapshot(
        string Targets,
        int PollIntervalMs,
        int GracePeriodSeconds,
        int LaunchTimeoutSeconds,
        int RestartBufferSeconds,
        bool StartWithWindows,
        bool Notifications,
        bool HotkeyFallback,
        bool HdrListSync,
        string Folders,
        LogLevel LogLevel,
        string Patterns);
}
