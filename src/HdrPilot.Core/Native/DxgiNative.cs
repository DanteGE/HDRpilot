using System.Runtime.InteropServices;

namespace HdrPilot.Core.Native;

// dxgi.h / dxgi1_6.h (SDK 10.0.26100.0): Helligkeitswerte der Ausgänge lesen (E89). Nur lesende Abfragen über die COM-Tabellen
// (ohne zusätzliches Paket): keine Swapchain, kein Gerät, keine Änderung am Bildschirm.
#pragma warning disable IDE1006

internal static unsafe partial class DxgiNative
{
    /// <summary>IID_IDXGIFactory1 {770aae78-f26f-4dba-a829-253c83d1b387}.</summary>
    public static readonly Guid IID_IDXGIFactory1 = new(0x770aae78, 0xf26f, 0x4dba, 0xa8, 0x29, 0x25, 0x3c, 0x83, 0xd1, 0xb3, 0x87);

    /// <summary>IID_IDXGIOutput6 {068346e8-aaec-4b84-add7-137f513f77a1}.</summary>
    public static readonly Guid IID_IDXGIOutput6 = new(0x068346e8, 0xaaec, 0x4b84, 0xad, 0xd7, 0x13, 0x7f, 0x51, 0x3f, 0x77, 0xa1);

    /// <summary>DXGI_ERROR_NOT_FOUND: Ende der Aufzählung von Adaptern bzw. Ausgängen.</summary>
    public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    /// <summary>DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020: HDR10 (PQ, BT.2020) – HDR ist auf dem Ausgang aktiv.</summary>
    public const uint DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 = 12;

    // Stellen in den COM-Tabellen, abgeleitet aus der Vererbung: IUnknown 0–2 (QueryInterface, AddRef, Release),
    // IDXGIObject 3–6, IDXGIFactory 7–11 (EnumAdapters …), IDXGIFactory1 12–13 (EnumAdapters1, IsCurrent);
    // IDXGIAdapter 7–9 (EnumOutputs, GetDesc, CheckInterfaceSupport); IDXGIOutput 7–18, IDXGIOutput1 19–22, IDXGIOutput2 23,
    // IDXGIOutput3 24, IDXGIOutput4 25, IDXGIOutput5 26, IDXGIOutput6 27–28 (GetDesc1, CheckHardwareCompositionSupport).
    public const int QueryInterfaceSlot = 0;
    public const int ReleaseSlot = 2;
    public const int EnumAdapters1Slot = 12;
    public const int EnumOutputsSlot = 7;
    public const int GetDesc1Slot = 27;

    [LibraryImport("dxgi.dll")]
    public static partial int CreateDXGIFactory1(Guid* riid, void** ppFactory);

    /// <summary>DXGI_OUTPUT_DESC1 (dxgi1_6.h), x64: 152 Byte.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC1
    {
        public fixed char DeviceName[32];
        public int DesktopLeft;
        public int DesktopTop;
        public int DesktopRight;
        public int DesktopBottom;
        public int AttachedToDesktop;
        public uint Rotation;
        public nint Monitor;
        public uint BitsPerColor;
        public uint ColorSpace;
        public fixed float RedPrimary[2];
        public fixed float GreenPrimary[2];
        public fixed float BluePrimary[2];
        public fixed float WhitePoint[2];
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    /// <summary>Eintrag einer COM-Tabelle.</summary>
    public static void* Slot(void* com, int index) => (*(void***)com)[index];

    public static uint Release(void* com) =>
        com is null ? 0 : ((delegate* unmanaged<void*, uint>)Slot(com, ReleaseSlot))(com);

    public static int QueryInterface(void* com, Guid* iid, void** result) =>
        ((delegate* unmanaged<void*, Guid*, void**, int>)Slot(com, QueryInterfaceSlot))(com, iid, result);

    public static int EnumAdapters1(void* factory, uint index, void** adapter) =>
        ((delegate* unmanaged<void*, uint, void**, int>)Slot(factory, EnumAdapters1Slot))(factory, index, adapter);

    public static int EnumOutputs(void* adapter, uint index, void** output) =>
        ((delegate* unmanaged<void*, uint, void**, int>)Slot(adapter, EnumOutputsSlot))(adapter, index, output);

    public static int GetDesc1(void* output6, DXGI_OUTPUT_DESC1* desc) =>
        ((delegate* unmanaged<void*, DXGI_OUTPUT_DESC1*, int>)Slot(output6, GetDesc1Slot))(output6, desc);
}
#pragma warning restore IDE1006
