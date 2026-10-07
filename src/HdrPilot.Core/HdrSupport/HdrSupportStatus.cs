using System.Text.Json.Serialization;

namespace HdrPilot.Core.HdrSupport;

/// <summary>HDR-Unterstützung eines Spiels laut PCGamingWiki (Feld „hdr“ der Vorlage „Video settings“).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HdrSupportStatus>))]
public enum HdrSupportStatus
{
    /// <summary>Nicht gefunden, nicht geprüft oder im Wiki „unknown“/„n/a“.</summary>
    Unknown,

    /// <summary>Eigenes HDR („true“ oder „always on“).</summary>
    Native,

    /// <summary>HDR mit Einschränkungen („limited“).</summary>
    Limited,

    /// <summary>Nur über Mods oder Eingriffe („hackable“).</summary>
    ModOnly,

    /// <summary>Kein HDR („false“).</summary>
    None,
}

public static class HdrSupportValues
{
    /// <summary>Wert des Wiki-Felds (auch aus der Cargo-Tabelle Video.HDR); Unbekanntes gilt als <see cref="HdrSupportStatus.Unknown"/>.</summary>
    public static HdrSupportStatus Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" or "always on" => HdrSupportStatus.Native,
        "limited" => HdrSupportStatus.Limited,
        "hackable" => HdrSupportStatus.ModOnly,
        "false" => HdrSupportStatus.None,
        _ => HdrSupportStatus.Unknown,
    };

    /// <summary>„HDR-fähig“ im Sinne von Kennzeichen und Filter: eigenes HDR, auch eingeschränkt.</summary>
    public static bool IsHdrCapable(HdrSupportStatus status) => status is HdrSupportStatus.Native or HdrSupportStatus.Limited;
}
