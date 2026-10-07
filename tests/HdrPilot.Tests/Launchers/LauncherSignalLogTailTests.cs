using System.Text;
using HdrPilot.Core.Launchers;

namespace HdrPilot.Tests.Launchers;

/// <summary>
/// Mitlesen einer fremden Protokolldatei mit Temp-Dateien: nur Angehängtes, unvollständige Zeilen, Kürzen, Umbenennen
/// und Neuanlegen (wie Steam und Epic beim Start), und das offene Handle behindert den Launcher nicht.
/// </summary>
public sealed class LauncherSignalLogTailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HdrPilotTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly List<LogTail> _tails = [];

    public LauncherSignalLogTailTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "console_log.txt");
    }

    public void Dispose()
    {
        _tails.ForEach(t => t.Dispose());
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    /// <summary>Hängt an wie ein Launcher: eigenes Handle, das Lesen, Umbenennen und Löschen erlaubt.</summary>
    private void Append(string text) => Append(Encoding.UTF8.GetBytes(text));

    private void Append(byte[] bytes)
    {
        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(bytes);
    }

    private LogTail NewTail()
    {
        var tail = new LogTail(_path);
        _tails.Add(tail);
        return tail;
    }

    private LogTail StartedTail()
    {
        var tail = NewTail();
        tail.StartAtEnd();
        return tail;
    }

    [Fact]
    public void Starts_at_the_end_and_never_replays_existing_lines()
    {
        Append("alt 1\r\nalt 2\r\n");
        var tail = StartedTail();

        Assert.Empty(tail.ReadNewLines());
        Append("neu\r\n");
        Assert.Equal(["neu"], tail.ReadNewLines());
    }

    [Fact]
    public void Reads_appended_lines_in_order_with_crlf_and_lf()
    {
        Append("x\n");
        var tail = StartedTail();
        Append("eins\r\nzwei\nDrei ünd Vier\r\n\r\n");

        Assert.Equal(["eins", "zwei", "Drei ünd Vier", ""], tail.ReadNewLines());
        Assert.Empty(tail.ReadNewLines());
    }

    [Fact]
    public void Partial_last_line_waits_for_its_line_end()
    {
        Append("x\r\n");
        var tail = StartedTail();
        Append("[2026-09-23 12:44:30] GameAction [AppID 99");

        Assert.Empty(tail.ReadNewLines());
        Append("90130, ActionID 5] : LaunchApp");
        Assert.Empty(tail.ReadNewLines());
        Append(" changed task to X\r\nnächste");

        Assert.Equal(["[2026-09-23 12:44:30] GameAction [AppID 9990130, ActionID 5] : LaunchApp changed task to X"], tail.ReadNewLines());
        Append("\r\n");
        Assert.Equal(["nächste"], tail.ReadNewLines());
    }

    [Fact]
    public void Multibyte_character_split_between_writes_is_decoded_once_complete()
    {
        Append("x\r\n");
        var tail = StartedTail();
        var bytes = Encoding.UTF8.GetBytes("Größe™\n");
        Append(bytes[..3]); // mitten im „ö“ getrennt
        Assert.Empty(tail.ReadNewLines());
        Append(bytes[3..]);

        Assert.Equal(["Größe™"], tail.ReadNewLines());
    }

    [Fact]
    public void Truncated_file_is_read_from_the_start()
    {
        Append("lange alte Zeile, die die Datei groß macht\r\n");
        var tail = StartedTail();
        using (var truncate = new FileStream(_path, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
        }

        Append("neu\r\n");

        Assert.True(tail.MayHaveNewData());
        Assert.Equal(["neu"], tail.ReadNewLines());
    }

    [Fact]
    public void Renamed_and_recreated_file_is_read_from_the_start_even_when_it_is_larger()
    {
        // Steam: console_log.txt wird zu console_log.previous.txt, eine neue Datei entsteht und wächst schnell über die
        // alte Lesestelle hinaus. Nur die Datei-ID verrät den Wechsel.
        Append("a\r\n");
        var tail = StartedTail();
        File.Move(_path, Path.Combine(_dir, "console_log.previous.txt"));
        Append("erste Zeile der neuen Datei\r\nzweite\r\n");

        Assert.Equal(["erste Zeile der neuen Datei", "zweite"], tail.ReadNewLines(verifyPath: true));
    }

    [Fact]
    public void Lines_written_to_the_old_file_before_the_switch_are_still_read()
    {
        Append("a\r\n");
        var tail = StartedTail();
        Append("letzte Zeile vor dem Umbenennen\r\n");
        File.Move(_path, Path.Combine(_dir, "EpicGamesLauncher-backup-2026.09.23-11.51.44.log"));
        Append("neue Datei\r\n");

        Assert.Equal(["letzte Zeile vor dem Umbenennen", "neue Datei"], tail.ReadNewLines(verifyPath: true));
    }

    [Fact]
    public void Without_path_check_the_tail_keeps_reading_the_renamed_file()
    {
        // Der Wechsel kostet ein Öffnen über den Pfad; der sekündliche Takt prüft nur das Handle.
        Append("a\r\n");
        var tail = StartedTail();
        var previous = Path.Combine(_dir, "console_log.previous.txt");
        File.Move(_path, previous);
        File.AppendAllText(previous, "noch in der alten Datei\r\n");
        Append("neu\r\n");

        Assert.Equal(["noch in der alten Datei"], tail.ReadNewLines());
        Assert.Equal(["neu"], tail.ReadNewLines(verifyPath: true));
    }

    [Fact]
    public void Missing_file_at_start_is_read_from_the_beginning_once_created()
    {
        var tail = StartedTail();
        Assert.False(tail.IsOpen);
        Assert.False(tail.MayHaveNewData());
        Assert.Empty(tail.ReadNewLines());

        Append("﻿Log file open, 09/23/26 11:56:42\r\nzweite\r\n");

        Assert.Equal(["Log file open, 09/23/26 11:56:42", "zweite"], tail.ReadNewLines()); // BOM entfernt
        Assert.True(tail.IsOpen);
    }

    [Fact]
    public void Deleted_and_recreated_file_is_read_from_the_start()
    {
        Append("alt alt alt alt\r\n");
        var tail = StartedTail();
        File.Delete(_path); // gelingt trotz offenem Handle

        Assert.Empty(tail.ReadNewLines(verifyPath: true));
        Assert.False(tail.IsOpen);
        Append("neue Zeile, länger als die ganze alte Datei\r\n");
        Assert.Equal(["neue Zeile, länger als die ganze alte Datei"], tail.ReadNewLines());
    }

    [Fact]
    public void Quick_check_reports_only_real_changes()
    {
        Append("x\r\n");
        var tail = StartedTail();
        Assert.True(tail.IsOpen);
        Assert.False(tail.MayHaveNewData());

        Append("y");
        Assert.True(tail.MayHaveNewData());
        Assert.Empty(tail.ReadNewLines());
        Assert.False(tail.MayHaveNewData()); // unvollständige Zeile ist gelesen, nur noch nicht gemeldet
    }

    [Fact]
    public void Filter_decides_on_bytes_which_lines_are_returned()
    {
        Append("x\r\n");
        var tail = StartedTail();
        Append("GameAction eins\r\nanderes\r\nGameAction zwei\r\n");

        var lines = tail.ReadNewLines(line => line.IndexOf("GameAction"u8) >= 0);

        Assert.Equal(["GameAction eins", "GameAction zwei"], lines);
    }

    [Fact]
    public void Overlong_line_is_dropped_and_the_next_line_still_read()
    {
        Append("x\r\n");
        var tail = StartedTail();
        Append(new string('x', LogTail.MaxLineBytes + 10));
        Assert.Empty(tail.ReadNewLines());
        Append(new string('y', 100) + "\r\nkurz\r\n");

        Assert.Equal(["kurz"], tail.ReadNewLines());
    }

    [Fact]
    public void Large_backlog_keeps_only_the_newest_part_and_starts_at_a_line_boundary()
    {
        Append("x\r\n");
        var tail = StartedTail();
        var sb = new StringBuilder();
        var count = 0;
        while (sb.Length < LogTail.MaxCatchUpBytes + (512 * 1024))
        {
            sb.Append("Zeile ").Append(count++.ToString("D8", System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
        }

        Append(sb.ToString());
        var lines = tail.ReadNewLines();

        Assert.InRange(lines.Count, 1, count - 1);
        Assert.All(lines, l => Assert.Matches(@"^Zeile \d{8}$", l));
        Assert.Equal("Zeile " + (count - 1).ToString("D8", System.Globalization.CultureInfo.InvariantCulture), lines[^1]);
    }

    [Fact]
    public void Open_handle_lets_the_launcher_write_rename_and_delete()
    {
        Append("x\r\n");
        var tail = StartedTail();
        Assert.True(tail.IsOpen);

        // Wie ein Launcher, der seine Datei neu öffnet (nur Lesen geteilt), umbenennt, ersetzt und alte Dateien löscht.
        using (var writer = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            writer.Write("y\r\n"u8);
        }

        var previous = _path + ".previous";
        File.Move(_path, previous);
        File.WriteAllText(_path, "neu\r\n");
        File.Delete(previous);
        File.Move(_path, previous, overwrite: true);

        Assert.Equal(["y"], tail.ReadNewLines());
    }

    [Fact]
    public void Dispose_releases_the_file()
    {
        Append("x\r\n");
        var tail = StartedTail();
        tail.Dispose();

        using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // exklusiv öffnen geht nur, wenn kein Handle mehr offen ist
        }
    }

    [Fact]
    public void File_locked_at_start_still_starts_at_its_end()
    {
        Append("alte Zeile\r\n");
        var tail = NewTail();
        using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            tail.StartAtEnd();
        }

        Assert.False(tail.IsOpen);
        Assert.True(tail.VerifyPending);
        Append("neu\r\n");
        Assert.Equal(["neu"], tail.ReadNewLines());
        Assert.False(tail.VerifyPending);
    }
}
