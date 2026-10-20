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

    /// <summary>
    /// HDRpilot-Verknüpfungen eines Spiels für „Einstellungen entfernen…“ (E90): nur .lnk mit Ziel HDRpilot.exe und genau
    /// <c>--run "&lt;Schlüssel&gt;"</c>. Liest nur, nicht auf dem UI-Thread. Standard: keine.
    /// </summary>
    IReadOnlyList<GameShortcut> FindFor(string gameKey) => [];

    /// <summary>
    /// Löscht die Verknüpfungen, die beim Löschen noch genau zu diesem Spiel passen; liefert die gelöschten. Andere Dateien
    /// nie. Standard: keine.
    /// </summary>
    IReadOnlyList<GameShortcut> Delete(string gameKey, IReadOnlyCollection<GameShortcut> shortcuts) => [];

    /// <summary>
    /// Zeigt noch eine Verknüpfung diese Symboldatei (E90, bevor die .ico gelöschter Verknüpfungen fällt)? Liest nur, auch
    /// in Unterordnern, im gemeinsamen Desktop und Startmenü und bei den angehefteten Verknüpfungen der Taskleiste. Nicht
    /// auf dem UI-Thread. Standard: nein.
    /// </summary>
    bool IsIconInUse(string iconPath) => false;
}

/// <summary>
/// Symbol eines Spiels (zwischengespeichert, im Hintergrund geladen): eigenes Symbol, Symbol des Launchers oder der EXE
/// (E86).
/// </summary>
public interface IGameIconService
{
    ImageSource? GetCachedIcon(GameConfig game);

    Task<ImageSource?> GetIconAsync(GameConfig game, CancellationToken cancellationToken = default);

    /// <summary>Zwischenspeicher leeren (beim Schließen des Fensters), wartende Anfragen freigeben.</summary>
    void ClearCache();

    /// <summary>Symbol eines Spiels vergessen, damit die nächste Anfrage neu lädt (eigenes Symbol geändert).</summary>
    void Forget(string gameKey);

    /// <summary>
    /// Spiel aus der Bibliothek entfernt (E90): Symbol vergessen und den Eintrag in icon-cache.json löschen (gleich
    /// geschrieben). Dateien löscht der Aufrufer. Standard: nur vergessen.
    /// </summary>
    void ForgetGame(string gameKey) => Forget(gameKey);

    /// <summary>
    /// Eigenes Symbol aus einer Bild- oder EXE-Datei erzeugen und im Datenordner speichern (verkleinert auf 96 px).
    /// Liefert den Dateinamen für <see cref="GameConfig.CustomIcon"/>. Wirft InvalidDataException, wenn die Datei
    /// kein Symbol hergibt, und IO-Ausnahmen beim Schreiben. Läuft nicht auf dem UI-Thread.
    /// </summary>
    string SaveCustomIcon(GameConfig game, string sourcePath);

    /// <summary>Gespeichertes eigenes Symbol löschen (ungültige oder fehlende Namen: nichts).</summary>
    void DeleteCustomIcon(string? fileName);

    /// <summary>
    /// Symboldatei für eine Verknüpfung „Mit HDR starten“ in der Reihenfolge der Liste, soweit eine Verknüpfung sie zeigen
    /// kann: ein eigenes Symbol als .ico neben seiner PNG, sonst die gemerkte automatische Wahl, wenn sie noch gilt, sonst
    /// dieselbe Wahl ohne tiefere Suche; ist die Wahl ein Bild, die EXE wie in der Liste (Standardsymbole übersprungen); null
    /// ohne Quelle. Schreibt höchstens die .ico im Datenordner. Nicht auf dem UI-Thread aufrufen (liest ohne gemerkte Wahl
    /// Symbole).
    /// </summary>
    string? ShortcutIconFile(GameConfig game);
}

/// <summary>Text in die Zwischenablage legen (in Tests ersetzbar, damit kein Test die echte Zwischenablage berührt).</summary>
public interface IClipboard
{
    /// <summary>Ersetzt den Inhalt der Zwischenablage. Ist sie gerade belegt, kommt eine ExternalException.</summary>
    void SetText(string text);
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
