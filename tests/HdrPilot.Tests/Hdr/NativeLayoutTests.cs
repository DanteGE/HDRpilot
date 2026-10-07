using System.Runtime.InteropServices;
using HdrPilot.Core.Native;
using static HdrPilot.Core.Native.DisplayConfigNative;

namespace HdrPilot.Tests.Hdr;

/// <summary>
/// Strukturgrößen und Offsets für x64, von Hand aus wingdi.h/WinUser.h (SDK 10.0.26100.0) berechnet.
/// Unabhängig davon prüft <see cref="WingdiHeaderConformanceTests"/> dieselben Strukturen direkt gegen den Header.
/// </summary>
public sealed unsafe class NativeLayoutTests
{
    [Fact]
    public void Process_is_64_bit() => Assert.Equal(8, IntPtr.Size);

    [Theory]
    [InlineData(typeof(LUID), 8)]
    [InlineData(typeof(DISPLAYCONFIG_RATIONAL), 8)]
    [InlineData(typeof(DISPLAYCONFIG_VIDEO_SIGNAL_INFO), 48)]
    [InlineData(typeof(DISPLAYCONFIG_SOURCE_MODE), 20)]
    [InlineData(typeof(DISPLAYCONFIG_TARGET_MODE), 48)]
    [InlineData(typeof(DISPLAYCONFIG_DESKTOP_IMAGE_INFO), 40)]
    [InlineData(typeof(DISPLAYCONFIG_MODE_INFO), 64)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_SOURCE_INFO), 20)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_TARGET_INFO), 48)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_INFO), 72)]
    [InlineData(typeof(DISPLAYCONFIG_DEVICE_INFO_HEADER), 20)]
    [InlineData(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME), 84)]
    [InlineData(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), 420)]
    [InlineData(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO), 32)]
    [InlineData(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE), 24)]
    [InlineData(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2), 36)]
    [InlineData(typeof(DISPLAYCONFIG_SET_HDR_STATE), 24)]
    [InlineData(typeof(InputNative.KEYBDINPUT), 24)]
    [InlineData(typeof(InputNative.MOUSEINPUT), 32)]
    [InlineData(typeof(InputNative.INPUT), 40)]
    public void Struct_has_native_size(Type type, int expected) => Assert.Equal(expected, Marshal.SizeOf(type));

    [Fact]
    public void Sizeof_matches_marshal_size_for_blittable_packets()
    {
        // Die Pakete werden per Zeiger übergeben: sizeof (verwaltet) muss der nativen Größe entsprechen.
        Assert.Equal(420, sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
        Assert.Equal(84, sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
        Assert.Equal(36, sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2));
        Assert.Equal(32, sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
        Assert.Equal(24, sizeof(DISPLAYCONFIG_SET_HDR_STATE));
        Assert.Equal(24, sizeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
        Assert.Equal(72, sizeof(DISPLAYCONFIG_PATH_INFO));
        Assert.Equal(64, sizeof(DISPLAYCONFIG_MODE_INFO));
        Assert.Equal(40, sizeof(InputNative.INPUT));
    }

    [Theory]
    [InlineData(typeof(DISPLAYCONFIG_PATH_INFO), "sourceInfo", 0)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_INFO), "targetInfo", 20)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_INFO), "flags", 68)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_TARGET_INFO), "outputTechnology", 16)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_TARGET_INFO), "refreshRate", 28)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_TARGET_INFO), "targetAvailable", 40)]
    [InlineData(typeof(DISPLAYCONFIG_PATH_TARGET_INFO), "statusFlags", 44)]
    [InlineData(typeof(DISPLAYCONFIG_MODE_INFO), "adapterId", 8)]
    [InlineData(typeof(DISPLAYCONFIG_MODE_INFO), "sourceMode", 16)]
    [InlineData(typeof(DISPLAYCONFIG_SOURCE_MODE), "position", 12)]
    [InlineData(typeof(DISPLAYCONFIG_DEVICE_INFO_HEADER), "adapterId", 8)]
    [InlineData(typeof(DISPLAYCONFIG_DEVICE_INFO_HEADER), "id", 16)]
    [InlineData(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), "flags", 20)]
    [InlineData(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), "edidManufactureId", 28)]
    [InlineData(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), "connectorInstance", 32)]
    [InlineData(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), "monitorFriendlyDeviceName", 36)]
    [InlineData(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), "monitorDevicePath", 164)]
    [InlineData(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO), "value", 20)]
    [InlineData(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO), "bitsPerColorChannel", 28)]
    [InlineData(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2), "value", 20)]
    [InlineData(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2), "activeColorMode", 32)]
    [InlineData(typeof(DISPLAYCONFIG_SET_HDR_STATE), "value", 20)]
    [InlineData(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE), "value", 20)]
    [InlineData(typeof(InputNative.INPUT), "u", 8)]
    [InlineData(typeof(InputNative.KEYBDINPUT), "dwExtraInfo", 16)]
    public void Field_has_native_offset(Type type, string field, int expected) =>
        Assert.Equal(expected, (int)Marshal.OffsetOf(type, field));

    [Fact]
    public void Device_info_type_constants_match_wingdi()
    {
        Assert.Equal(1u, DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME);
        Assert.Equal(2u, DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME);
        Assert.Equal(9u, DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO);
        Assert.Equal(10u, DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE);
        Assert.Equal(15u, DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2);
        Assert.Equal(16u, DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE);
        Assert.Equal(2u, QDC_ONLY_ACTIVE_PATHS);
        Assert.Equal(2u, DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR);
    }
}
