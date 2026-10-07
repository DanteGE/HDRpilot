using System.Windows.Media;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Services;

/// <summary>Tray-Symbol mit Menü (HDR jetzt umschalten, Automatik pausieren, Fenster öffnen, Protokollordner, Beenden).</summary>
public interface ITrayService : IDisposable
{
    void Initialize();

    /// <summary>Symbol und Tooltip an den aktuellen HDR-Zustand und die Pause anpassen.</summary>
    void Refresh();

    /// <summary>Kurze Benachrichtigung, die nie den Fokus nimmt. Wird unterdrückt, wenn Benachrichtigungen aus sind.</summary>
    void Notify(string title, string message, bool isWarning = false);
}

/// <summary>Verknüpfungen „HdrPilot.exe --run &lt;Quelle:ID&gt;“ auf Desktop und im Startmenü.</summary>
public interface IShortcutService
{
    string CreateDesktopShortcut(GameConfig game);

    string CreateStartMenuShortcut(GameConfig game);
}

/// <summary>Symbol aus der EXE eines Spiels (zwischengespeichert, im Hintergrund geladen).</summary>
public interface IGameIconService
{
    ImageSource? GetCachedIcon(GameConfig game);

    Task<ImageSource?> GetIconAsync(GameConfig game, CancellationToken cancellationToken = default);
}

/// <summary>Autostart über HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
public interface IAutostartService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>Meldet Bildschirmänderungen (WM_DISPLAYCHANGE, Aufwachen aus dem Ruhezustand), entprellt.</summary>
public interface IDisplayEventsService : IDisposable
{
    event EventHandler? DisplaysChanged;

    void Start();
}
