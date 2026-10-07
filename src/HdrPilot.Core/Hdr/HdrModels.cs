using System.Text.Json.Serialization;

namespace HdrPilot.Core.Hdr;

/// <summary>DISPLAYCONFIG_ADVANCED_COLOR_MODE aus wingdi.h.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AdvancedColorMode>))]
public enum AdvancedColorMode
{
    Sdr = 0,
    Wcg = 1,
    Hdr = 2,
}

/// <summary>Welcher API-Weg einen Zustand geliefert oder gesetzt hat.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HdrApi>))]
public enum HdrApi
{
    /// <summary>Ab Windows 11 24H2 (Build 26100): GET_ADVANCED_COLOR_INFO_2 (15) und SET_HDR_STATE (16).</summary>
    Modern,

    /// <summary>Davor: GET_ADVANCED_COLOR_INFO (9) und SET_ADVANCED_COLOR_STATE (10).</summary>
    Legacy,
}

/// <summary>Ein aktiver Bildschirm mit seinem HDR-Zustand.</summary>
public sealed record DisplayInfo
{
    /// <summary>monitorDevicePath aus DISPLAYCONFIG_TARGET_DEVICE_NAME: dauerhafter Schlüssel.</summary>
    public required string MonitorDevicePath { get; init; }

    public required string FriendlyName { get; init; }

    /// <summary>GDI-Name der Quelle, etwa "\\.\DISPLAY1".</summary>
    public string? GdiDeviceName { get; init; }

    public bool IsPrimary { get; init; }

    public required bool HdrSupported { get; init; }

    public required bool HdrEnabled { get; init; }

    /// <summary>Nur über den neuen Weg bekannt.</summary>
    public AdvancedColorMode? ActiveColorMode { get; init; }

    /// <summary>Advanced Color durch Richtlinie gesperrt oder begrenzt.</summary>
    public bool BlockedByPolicy { get; init; }

    public required HdrApi Api { get; init; }

    /// <summary>Roher Bitfeld-Wert der Leseabfrage, für Diagnose.</summary>
    public uint RawColorFlags { get; init; }

    public uint BitsPerColorChannel { get; init; }

    /// <summary>Flüchtige Adressierung (adapterId, Ziel-ID). Vor jedem Schaltvorgang neu ermitteln.</summary>
    [JsonIgnore]
    public DisplayTarget Target { get; init; }
}

/// <summary>Flüchtige Adresse eines Bildschirms: adapterId (LUID) und Ziel-ID. Kann sich nach Neustart oder Treiberwechsel ändern.</summary>
public readonly record struct DisplayTarget(uint AdapterLow, int AdapterHigh, uint TargetId)
{
    public override string ToString() => $"{AdapterHigh:X8}:{AdapterLow:X8}/{TargetId}";
}

[JsonConverter(typeof(JsonStringEnumConverter<HdrSetStatus>))]
public enum HdrSetStatus
{
    /// <summary>Zustand gesetzt und durch erneutes Lesen bestätigt.</summary>
    Success,

    /// <summary>War schon im gewünschten Zustand, nichts geschaltet.</summary>
    AlreadyInState,

    /// <summary>Bildschirm kann kein HDR.</summary>
    NotHdrCapable,

    /// <summary>Bildschirm nicht angeschlossen oder nicht aktiv.</summary>
    NotConnected,

    /// <summary>Setzen oder Bestätigen gescheitert.</summary>
    Failed,
}

public sealed record HdrSetResult
{
    public required HdrSetStatus Status { get; init; }

    public required string MonitorDevicePath { get; init; }

    public string? FriendlyName { get; init; }

    public bool? Before { get; init; }

    public bool? After { get; init; }

    public HdrApi? Api { get; init; }

    public bool UsedHotkeyFallback { get; init; }

    public required string Message { get; init; }

    [JsonIgnore]
    public bool IsSuccess => Status is HdrSetStatus.Success or HdrSetStatus.AlreadyInState;
}

/// <summary>HDR-Steuerung. Alle Bildschirme werden bei jedem Aufruf frisch ermittelt.</summary>
public interface IHdrController
{
    /// <summary>Alle aktiven Bildschirme mit aktuellem Zustand.</summary>
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>
    /// Schaltet HDR am Bildschirm mit diesem monitorDevicePath und bestätigt den Zustand durch erneutes Lesen.
    /// Wirft nicht bei fehlendem oder nicht HDR-fähigem Bildschirm, sondern meldet das im Ergebnis.
    /// </summary>
    Task<HdrSetResult> SetHdrAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken = default);
}
