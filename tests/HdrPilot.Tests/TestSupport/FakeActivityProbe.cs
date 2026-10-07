using HdrPilot.Core.Processes;

namespace HdrPilot.Tests.TestSupport;

/// <summary>
/// Aktivität der Spielprozesse aus dem Test: CPU-Zeit und Startzeit je PID, Besitzer sichtbarer Fenster, Aufrufe gezählt.
/// PIDs ohne Eintrag in <see cref="CpuTime"/> gelten als nicht lesbar (zählen als aktiv).
/// </summary>
public sealed class FakeActivityProbe : IProcessActivityProbe
{
    public Dictionary<int, long> CpuTime { get; } = [];

    /// <summary>Startzeit je PID (Standard 1); eine neue Startzeit heißt: PID gehört einem anderen Prozess.</summary>
    public Dictionary<int, long> CreateTime { get; } = [];

    public HashSet<int> Windows { get; } = [];

    public int CpuQueries { get; private set; }

    public int WindowQueries { get; private set; }

    public int Releases { get; private set; }

    /// <summary>Lässt einen Prozess CPU-Zeit verbrauchen (Aktivität).</summary>
    public void Burn(int pid, long ticks = 10_000) => CpuTime[pid] = CpuTime.GetValueOrDefault(pid) + ticks;

    public IReadOnlyDictionary<int, ProcessCpuSample> SampleCpu(IReadOnlySet<int> processIds)
    {
        CpuQueries++;
        return processIds
            .Where(CpuTime.ContainsKey)
            .ToDictionary(pid => pid, pid => new ProcessCpuSample(CreateTime.GetValueOrDefault(pid, 1), CpuTime[pid]));
    }

    public IReadOnlySet<int> WithVisibleWindow(IReadOnlySet<int> processIds)
    {
        WindowQueries++;
        return processIds.Where(Windows.Contains).ToHashSet();
    }

    public void ReleaseBuffers() => Releases++;
}
