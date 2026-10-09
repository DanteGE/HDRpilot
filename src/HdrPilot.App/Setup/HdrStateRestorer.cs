using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Hdr;

namespace HdrPilot.App.Setup;

/// <summary>Stellt beim Entfernen einen gemerkten HDR-Zustand wieder her (Naht: Tests schalten nie den echten Bildschirm).</summary>
public interface IHdrStateRestorer
{
    Task<HdrSetResult> SetHdrAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken = default);
}

/// <summary>
/// Über die Display-Configuration-API wie die App (<see cref="DisplayConfigHdrController"/>), aber nie mit der
/// Notlösung Win+Alt+B: das Entfernen sendet keine Tastendrücke.
/// </summary>
public sealed class DisplayConfigHdrStateRestorer : IHdrStateRestorer
{
    private readonly DisplayConfigHdrController _controller = new(
        Win32DisplayConfigApi.Instance,
        SystemEnvironment.Instance,
        SystemClock.Instance,
        new HdrControllerOptions { HotkeyFallbackEnabled = false });

    public Task<HdrSetResult> SetHdrAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken = default) =>
        _controller.SetHdrAsync(monitorDevicePath, enable, allowHotkeyFallback: false, cancellationToken);
}

/// <summary>Prüflauf (<c>--test-root</c>): schaltet nie den echten Bildschirm, schreibt nur ins Setup-Protokoll.</summary>
public sealed class LoggingHdrStateRestorer(SetupLog log) : IHdrStateRestorer
{
    public Task<HdrSetResult> SetHdrAsync(string monitorDevicePath, bool enable, CancellationToken cancellationToken = default)
    {
        log.Info("Prüflauf: HDR für " + monitorDevicePath + " würde " + (enable ? "eingeschaltet" : "ausgeschaltet") + " (Bildschirm bleibt unberührt).");
        return Task.FromResult(new HdrSetResult { Status = HdrSetStatus.Success, MonitorDevicePath = monitorDevicePath, Message = "Prüflauf" });
    }
}
