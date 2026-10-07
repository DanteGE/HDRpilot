using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.Logging;

/// <summary>Minimale LoggerFactory: verteilt jede Zeile an alle Provider (Datei, Konsole). Spart eine weitere Abhängigkeit.</summary>
public sealed class CompositeLoggerFactory(params ILoggerProvider[] providers) : ILoggerFactory
{
    private readonly List<ILoggerProvider> _providers = [.. providers];

    public void AddProvider(ILoggerProvider provider) => _providers.Add(provider);

    public ILogger CreateLogger(string categoryName) =>
        new CompositeLogger(_providers.Select(p => p.CreateLogger(categoryName)).ToArray());

    public void Dispose()
    {
        foreach (var p in _providers)
        {
            p.Dispose();
        }
    }

    private sealed class CompositeLogger(ILogger[] loggers) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => loggers.Any(l => l.IsEnabled(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            foreach (var l in loggers)
            {
                if (l.IsEnabled(logLevel))
                {
                    l.Log(logLevel, eventId, state, exception, formatter);
                }
            }
        }
    }
}

/// <summary>Schreibt auf eine TextWriter-Ausgabe (Kommandozeile).</summary>
public sealed class TextWriterLoggerProvider(TextWriter writer, Func<LogLevel> minimumLevel) : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName[(categoryName.LastIndexOf('.') + 1)..]);

    public void Dispose()
    {
    }

    private sealed class Logger(TextWriterLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= owner._minimumLevel();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTime.Now:HH:mm:ss.fff} {logLevel.ToString()[..3].ToUpperInvariant()} {category}: {formatter(state, exception)}";
            lock (owner._gate)
            {
                owner._writer.WriteLine(exception is null ? line : line + " | " + exception.Message);
                owner._writer.Flush();
            }
        }
    }

    private readonly TextWriter _writer = writer;
    private readonly Func<LogLevel> _minimumLevel = minimumLevel;
}
