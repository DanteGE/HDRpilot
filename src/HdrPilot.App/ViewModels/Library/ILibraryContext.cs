using HdrPilot.App.Services;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Sessions;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Schmale Sicht der Spielebibliothek auf die App (<see cref="AppLibraryContext"/> für AppServices, in Tests eine
/// Attrappe). Alle Ereignisse kommen auf dem UI-Thread an. Wird mit dem Fenster freigegeben.
/// </summary>
public interface ILibraryContext : IDisposable
{
    /// <summary>Aktueller, unveränderlicher Stand der Konfiguration.</summary>
    AppConfig Config { get; }

    IFileSystem FileSystem { get; }

    IAppEnvironment Environment { get; }

    IClock Clock { get; }

    IGameIconService Icons { get; }

    IShortcutService Shortcuts { get; }

    IUiDispatcher Ui { get; }

    IReadOnlyList<SessionInfo> Sessions { get; }

    bool IsPaused { get; }

    bool IsScanning { get; }

    /// <summary>Ergebnis des letzten Einlesens in dieser Sitzung (Fehler je Scanner).</summary>
    LibraryScanReport? LastScan { get; }

    /// <summary>Konfiguration ändern: der Reihe nach und außerhalb des UI-Threads (Speichern dauert).</summary>
    Task UpdateConfigAsync(Action<AppConfig> mutate);

    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>Bildschirme, die HdrPilot ohne Spiel-Einstellung schaltet (globale Einstellung).</summary>
    IReadOnlyList<DisplayInfo> TargetDisplays();

    void SetPaused(bool paused);

    Task ToggleHdrNowAsync();

    Task RescanAsync();

    Task<LaunchOutcome> LaunchAsync(string gameKey);

    void ShowSettings();

    /// <summary>Konfiguration wurde geändert (von der Oberfläche, der Automatik oder dem Einlesen).</summary>
    event EventHandler? ConfigChanged;

    /// <summary>HDR-Zustand, Pause oder laufende Sitzungen könnten sich geändert haben.</summary>
    event EventHandler? HdrStateChanged;

    /// <summary>Einlesen begonnen oder beendet.</summary>
    event EventHandler? ScanStateChanged;
}

/// <summary>Arbeit später auf dem UI-Thread ausführen (in Tests sofort).</summary>
public interface IUiDispatcher
{
    /// <summary>Später auf dem UI-Thread ausführen, mit niedriger Priorität.</summary>
    void Post(Action action);

    /// <summary>Einmal nach einer Wartezeit ausführen; Dispose bricht ab. Läuft nie dauerhaft.</summary>
    IDisposable Delay(TimeSpan delay, Action action);
}

/// <summary>Rückfragen und Dateiauswahl der Spielebibliothek (in Tests ersetzbar).</summary>
public interface ILibraryDialogs
{
    string? PickExecutable(string? initialDirectory);

    string? PickFolder(string? initialDirectory);

    bool Confirm(string title, string message, string confirmText, bool isDanger);

    /// <summary>Zeigt den Dialog „Spiel hinzufügen“. True, wenn bestätigt (Ergebnis in <see cref="AddGameViewModel.Result"/>).</summary>
    bool ShowAddGame(AddGameViewModel viewModel);
}
