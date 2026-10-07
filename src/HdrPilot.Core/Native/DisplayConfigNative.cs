using System.Runtime.InteropServices;

namespace HdrPilot.Core.Native;

// Deklarationen aus wingdi.h (Windows SDK 10.0.26100.0). Struktur- und Feldnamen sind absichtlich
// identisch mit dem Header, damit WingdiHeaderConformanceTests Reihenfolge, Offsets und Größen
// automatisch gegen die wingdi.h abgleichen können. Unions sind als einzelnes Feld mit dem Namen
// eines ihrer Member abgebildet (meist "value"), Bitfelder über die Konstanten in AdvancedColorBits.
#pragma warning disable IDE1006, SA1307, SA1310 // Namen folgen wingdi.h

internal static unsafe partial class DisplayConfigNative
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_INVALID_PARAMETER = 87;
    public const int ERROR_NOT_SUPPORTED = 50;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_GEN_FAILURE = 31;
    public const int ERROR_ACCESS_DENIED = 5;

    public const uint QDC_ALL_PATHS = 0x00000001;
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    public const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xffffffff;

    public const uint DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1;
    public const uint DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2;
    public const uint DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE = 3;

    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    public const uint DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15;
    public const uint DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE = 16;

    public const uint DISPLAYCONFIG_ADVANCED_COLOR_MODE_SDR = 0;
    public const uint DISPLAYCONFIG_ADVANCED_COLOR_MODE_WCG = 1;
    public const uint DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR = 2;

    public const int CCHDEVICENAME = 32;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECTL
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard; // union { AdditionalSignalInfo; videoStandard; }
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public POINTL PathSourceSize;
        public RECTL DesktopImageRegion;
        public RECTL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)]
        public uint infoType;

        [FieldOffset(4)]
        public uint id;

        [FieldOffset(8)]
        public LUID adapterId;

        [FieldOffset(16)]
        public DISPLAYCONFIG_TARGET_MODE targetMode;

        [FieldOffset(16)]
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [FieldOffset(16)]
        public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx; // union { modeInfoIdx; { cloneGroupId:16; sourceModeInfoIdx:16 } }
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx; // union { modeInfoIdx; { desktopModeInfoIdx:16; targetModeInfoIdx:16 } }
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable; // BOOL
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public fixed char viewGdiDeviceName[CCHDEVICENAME];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
    {
        public uint value; // union { { friendlyNameFromEdid:1; friendlyNameForced:1; edidIdsValid:1; reserved:29 }; value; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        public fixed char monitorFriendlyDeviceName[64];
        public fixed char monitorDevicePath[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // union, Bits siehe AdvancedColorBits.Legacy*
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // union { enableAdvancedColor:1; reserved:31 }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // union, Bits siehe AdvancedColorBits.Info2*
        public uint colorEncoding;
        public uint bitsPerColorChannel;
        public uint activeColorMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SET_HDR_STATE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // union { enableHdr:1; reserved:31 }
    }

    [LibraryImport("user32.dll")]
    public static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [LibraryImport("user32.dll")]
    public static partial int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        DISPLAYCONFIG_PATH_INFO* pathArray,
        ref uint numModeInfoArrayElements,
        DISPLAYCONFIG_MODE_INFO* modeInfoArray,
        IntPtr currentTopologyId);

    [LibraryImport("user32.dll")]
    public static partial int DisplayConfigGetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_HEADER* requestPacket);

    [LibraryImport("user32.dll")]
    public static partial int DisplayConfigSetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_HEADER* setPacket);
}

/// <summary>Bitpositionen der Bitfelder aus wingdi.h. Ein Test gleicht sie mit dem Header ab.</summary>
internal static class AdvancedColorBits
{
    // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO (Typ 9)
    public const int LegacyAdvancedColorSupported = 0;
    public const int LegacyAdvancedColorEnabled = 1;
    public const int LegacyWideColorEnforced = 2;
    public const int LegacyAdvancedColorForceDisabled = 3;

    // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 (Typ 15)
    public const int Info2AdvancedColorSupported = 0;
    public const int Info2AdvancedColorActive = 1;
    public const int Info2AdvancedColorLimitedByPolicy = 3;
    public const int Info2HighDynamicRangeSupported = 4;
    public const int Info2HighDynamicRangeUserEnabled = 5;
    public const int Info2WideColorSupported = 6;
    public const int Info2WideColorUserEnabled = 7;

    // DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE (Typ 10) und DISPLAYCONFIG_SET_HDR_STATE (Typ 16)
    public const int SetEnableAdvancedColor = 0;
    public const int SetEnableHdr = 0;

    public static bool IsSet(uint value, int bit) => (value & (1u << bit)) != 0;
}
#pragma warning restore IDE1006, SA1307, SA1310
