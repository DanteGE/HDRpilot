using System.IO;
using System.Windows.Media.Imaging;
using HdrPilot.Core.Games;

namespace HdrPilot.App.Services;

/// <summary>
/// Eigene Symbole der Spiele (Rechtsklick „Symbol ändern…“, E86): Aus einem Bild (.png, .jpg, .webp, .bmp, .gif), einer
/// .ico oder einer EXE wird eine PNG mit <see cref="CustomIcons.StoredSize"/> px (Seitenverhältnis bleibt, Rest
/// durchsichtig) in <c>&lt;Datenordner&gt;\icons</c>; der Name kommt aus <see cref="CustomIcons.FileNameFor"/>.
/// Die gewählte Datei selbst wird nie verändert und nicht kopiert.
/// </summary>
public sealed class CustomIconStore(string directory)
{
    public string Directory => directory;

    /// <summary>Vollständiger Pfad zu einem gespeicherten Namen; null bei leerem oder ungültigem Namen.</summary>
    public string? PathFor(string? fileName) => CustomIcons.IsValidFileName(fileName) ? Path.Combine(directory, fileName!) : null;

    /// <summary>
    /// Erzeugt das Symbol und ersetzt eine frühere PNG des Spiels atomar. Liefert den Dateinamen für
    /// <c>GameConfig.CustomIcon</c>. Wirft <see cref="InvalidDataException"/>, wenn die Datei kein Symbol hergibt,
    /// und IO-Ausnahmen beim Schreiben.
    /// </summary>
    public string Save(string gameKey, string sourcePath)
    {
        var image = Render(sourcePath, CustomIcons.StoredSize)
            ?? throw new InvalidDataException("Die Datei enthält kein lesbares Bild oder Symbol: " + sourcePath);
        var name = CustomIcons.FileNameFor(gameKey);
        System.IO.Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, name);
        var temp = Path.Combine(directory, "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }

            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return name;
    }

    /// <summary>Löscht die PNG eines Spiels, falls vorhanden (ungültige Namen werden ignoriert).</summary>
    public void Delete(string? fileName)
    {
        if (PathFor(fileName) is { } path && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Bild der Datei als Quadrat mit <paramref name="size"/> px oder null, wenn sie keins enthält. EXE und .ico über
    /// Windows (beste Größe, von Windows skaliert), Bilder über die Bilddecoder (fehlt etwa der WebP-Decoder, wirft das).
    /// </summary>
    public static BitmapSource? Render(string sourcePath, int size)
    {
        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            return GameIconService.ExtractIcon(sourcePath, size) as BitmapSource;
        }

        return IconFiles.IsImage(sourcePath) ? IconReader.DecodeFitted(sourcePath, size).Image : null;
    }
}
