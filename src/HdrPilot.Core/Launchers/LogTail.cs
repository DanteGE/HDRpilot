using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HdrPilot.Core.Launchers;

/// <summary>Vorauswahl einer Zeile auf Byte-Ebene (ohne Zeilenende), bevor sie dekodiert wird.</summary>
public delegate bool LogLineFilter(ReadOnlySpan<byte> line);

/// <summary>
/// Liest nur die neu angehängten Zeilen einer Protokolldatei, die ein anderes Programm schreibt.
/// <para>
/// Die Datei bleibt nur lesend geöffnet, mit FileShare.ReadWrite | Delete: Der Launcher kann weiter schreiben, sie
/// umbenennen oder löschen. Dadurch kostet die sekündliche Prüfung nur eine Längenabfrage über das Handle (gemessen
/// rund 1 µs statt 45 bis 70 µs für eine Abfrage über den Pfad, die jeder Dateisystemfilter mitprüft).
/// </para>
/// <para>
/// Benennt der Launcher die Datei um oder legt sie neu an (Steam: console_log.txt → console_log.previous.txt, Epic:
/// Sicherung beim Start), zeigt das Handle weiter auf die alte Datei. Deshalb prüft <see cref="ReadNewLines"/> mit
/// <c>verifyPath</c> über den Pfad, ob dort noch dieselbe Datei liegt (Datei-ID); der Watcher tut das bei Dateiereignissen
/// und selten zur Sicherheit. Bei einem Wechsel wird der Rest der alten Datei noch gelesen und die neue von vorn. Eine
/// gekürzte Datei wird ebenfalls von vorn gelesen, eine unvollständige letzte Zeile wartet auf ihr Zeilenende.
/// Nicht threadsicher.
/// </para>
/// </summary>
public sealed class LogTail : IDisposable
{
    /// <summary>Längere Zeilen werden verworfen (Schutz vor unbegrenztem Puffer).</summary>
    internal const int MaxLineBytes = 64 * 1024;

    /// <summary>Wurde mehr angehängt, zählt nur das Ende: so alte Zeilen kündigen keinen bevorstehenden Start mehr an.</summary>
    internal const long MaxCatchUpBytes = 4L * 1024 * 1024;

    private const int ChunkSize = 64 * 1024;

    private SafeFileHandle? _handle;
    private FileIdentity? _identity;
    private byte[] _partial = [];
    private int _partialLength;
    private bool _discardUntilNewline;
    private bool _started;

    public LogTail(string path) => FullPath = path;

    public string FullPath { get; }

    /// <summary>Lesestelle in der aktuell gelesenen Datei (Byte).</summary>
    public long Position { get; private set; }

    /// <summary>True, solange die Datei geöffnet ist (sonst fehlt sie oder war beim letzten Versuch gesperrt).</summary>
    public bool IsOpen => _handle is not null;

    /// <summary>Die letzte Prüfung über den Pfad scheiterte an einer Sperre: beim nächsten Mal wiederholen.</summary>
    public bool VerifyPending { get; private set; }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>Öffnet die Datei und merkt sich ihr Ende: Was schon darin steht, wird nie gemeldet.</summary>
    public void StartAtEnd()
    {
        _started = true;
        CloseHandle();
        ResetLine();
        _identity = null;
        VerifyPending = false;
        Position = 0;
        if (TryOpen(out var handle, out var missing))
        {
            _handle = handle;
            _identity = FileIdentity.Of(handle);
            Position = RandomAccess.GetLength(handle);
        }
        else if (!missing)
        {
            // Gerade gesperrt: trotzdem hinter dem bisherigen Inhalt beginnen, nie den Verlauf nachspielen.
            var info = new FileInfo(FullPath);
            Position = info.Exists ? info.Length : 0;
            VerifyPending = true;
        }
    }

