using HdrPilot.Core.Logging;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Logging;

public sealed class RollingFileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HdrPilotTests", Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Writes_lines_immediately_with_level_and_category()
    {
        using var provider = new RollingFileLoggerProvider(_dir, () => LogLevel.Information, _clock);
        var logger = provider.CreateLogger("HdrPilot.Core.Sessions.SessionManager");
        logger.LogInformation("HDR-Schaltung | Auslöser: {Trigger}", "Spielstart erkannt");
        logger.LogDebug("unsichtbar");

        var text = ReadShared(provider.CurrentFile!);
        Assert.Contains("[INF] SessionManager: HDR-Schaltung | Auslöser: Spielstart erkannt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unsichtbar", text, StringComparison.Ordinal);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[INF\]", text);
    }

    [Fact]
    public void Level_can_change_at_runtime()
    {
        var level = LogLevel.Warning;
        using var provider = new RollingFileLoggerProvider(_dir, () => level, _clock);
        var logger = provider.CreateLogger("X");
        logger.LogInformation("eins");
        level = LogLevel.Debug;
        logger.LogDebug("zwei");
        logger.LogWarning("drei");
        var text = ReadShared(provider.CurrentFile!);
        Assert.DoesNotContain("eins", text, StringComparison.Ordinal);
        Assert.Contains("zwei", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rolls_by_day_and_size_and_deletes_old_files()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "hdrpilot-20260901.log");
        File.WriteAllText(old, "alt");
        File.SetLastWriteTime(old, new DateTime(2026, 9, 1));

        using var provider = new RollingFileLoggerProvider(_dir, () => LogLevel.Information, _clock, retainDays: 14, maxFileBytes: 200);
        Assert.False(File.Exists(old));
        var logger = provider.CreateLogger("X");
        for (var i = 0; i < 10; i++)
        {
            logger.LogInformation("Zeile {I} mit etwas Text, damit die Größe überschritten wird", i);
        }

        Assert.True(Directory.GetFiles(_dir, "hdrpilot-20261007*.log").Length >= 2);
        _clock.Advance(TimeSpan.FromDays(1));
        logger.LogInformation("neuer Tag");
        Assert.EndsWith("hdrpilot-20261008.log", provider.CurrentFile, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleted_log_folder_never_breaks_logging()
    {
        using var provider = new RollingFileLoggerProvider(_dir, () => LogLevel.Information, _clock);
        var logger = provider.CreateLogger("X");
        logger.LogInformation("vorher");
        provider.Dispose();
        using var provider2 = new RollingFileLoggerProvider(_dir, () => LogLevel.Information, _clock);
        var logger2 = provider2.CreateLogger("X");
        logger2.LogInformation("eins");
        Directory.Delete(_dir, recursive: true); // Nutzer löscht den Ordner im Betrieb
        _clock.Advance(TimeSpan.FromDays(1));

        Assert.Null(Record.Exception(() => logger2.LogInformation("zwei")));
        Assert.Null(Record.Exception(() => logger2.LogInformation("drei")));
        Assert.Contains("drei", ReadShared(provider2.CurrentFile!), StringComparison.Ordinal);
    }
    [Fact]
    public void Composite_factory_writes_to_all_providers()
    {
        var writer = new StringWriter();
        using var file = new RollingFileLoggerProvider(_dir, () => LogLevel.Information, _clock);
        using var factory = new CompositeLoggerFactory(file, new TextWriterLoggerProvider(writer, () => LogLevel.Information));
        factory.CreateLogger("A.B").LogWarning("beides");
        Assert.Contains("beides", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("beides", ReadShared(file.CurrentFile!), StringComparison.Ordinal);
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
