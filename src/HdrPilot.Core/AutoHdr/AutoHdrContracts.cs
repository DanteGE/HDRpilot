using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;

namespace HdrPilot.Core.AutoHdr;

/// <summary>Stand des globalen Auto-HDR-Schalters von Windows (Token <c>AutoHDREnable</c> in DirectXUserGlobalSettings).</summary>
public enum AutoHdrSwitchState
{
    /// <summary>Aus: Token fehlt, Wert fehlt oder Token ungleich <c>1</c>.</summary>
    Off,

    /// <summary>An: <c>AutoHDREnable=1</c>, wie „Auto HDR“ in den Windows-Einstellungen.</summary>
    On,

    /// <summary>Nicht lesbar oder kein sauberer REG_SZ: HDRpilot fasst den Wert nicht an.</summary>
    Unknown,
}

/// <summary>
/// Was die App von Windows Auto HDR braucht (E88). Umsetzung: <see cref="AutoHdrService"/>; in Tests eine Attrappe.
/// Den Schalter je Spiel (<see cref="GameConfig.AutoHdr"/>) setzt die Oberfläche in der Konfiguration; wirksam wird er nur
/// bei Starts, die HDRpilot vorher kennt (<see cref="IAutoHdrSessionHooks"/>).
/// </summary>
public interface IAutoHdrService
{
    /// <summary>Wohin geschrieben wird (echter Schlüssel oder Testwurzel).</summary>
    AutoHdrRegistryLocation Location { get; }

    /// <summary>HDRpilot hält den globalen Schalter gerade für einen Spielstart (stellt ihn nach der Sitzung zurück).</summary>
    bool HoldsGlobalSwitch { get; }

    /// <summary>Liest den globalen Schalter jetzt aus der Registry (etwa beim Öffnen des Tray-Menüs). Wirft nie.</summary>
    AutoHdrSwitchState ReadGlobalSwitch();

    /// <summary>
    /// Der Nutzer schaltet den globalen Schalter selbst (Tray-Eintrag „Auto HDR“, G9.9): setzt nur das Token (an
    /// <c>1</c>, aus <c>0</c>; andere Tokens, Reihenfolge und Abschluss bleiben) und gilt ab dem nächsten Spielstart, auch
    /// ohne HDRpilot. Das ist die Wahl des Nutzers: Hält HDRpilot den Schalter gerade für eine Sitzung, gibt es ihn ab und
    /// stellt danach nichts zurück. Liefert den Stand danach (<see cref="AutoHdrSwitchState.Unknown"/> bei einem Fehler
    /// oder fremdem Format; dann ist nichts geschrieben). Wirft nie.
    /// </summary>
    AutoHdrSwitchState SetGlobalSwitchByUser(bool on);
}

/// <summary>
/// Wie die Sitzungslogik Auto HDR über Spielstarts informiert (E88): Der globale Schalter wird direkt vor einem Start
/// gesetzt, den HDRpilot vorher kennt, und nach der Sitzung zurückgestellt. Alle Aufrufe kommen nacheinander unter der
/// Sperre der Sitzungslogik; sie rufen nie zurück in die Sitzungslogik, kehren schnell zurück und werfen nie.
/// </summary>
public interface IAutoHdrSessionHooks
{
    /// <summary>
    /// Ein Start ist angekündigt, der Spielprozess folgt gleich: Start über HDRpilot („Mit HDR starten“, Verknüpfung),
    /// Steam-Startoption oder Frühwarnung eines Launchers. Kommt vor dem Schalten von HDR und vor dem Start-Befehl.
    /// </summary>
    void OnStartAnnounced(GameConfig game, SessionTrigger trigger, DateTimeOffset now);

    /// <summary>Erster Zyklus nach dem Start von HDRpilot (Wiederanlauf): zugeordnete laufende Spiele der Bibliothek.</summary>
    void OnRecovery(IReadOnlyCollection<string> runningGameKeys, AppConfig config, DateTimeOffset now);

    /// <summary>
    /// Jeder Zyklus: zugeordnete laufende Spiele der Bibliothek (auch ohne HDR-Sitzung) und Spiele, deren HDR-Sitzung noch
    /// auf den Spielprozess wartet (<paramref name="waitingGameKeys"/>: Startfrist samt Verlängerung für Launcher-Fenster).
    /// </summary>
    void OnCycle(IReadOnlyCollection<string> runningGameKeys, IReadOnlyCollection<string> waitingGameKeys, AppConfig config, DateTimeOffset now);
}

/// <summary>Ohne Auto HDR (Standard der Sitzungslogik in Tests).</summary>
public sealed class NoAutoHdrSessionHooks : IAutoHdrSessionHooks
{
    public static NoAutoHdrSessionHooks Instance { get; } = new();

    public void OnStartAnnounced(GameConfig game, SessionTrigger trigger, DateTimeOffset now)
    {
    }

    public void OnRecovery(IReadOnlyCollection<string> runningGameKeys, AppConfig config, DateTimeOffset now)
    {
    }

    public void OnCycle(IReadOnlyCollection<string> runningGameKeys, IReadOnlyCollection<string> waitingGameKeys, AppConfig config, DateTimeOffset now)
    {
    }
}
