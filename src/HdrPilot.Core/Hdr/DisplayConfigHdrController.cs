using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Hdr;

public sealed record HdrControllerOptions
{
    /// <summary>Ab diesem Build (Windows 11 24H2) gilt der neue API-Weg (Typ 15/16).</summary>
    public const int ModernApiMinBuild = 26100;

    /// <summary>Erzwingt einen API-Weg (Diagnose). Null: nach Windows-Build.</summary>
    public HdrApi? ForceApi { get; init; }

    /// <summary>Wie oft nach dem Setzen erneut gelesen wird.</summary>
    public int VerifyAttempts { get; init; } = 10;

    /// <summary>Wartezeit vor jedem erneuten Lesen.</summary>
    public TimeSpan VerifyInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Notlösung Win+Alt+B, nur wenn die API scheitert. Standard: aus.</summary>
    public bool HotkeyFallbackEnabled { get; init; }
}

/// <summary>Sendet das Windows-Tastenkürzel für HDR (Win+Alt+B, Xbox Game Bar).</summary>
public interface IHdrHotkeySender
{
    bool SendToggle();
}

/// <summary>
/// HDR-Steuerung über die Display-Configuration-API. Ermittelt Bildschirme bei jedem Aufruf neu,
/// wählt den API-Weg nach Windows-Build, fällt bei Fehlern auf den älteren Weg zurück und bestätigt
/// jeden Schaltvorgang durch erneutes Lesen.
/// </summary>
public sealed class DisplayConfigHdrController : IHdrController
{
    private readonly IDisplayConfigApi _api;
    private readonly IAppEnvironment _environment;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly HdrControllerOptions _options;
    private readonly IHdrHotkeySender? _hotkey;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private int _modernReadFallbackLogged;

    public DisplayConfigHdrController(
        IDisplayConfigApi api,
        IAppEnvironment environment,
        IClock clock,
        HdrControllerOptions? options = null,
        IHdrHotkeySender? hotkey = null,
        ILogger<DisplayConfigHdrController>? logger = null)
    {
        _api = api;
        _environment = environment;
        _clock = clock;
        _options = options ?? new HdrControllerOptions();
        _hotkey = hotkey;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>True, wenn der neue API-Weg (Typ 15/16) zuerst versucht wird.</summary>
    public bool PrefersModernApi => _options.ForceApi switch
    {
        HdrApi.Modern => true,
        HdrApi.Legacy => false,
        _ => _environment.WindowsBuild >= HdrControllerOptions.ModernApiMinBuild,
    };

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var rc = _api.QueryActivePaths(out var paths);
        if (rc != DisplayConfigNative.ERROR_SUCCESS)
        {
            _logger.LogWarning("QueryDisplayConfig fehlgeschlagen (Win32-Fehler {Code}).", rc);
            return [];
        }

        var displays = new List<DisplayInfo>(paths.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            rc = _api.GetTargetName(path.Target, out var name);
            if (rc != DisplayConfigNative.ERROR_SUCCESS || string.IsNullOrEmpty(name.MonitorDevicePath))
            {
                _logger.LogDebug("Ziel {Target} ohne Gerätenamen übersprungen (Win32-Fehler {Code}).", path.Target, rc);
                continue;
            }

            if (!seen.Add(name.MonitorDevicePath))
            {
                continue; // Klonmodus: dasselbe Ziel über mehrere Quellen.
            }

            _api.GetSourceGdiName(path.SourceAdapterLow, path.SourceAdapterHigh, path.SourceId, out var gdiName);
            displays.Add(BuildDisplayInfo(path, name, gdiName));
        }

        return displays;
    }

