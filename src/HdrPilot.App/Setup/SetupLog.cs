using System.Globalization;
using System.IO;
using System.Text;

namespace HdrPilot.App.Setup;

/// <summary>
/// Protokoll von Setup und Entfernen: eine Textdatei, Zeilen mit Zeitstempel (normal %TEMP%\HDRpilot-Setup.log,
/// im Prüflauf im Testordner). Schreibfehler werden verschluckt: das Protokoll darf nie eine Installation verhindern.
/// </summary>
public sealed class SetupLog
{
    private readonly object _gate = new();

    public SetupLog(string path)
    {
        FilePath = path;
    }

    public string FilePath { get; }

    /// <summary>Normaler Ort, oder im Prüflauf <c>&lt;test-root&gt;\setup.log</c>.</summary>
    public static SetupLog For(SetupLocations locations, string? testRoot) =>
        new(testRoot is null ? Path.Combine(Path.GetTempPath(), "HDRpilot-Setup.log") : Path.Combine(Path.GetFullPath(testRoot), "setup.log"));

    public void Info(string message) => Write("INF", message);

    public void Warn(string message) => Write("WRN", message);

    public void Error(string message, Exception? ex = null) => Write("ERR", ex is null ? message : message + " | " + ex.GetType().Name + ": " + ex.Message);

    private void Write(string level, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Protokoll ist Kür.
            }
        }
    }
}
