using HdrPilot.Core.Launch;

namespace HdrPilot.Tests.Launch;

public sealed class WrapLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HdrPilotTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Appends_one_line_per_call_and_creates_the_folder()
    {
        var log = new WrapLog(Path.Combine(_dir, "logs"));

        Assert.True(log.TryAppend("eins"));
        Assert.True(log.TryAppend("zwei\r\nmit Umbruch"));

        Assert.Equal(Path.Combine(_dir, "logs", "wrap.log"), log.FilePath);
        Assert.Equal(["eins", "zwei  mit Umbruch"], File.ReadAllLines(log.FilePath));
    }

    [Fact]
    public void Rotates_to_old_file_when_too_big_so_it_stays_small()
    {
        var log = new WrapLog(_dir, maxBytes: 100);
        var line = new string('x', 60);

        log.TryAppend(line + "1");
        log.TryAppend(line + "2"); // Datei hat jetzt über 100 Bytes
        log.TryAppend(line + "3"); // wird zuerst rotiert

        Assert.Equal([line + "3"], File.ReadAllLines(log.FilePath));
        Assert.Equal([line + "1", line + "2"], File.ReadAllLines(Path.Combine(_dir, WrapLog.OldFileName)));

        log.TryAppend(line + "4");
        log.TryAppend(line + "5");
        Assert.Equal([line + "5"], File.ReadAllLines(log.FilePath));
        Assert.Equal([line + "3", line + "4"], File.ReadAllLines(Path.Combine(_dir, WrapLog.OldFileName))); // vorige entfällt
    }

    [Fact]
    public void Default_limit_is_256_kilobytes()
    {
        Assert.Equal(256 * 1024, new WrapLog(_dir).MaxBytes);
    }

    [Fact]
    public void Unwritable_target_returns_false_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "ich-bin-eine-datei");
        File.WriteAllText(file, "x");

        Assert.False(new WrapLog(file).TryAppend("zeile"));
    }
}
