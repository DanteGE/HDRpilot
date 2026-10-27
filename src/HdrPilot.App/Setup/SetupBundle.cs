using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace HdrPilot.App.Setup;

/// <summary>
/// Setup-Datei (E83): die normale HDRpilot.exe, dahinter ein Zip mit den Beigaben und ein Abschluss.
/// <code>
/// [EXE, ExeLength Bytes][Zip, PayloadLength Bytes][Abschluss, 64 Bytes]
/// Abschluss: 16 Bytes ASCII "HDRPILOT-SETUP01" | Int64 ExeLength | Int64 PayloadLength | 32 Bytes SHA-256 der EXE (little endian)
/// </code>
/// Der .NET-Einzeldatei-Start liest nur seinen eigenen Teil; angehängte Daten stören ihn nicht. Dasselbe Format schreibt
/// <c>tools\publish.ps1</c>.
/// </summary>
public sealed class SetupBundle
{
    public const string Magic = "HDRPILOT-SETUP01";
    public const int FooterLength = 64;

    /// <summary>
    /// Einträge, die das Zip enthalten darf (Pfade mit „/“); alles andere wird nie ausgepackt. <c>LICENSE</c> (Lizenztext
    /// von HDRpilot, E92) gibt es ab 1.5.0, die deutsche <c>README.de.md</c> neben der englischen README (E95) nach 1.5.0;
    /// ältere Setup-Dateien ohne diese Einträge bleiben gültig.
    /// </summary>
    public static readonly IReadOnlyList<string> PayloadEntryNames = ["README.md", "README.de.md", "LICENSE", "THIRD-PARTY-NOTICES.txt", "cli/hdrpilot.exe"];

    private SetupBundle(string path, long exeLength, long payloadLength, byte[] exeSha256)
    {
        FilePath = path;
        ExeLength = exeLength;
        PayloadLength = payloadLength;
        ExeSha256 = exeSha256;
    }

    public string FilePath { get; }

    public long ExeLength { get; }

    public long PayloadLength { get; }

    public IReadOnlyList<byte> ExeSha256 { get; }

    /// <summary>
    /// Liest den Abschluss. Null, wenn die Datei keiner ist (normale EXE, fremde Datei, abgeschnitten, nicht lesbar).
    /// Liest nur die letzten 64 Bytes: billig genug für jeden Start der App.
    /// </summary>
    public static SetupBundle? TryOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.None);
            if (stream.Length < FooterLength + 2)
            {
                return null;
            }

            var footer = new byte[FooterLength];
            stream.Seek(-FooterLength, SeekOrigin.End);
            stream.ReadExactly(footer);
            if (!footer.AsSpan(0, 16).SequenceEqual(Encoding.ASCII.GetBytes(Magic)))
            {
                return null;
            }

            var exeLength = BitConverter.ToInt64(footer, 16);
            var payloadLength = BitConverter.ToInt64(footer, 24);
            if (exeLength <= 0 || payloadLength < 0 || exeLength + payloadLength + FooterLength != stream.Length)
            {
                return null; // abgeschnitten oder verändert
            }

            return new SetupBundle(path, exeLength, payloadLength, footer[32..64]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True, wenn die EXE im Paket unverändert ist (SHA-256 wie im Abschluss).</summary>
    public bool VerifyExe()
    {
        using var stream = OpenRead();
        using var exe = new BoundedStream(stream, 0, ExeLength);
        return SHA256.HashData(exe).AsSpan().SequenceEqual(ExeSha256.ToArray());
    }

    /// <summary>Schreibt die EXE (ohne Paket und Abschluss) in <paramref name="destination"/>.</summary>
    public void CopyExeTo(Stream destination)
    {
        using var stream = OpenRead();
        using var exe = new BoundedStream(stream, 0, ExeLength);
        exe.CopyTo(destination, 1024 * 1024);
    }

    /// <summary>Namen der Einträge im Zip (nur erlaubte, siehe <see cref="PayloadEntryNames"/>).</summary>
    public IReadOnlyList<string> PayloadEntries()
    {
        if (PayloadLength == 0)
        {
            return [];
        }

        using var stream = OpenRead();
        using var zip = new ZipArchive(new BoundedStream(stream, ExeLength, PayloadLength), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => PayloadEntryNames.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Schreibt einen erlaubten Eintrag nach <paramref name="destination"/>. False, wenn das Paket ihn nicht enthält.
    /// </summary>
    public bool CopyPayloadEntryTo(string entryName, Stream destination)
    {
        if (!PayloadEntryNames.Contains(entryName, StringComparer.OrdinalIgnoreCase) || PayloadLength == 0)
        {
            return false;
        }

        using var stream = OpenRead();
        using var zip = new ZipArchive(new BoundedStream(stream, ExeLength, PayloadLength), ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), entryName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return false;
        }

        using var source = entry.Open();
        source.CopyTo(destination, 1024 * 1024);
        return true;
    }

    /// <summary>Baut eine Setup-Datei (Tests; die Auslieferung nutzt dasselbe Format in publish.ps1).</summary>
    /// <param name="payload">Eintragsname (aus <see cref="PayloadEntryNames"/>) → Quelldatei.</param>
    public static void Write(string exePath, IReadOnlyDictionary<string, string> payload, string outputPath)
    {
        byte[] exeBytesHash;
        using (var exeForHash = File.OpenRead(exePath))
        {
            exeBytesHash = SHA256.HashData(exeForHash);
        }

        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var exe = File.OpenRead(exePath))
        {
            exe.CopyTo(output);
        }

        var exeLength = output.Length;
        using (var zipBuffer = new MemoryStream())
        {
            using (var zip = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, source) in payload)
                {
                    if (!PayloadEntryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException($"Eintrag nicht erlaubt: {name}", nameof(payload));
                    }

                    zip.CreateEntryFromFile(source, name, CompressionLevel.Optimal);
                }
            }

            zipBuffer.Position = 0;
            zipBuffer.CopyTo(output);
        }

        var payloadLength = output.Length - exeLength;
        var footer = new byte[FooterLength];
        Encoding.ASCII.GetBytes(Magic).CopyTo(footer, 0);
        BitConverter.GetBytes(exeLength).CopyTo(footer, 16);
        BitConverter.GetBytes(payloadLength).CopyTo(footer, 24);
        exeBytesHash.CopyTo(footer, 32);
        output.Write(footer);
    }

    private FileStream OpenRead() =>
        new(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);

    /// <summary>Lesender Ausschnitt eines Streams (für die EXE und das Zip im Paket).</summary>
    private sealed class BoundedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public BoundedStream(Stream inner, long start, long length)
        {
            _inner = inner;
            _start = start;
            _length = length;
            _inner.Seek(start, SeekOrigin.Begin);
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = _length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            if (buffer.Length > remaining)
            {
                buffer = buffer[..(int)remaining];
            }

            var read = _inner.Read(buffer);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0 || target > _length)
            {
                throw new IOException("Position außerhalb des Ausschnitts.");
            }

            _inner.Seek(_start + target, SeekOrigin.Begin);
            _position = target;
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
