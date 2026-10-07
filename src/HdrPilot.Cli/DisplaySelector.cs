using System.Globalization;
using HdrPilot.Core.Hdr;

namespace HdrPilot.Cli;

public static class DisplaySelector
{
    /// <summary>
    /// Wählt Bildschirme: null/"primary" = Hauptbildschirm, "all" = alle HDR-fähigen, Zahl = Position aus
    /// "hdr status" (ab 1), sonst exakter monitorDevicePath oder eindeutiger Namensteil.
    /// </summary>
    public static IReadOnlyList<DisplayInfo>? Select(IReadOnlyList<DisplayInfo> displays, string? selector, out string? error)
    {
        error = null;
        if (displays.Count == 0)
        {
            error = "Keine aktiven Bildschirme gefunden.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(selector) || selector.Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
            return [primary];
        }

        if (selector.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var capable = displays.Where(d => d.HdrSupported).ToList();
            if (capable.Count == 0)
            {
                // Kein HDR-fähiger Bildschirm: alle zurückgeben, damit jeder einzeln "nicht HDR-fähig" meldet.
                return displays;
            }

            return capable;
        }

        if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            if (index >= 1 && index <= displays.Count)
            {
                return [displays[index - 1]];
            }

            error = $"Bildschirm Nummer {index} nicht gefunden (vorhanden: 1 bis {displays.Count}).";
            return null;
        }

        var byPath = displays.FirstOrDefault(d => d.MonitorDevicePath.Equals(selector, StringComparison.OrdinalIgnoreCase));
        if (byPath is not null)
        {
            return [byPath];
        }

        var byName = displays.Where(d => d.FriendlyName.Contains(selector, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1)
        {
            return byName;
        }

        error = byName.Count > 1
            ? $"Mehrdeutig: \"{selector}\" passt zu {byName.Count} Bildschirmen. Nummer oder monitorDevicePath angeben."
            : $"Kein Bildschirm passt zu \"{selector}\".";
        return null;
    }
}
