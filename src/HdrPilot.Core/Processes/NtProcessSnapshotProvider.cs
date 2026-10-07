using System.Runtime.InteropServices;
using HdrPilot.Core.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static HdrPilot.Core.Native.NtNative;

namespace HdrPilot.Core.Processes;

/// <summary>
/// Sparsame Prozessliste über NtQuerySystemInformation (winternl.h). Ab Windows 11 24H2 mit
/// SystemBasicProcessInformation (nur PID, Name, Sequenznummer, keine Thread-Daten; gemessen etwa 30-mal billiger als
/// CreateToolhelp32Snapshot), sonst SystemProcessInformation, im Notfall Toolhelp. Der Puffer wird wiederverwendet.
/// Den EXE-Pfad liest es nur für neue Prozesse, ohne Handle auf sie (<see cref="ProcessImagePathResolver"/>).
/// </summary>
public sealed unsafe class NtProcessSnapshotProvider : IProcessSnapshotProvider, IDisposable
{
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<uint, CacheEntry> _cache = [];
    private readonly ProcessImagePathResolver _paths;
    private readonly Win32ProcessSnapshotProvider _toolhelpFallback;
    private IntPtr _buffer;
    private uint _bufferSize = 128 * 1024;
    private int _infoClass = SystemBasicProcessInformation;
    private bool _useToolhelp;

    public NtProcessSnapshotProvider(ILogger? logger = null)
        : this(logger, SystemBasicProcessInformation)
    {
    }

    /// <summary>Für Tests: mit einer bestimmten Informationsklasse beginnen (etwa 5 wie unter Windows 10).</summary>
    internal NtProcessSnapshotProvider(ILogger? logger, int infoClass)
    {
        _logger = logger ?? NullLogger.Instance;
        _paths = new ProcessImagePathResolver(logger);
        _toolhelpFallback = new Win32ProcessSnapshotProvider(_paths);
        _infoClass = infoClass;
        _buffer = Marshal.AllocHGlobal((int)_bufferSize);
    }

    /// <summary>Welcher Weg gerade benutzt wird (Diagnose).</summary>
    public string Method => _useToolhelp ? "Toolhelp" : _infoClass == SystemBasicProcessInformation ? "SystemBasicProcessInformation" : "SystemProcessInformation";

    public long PathQueries { get; private set; }

    public IReadOnlyList<ProcessInfo> GetProcesses()
    {
        lock (_gate)
        {
            if (_useToolhelp)
            {
                return _toolhelpFallback.GetProcesses();
            }

            var status = Query();
            if (status != STATUS_SUCCESS && _infoClass == SystemBasicProcessInformation)
            {
                _logger.LogInformation("SystemBasicProcessInformation nicht verfügbar (Status 0x{Status:X8}), nutze SystemProcessInformation.", status);
                _infoClass = SystemProcessInformation;
                status = Query();
            }

            if (status != STATUS_SUCCESS)
            {
                _logger.LogWarning("NtQuerySystemInformation fehlgeschlagen (Status 0x{Status:X8}), nutze Toolhelp.", status);
                _useToolhelp = true;
                return _toolhelpFallback.GetProcesses();
            }

            return Parse();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
        }
    }

    private uint Query()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var status = NtQuerySystemInformation(_infoClass, (void*)_buffer, _bufferSize, out var needed);
            if (status is not (STATUS_INFO_LENGTH_MISMATCH or STATUS_BUFFER_TOO_SMALL))
            {
                return status;
            }

            // Puffer vergrößern (mit Reserve, weil zwischen den Aufrufen Prozesse dazukommen können).
            _bufferSize = Math.Max(_bufferSize * 2, needed + 64 * 1024);
            Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)_bufferSize);
        }

        return STATUS_INFO_LENGTH_MISMATCH;
    }

    private List<ProcessInfo> Parse()
    {
        var result = new List<ProcessInfo>(_cache.Count + 16);
        var seen = new HashSet<uint>();
        var p = (byte*)_buffer;
        var end = p + _bufferSize;
        while (p < end)
        {
            uint next;
            uint pid;
            ulong sequence;
            UNICODE_STRING name;
            if (_infoClass == SystemBasicProcessInformation)
            {
                var e = (SYSTEM_BASICPROCESS_INFORMATION*)p;
                next = e->NextEntryOffset;
                pid = (uint)e->UniqueProcessId;
                sequence = e->SequenceNumber;
                name = e->ImageName;
            }
            else
            {
                var e = (SYSTEM_PROCESS_INFORMATION_HEAD*)p;
                next = e->NextEntryOffset;
                pid = (uint)e->UniqueProcessId;
                sequence = 0;
                name = e->ImageName;
            }

            if (pid != 0)
            {
                var exeName = name.Buffer is null || name.Length == 0 ? string.Empty : new string(name.Buffer, 0, name.Length / 2);
                if (!_cache.TryGetValue(pid, out var cached) || cached.Sequence != sequence || !string.Equals(cached.Info.ExeName, exeName, StringComparison.OrdinalIgnoreCase))
                {
                    cached = new CacheEntry(new ProcessInfo((int)pid, exeName, QueryPath(pid)), sequence);
                    _cache[pid] = cached;
                }

                seen.Add(pid);
                result.Add(cached.Info);
            }

            if (next == 0)
            {
                break;
            }

            p += next;
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

    private string? QueryPath(uint pid)
    {
        PathQueries++;
        return _paths.Resolve((int)pid);
    }

    private readonly record struct CacheEntry(ProcessInfo Info, ulong Sequence);
}
