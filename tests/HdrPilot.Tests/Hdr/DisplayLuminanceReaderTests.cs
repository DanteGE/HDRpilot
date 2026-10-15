using System.Runtime.InteropServices;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Native;

namespace HdrPilot.Tests.Hdr;

/// <summary>
/// DXGI-Leser der Helligkeit (E89, G10.7): Aufbau von DXGI_OUTPUT_DESC1 für x64 (von Hand aus dxgi1_6.h berechnet, ohne
/// heruntergeladenen Header), die Stellen in den COM-Tabellen und die Umrechnung einer Beschreibung in Werte.
/// </summary>
public sealed unsafe class DisplayLuminanceReaderTests
{
    [Fact]
    public void Output_description_has_the_x64_layout_of_dxgi1_6_h()
    {
        // WCHAR[32] 64 + RECT 16 + BOOL 4 + Rotation 4 + HMONITOR 8 (ausgerichtet) + 2×UINT 8 + 8 Floats 32 + 3 Floats 12 = 148,
        // aufgerundet auf die Ausrichtung des Zeigers: 152.
        Assert.Equal(152, sizeof(DxgiNative.DXGI_OUTPUT_DESC1));
        Assert.Equal(152, Marshal.SizeOf<DxgiNative.DXGI_OUTPUT_DESC1>());
        Assert.Equal(64, (int)Marshal.OffsetOf<DxgiNative.DXGI_OUTPUT_DESC1>(nameof(DxgiNative.DXGI_OUTPUT_DESC1.DesktopLeft)));
        Assert.Equal(80, (int)Marshal.OffsetOf<DxgiNative.DXGI_OUTPUT_DESC1>(nameof(DxgiNative.DXGI_OUTPUT_DESC1.AttachedToDesktop)));
        Assert.Equal(88, (int)Marshal.OffsetOf<DxgiNative.DXGI_OUTPUT_DESC1>(nameof(DxgiNative.DXGI_OUTPUT_DESC1.Monitor)));
        Assert.Equal(96, (int)Marshal.OffsetOf<DxgiNative.DXGI_OUTPUT_DESC1>(nameof(DxgiNative.DXGI_OUTPUT_DESC1.BitsPerColor)));
        Assert.Equal(100, (int)Marshal.OffsetOf<DxgiNative.DXGI_OUTPUT_DESC1>(nameof(DxgiNative.DXGI_OUTPUT_DESC1.ColorSpace)));
        Assert.Equal(136, (int)Marshal.OffsetOf<DxgiNative.DXGI_OUTPUT_DESC1>(nameof(DxgiNative.DXGI_OUTPUT_DESC1.MinLuminance)));
        Assert.Equal(140, (int)Marshal.OffsetOf<DxgiNative.DXGI_OUTPUT_DESC1>(nameof(DxgiNative.DXGI_OUTPUT_DESC1.MaxLuminance)));
        Assert.Equal(144, (int)Marshal.OffsetOf<DxgiNative.DXGI_OUTPUT_DESC1>(nameof(DxgiNative.DXGI_OUTPUT_DESC1.MaxFullFrameLuminance)));
    }

    [Fact]
    public void Vtable_slots_follow_the_interface_inheritance()
    {
        // IUnknown 3 + IDXGIObject 4 + IDXGIFactory 5 → EnumAdapters1 ist der 13. Eintrag (12).
        Assert.Equal(3 + 4 + 5, DxgiNative.EnumAdapters1Slot);

        // IDXGIAdapter: EnumOutputs direkt nach IDXGIObject.
        Assert.Equal(3 + 4, DxgiNative.EnumOutputsSlot);

        // IDXGIOutput 12 Methoden, Output1 4, Output2–5 je 1 → GetDesc1 von IDXGIOutput6 an 27.
        Assert.Equal(3 + 4 + 12 + 4 + 1 + 1 + 1 + 1, DxgiNative.GetDesc1Slot);
        Assert.Equal(new Guid("068346e8-aaec-4b84-add7-137f513f77a1"), DxgiNative.IID_IDXGIOutput6);
        Assert.Equal(new Guid("770aae78-f26f-4dba-a829-253c83d1b387"), DxgiNative.IID_IDXGIFactory1);
    }

    [Fact]
    public void Description_maps_to_luminance_with_hdr_from_the_pq_color_space()
    {
        var desc = Description(@"\\.\DISPLAY1", attached: true, colorSpace: 12, max: 456.2f, full: 254.0f, min: 0.0f, bits: 10);

        var value = DxgiLuminanceReader.FromDescription(desc);

        Assert.Equal(new DisplayLuminance(@"\\.\DISPLAY1", 456.2, 254.0, 0.0, 10, HdrActive: true), value! with { MaxLuminance = Math.Round(value.MaxLuminance, 1) });

        // Gegenproben: SDR-Farbraum (G22/P709 = 0) ist kein HDR; ein Ausgang ohne Desktop zählt nicht; Unsinn wird 0.
        Assert.False(DxgiLuminanceReader.FromDescription(Description(@"\\.\DISPLAY1", true, 0, 456, 254, 0, 8))!.HdrActive);
        Assert.Null(DxgiLuminanceReader.FromDescription(Description(@"\\.\DISPLAY2", attached: false, 12, 456, 254, 0, 10)));
        Assert.Equal(0, DxgiLuminanceReader.FromDescription(Description(@"\\.\DISPLAY1", true, 12, float.NaN, -3, 0, 10))!.MaxLuminance);
        Assert.Null(DxgiLuminanceReader.FromDescription(Description(string.Empty, true, 12, 456, 254, 0, 10)));
    }

    [Fact]
    public void Real_reader_never_throws_and_reports_plausible_values()
    {
        // Liest die echten Ausgänge (nur lesend); ohne eingeschalteten Bildschirm darf die Liste leer sein.
        var values = DxgiLuminanceReader.Instance.ReadAll();

        foreach (var value in values)
        {
            Assert.StartsWith(@"\\.\DISPLAY", value.GdiDeviceName, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(value.MaxLuminance, 0, 100_000);
            Assert.InRange(value.BitsPerColor, 0, 32);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"DXGI {value.GdiDeviceName}: Spitze {value.MaxLuminance} nits, Vollbild {value.MaxFullFrameLuminance} nits, Minimum {value.MinLuminance} nits, {value.BitsPerColor} Bit, HDR {value.HdrActive}");
        }
    }

    private static DxgiNative.DXGI_OUTPUT_DESC1 Description(string name, bool attached, uint colorSpace, float max, float full, float min, uint bits)
    {
        var desc = default(DxgiNative.DXGI_OUTPUT_DESC1);
        for (var i = 0; i < name.Length && i < 31; i++)
        {
            desc.DeviceName[i] = name[i];
        }

        desc.AttachedToDesktop = attached ? 1 : 0;
        desc.ColorSpace = colorSpace;
        desc.MaxLuminance = max;
        desc.MaxFullFrameLuminance = full;
        desc.MinLuminance = min;
        desc.BitsPerColor = bits;
        return desc;
    }
}
