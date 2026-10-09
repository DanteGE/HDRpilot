using System.IO;

namespace HdrPilot.App.Setup;

/// <summary>Dateioperationen, die Setup und Entfernen verändern (Naht für Tests, die Fehler einspeisen).</summary>
public interface ISetupFileOperations
{
    void CreateDirectory(string path);

    /// <summary>Legt eine Datei neu an (vorhandene wird überschrieben) und öffnet sie zum Schreiben.</summary>
    Stream Create(string path);

    /// <summary>
    /// Ersetzt <paramref name="destination"/> (auch schreibgeschützt) durch <paramref name="source"/>; die bisherige Datei
    /// wird zu <paramref name="backup"/>.
    /// </summary>
    void Replace(string source, string destination, string backup);

    /// <summary>Verschiebt eine Datei; ein vorhandenes Ziel wird überschrieben.</summary>
    void Move(string source, string destination);

    /// <summary>Löscht eine Datei (auch schreibgeschützt); fehlt sie, passiert nichts.</summary>
    void Delete(string path);
}

public sealed class SetupFileOperations : ISetupFileOperations
{
    public static SetupFileOperations Instance { get; } = new();

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Stream Create(string path) => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);

    public void Replace(string source, string destination, string backup)
    {
        // ReplaceFile verweigert ein schreibgeschütztes Ziel (etwa eine von Hand geschützte README): wie beim Löschen.
        ClearReadOnly(new FileInfo(destination));
        File.Replace(source, destination, backup, ignoreMetadataErrors: true);
    }

    public void Move(string source, string destination) => File.Move(source, destination, overwrite: true);

    public void Delete(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return;
        }

        ClearReadOnly(info);
        info.Delete();
    }

    private static void ClearReadOnly(FileInfo info)
    {
        if (info.Exists && info.IsReadOnly)
        {
            info.IsReadOnly = false;
        }
    }
}
