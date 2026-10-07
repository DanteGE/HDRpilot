using System.Runtime.InteropServices;
using HdrPilot.Core.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static HdrPilot.Core.Native.NtNative;

namespace HdrPilot.Core.Processes;

/// <summary>CPU-Zeit eines Prozesses zu einem Zeitpunkt.</summary>
/// <param name="CreateTime">Startzeit (FILETIME, UTC). Ändert sie sich, gehört die PID inzwischen einem anderen Prozess.</param>
/// <param name="CpuTime">Benutzer- plus Kernelzeit in 100-ns-Einheiten.</param>
public readonly record struct ProcessCpuSample(long CreateTime, long CpuTime);

/// <summary>
/// Aktivität laufender Prozesse, gelesen ohne Handle auf sie (E65–E67): CPU-Zeit aus der Prozessliste des Systems und
/// ob ihnen ein sichtbares Hauptfenster gehört. Für den Hinweis „Spiel hängt im Hintergrund“.
/// </summary>
public interface IProcessActivityProbe
{
    /// <summary>CPU-Zeit je PID. PIDs, die nicht (mehr) laufen oder nicht lesbar sind, fehlen im Ergebnis.</summary>
    IReadOnlyDictionary<int, ProcessCpuSample> SampleCpu(IReadOnlySet<int> processIds);

    /// <summary>Die PIDs aus der Menge, denen mindestens ein sichtbares (auch minimiertes) Hauptfenster gehört.</summary>
    IReadOnlySet<int> WithVisibleWindow(IReadOnlySet<int> processIds);

    /// <summary>Puffer freigeben, solange keine Abfrage gebraucht wird (kein Spiel läuft).</summary>
    void ReleaseBuffers();
}

/// <summary>
/// CPU-Zeit über NtQuerySystemInformation(SystemProcessInformation = 5): eine Abfrage für alle Prozesse, ohne
/// OpenProcess (auch geschützte Prozesse und Spiele mit Anti-Cheat). Fenster über EnumWindows,
/// GetWindowThreadProcessId und IsWindowVisible: liest nur Fensterdaten, schickt keine Nachricht an das Spiel.
/// Der Puffer wird wiederverwendet und freigegeben, sobald kein Spiel mehr läuft (<see cref="ReleaseBuffers"/>).
/// </summary>
public sealed unsafe class NtProcessActivityProbe : IProcessActivityProbe, IDisposable
{
    /// <summary>Startgröße; mit Thread-Daten braucht die Abfrage bei rund 300 Prozessen einige hundert KB.</summary>
    private const uint InitialBufferSize = 512 * 1024;

    private readonly ILogger _logger;
    private readonly object _gate = new();
    private IntPtr _buffer;
    private uint _bufferSize = InitialBufferSize;
    private bool _failureLogged;

    public NtProcessActivityProbe(ILogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Ob gerade ein Puffer belegt ist (Diagnose und Tests).</summary>
    public bool HoldsBuffer
    {
        get
        {
            lock (_gate)
            {
                return _buffer != IntPtr.Zero;
            }
        }
    }

    public IReadOnlyDictionary<int, ProcessCpuSample> SampleCpu(IReadOnlySet<int> processIds)
    {
        var result = new Dictionary<int, ProcessCpuSample>(processIds.Count);
        if (processIds.Count == 0)
        {
            return result;
        }

        lock (_gate)
        {
            var status = Query();
            if (status != STATUS_SUCCESS)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    _logger.LogWarning("CPU-Zeit der Spielprozesse nicht lesbar (Status 0x{Status:X8}); kein Hinweis auf hängende Spiele.", status);
                }

                return result;
            }

            var p = (byte*)_buffer;
            var end = p + _bufferSize;
            while (p + sizeof(SYSTEM_PROCESS_INFORMATION_TIMES) <= end)
            {
                var e = (SYSTEM_PROCESS_INFORMATION_TIMES*)p;
                var pid = (int)e->UniqueProcessId;
                if (pid != 0 && processIds.Contains(pid))
                {
                    result[pid] = new ProcessCpuSample(e->CreateTime, e->UserTime + e->KernelTime);
                    if (result.Count == processIds.Count)
                    {
                        break;
                    }
                }

                if (e->NextEntryOffset == 0)
                {
                    break;
                }

                p += e->NextEntryOffset;
            }
        }

        return result;
    }

    public IReadOnlySet<int> WithVisibleWindow(IReadOnlySet<int> processIds)
    {
        var search = new WindowSearch(processIds);
        if (processIds.Count == 0)
        {
            return search.Found;
        }

        var handle = GCHandle.Alloc(search);
        try
        {
            // Rückgabe false heißt auch „vorzeitig beendet, alle gefunden“: das Ergebnis steht in search.Found.
            _ = WindowNative.EnumWindows(&CollectVisible, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return search.Found;
    }

    public void ReleaseBuffers()
    {
        lock (_gate)
        {
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero; // die Größe bleibt als Startwert für die nächste Abfrage
            }
        }
    }

    public void Dispose() => ReleaseBuffers();

    private uint Query()
    {
        if (_buffer == IntPtr.Zero)
        {
            _buffer = Marshal.AllocHGlobal((int)_bufferSize);
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var status = NtQuerySystemInformation(SystemProcessInformation, (void*)_buffer, _bufferSize, out var needed);
            if (status is not (STATUS_INFO_LENGTH_MISMATCH or STATUS_BUFFER_TOO_SMALL))
            {
                return status;
            }

            // Puffer vergrößern (mit Reserve, weil zwischen den Aufrufen Prozesse und Threads dazukommen können).
            _bufferSize = Math.Max(_bufferSize * 2, needed + (64 * 1024));
            Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)_bufferSize);
        }

        return STATUS_INFO_LENGTH_MISMATCH;
    }

    /// <summary>Rückruf von EnumWindows: merkt sich Besitzer sichtbarer Fenster aus der gesuchten Menge.</summary>
    [UnmanagedCallersOnly]
    private static int CollectVisible(nint hWnd, nint lParam)
    {
        var search = (WindowSearch)GCHandle.FromIntPtr(lParam).Target!;
        if (WindowNative.GetWindowThreadProcessId(hWnd, out var pid) != 0
            && search.Wanted.Contains((int)pid)
            && !search.Found.Contains((int)pid)
            && WindowNative.IsWindowVisible(hWnd))
        {
            search.Found.Add((int)pid);
            if (search.Found.Count == search.Wanted.Count)
            {
                return 0; // alle gefunden: Aufzählung beenden
            }
        }

        return 1;
    }

    private sealed class WindowSearch(IReadOnlySet<int> wanted)
    {
        public IReadOnlySet<int> Wanted { get; } = wanted;

        public HashSet<int> Found { get; } = [];
    }
}
