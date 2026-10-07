using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using HdrPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.Logging;

/// <summary>
/// Rollierendes Protokoll: eine Datei je Tag (hdrpilot-JJJJMMTT.log), bei Überschreiten der Größe weitere
/// Dateien mit Zähler. Dateien älter als <see cref="RetainDays"/> Tage werden gelöscht. Jede Zeile wird sofort
/// geschrieben, damit nach einem Absturz nichts fehlt.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly Func<LogLevel> _minimumLevel;
    private readonly IClock _clock;
    private readonly long _maxFileBytes;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private StreamWriter? _writer;
    private string? _currentPath;
    private DateOnly _currentDay;
    private bool _disposed;

    public RollingFileLoggerProvider(string directory, Func<LogLevel> minimumLevel, IClock clock, int retainDays = 14, long maxFileBytes = 5 * 1024 * 1024)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        _clock = clock;
        RetainDays = retainDays;
        _maxFileBytes = maxFileBytes;
        Directory.CreateDirectory(directory);
        DeleteOldFiles();
    }

    public int RetainDays { get; }

    public string LogDirectory => _directory;

    /// <summary>Pfad der Datei, in die gerade geschrieben wird.</summary>
    public string? CurrentFile
    {
        get
        {
            lock (_gate)
            {
                return _currentPath;
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, ShortName(name)));

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimumLevel();

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = _clock.UtcNow.ToLocalTime();
        var line = new StringBuilder(128)
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(LevelText(level)).Append("] ")
            .Append(category).Append(": ")
            .Append(message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal));
        if (exception is not null)
        {
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                EnsureWriter(DateOnly.FromDateTime(now.DateTime));
                _writer!.WriteLine(line.ToString());
                _writer.Flush();
            }
            catch (IOException)
            {
                // Protokoll darf die App nie stören.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void EnsureWriter(DateOnly day)
    {
        if (_writer is not null && day == _currentDay && _writer.BaseStream.Length < _maxFileBytes)
        {
            return;
        }

        if (day != _currentDay)
        {
            DeleteOldFiles();
        }

        _writer?.Dispose();
        _currentDay = day;
        var baseName = "hdrpilot-" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        for (var i = 0; ; i++)
        {
            var path = Path.Combine(_directory, i == 0 ? baseName + ".log" : $"{baseName}-{i}.log");
            if (File.Exists(path) && new FileInfo(path).Length >= _maxFileBytes)
            {
                continue;
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream, new UTF8Encoding(false));
            _currentPath = path;
            return;
        }
    }

    private void DeleteOldFiles()
    {
        try
        {
            var limit = _clock.UtcNow.ToLocalTime().Date.AddDays(-RetainDays);
            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "hdrpilot-*.log"))
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ShortName(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private sealed class FileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
