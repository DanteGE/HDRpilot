using HdrPilot.Core.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Hdr;

/// <summary>
/// Helligkeitswerte eines Bildschirms laut DXGI (<c>IDXGIOutput6::GetDesc1</c>, E89): Spitze
/// (<see cref="MaxLuminance"/>), Vollbild (<see cref="MaxFullFrameLuminance"/>), Minimum, Farbtiefe und ob HDR (Farbraum
/// G2084/P2020) gerade aktiv ist. Zugeordnet über den GDI-Namen (<see cref="DisplayInfo.GdiDeviceName"/>).
/// </summary>
public sealed record DisplayLuminance(
    string GdiDeviceName,
    double MaxLuminance,
    double MaxFullFrameLuminance,
    double MinLuminance,
    int BitsPerColor,
    bool HdrActive);

/// <summary>Liest die Helligkeitswerte aller Ausgänge (in Tests eine Attrappe). Darf auf jedem Thread laufen; wirft nie.</summary>
public interface IDisplayLuminanceReader
{
    IReadOnlyList<DisplayLuminance> ReadAll();
}

/// <summary>
/// DXGI-Leser: <c>CreateDXGIFactory1</c> → <c>IDXGIFactory1::EnumAdapters1</c> → <c>IDXGIAdapter::EnumOutputs</c> →
/// <c>QueryInterface(IDXGIOutput6)</c> → <c>GetDesc1</c>, alles über die COM-Tabellen ohne zusätzliches Paket; jede
/// Schnittstelle wird gleich wieder freigegeben. Nur Ausgänge am Desktop zählen. Scheitert ein Schritt (alte Treiber ohne
/// IDXGIOutput6, Remote-Sitzung), fehlt dieser Ausgang; ohne Fabrik ist die Liste leer. Wirft nie.
/// </summary>
public sealed class DxgiLuminanceReader(ILogger? logger = null) : IDisplayLuminanceReader
{
    /// <summary>Höchstens so viele Adapter bzw. Ausgänge je Adapter (Schutz vor fehlerhaften Treibern).</summary>
    public const int MaxEntries = 16;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public static DxgiLuminanceReader Instance { get; } = new();

    public unsafe IReadOnlyList<DisplayLuminance> ReadAll()
    {
        var result = new List<DisplayLuminance>();
        void* factory = null;
        try
        {
            var iid = DxgiNative.IID_IDXGIFactory1;
            var hr = DxgiNative.CreateDXGIFactory1(&iid, &factory);
            if (hr < 0 || factory is null)
            {
                _logger.LogDebug("DXGI: keine Fabrik (HRESULT 0x{Hr:X8}).", hr);
                return result;
            }

            for (uint a = 0; a < MaxEntries; a++)
            {
                void* adapter = null;
                hr = DxgiNative.EnumAdapters1(factory, a, &adapter);
                if (hr < 0 || adapter is null)
                {
                    break; // DXGI_ERROR_NOT_FOUND: keine weiteren Adapter
                }

                try
                {
                    ReadOutputs(adapter, result);
                }
                finally
                {
                    DxgiNative.Release(adapter);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.SEHException or InvalidOperationException)
        {
            _logger.LogDebug("DXGI: Helligkeitswerte nicht lesbar: {Error}", ex.GetType().Name);
        }
        finally
        {
            DxgiNative.Release(factory);
        }

        return result;
    }

    /// <summary>Werte aus einer Beschreibung (eigene Methode, damit die Umrechnung ohne Grafikkarte testbar ist).</summary>
    internal static unsafe DisplayLuminance? FromDescription(in DxgiNative.DXGI_OUTPUT_DESC1 desc)
    {
        if (desc.AttachedToDesktop == 0)
        {
            return null;
        }

        string name;
        fixed (char* chars = desc.DeviceName)
        {
            name = new string(chars, 0, Length(chars, 32));
        }

        if (name.Length == 0)
        {
            return null;
        }

        static double Nits(float v) => float.IsFinite(v) && v >= 0 && v < 100_000 ? Math.Round(v, 4) : 0;
        return new DisplayLuminance(
            name,
            Nits(desc.MaxLuminance),
            Nits(desc.MaxFullFrameLuminance),
            Nits(desc.MinLuminance),
            (int)Math.Min(desc.BitsPerColor, 32),
            desc.ColorSpace == DxgiNative.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020);
    }

    private static unsafe int Length(char* chars, int max)
    {
        var n = 0;
        while (n < max && chars[n] != '\0')
        {
            n++;
        }

        return n;
    }

    private unsafe void ReadOutputs(void* adapter, List<DisplayLuminance> result)
    {
        for (uint o = 0; o < MaxEntries; o++)
        {
            void* output = null;
            var hr = DxgiNative.EnumOutputs(adapter, o, &output);
            if (hr < 0 || output is null)
            {
                break;
            }

            void* output6 = null;
            try
            {
                var iid = DxgiNative.IID_IDXGIOutput6;
                hr = DxgiNative.QueryInterface(output, &iid, &output6);
                if (hr < 0 || output6 is null)
                {
                    _logger.LogDebug("DXGI: Ausgang {Index} ohne IDXGIOutput6 (HRESULT 0x{Hr:X8}).", o, hr);
                    continue;
                }

                DxgiNative.DXGI_OUTPUT_DESC1 desc;
                hr = DxgiNative.GetDesc1(output6, &desc);
                if (hr >= 0 && FromDescription(desc) is { } value)
                {
                    result.Add(value);
                }
            }
            finally
            {
                DxgiNative.Release(output6);
                DxgiNative.Release(output);
            }
        }
    }
}
