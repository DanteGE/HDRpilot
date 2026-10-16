using System.Globalization;

namespace HdrPilot.App.Services.Display;

/// <summary>
/// Echte Helligkeitswerte eines Bildschirms für die Oberfläche (E89, G10.7). <see cref="PeakNits"/> ist die höchste
/// Nit-Zahl, die der Bildschirm bei eingeschaltetem HDR meldet (DXGI MaxLuminance, beim MAG 341C 456), auch wenn HDR gerade
/// aus ist (zuletzt mit HDR gemessen und gespeichert). Wurde er noch nie mit HDR gemessen, ist es der aktuelle Wert und
/// <see cref="PeakMeasuredWithHdr"/> ist false.
/// </summary>
public sealed record DisplayNits(
    string MonitorDevicePath,
    string FriendlyName,
    double PeakNits,
    double FullFrameNits,
    double MinNits,
    int BitsPerColor,
    bool HdrActive,
    bool PeakMeasuredWithHdr);

/// <summary>
/// Helligkeitswerte der Bildschirme über DXGI <c>IDXGIOutput6::GetDesc1</c>. Gelesen wird nur nach einem HDR- oder
/// Bildschirmwechsel (<see cref="Invalidate"/>) und nur, solange ein Fenster die Werte zeigt (<see cref="Acquire"/>); im
/// Hintergrund nie. Ereignisse auf dem UI-Thread. Änderungen nur hinzufügend und mit Standardumsetzung.
/// </summary>
public interface IDisplayLuminanceService : IDisposable
{
    /// <summary>Werte haben sich geändert (UI-Thread).</summary>
    event EventHandler? Changed;

    /// <summary>Erster Zielbildschirm (den HDRpilot ohne Spiel-Einstellung schaltet); null, solange unbekannt.</summary>
    DisplayNits? Primary { get; }

    /// <summary>Alle aktiven Bildschirme mit bekannten Werten.</summary>
    IReadOnlyList<DisplayNits> Displays { get; }

    /// <summary>HDR oder Bildschirme könnten sich geändert haben (AppServices ruft das auf); liest neu, solange Freigaben bestehen.</summary>
    void Invalidate();

    /// <summary>Ein Fenster zeigt die Werte: lesen (im Hintergrund) und aktuell halten, bis die Rückgabe freigegeben ist.</summary>
    IDisposable Acquire();
}

/// <summary>
/// Gemeinsame Nit-Skala aller Anzeigen (Anmerkung 3 des Nutzers): Das Maximum ist immer <see cref="DisplayNits.PeakNits"/>,
/// nie ein fester Wert wie 1000.
/// </summary>
public static class NitScale
{
    /// <summary>Höchster Wert der Skala.</summary>
    public static double Maximum(DisplayNits display) => display.PeakNits > 0 ? display.PeakNits : 0;

    /// <summary>Anteil 0–1 eines Werts auf der Skala dieses Bildschirms.</summary>
    public static double Fraction(double nits, DisplayNits display)
    {
        var max = Maximum(display);
        return max <= 0 || !double.IsFinite(nits) ? 0 : Math.Clamp(nits / max, 0, 1);
    }

    /// <summary>Anzeige wie im Prototyp: unter 1 Nit mit vier Nachkommastellen („0,0000“), sonst ganze Zahl („456“).</summary>
    public static string Format(double nits, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return nits < 1 ? nits.ToString("0.0000", culture) : Math.Round(nits).ToString("0", culture);
    }
}

/// <summary>Ohne Bildschirmwerte (Tests, Gerüst): nichts bekannt, nichts wird gelesen.</summary>
public sealed class NullDisplayLuminanceService : IDisplayLuminanceService
{
    public static NullDisplayLuminanceService Instance { get; } = new();

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public DisplayNits? Primary => null;

    public IReadOnlyList<DisplayNits> Displays => [];

    public void Invalidate()
    {
    }

    public IDisposable Acquire() => Artwork.NoLease.Instance;

    public void Dispose()
    {
    }
}

/// <summary>
/// Feste Werte eines Bildschirms, nur für Bildschirmfotos (<c>--snapshot-display</c>, E89): So zeigen Kapsel, Skala und
/// Vorschau die Werte auch bei ausgeschaltetem Monitor, wenn DXGI nichts meldet. Liest nie etwas.
/// </summary>
public sealed class FixedDisplayLuminanceService(DisplayNits display) : IDisplayLuminanceService
{
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public DisplayNits? Primary => display;

    public IReadOnlyList<DisplayNits> Displays { get; } = [display];

    public void Invalidate()
    {
    }

    public IDisposable Acquire() => Artwork.NoLease.Instance;

    public void Dispose()
    {
    }
}
