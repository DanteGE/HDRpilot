using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;

namespace HdrPilot.App.Infrastructure.Effects;

/// <summary>
/// Bildtakt für kurze Effekte mit eigenem Zeichnen (Partikel des HDR-Moments). In der App <see cref="TimelineFrameClock"/>,
/// in Tests eine Attrappe. Wer sich anmeldet, muss sich wieder abmelden: Solange jemand angemeldet ist, zeichnet WPF.
/// </summary>
public interface IFrameClock
{
    /// <summary>Wird je Takt ausgelöst (UI-Thread).</summary>
    event EventHandler? Frame;

    /// <summary>Anzahl der Anmeldungen (für Tests und die Prüfung „nach 2,5 s 0 Bildaufrufe“).</summary>
    int Subscribers { get; }
}

/// <summary>Zeitquelle der Effekte (Tests stellen die Zeit selbst).</summary>
public interface IEffectTime
{
    TimeSpan Now { get; }
}

/// <summary>
/// Bildtakt über eine Zeitleiste mit höchstens <see cref="EffectPolicy.OneShotFrameRate"/> Bildern pro Sekunde (Timeline.
/// DesiredFrameRate), nicht über CompositionTarget.Rendering: Das zeichnete das ganze Fenster in der Bildwiederholrate des
/// Bildschirms (100 Hz und mehr), egal was die Storyboards wünschen. Die Uhr der Zeitleiste läuft nur, solange jemand
/// angemeldet ist; danach ist sie angehalten und freigegeben.
/// </summary>
public sealed class TimelineFrameClock : IFrameClock
{
    private EventHandler? _frame;
    private AnimationClock? _clock;
    private int _subscribers;

    public TimelineFrameClock(int frameRate = EffectPolicy.OneShotFrameRate)
    {
        // Wert und Ziel sind gleich: Die Zeitleiste animiert nichts, sie gibt nur den Takt.
        Timeline = new DoubleAnimation(0, 0, new Duration(TimeSpan.FromSeconds(1))) { RepeatBehavior = RepeatBehavior.Forever };
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(Timeline, frameRate);
        Timeline.Freeze();
    }

    /// <summary>Die taktgebende Zeitleiste (Tests: Bildrate).</summary>
    public DoubleAnimation Timeline { get; }

    /// <summary>Gerade laufende Uhr (null ohne Anmeldung).</summary>
    public AnimationClock? Clock => _clock;

    public event EventHandler? Frame
    {
        add
        {
            if (value is null)
            {
                return;
            }

            _frame += value;
            if (_subscribers++ == 0)
            {
                _clock = Timeline.CreateClock();
                _clock.CurrentTimeInvalidated += OnTick;
            }
        }

        remove
        {
            if (value is null || _frame is null || !_frame.GetInvocationList().Contains(value))
            {
                return;
            }

            _frame -= value;
            if (--_subscribers == 0 && _clock is { } clock)
            {
                clock.CurrentTimeInvalidated -= OnTick;
                clock.Controller?.Stop();
                clock.Controller?.Remove();
                _clock = null;
            }
        }
    }

    public int Subscribers => _subscribers;

    private void OnTick(object? sender, EventArgs e)
    {
        if (_clock is { CurrentState: ClockState.Active })
        {
            _frame?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Zeit seit dem Start eines Stoppuhr-Objekts.</summary>
public sealed class StopwatchEffectTime : IEffectTime
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public TimeSpan Now => _watch.Elapsed;
}
