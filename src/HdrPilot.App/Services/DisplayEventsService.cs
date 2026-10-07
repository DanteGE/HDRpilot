using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace HdrPilot.App.Services;

/// <summary>Quelle roher Bildschirmereignisse (für Tests austauschbar). Meldet auf einem beliebigen Thread.</summary>
public interface IDisplayEventSource
{
    void Attach(Action<string> onEvent);

    void Detach();
}

/// <summary>Einmal-Zeitgeber für die Entprellung (für Tests austauschbar). Wird nur auf dem UI-Thread bedient.</summary>
public interface IDebounceTimer : IDisposable
{
    event EventHandler? Elapsed;

    /// <summary>Startet die Wartezeit neu.</summary>
    void Restart();

    void Stop();
}

/// <summary>
/// WM_DISPLAYCHANGE (SystemEvents.DisplaySettingsChanged) und Aufwachen aus dem Ruhezustand (PowerModeChanged/Resume).
/// Die statischen SystemEvents werden im <see cref="Detach"/> wieder abgemeldet.
/// </summary>
public sealed class SystemDisplayEventSource : IDisplayEventSource
{
    private Action<string>? _onEvent;

    public void Attach(Action<string> onEvent)
    {
        Detach();
        _onEvent = onEvent;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Detach()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _onEvent = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _onEvent?.Invoke("WM_DISPLAYCHANGE");

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _onEvent?.Invoke("Resume");
        }
    }
}

/// <summary>DispatcherTimer, der nur läuft, solange eine Entprellung aussteht (kein Dauer-Timer).</summary>
public sealed class DispatcherDebounceTimer(Dispatcher dispatcher, TimeSpan delay) : IDebounceTimer
{
    private DispatcherTimer? _timer;

    public event EventHandler? Elapsed;

    public void Restart()
    {
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    public void Dispose()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = delay };
        timer.Tick += OnTick;
        return timer;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        Elapsed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Meldet Bildschirmänderungen entprellt: Nach dem letzten rohen Ereignis wird <see cref="DebounceDelay"/> gewartet,
/// dann einmal <see cref="DisplaysChanged"/> auf dem UI-Thread ausgelöst. Anstecken, Auflösungswechsel und Aufwachen
/// erzeugen oft mehrere Ereignisse in Folge; so wird nur einmal neu geprüft.
/// </summary>
public sealed class DisplayEventsService : IDisplayEventsService
{
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1.5);

    private readonly IDisplayEventSource _source;
    private readonly IDebounceTimer _timer;
    private readonly Action<Action> _postToUi;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly List<string> _pending = [];
    private bool _started;
    private volatile bool _disposed;

    /// <param name="postToUi">Führt eine Aktion auf dem UI-Thread aus (dort lebt der Zeitgeber).</param>
    public DisplayEventsService(IDisplayEventSource source, IDebounceTimer timer, Action<Action> postToUi, ILogger? logger = null)
    {
        _source = source;
        _timer = timer;
        _postToUi = postToUi;
        _logger = logger ?? NullLogger.Instance;
        _timer.Elapsed += OnTimerElapsed;
    }

    /// <summary>Entprellt, auf dem UI-Thread.</summary>
    public event EventHandler? DisplaysChanged;

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _source.Attach(OnRawEvent);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started)
        {
            _source.Detach();
        }

        _timer.Elapsed -= OnTimerElapsed;
        _timer.Stop();
        _timer.Dispose();
        DisplaysChanged = null;
    }

    private void OnRawEvent(string reason)
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            _pending.Add(reason);
        }

        _postToUi(() =>
        {
            if (!_disposed)
            {
                _timer.Restart();
            }
        });
    }

    private void OnTimerElapsed(object? sender, EventArgs e)
    {
        _timer.Stop();
        string[] reasons;
        lock (_gate)
        {
            reasons = [.. _pending];
            _pending.Clear();
        }

        if (_disposed || reasons.Length == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Bildschirmänderung erkannt ({Count} Ereignisse entprellt: {Reasons}). Ausstehendes wird geprüft.",
            reasons.Length, string.Join(", ", reasons.Distinct(StringComparer.Ordinal)));
        DisplaysChanged?.Invoke(this, EventArgs.Empty);
    }
}