    /// <summary>
    /// Billige Vorprüfung über das offene Handle (keine Dateiabfrage über den Pfad): Ist die Datei länger oder kürzer als
    /// die Lesestelle? Ohne offenes Handle false; dann hilft nur <see cref="ReadNewLines"/> mit <c>verifyPath</c>.
    /// </summary>
    public bool MayHaveNewData()
    {
        if (_handle is null)
        {
            return false;
        }

        try
        {
            return RandomAccess.GetLength(_handle) != Position;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>Liest alle seit dem letzten Aufruf vollständig angehängten Zeilen (ohne Zeilenende, UTF-8).</summary>
    /// <param name="filter">Vorauswahl auf Byte-Ebene; nur passende Zeilen werden dekodiert. Null: alle Zeilen.</param>
    /// <param name="verifyPath">
    /// Über den Pfad prüfen, ob dort noch dieselbe Datei liegt (nach Umbenennen, Löschen, Neuanlage). Kostet ein Öffnen;
    /// ohne offenes Handle geschieht es immer.
    /// </param>
    public IReadOnlyList<string> ReadNewLines(LogLineFilter? filter = null, bool verifyPath = false)
    {
        var lines = new List<string>();
        if (!_started)
        {
            StartAtEnd();
            return lines;
        }

        if (verifyPath || _handle is null)
        {
            FollowPath(lines, filter);
        }

        if (_handle is not null)
        {
            ReadAppended(_handle, lines, filter);
        }

        return lines;
    }

    public void Dispose() => CloseHandle();

    /// <summary>Wechselt auf die Datei, die jetzt unter dem Pfad liegt; der Rest der alten Datei wird vorher gelesen.</summary>
    private void FollowPath(List<string> lines, LogLineFilter? filter)
    {
        VerifyPending = false;
        if (!TryOpen(out var current, out var missing))
        {
            if (missing)
            {
                // Umbenannt oder gelöscht: Rest der alten Datei lesen, loslassen; die nächste Datei ist neu.
                DrainAndClose(lines, filter);
                _identity = null;
                Position = 0;
            }
            else
            {
                VerifyPending = true; // gesperrt: beim nächsten Mal erneut
            }

            return;
        }

        var adopted = false;
        try
        {
            var identity = FileIdentity.Of(current);
            if (_handle is null)
            {
                // Neu angelegt (Lesestelle 0) oder beim Start gesperrt (Lesestelle am damaligen Ende).
                _handle = current;
                _identity = identity;
                adopted = true;
                return;
            }

            var replaced = identity is { } id && _identity is { } known
                ? id != known
                : RandomAccess.GetLength(current) < Position; // ohne Datei-ID: kürzer als gelesen heißt neu
            if (replaced)
            {
                DrainAndClose(lines, filter);
                _handle = current;
                _identity = identity;
                Position = 0;
                adopted = true;
            }
        }
        finally
        {
            if (!adopted)
            {
                current.Dispose(); // dieselbe Datei (oder Fehler): kein zweites Handle behalten
            }
        }
    }

    private void DrainAndClose(List<string> lines, LogLineFilter? filter)
    {
        if (_handle is not null)
        {
            try
            {
                ReadAppended(_handle, lines, filter);
            }
            catch (IOException)
            {
                // Die alte Datei ist nicht mehr lesbar: ihr Rest geht verloren.
            }
        }

        CloseHandle();
        ResetLine();
    }

    private void ReadAppended(SafeFileHandle handle, List<string> lines, LogLineFilter? filter)
    {
        var length = RandomAccess.GetLength(handle);
        if (length < Position)
        {
            // Gekürzt: alles darin ist neu.
            Position = 0;
            ResetLine();
        }

        if (length - Position > MaxCatchUpBytes)
        {
            Position = length - MaxCatchUpBytes;
            ResetLine();
            _discardUntilNewline = true; // mitten in einer Zeile begonnen
        }

        if (length <= Position)
        {
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            while (Position < length)
            {
                var read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - Position)), Position);
                if (read <= 0)
                {
                    break;
                }

                Consume(buffer.AsSpan(0, read), lines, filter);
                Position += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Consume(ReadOnlySpan<byte> data, List<string> lines, LogLineFilter? filter)
    {
        while (!data.IsEmpty)
        {
            var newline = data.IndexOf((byte)'\n');
            var piece = newline < 0 ? data : data[..newline];
            data = newline < 0 ? default : data[(newline + 1)..];
            if (!_discardUntilNewline)
            {
                if (_partialLength + piece.Length > MaxLineBytes)
                {
                    _discardUntilNewline = true;
                    _partialLength = 0;
                }
                else if (newline < 0)
                {
                    AppendPartial(piece);
                }
                else if (_partialLength == 0)
                {
                    Emit(piece, lines, filter);
                }
                else
                {
                    AppendPartial(piece);
                    Emit(_partial.AsSpan(0, _partialLength), lines, filter);
                    _partialLength = 0;
                }
            }

            if (newline >= 0)
            {
                _discardUntilNewline = false;
            }
        }
    }

    private static void Emit(ReadOnlySpan<byte> line, List<string> lines, LogLineFilter? filter)
    {
        if (line.StartsWith(Utf8Bom))
        {
            line = line[Utf8Bom.Length..]; // Epic schreibt die Datei mit BOM
        }

        if (!line.IsEmpty && line[^1] == (byte)'\r')
        {
            line = line[..^1];
        }

        if (filter is null || filter(line))
        {
            lines.Add(Encoding.UTF8.GetString(line));
        }
    }

    private void AppendPartial(ReadOnlySpan<byte> piece)
    {
        if (_partial.Length < _partialLength + piece.Length)
        {
            Array.Resize(ref _partial, Math.Max(_partialLength + piece.Length, Math.Min(MaxLineBytes, Math.Max(256, _partial.Length * 2))));
        }

        piece.CopyTo(_partial.AsSpan(_partialLength));
        _partialLength += piece.Length;
    }

    private void ResetLine()
    {
        _partialLength = 0;
        _discardUntilNewline = false;
        if (_partial.Length > 4096)
        {
            _partial = []; // nach einer langen Zeile keinen großen Puffer behalten
        }
    }

    private void CloseHandle()
    {
        _handle?.Dispose();
        _handle = null;
    }

    private bool TryOpen([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SafeFileHandle? handle, out bool missing)
    {
        missing = false;
        try
        {
            handle = File.OpenHandle(FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            missing = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kurz gesperrt (etwa beim Umbenennen): beim nächsten Mal erneut.
        }

        handle = null;
        return false;
    }
}

/// <summary>Datei-ID (Volume-Seriennummer und Dateiindex) einer geöffneten Datei; unterscheidet eine neu angelegte Datei gleichen Namens.</summary>
internal readonly partial record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex)
{
    /// <summary>winnt.h und WinBase.h: nur Attribute lesen; FILE_FLAG_BACKUP_SEMANTICS öffnet auch Ordner.</summary>
    private const uint FILE_READ_ATTRIBUTES = 0x80;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    public static FileIdentity? Of(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var info)
            ? new FileIdentity(info.dwVolumeSerialNumber, ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow)
            : null;

    /// <summary>
    /// Kennung eines vorhandenen Ordners (oder einer Datei), egal wie der Pfad geschrieben ist: kurze 8.3-Namen, Präfix
    /// <c>\\?\</c>, Junctions und symbolische Verknüpfungen (Windows folgt ihnen), mit subst verbundene Laufwerke. Öffnet nur
    /// zum Lesen der Attribute und sperrt nichts; null, wenn es den Pfad nicht gibt oder er sich nicht öffnen lässt.
    /// </summary>
    public static FileIdentity? OfPath(string path)
    {
        using var handle = CreateFileW(path, FILE_READ_ATTRIBUTES, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        return handle.IsInvalid ? null : Of(handle);
    }

    // fileapi.h: BY_HANDLE_FILE_INFORMATION. Für die eigene, gerade geöffnete Protokolldatei und für Ordner, die nur zum Lesen
    // der Attribute geöffnet sind (PathUtil.SameDirectory).
#pragma warning disable IDE1006
    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    /// <summary>minwinbase.h: zwei DWORDs (Ausrichtung 4, daher kein ulong).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }
#pragma warning restore IDE1006

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    // fileapi.h: CreateFileW (dwShareMode und dwCreationDisposition haben dieselben Werte wie FileShare und FileMode).
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
}
