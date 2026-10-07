using HdrPilot.Core.Abstractions;

namespace HdrPilot.Tests.TestSupport;

/// <summary>Steuerbare Uhr. Delay rückt die Zeit sofort vor und protokolliert die Wartezeit.</summary>
public sealed class FakeClock : IClock
{
    private readonly object _gate = new();

    public FakeClock(DateTimeOffset? start = null) => UtcNow = start ?? new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; private set; }

    public List<TimeSpan> Delays { get; } = [];

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            UtcNow += by;
        }
    }

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Delays.Add(delay);
            if (delay > TimeSpan.Zero)
            {
                UtcNow += delay;
            }
        }

        return Task.CompletedTask;
    }
}
