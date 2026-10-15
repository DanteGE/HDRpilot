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
/// DXGI-Leser (CreateDXGIFactory1 → Adapter → Ausgänge → IDXGIOutput6::GetDesc1). Gerüst: liefert noch nichts; die
/// Umsetzung über die COM-Tabellen (ohne zusätzliches Paket) folgt mit G10.7.
/// </summary>
public sealed class DxgiLuminanceReader : IDisplayLuminanceReader
{
    public static DxgiLuminanceReader Instance { get; } = new();

    public IReadOnlyList<DisplayLuminance> ReadAll() => [];
}
