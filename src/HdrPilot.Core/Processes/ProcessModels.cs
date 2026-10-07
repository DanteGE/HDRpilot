namespace HdrPilot.Core.Processes;

/// <summary>Ein laufender Prozess. <see cref="ExePath"/> ist null, wenn der Pfad nicht lesbar ist (geschützte Prozesse).</summary>
public sealed record ProcessInfo(int ProcessId, string ExeName, string? ExePath);

/// <summary>Ergebnis eines Abfragezyklus: neue und beendete Prozesse aus der Differenz, dazu die vollständige Liste.</summary>
public sealed record ProcessChanges(
    IReadOnlyList<ProcessInfo> Started,
    IReadOnlyList<ProcessInfo> Exited,
    IReadOnlyList<ProcessInfo> Current)
{
    public static ProcessChanges Empty { get; } = new([], [], []);
}

/// <summary>Liefert die aktuelle Prozessliste.</summary>
public interface IProcessSnapshotProvider
{
    IReadOnlyList<ProcessInfo> GetProcesses();
}

/// <summary>Fragt die Prozessliste zyklisch ab und ruft je Zyklus einen Rückruf auf (nie überlappend).</summary>
public interface IProcessWatcher : IDisposable
{
    TimeSpan Interval { get; set; }

    bool IsRunning { get; }

    /// <summary>Ein Abfragezyklus: Differenz zur vorigen Abfrage.</summary>
    ProcessChanges Poll();

    /// <summary>Startet die Hintergrundabfrage. Der Rückruf wird nach jedem Zyklus abgewartet, bevor der nächste beginnt.</summary>
    void Start(Func<ProcessChanges, CancellationToken, Task> onPoll);

    Task StopAsync();
}
