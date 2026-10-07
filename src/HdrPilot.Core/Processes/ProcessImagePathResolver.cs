using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static HdrPilot.Core.Native.NtNative;
using static HdrPilot.Core.Native.ToolhelpNative;

namespace HdrPilot.Core.Processes;

/// <summary>
/// EXE-Pfad eines Prozesses, ohne ihn zu öffnen: NtQuerySystemInformation(SystemProcessIdInformation) liefert zur PID
/// den NT-Pfad, auch für geschützte Prozesse. So entsteht kein Handle auf fremde Prozesse, also auch keines auf ein
/// Spiel, das ein Anti-Cheat bemerken könnte (E65). Den NT-Pfad rechnet die Klasse über die Laufwerkszuordnung
/// (QueryDosDevice) in einen Win32-Pfad um. Nur wenn Windows die Informationsklasse gar nicht kennt, fällt sie auf
/// OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) zurück; das Handle wird dann sofort geschlossen.
/// </summary>
public sealed unsafe class ProcessImagePathResolver
{
    /// <summary>Unbekannte Geräte (neues Laufwerk): Zuordnung höchstens so oft neu lesen.</summary>
    private static readonly TimeSpan DeviceMapRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;
    private readonly Func<IReadOnlyList<DeviceMapping>> _readDeviceMap;
    private readonly QueryNtPathFunc _queryNtPath;
    private readonly IClock _clock;
    private IReadOnlyList<DeviceMapping>? _deviceMap;
    private DateTimeOffset _deviceMapReadAt;
    private bool _useHandleFallback;

    public ProcessImagePathResolver(ILogger? logger = null)
        : this(logger, ReadDeviceMap, QueryNtPath, new SystemClock())
    {
    }

    /// <summary>Für Tests: eigene Laufwerkszuordnung, eigene Pfadabfrage und Uhr.</summary>
    internal ProcessImagePathResolver(ILogger? logger, Func<IReadOnlyList<DeviceMapping>> readDeviceMap, QueryNtPathFunc queryNtPath, IClock clock)
    {
        _logger = logger ?? NullLogger.Instance;
        _readDeviceMap = readDeviceMap;
        _queryNtPath = queryNtPath;
        _clock = clock;
    }

    /// <summary>NtQuerySystemInformation(SystemProcessIdInformation): NTSTATUS und NT-Pfad (null bei leerem Namen).</summary>
    internal delegate uint QueryNtPathFunc(int processId, out string? ntPath);

    /// <summary>True, wenn Windows die Informationsklasse nicht kennt und Pfade über ein kurzes Handle gelesen werden.</summary>
    public bool UsesHandleFallback => _useHandleFallback;

    /// <summary>Win32-Pfad des EXE oder null (Prozess beendet, Pseudo-Prozess wie "Registry", unbekanntes Laufwerk).</summary>
    public string? Resolve(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        if (!_useHandleFallback)
        {
            var status = _queryNtPath(processId, out var ntPath);
            if (status == STATUS_SUCCESS)
            {
                return ntPath is null ? null : MapToDosPath(ntPath);
            }

            if (status is not (STATUS_INVALID_INFO_CLASS or STATUS_NOT_IMPLEMENTED))
            {
                return null; // etwa STATUS_INVALID_CID: Prozess schon beendet
            }

            _logger.LogWarning("SystemProcessIdInformation nicht verfügbar (Status 0x{Status:X8}), lese EXE-Pfade über ein kurzes Handle.", status);
            _useHandleFallback = true;
        }

        return QueryWithHandle((uint)processId);
    }

