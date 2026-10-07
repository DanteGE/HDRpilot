using HdrPilot.Core.Native;
using static HdrPilot.Core.Native.ToolhelpNative;

namespace HdrPilot.Core.Processes;

/// <summary>
/// Prozessliste über CreateToolhelp32Snapshot. Den EXE-Pfad liest es nur für neue Prozesse, mit
/// PROCESS_QUERY_LIMITED_INFORMATION und QueryFullProcessImageNameW; jedes Handle wird sofort geschlossen.
/// Bekannte Prozesse (gleiche PID, gleicher Name) kommen aus dem Zwischenspeicher. Es wird nichts in fremde
/// Prozesse geschrieben oder eingehängt.
/// </summary>
public sealed unsafe class Win32ProcessSnapshotProvider : IProcessSnapshotProvider
{
    private readonly Dictionary<uint, ProcessInfo> _cache = [];
    private readonly object _gate = new();

    /// <summary>Anzahl Pfadabfragen seit Start (Diagnose: zeigt, dass nur neue Prozesse abgefragt werden).</summary>
    public long PathQueries { get; private set; }

    public IReadOnlyList<ProcessInfo> GetProcesses()
    {
        lock (_gate)
        {
            var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero)
            {
                return _cache.Values.ToList();
            }

            var result = new List<ProcessInfo>(_cache.Count + 16);
            var seen = new HashSet<uint>();
            try
            {
                var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
                if (!Process32FirstW(snapshot, &entry))
                {
                    return result;
                }

                do
                {
                    var pid = entry.th32ProcessID;
                    if (pid == 0)
                    {
                        continue; // Leerlaufprozess
                    }

                    var name = ReadName(entry.szExeFile);
                    if (!_cache.TryGetValue(pid, out var info) || !string.Equals(info.ExeName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        info = new ProcessInfo((int)pid, name, QueryPath(pid));
                        _cache[pid] = info;
                    }

                    seen.Add(pid);
                    result.Add(info);
                }
                while (Process32NextW(snapshot, &entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            if (seen.Count != _cache.Count)
            {
                foreach (var gone in _cache.Keys.Where(k => !seen.Contains(k)).ToList())
                {
                    _cache.Remove(gone);
                }
            }

            return result;
        }
    }

    private string? QueryPath(uint pid)
    {
        PathQueries++;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null; // geschützter Prozess oder schon beendet
        }

        try
        {
            const int capacity = 1024;
            var buffer = stackalloc char[capacity];
            var size = (uint)capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string ReadName(char* buffer)
    {
        var length = 0;
        while (length < MAX_PATH && buffer[length] != '\0')
        {
            length++;
        }

        return new string(buffer, 0, length);
    }
}
