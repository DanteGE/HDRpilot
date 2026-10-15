using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Display;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Bildschirm-Kapsel im Kopf der Bibliothek (Anmerkung 3 des Nutzers, G10.7): Name und Farbtiefe, Spitze, Vollbild und
/// Minimum in Nits, dazu eine Skala von 0 bis zur Spitze bei eingeschaltetem HDR (<see cref="NitScale.Maximum"/>, nie ein
/// fester Wert wie 1000). Ohne bekannte Werte stehen Striche da und es gibt keine Skala.
/// </summary>
public sealed class NitsReadout : ObservableObject
{
    private DisplayNits? _display;
    private string _fallbackName = string.Empty;

    /// <summary>Werte des Bildschirms (null: unbekannt).</summary>
    public DisplayNits? Display => _display;

    public bool HasValues => _display is { PeakNits: > 0 };

    /// <summary>„MAG 341C OLED · 10 BIT“ (ohne bekannte Farbtiefe nur der Name).</summary>
    public string DisplayName
    {
        get
        {
            var name = _display?.FriendlyName is { Length: > 0 } n ? n : _fallbackName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = LibraryStrings.Nits_UnknownDisplay;
            }

            return _display is { BitsPerColor: > 0 } d
                ? LibraryText.Format(LibraryStrings.Nits_DisplayWithBits, name, d.BitsPerColor).ToUpper(System.Globalization.CultureInfo.CurrentCulture)
                : name.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    public string PeakText => HasValues ? NitScale.Format(_display!.PeakNits) : LibraryStrings.Nits_Unknown;

    public string FullFrameText => HasValues && _display!.FullFrameNits > 0 ? NitScale.Format(_display.FullFrameNits) : LibraryStrings.Nits_Unknown;

    public string MinimumText => HasValues ? NitScale.Format(_display!.MinNits) : LibraryStrings.Nits_Unknown;

    /// <summary>Höchster Wert der Skala = Spitze bei eingeschaltetem HDR; 0 ohne Werte.</summary>
    public double ScaleMaximum => _display is { } d ? NitScale.Maximum(d) : 0;

    /// <summary>Lage der Vollbild-Marke auf der Skala (0–1).</summary>
    public double FullFrameFraction => _display is { } d ? NitScale.Fraction(d.FullFrameNits, d) : 0;

    /// <summary>Beschriftung des rechten Skalenendes: „456 nits“.</summary>
    public string ScaleEndText => HasValues ? LibraryText.Format(LibraryStrings.Nits_ScaleEnd, NitScale.Format(ScaleMaximum)) : string.Empty;

    /// <summary>Erklärung zum Überfahren: Herkunft der Werte, Skala bis zur Spitze.</summary>
    public string Tooltip => HasValues
        ? LibraryText.Format(LibraryStrings.Nits_Tooltip, NitScale.Format(_display!.PeakNits), NitScale.Format(_display.FullFrameNits), NitScale.Format(_display.MinNits), _display.BitsPerColor)
        : LibraryStrings.Nits_TooltipUnknown;

    /// <summary>Spitze noch nie mit eingeschaltetem HDR gemessen: Hinweis im Tooltip.</summary>
    public bool PeakMeasuredWithHdr => _display?.PeakMeasuredWithHdr == true;

    /// <summary>Neue Werte übernehmen (nur bei Änderung wird gemeldet).</summary>
    internal void Update(DisplayNits? display, string fallbackName)
    {
        if (Equals(display, _display) && string.Equals(fallbackName, _fallbackName, StringComparison.Ordinal))
        {
            return;
        }

        _display = display;
        _fallbackName = fallbackName;
        OnPropertyChanged(string.Empty);
    }
}