    /// <summary>NT-Pfad in Win32-Pfad umrechnen: Laufwerksbuchstabe, verbundenes Netzlaufwerk oder UNC-Pfad.</summary>
    internal static string? ToDosPath(string ntPath, IReadOnlyList<DeviceMapping> map)
    {
        if (!ntPath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
        {
            return null; // "Registry", "MemCompression" und Ähnliches haben keinen Dateipfad
        }

        DeviceMapping? best = null;
        foreach (var m in map)
        {
            if (ntPath.StartsWith(m.Device, StringComparison.OrdinalIgnoreCase)
                && (ntPath.Length == m.Device.Length || ntPath[m.Device.Length] == '\\')
                && (best is null || m.Device.Length > best.Value.Device.Length))
            {
                best = m;
            }
        }

        if (best is { } hit)
        {
            var rest = ntPath[hit.Device.Length..];
            return hit.Drive + (rest.Length == 0 ? @"\" : rest);
        }

        const string mup = @"\Device\Mup\";
        return ntPath.StartsWith(mup, StringComparison.OrdinalIgnoreCase) ? @"\\" + ntPath[mup.Length..] : null;
    }

    /// <summary>Ziel eines Laufwerksbuchstabens als Zuordnung, oder null (SUBST-Ziele und Ähnliches).</summary>
    internal static DeviceMapping? ParseDosDeviceTarget(string drive, string target)
    {
        // Verbundenes Netzlaufwerk: \Device\LanmanRedirector\;Z:0000000000012345\server\share; Prozesse von dort
        // meldet Windows als \Device\Mup\server\share\...
        const string redirector = @"\Device\LanmanRedirector\;";
        if (target.StartsWith(redirector, StringComparison.OrdinalIgnoreCase))
        {
            var slash = target.IndexOf('\\', redirector.Length);
            return slash < 0 ? null : new DeviceMapping(@"\Device\Mup" + target[slash..].TrimEnd('\\'), drive);
        }

        return target.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase) ? new DeviceMapping(target.TrimEnd('\\'), drive) : null;
    }

    private string? MapToDosPath(string ntPath)
    {
        var now = _clock.UtcNow;
        _deviceMap ??= RefreshDeviceMap(now);
        var path = ToDosPath(ntPath, _deviceMap);
        if (path is null && ntPath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)
            && !ntPath.StartsWith(@"\Device\Mup\", StringComparison.OrdinalIgnoreCase)
            && now - _deviceMapReadAt >= DeviceMapRefreshInterval)
        {
            // Gerät unbekannt: vielleicht wurde ein Laufwerk verbunden.
            _deviceMap = RefreshDeviceMap(now);
            path = ToDosPath(ntPath, _deviceMap);
        }

        return path;
    }

    private IReadOnlyList<DeviceMapping> RefreshDeviceMap(DateTimeOffset now)
    {
        _deviceMapReadAt = now;
        return _readDeviceMap();
    }

    private static uint QueryNtPath(int processId, out string? ntPath)
    {
        ntPath = null;
        const int stackChars = 1024;
        var stackBuffer = stackalloc char[stackChars];
        var info = new SYSTEM_PROCESS_ID_INFORMATION
        {
            ProcessId = processId,
            ImageName = new UNICODE_STRING { Length = 0, MaximumLength = stackChars * sizeof(char), Buffer = stackBuffer },
        };

        var status = NtQuerySystemInformation(SystemProcessIdInformation, &info, (uint)sizeof(SYSTEM_PROCESS_ID_INFORMATION), out _);
        if (status == STATUS_INFO_LENGTH_MISMATCH && info.ImageName.MaximumLength > stackChars * sizeof(char))
        {
            // Sehr langer Pfad: Windows meldet die nötige Größe in MaximumLength.
            var needed = info.ImageName.MaximumLength;
            var heapBuffer = new char[(needed / sizeof(char)) + 1];
            fixed (char* p = heapBuffer)
            {
                info.ImageName = new UNICODE_STRING { Length = 0, MaximumLength = needed, Buffer = p };
                status = NtQuerySystemInformation(SystemProcessIdInformation, &info, (uint)sizeof(SYSTEM_PROCESS_ID_INFORMATION), out _);
                if (status == STATUS_SUCCESS && info.ImageName.Length > 0)
                {
                    ntPath = new string(p, 0, info.ImageName.Length / sizeof(char));
                }
            }

            return status;
        }

        if (status == STATUS_SUCCESS && info.ImageName.Buffer is not null && info.ImageName.Length > 0)
        {
            ntPath = new string(info.ImageName.Buffer, 0, info.ImageName.Length / sizeof(char));
        }

        return status;
    }

    private static string? QueryWithHandle(uint processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
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

    private static List<DeviceMapping> ReadDeviceMap()
    {
        var map = new List<DeviceMapping>();
        const int capacity = 1024;
        var buffer = stackalloc char[capacity];
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var drive = letter + ":";
            var length = NtNative.QueryDosDevice(drive, buffer, capacity);
            if (length == 0)
            {
                continue;
            }

            var end = 0;
            while (end < length && buffer[end] != '\0')
            {
                end++;
            }

            if (ParseDosDeviceTarget(drive, new string(buffer, 0, end)) is { } mapping)
            {
                map.Add(mapping);
            }
        }

        return map;
    }

    /// <summary>NT-Gerät (etwa \Device\HarddiskVolume3) und sein Laufwerksbuchstabe (etwa C:).</summary>
    internal readonly record struct DeviceMapping(string Device, string Drive);
}
