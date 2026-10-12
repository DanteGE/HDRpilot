using HdrPilot.Core.Config;
using HdrPilot.Core.Sessions;

namespace HdrPilot.Core.AutoHdr;

/// <summary>Stand des Auto-HDR-Werts einer Spiel-EXE aus Sicht von HDRpilot.</summary>
public enum AutoHdrTargetState
{
    /// <summary>HDRpilot hat <c>AutoHDREnable=2097</c> eingetragen; wirkt ab dem nächsten Start des Spiels.</summary>
    Applied,

    /// <summary>Stand schon vorher so in Windows: nichts geschrieben, nichts zurückzunehmen.</summary>
    AlreadyOn,

    /// <summary>Seit dem Schreiben außerhalb von HDRpilot geändert: bleibt, wie er ist (protokolliert).</summary>
    ExternallyChanged,

    /// <summary>Vorhandener Wert ist kein sauberer REG_SZ aus <c>Schlüssel=Wert;</c>-Paaren: nie angefasst.</summary>
    Unsupported,

    /// <summary>Merken oder Schreiben scheiterte; neuer Versuch beim nächsten Abgleich.</summary>
    Failed,
}

/// <param name="ExePath">Voller Pfad der Spiel-EXE (zugleich der Wertname in der Registry).</param>
public sealed record AutoHdrTargetStatus(string ExePath, AutoHdrTargetState State);

/// <summary>
/// Stand eines Spiels für die Oberfläche: je Ziel-EXE eine Zeile. Leer: Option aus, noch kein Abgleich oder noch keine
/// Spiel-EXE bekannt (die Oberfläche unterscheidet das über die Option).
/// </summary>
public sealed record AutoHdrGameStatus(IReadOnlyList<AutoHdrTargetStatus> Targets)
{
    public static AutoHdrGameStatus None { get; } = new([]);

    public int Count(AutoHdrTargetState state) => Targets.Count(t => t.State == state);
}

/// <summary>Ergebnis von „Auto-HDR-Einträge zurücknehmen“.</summary>
/// <param name="GamesSwitchedOff">Spiele, bei denen „Windows Auto HDR“ dabei ausgeschaltet wurde.</param>
/// <param name="Restored">Werte, deren Originalzustand wiederhergestellt ist (auch der globale Schalter).</param>
/// <param name="LeftAsIs">Seit dem Schreiben außerhalb geänderte Werte: bleiben, wie sie sind.</param>
/// <param name="Failed">Ließen sich nicht zurücknehmen; bleiben gemerkt, neuer Versuch beim nächsten Abgleich.</param>
/// <param name="Error">Die Konfiguration ließ sich nicht speichern (dann wurde nichts zurückgenommen), sonst null.</param>
public sealed record AutoHdrRollbackResult(int GamesSwitchedOff, int Restored, int LeftAsIs, int Failed, string? Error = null)
{
    public bool Success => Error is null && Failed == 0;
}

/// <summary>
/// Was App und Oberfläche von Auto HDR brauchen (E88). Umsetzung: <see cref="AutoHdrService"/>; in Tests eine Attrappe.
/// Diese Schnittstelle ist zwischen Automatik und Oberfläche abgesprochen und ändert sich nicht.
/// </summary>
public interface IAutoHdrService
{
    /// <summary>Wohin geschrieben wird (echter Schlüssel oder Testwurzel).</summary>
    AutoHdrRegistryLocation Location { get; }

    /// <summary>Stand eines Spiels aus dem letzten Abgleich (aus dem Speicher, ohne Registry-Zugriff). Wirft nie.</summary>
    AutoHdrGameStatus GetStatus(string gameKey);

    /// <summary>Stand hat sich geändert (Abgleich, Rücknahme). Kann von jedem Thread kommen.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// „Auto-HDR-Einträge zurücknehmen“: schaltet „Windows Auto HDR“ bei allen Spielen aus und nimmt alle Werte zurück, die
    /// HDRpilot geschrieben hat, auch den globalen Schalter der Notlösung. Wirft nicht (Fehler stehen im Ergebnis).
    /// </summary>
    Task<AutoHdrRollbackResult> RollbackAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Wie die Sitzungslogik Auto HDR über Spielstarts informiert (globale Notlösung, Werte je Spiel vor einem angekündigten
/// Start sicherstellen). Alle Aufrufe kommen nacheinander unter der Sperre der Sitzungslogik; sie rufen nie zurück in die
/// Sitzungslogik, kehren schnell zurück und werfen nie.
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
    /// Jeder weitere Zyklus: zugeordnete laufende Spiele der Bibliothek, auch ohne HDR-Sitzung. Bei pausierter Automatik
    /// (<paramref name="paused"/>) zählen neu erkannte Spiele nicht, schon erfasste laufen normal aus.
    /// </summary>
    void OnCycle(IReadOnlyCollection<string> runningGameKeys, AppConfig config, bool paused, DateTimeOffset now);
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

    public void OnCycle(IReadOnlyCollection<string> runningGameKeys, AppConfig config, bool paused, DateTimeOffset now)
    {
    }
}
