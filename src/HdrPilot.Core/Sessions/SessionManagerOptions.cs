using HdrPilot.Core.Processes;

namespace HdrPilot.Core.Sessions;

/// <summary>
/// Feste Schwellen der Sitzungslogik für Neustart-Puffer und Hinweis „Spiel hängt“ (vom Nutzer im gemeinsamen Test
/// G4.5 abgestimmt, keine Einstellung; nur die Länge des Neustart-Puffers ist einstellbar, E85) und die Abfragen dafür.
/// Standard: Werte für den Betrieb; Tests setzen eigene.
/// </summary>
public sealed class SessionManagerOptions
{
    public static SessionManagerOptions Default { get; } = new();

    /// <summary>
    /// Lief das Spiel kürzer (erster erkannter Prozess bis „kein Prozess“), gilt die Sitzung als kurz: Absturz, Hänger
    /// oder Neustart von Hand. Beobachtet mit Dead Island 2 (hing gleich nach dem Umschalten, Neustart nach 96 s).
    /// </summary>
    public TimeSpan ShortSessionLimit { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Nachlauf nach einer kurzen Sitzung, die HdrPilot selbst eingeschaltet hat (die längere von dieser und der
    /// eingestellten Nachlaufzeit gilt). Startet das Spiel in dieser Zeit neu, läuft die Sitzung einfach weiter.
    /// Standard, solange <see cref="Config.AppSettings.RestartBufferSeconds"/> nicht gesetzt ist (Einstellung
    /// „Neustart-Puffer“). 1 Minute seit dem Helldivers-2-Test des Nutzers (E85, vorher 3 Minuten: HDR blieb nach dem
    /// Schließen spürbar zu lange an). <see cref="TimeSpan.Zero"/> schaltet den Neustart-Puffer ab.
    /// </summary>
    public TimeSpan RestartBuffer { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Läuft nach dem Neustart-Puffer eine Vollbild-Anwendung (etwa ein anderes Spiel beim Start), wartet das
    /// Zurückstellen, bis sie weg ist, aber höchstens so lange (sonst bliebe HDR womöglich ewig an).
    /// </summary>
    public TimeSpan MaxFullscreenHold { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Abstand der Prüfung auf hängende Spiele; geprüft wird nur, solange eine Sitzung läuft.</summary>
    public TimeSpan HangCheckInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// So lange ohne sichtbares Fenster und ohne zusätzliche CPU-Zeit (alle Prozesse der Sitzung), dann ein Hinweis.
    /// Beobachtet mit Palworld: Prozess lief nach dem Beenden unsichtbar weiter, HDR blieb (richtigerweise) an.
    /// </summary>
    public TimeSpan HangThreshold { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Ob eine Vollbild-Anwendung im Vordergrund läuft. Null: SHQueryUserNotificationState.</summary>
    public Func<bool>? FullscreenAppRunning { get; init; }

    /// <summary>
    /// Ob das Tray Hinweise gerade nur protokolliert (Vollbild, gesperrter Bildschirm, Bildschirmschoner): dann wartet der
    /// Hinweis „Spiel hängt“. Null: SHQueryUserNotificationState.
    /// </summary>
    public Func<bool>? NotificationsSuppressed { get; init; }

    /// <summary>CPU-Zeit und Fenster der Spielprozesse. Null: <see cref="NtProcessActivityProbe"/>.</summary>
    public IProcessActivityProbe? ActivityProbe { get; init; }
}