    public async Task<HdrSetResult> SetHdrAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken = default)
    {
        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SetHdrCoreAsync(monitorDevicePath, enable, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    /// <summary>True, wenn der Bildschirm den gewünschten Zustand zeigt (neuer Weg: Schalter und aktiver Modus).</summary>
    public static bool IsInState(DisplayInfo display, bool enable)
    {
        if (display.HdrEnabled != enable)
        {
            return false;
        }

        return display.ActiveColorMode is not { } mode || (mode == AdvancedColorMode.Hdr) == enable;
    }

    private async Task<HdrSetResult> SetHdrCoreAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken)
    {
        var display = Find(monitorDevicePath);
        if (display is null)
        {
            return Result(HdrSetStatus.NotConnected, monitorDevicePath, null, null, null, null,
                "Bildschirm nicht angeschlossen oder nicht aktiv.");
        }

        if (!display.HdrSupported)
        {
            return Result(HdrSetStatus.NotHdrCapable, monitorDevicePath, display.FriendlyName, display.HdrEnabled, display.HdrEnabled, display.Api,
                $"Bildschirm \"{display.FriendlyName}\" unterstützt kein HDR.");
        }

        if (IsInState(display, enable))
        {
            return Result(HdrSetStatus.AlreadyInState, monitorDevicePath, display.FriendlyName, display.HdrEnabled, display.HdrEnabled, display.Api,
                $"HDR ist bereits {OnOff(enable)}.");
        }

        var before = display.HdrEnabled;
        var api = display.Api;
        var rc = api == HdrApi.Modern
            ? _api.SetHdrState(display.Target, enable)
            : _api.SetAdvancedColorState(display.Target, enable);

        if (rc != DisplayConfigNative.ERROR_SUCCESS && api == HdrApi.Modern)
        {
            _logger.LogWarning(
                "SET_HDR_STATE (Typ 16) für \"{Name}\" fehlgeschlagen (Win32-Fehler {Code}), falle auf SET_ADVANCED_COLOR_STATE (Typ 10) zurück.",
                display.FriendlyName, rc);
            api = HdrApi.Legacy;
            rc = _api.SetAdvancedColorState(display.Target, enable);
        }

        DisplayInfo? last = display;
        if (rc == DisplayConfigNative.ERROR_SUCCESS)
        {
            (var confirmed, last) = await VerifyAsync(monitorDevicePath, enable, cancellationToken).ConfigureAwait(false);
            if (confirmed)
            {
                return Result(HdrSetStatus.Success, monitorDevicePath, display.FriendlyName, before, enable, api,
                    $"HDR {OnOff(enable)} (bestätigt).");
            }

            _logger.LogWarning(
                "HDR {State} für \"{Name}\" über {Api} gesetzt, aber nicht bestätigt (zuletzt gelesen: HDR {Last}, Modus {Mode}).",
                OnOff(enable), display.FriendlyName, api, last is null ? "unbekannt" : OnOff(last.HdrEnabled), last?.ActiveColorMode);
        }
        else
        {
            _logger.LogWarning("HDR-Schaltung für \"{Name}\" über {Api} fehlgeschlagen (Win32-Fehler {Code}).", display.FriendlyName, api, rc);
        }

        if (_options.HotkeyFallbackEnabled && _hotkey is not null)
        {
            return await HotkeyFallbackAsync(monitorDevicePath, enable, display, before, cancellationToken).ConfigureAwait(false);
        }

        var detail = rc != DisplayConfigNative.ERROR_SUCCESS
            ? $"Windows meldet Fehler {rc}."
            : last is null
                ? "Bildschirm nach dem Schalten nicht mehr gefunden."
                : last.HdrEnabled == enable && last.ActiveColorMode is { } mode && (mode == AdvancedColorMode.Hdr) != enable
                    ? $"Schalter steht auf {OnOff(enable)}, aktiver Modus ist aber {mode} (evtl. durch Richtlinie begrenzt)."
                    : $"Zustand nach {_options.VerifyAttempts} Prüfungen weiterhin HDR {OnOff(last.HdrEnabled)}.";
        return Result(HdrSetStatus.Failed, monitorDevicePath, display.FriendlyName, before, last?.HdrEnabled, api,
            $"HDR ließ sich nicht {(enable ? "einschalten" : "ausschalten")}. {detail}");
    }

    private async Task<HdrSetResult> HotkeyFallbackAsync(string monitorDevicePath, bool enable, DisplayInfo display, bool before, CancellationToken cancellationToken)
    {
        // Das Kürzel schaltet nur um: vorher den Zustand prüfen, damit es nicht in die falsche Richtung geht.
        var current = Find(monitorDevicePath);
        if (current is null)
        {
            return Result(HdrSetStatus.NotConnected, monitorDevicePath, display.FriendlyName, before, null, display.Api,
                "Bildschirm vor der Tastenkürzel-Notlösung nicht mehr gefunden.");
        }

        if (IsInState(current, enable))
        {
            return Result(HdrSetStatus.Success, monitorDevicePath, display.FriendlyName, before, enable, current.Api,
                $"HDR {OnOff(enable)} (verspätet bestätigt).");
        }

        if (current.HdrEnabled == enable)
        {
            // Schalter steht schon richtig, nur der aktive Modus fehlt: Umschalten würde es verschlimmern.
            return Result(HdrSetStatus.Failed, monitorDevicePath, display.FriendlyName, before, current.HdrEnabled, current.Api,
                "HDR-Schalter steht richtig, aber Windows aktiviert den Modus nicht. Tastenkürzel nicht verwendet.");
        }

        _logger.LogWarning("Notlösung: sende Win+Alt+B für \"{Name}\". Das Kürzel betrifft alle HDR-fähigen Bildschirme.", display.FriendlyName);
        if (!_hotkey!.SendToggle())
        {
            return Result(HdrSetStatus.Failed, monitorDevicePath, display.FriendlyName, before, current.HdrEnabled, current.Api,
                "Tastenkürzel Win+Alt+B ließ sich nicht senden.");
        }

        var (confirmed, last) = await VerifyAsync(monitorDevicePath, enable, cancellationToken).ConfigureAwait(false);
        return confirmed
            ? Result(HdrSetStatus.Success, monitorDevicePath, display.FriendlyName, before, enable, current.Api,
                $"HDR {OnOff(enable)} über Win+Alt+B (bestätigt).", usedHotkey: true)
            : Result(HdrSetStatus.Failed, monitorDevicePath, display.FriendlyName, before, last?.HdrEnabled, current.Api,
                "Auch Win+Alt+B hat den Zustand nicht geändert (Xbox Game Bar aktiv?).", usedHotkey: true);
    }

    private async Task<(bool Confirmed, DisplayInfo? Last)> VerifyAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken)
    {
        DisplayInfo? last = null;
        var attempts = Math.Max(1, _options.VerifyAttempts);
        for (var i = 0; i < attempts; i++)
        {
            await _clock.Delay(_options.VerifyInterval, cancellationToken).ConfigureAwait(false);
            last = Find(monitorDevicePath);
            if (last is not null && IsInState(last, enable))
            {
                return (true, last);
            }
        }

        return (false, last);
    }

    private DisplayInfo? Find(string monitorDevicePath) =>
        GetDisplays().FirstOrDefault(d => string.Equals(d.MonitorDevicePath, monitorDevicePath, StringComparison.OrdinalIgnoreCase));

    private DisplayInfo BuildDisplayInfo(RawDisplayPath path, RawTargetName name, string gdiName)
    {
        if (PrefersModernApi)
        {
            var rc2 = _api.GetAdvancedColorInfo2(path.Target, out var info2);
            if (rc2 == DisplayConfigNative.ERROR_SUCCESS)
            {
                return FromInfo2(path, name, gdiName, info2);
            }

            if (Interlocked.Exchange(ref _modernReadFallbackLogged, 1) == 0)
            {
                _logger.LogWarning(
                    "GET_ADVANCED_COLOR_INFO_2 (Typ 15) für \"{Name}\" fehlgeschlagen (Win32-Fehler {Code}), falle auf Typ 9 zurück.",
                    name.FriendlyName, rc2);
            }
        }

        var rc = _api.GetAdvancedColorInfo(path.Target, out var info);
        if (rc == DisplayConfigNative.ERROR_SUCCESS)
        {
            return FromLegacy(path, name, gdiName, info);
        }

        _logger.LogWarning("HDR-Zustand für \"{Name}\" nicht lesbar (Win32-Fehler {Code}).", name.FriendlyName, rc);
        return new DisplayInfo
        {
            MonitorDevicePath = name.MonitorDevicePath,
            FriendlyName = DisplayName(name),
            GdiDeviceName = NullIfEmpty(gdiName),
            IsPrimary = path.IsPrimary,
            HdrSupported = false,
            HdrEnabled = false,
            Api = HdrApi.Legacy,
            Target = path.Target,
        };
    }

    /// <summary>Deutet Typ 15: maßgeblich sind highDynamicRangeSupported, highDynamicRangeUserEnabled und activeColorMode.</summary>
    internal static DisplayInfo FromInfo2(RawDisplayPath path, RawTargetName name, string gdiName, RawColorInfo2 info) => new()
    {
        MonitorDevicePath = name.MonitorDevicePath,
        FriendlyName = DisplayName(name),
        GdiDeviceName = NullIfEmpty(gdiName),
        IsPrimary = path.IsPrimary,
        HdrSupported = AdvancedColorBits.IsSet(info.Value, AdvancedColorBits.Info2HighDynamicRangeSupported),
        HdrEnabled = AdvancedColorBits.IsSet(info.Value, AdvancedColorBits.Info2HighDynamicRangeUserEnabled),
        ActiveColorMode = info.ActiveColorMode <= 2 ? (AdvancedColorMode)info.ActiveColorMode : null,
        BlockedByPolicy = AdvancedColorBits.IsSet(info.Value, AdvancedColorBits.Info2AdvancedColorLimitedByPolicy),
        Api = HdrApi.Modern,
        RawColorFlags = info.Value,
        BitsPerColorChannel = info.BitsPerColorChannel,
        Target = path.Target,
    };

    /// <summary>
    /// Deutet Typ 9. advancedColorEnabled ist auch bei Wide Color (automatische Farbverwaltung) gesetzt;
    /// dann ist zusätzlich wideColorEnforced gesetzt. HDR gilt nur als an, wenn Advanced Color ohne
    /// erzwungene Wide Color aktiv ist (am System mit Build 26200 gemessen, siehe docs/entscheidungen.md).
    /// </summary>
    internal static DisplayInfo FromLegacy(RawDisplayPath path, RawTargetName name, string gdiName, RawColorInfo info)
    {
        var supported = AdvancedColorBits.IsSet(info.Value, AdvancedColorBits.LegacyAdvancedColorSupported);
        var enabled = AdvancedColorBits.IsSet(info.Value, AdvancedColorBits.LegacyAdvancedColorEnabled);
        var wideColorEnforced = AdvancedColorBits.IsSet(info.Value, AdvancedColorBits.LegacyWideColorEnforced);
        var forceDisabled = AdvancedColorBits.IsSet(info.Value, AdvancedColorBits.LegacyAdvancedColorForceDisabled);
        return new DisplayInfo
        {
            MonitorDevicePath = name.MonitorDevicePath,
            FriendlyName = DisplayName(name),
            GdiDeviceName = NullIfEmpty(gdiName),
            IsPrimary = path.IsPrimary,
            HdrSupported = supported && !forceDisabled,
            HdrEnabled = enabled && !wideColorEnforced,
            ActiveColorMode = null,
            BlockedByPolicy = forceDisabled,
            Api = HdrApi.Legacy,
            RawColorFlags = info.Value,
            BitsPerColorChannel = info.BitsPerColorChannel,
            Target = path.Target,
        };
    }

    private static string DisplayName(RawTargetName name) =>
        string.IsNullOrWhiteSpace(name.FriendlyName) ? "Unbenannter Bildschirm" : name.FriendlyName;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static string OnOff(bool on) => on ? "an" : "aus";

    private static HdrSetResult Result(
        HdrSetStatus status, string path, string? name, bool? before, bool? after, HdrApi? api, string message, bool usedHotkey = false) => new()
    {
        Status = status,
        MonitorDevicePath = path,
        FriendlyName = name,
        Before = before,
        After = after,
        Api = api,
        Message = message,
        UsedHotkeyFallback = usedHotkey,
    };
}
