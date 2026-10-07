using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Processes;

/// <summary>
/// Fragt die Prozessliste im eingestellten Abstand ab (Standard 1000 ms) und ermittelt neue und beendete
/// Prozesse aus der Differenz. Der Rückruf läuft nach jedem Zyklus, auch ohne Änderungen, damit die
/// Sitzungslogik Nachlaufzeiten prüfen kann. Zyklen überlappen nie.
/// </summary>
public sealed class PollingProcessWatcher(IProcessSnapshotProvider provider, TimeSpan interval, ILogger? logger = null) : IProcessWatcher
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly object _gate = new();
    private Dictionary<int, ProcessInfo> _previous = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private TimeSpan _interval = interval;

    public TimeSpan Interval
    {
        get => _interval;
        set => _interval = value < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : value;
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public ProcessChanges Poll()
    {
        var current = provider.GetProcesses();
        lock (_gate)
        {
            var now = new Dictionary<int, ProcessInfo>(current.Count);
            foreach (var p in current)
            {
                now[p.ProcessId] = p;
            }

            var started = new List<ProcessInfo>();
            foreach (var p in current)
            {
                // Neue PID oder wiederverwendete PID mit anderem Prozess.
                if (!_previous.TryGetValue(p.ProcessId, out var old) || !SameProcess(old, p))
                {
                    started.Add(p);
                }
            }

            var exited = new List<ProcessInfo>();
            foreach (var (pid, old) in _previous)
            {
                if (!now.TryGetValue(pid, out var p) || !SameProcess(old, p))
                {
                    exited.Add(old);
                }
            }

            _previous = now;
            return new ProcessChanges(started, exited, current);
        }
    }

    public void Start(Func<ProcessChanges, CancellationToken, Task> onPoll)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Überwachung läuft bereits.");
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => LoopAsync(onPoll, token), token);
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        if (cts is null || loop is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private async Task LoopAsync(Func<ProcessChanges, CancellationToken, Task> onPoll, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var started = Environment.TickCount64;
            try
            {
                var changes = Poll();
                await onPoll(changes, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler im Abfragezyklus der Prozessüberwachung.");
            }

            // Abstand von Beginn zu Beginn, mindestens 50 ms Pause.
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
            var wait = Interval - elapsed;
            await Task.Delay(wait > TimeSpan.FromMilliseconds(50) ? wait : TimeSpan.FromMilliseconds(50), token).ConfigureAwait(false);
        }
    }

    private static bool SameProcess(ProcessInfo a, ProcessInfo b) =>
        string.Equals(a.ExeName, b.ExeName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.ExePath, b.ExePath, StringComparison.OrdinalIgnoreCase);
}
