using HdrPilot.Core.Native;
using static HdrPilot.Core.Native.DisplayConfigNative;

namespace HdrPilot.Core.Hdr;

/// <summary>Ein aktiver Pfad aus QueryDisplayConfig.</summary>
public readonly record struct RawDisplayPath(DisplayTarget Target, uint SourceAdapterLow, int SourceAdapterHigh, uint SourceId, bool IsPrimary);

public readonly record struct RawTargetName(string FriendlyName, string MonitorDevicePath, uint OutputTechnology);

/// <summary>Antwort auf DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO (Typ 9).</summary>
public readonly record struct RawColorInfo(uint Value, uint ColorEncoding, uint BitsPerColorChannel);

/// <summary>Antwort auf DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 (Typ 15).</summary>
public readonly record struct RawColorInfo2(uint Value, uint ColorEncoding, uint BitsPerColorChannel, uint ActiveColorMode);

/// <summary>
/// Dünne Hülle um die Display-Configuration-API. Jede Methode liefert den Win32-Fehlercode (0 = Erfolg).
/// Die Logik darüber (<see cref="DisplayConfigHdrController"/>) ist gegen eine Attrappe testbar.
/// </summary>
public interface IDisplayConfigApi
{
    int QueryActivePaths(out IReadOnlyList<RawDisplayPath> paths);

    int GetTargetName(DisplayTarget target, out RawTargetName name);

    int GetSourceGdiName(uint adapterLow, int adapterHigh, uint sourceId, out string gdiDeviceName);

    int GetAdvancedColorInfo(DisplayTarget target, out RawColorInfo info);

    int GetAdvancedColorInfo2(DisplayTarget target, out RawColorInfo2 info);

    int SetAdvancedColorState(DisplayTarget target, bool enable);

    int SetHdrState(DisplayTarget target, bool enable);
}

public sealed unsafe class Win32DisplayConfigApi : IDisplayConfigApi
{
    public static Win32DisplayConfigApi Instance { get; } = new();

    public int QueryActivePaths(out IReadOnlyList<RawDisplayPath> paths)
    {
        paths = [];

        // Die Pfadanzahl kann sich zwischen den beiden Aufrufen ändern (An-/Abstecken): dann erneut versuchen.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var rc = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var numPaths, out var numModes);
            if (rc != ERROR_SUCCESS)
            {
                return rc;
            }

            var pathArray = new DISPLAYCONFIG_PATH_INFO[numPaths];
            var modeArray = new DISPLAYCONFIG_MODE_INFO[numModes];
            fixed (DISPLAYCONFIG_PATH_INFO* pPaths = pathArray)
            fixed (DISPLAYCONFIG_MODE_INFO* pModes = modeArray)
            {
                rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, pPaths, ref numModes, pModes, IntPtr.Zero);
            }

            if (rc == ERROR_INSUFFICIENT_BUFFER)
            {
                continue;
            }

            if (rc != ERROR_SUCCESS)
            {
                return rc;
            }

            var result = new List<RawDisplayPath>((int)numPaths);
            for (var i = 0; i < numPaths; i++)
            {
                ref readonly var p = ref pathArray[i];
                if ((p.flags & DISPLAYCONFIG_PATH_ACTIVE) == 0)
                {
                    continue;
                }

                var isPrimary = false;
                var modeIdx = p.sourceInfo.modeInfoIdx;
                if (modeIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && modeIdx < numModes)
                {
                    ref readonly var mode = ref modeArray[modeIdx];
                    if (mode.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
                    {
                        // Der Hauptbildschirm liegt im virtuellen Desktop am Ursprung.
                        isPrimary = mode.sourceMode.position.x == 0 && mode.sourceMode.position.y == 0;
                    }
                }

                result.Add(new RawDisplayPath(
                    new DisplayTarget(p.targetInfo.adapterId.LowPart, p.targetInfo.adapterId.HighPart, p.targetInfo.id),
                    p.sourceInfo.adapterId.LowPart,
                    p.sourceInfo.adapterId.HighPart,
                    p.sourceInfo.id,
                    isPrimary));
            }

            paths = result;
            return ERROR_SUCCESS;
        }

        return ERROR_INSUFFICIENT_BUFFER;
    }

    public int GetTargetName(DisplayTarget target, out RawTargetName name)
    {
        var packet = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        packet.header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), target);
        var rc = DisplayConfigGetDeviceInfo(&packet.header);
        name = rc == ERROR_SUCCESS
            ? new RawTargetName(ReadString(packet.monitorFriendlyDeviceName, 64), ReadString(packet.monitorDevicePath, 128), packet.outputTechnology)
            : default;
        return rc;
    }

    public int GetSourceGdiName(uint adapterLow, int adapterHigh, uint sourceId, out string gdiDeviceName)
    {
        var packet = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        packet.header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
            size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME),
            adapterId = new LUID { LowPart = adapterLow, HighPart = adapterHigh },
            id = sourceId,
        };
        var rc = DisplayConfigGetDeviceInfo(&packet.header);
        gdiDeviceName = rc == ERROR_SUCCESS ? ReadString(packet.viewGdiDeviceName, CCHDEVICENAME) : string.Empty;
        return rc;
    }

    public int GetAdvancedColorInfo(DisplayTarget target, out RawColorInfo info)
    {
        var packet = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        packet.header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO), target);
        var rc = DisplayConfigGetDeviceInfo(&packet.header);
        info = rc == ERROR_SUCCESS ? new RawColorInfo(packet.value, packet.colorEncoding, packet.bitsPerColorChannel) : default;
        return rc;
    }

    public int GetAdvancedColorInfo2(DisplayTarget target, out RawColorInfo2 info)
    {
        var packet = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2();
        packet.header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2, (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2), target);
        var rc = DisplayConfigGetDeviceInfo(&packet.header);
        info = rc == ERROR_SUCCESS
            ? new RawColorInfo2(packet.value, packet.colorEncoding, packet.bitsPerColorChannel, packet.activeColorMode)
            : default;
        return rc;
    }

    public int SetAdvancedColorState(DisplayTarget target, bool enable)
    {
        var packet = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
        packet.header = Header(DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE, (uint)sizeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE), target);
        packet.value = enable ? 1u << AdvancedColorBits.SetEnableAdvancedColor : 0u;
        return DisplayConfigSetDeviceInfo(&packet.header);
    }

    public int SetHdrState(DisplayTarget target, bool enable)
    {
        var packet = new DISPLAYCONFIG_SET_HDR_STATE();
        packet.header = Header(DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE, (uint)sizeof(DISPLAYCONFIG_SET_HDR_STATE), target);
        packet.value = enable ? 1u << AdvancedColorBits.SetEnableHdr : 0u;
        return DisplayConfigSetDeviceInfo(&packet.header);
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(uint type, uint size, DisplayTarget target) => new()
    {
        type = type,
        size = size,
        adapterId = new LUID { LowPart = target.AdapterLow, HighPart = target.AdapterHigh },
        id = target.TargetId,
    };

    private static string ReadString(char* buffer, int capacity)
    {
        var length = 0;
        while (length < capacity && buffer[length] != '\0')
        {
            length++;
        }

        return new string(buffer, 0, length);
    }
}
