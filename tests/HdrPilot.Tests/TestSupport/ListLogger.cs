using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.TestSupport;

/// <summary>Sammelt Protokollzeilen, damit Tests prüfen können, ob etwas protokolliert wurde.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    private readonly object _gate = new();

    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    public bool Contains(LogLevel level, string fragment)
    {
        lock (_gate)
        {
            return Entries.Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }
}
