using System.Diagnostics;
using System.Windows.Media;

namespace HdrPilot.App.Infrastructure.Effects;

/// <summary>
/// Bildtakt für kurze Effekte mit eigenem Zeichnen (Partikel des HDR-Moments). In der App <see cref="CompositionTarget.Rendering"/>,
/// in Tests eine Attrappe. Wer sich anmeldet, muss sich wieder abmelden: Solange jemand angemeldet ist, zeichnet WPF jedes Bild.
/// </summary>
public interface IFrameClock
{
    /// <summary>Wird je gezeichnetem Bild ausgelöst (UI-Thread).</summary>
    event EventHandler? Frame;

    /// <summary>Anzahl der Anmeldungen (für Tests und die Prüfung „nach 2,5 s 0 Bildaufrufe“).</summary>
    int Subscribers { get; }
}

/// <summary>Zeitquelle der Effekte (Tests stellen die Zeit selbst).</summary>
public interface IEffectTime
{
    TimeSpan Now { get; }
}

/// <summary><see cref="CompositionTarget.Rendering"/> als <see cref="IFrameClock"/>; meldet sich nur an, solange jemand zuhört.</summary>
public sealed class CompositionFrameClock : IFrameClock
{
    private EventHandler? _frame;
    private int _subscribers;

    public event EventHandler? Frame
    {
        add
        {
            if (value is null)
            {
                return;
            }

            if (_subscribers++ == 0)
            {
                CompositionTarget.Rendering += OnRendering;
            }

            _frame += value;
        }

        remove
        {
            if (value is null || _frame is null || !_frame.GetInvocationList().Contains(value))
            {
                return;
            }

            _frame -= value;
            if (--_subscribers == 0)
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }
    }

    public int Subscribers => _subscribers;

    private void OnRendering(object? sender, EventArgs e) => _frame?.Invoke(this, EventArgs.Empty);
}

/// <summary>Zeit seit dem Start eines Stoppuhr-Objekts.</summary>
public sealed class StopwatchEffectTime : IEffectTime
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public TimeSpan Now => _watch.Elapsed;
}
