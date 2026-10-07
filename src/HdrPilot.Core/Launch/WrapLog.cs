using System.Text;

namespace HdrPilot.Core.Launch;

/// <summary>
/// Eigenes kleines Protokoll der Steam-Startoption (<c>wrap.log</c> neben den normalen Protokollen): eine Zeile je Aufruf.
/// Ab <see cref="MaxBytes"/> wird die Datei zu <c>wrap.old.log</c> (die vorige entfällt), zusammen also höchstens rund
/// das Doppelte. Fehler beim Schreiben werden verschluckt: das Spiel startet immer.
/// </summary>
public sealed class WrapLog(string directory, long maxBytes = WrapLog.DefaultMaxBytes)
{
    public const string FileName = "wrap.log";

    public const string OldFileName = "wrap.old.log";

    public const long DefaultMaxBytes = 256 * 1024;

    public string FilePath => Path.Combine(directory, FileName);

    public long MaxBytes => maxBytes;

    /// <summary>Hängt eine Zeile an (Zeilenumbrüche im Text werden zu Leerzeichen). False, wenn das Schreiben scheiterte.</summary>
    public bool TryAppend(string line)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = FilePath;
            var info = new FileInfo(path);
            if (info.Exists && info.Length >= maxBytes)
            {
                File.Move(path, Path.Combine(directory, OldFileName), overwrite: true);
            }

            var text = line.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal) + Environment.NewLine;
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new UTF8Encoding(false).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
