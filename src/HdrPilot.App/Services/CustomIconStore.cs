using System.Buffers.Binary;
using System.IO;
using System.Windows.Media.Imaging;
using HdrPilot.Core.Games;

namespace HdrPilot.App.Services;

/// <summary>
/// Eigene Symbole der Spiele (Rechtsklick „Symbol ändern…“, E86): Aus einem Bild (.png, .jpg, .webp, .bmp, .gif), einer
/// .ico oder einer EXE wird eine PNG mit <see cref="CustomIcons.StoredSize"/> px (Seitenverhältnis bleibt, Rest
/// durchsichtig) in <c>&lt;Datenordner&gt;\icons</c>; der Name kommt aus <see cref="CustomIcons.FileNameFor"/>. Für
/// Verknüpfungen entsteht bei Bedarf daneben eine .ico gleichen Namens (<see cref="ShortcutIcon"/>).
/// Die gewählte Datei selbst wird nie verändert und nicht kopiert.
/// </summary>
public sealed class CustomIconStore(string directory)
{
    public string Directory => directory;

    /// <summary>Vollständiger Pfad zu einem gespeicherten Namen; null bei leerem oder ungültigem Namen.</summary>
    public string? PathFor(string? fileName) => CustomIcons.IsValidFileName(fileName) ? Path.Combine(directory, fileName!) : null;

    /// <summary>
    /// Erzeugt das Symbol und ersetzt eine frühere PNG des Spiels atomar (eine vorhandene .ico für Verknüpfungen nach
    /// Möglichkeit gleich mit; ist sie gesperrt, gilt das Speichern trotzdem).
    /// Liefert den Dateinamen für <c>GameConfig.CustomIcon</c>. Wirft <see cref="InvalidDataException"/>, wenn die Datei
    /// kein Symbol hergibt, und IO-Ausnahmen beim Schreiben.
    /// </summary>
    public string Save(string gameKey, string sourcePath)
    {
        var image = Render(sourcePath, CustomIcons.StoredSize)
            ?? throw new InvalidDataException("Die Datei enthält kein lesbares Bild oder Symbol: " + sourcePath);
        var name = CustomIcons.FileNameFor(gameKey);
        System.IO.Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, name);
        using (var png = new MemoryStream())
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(png);
            WriteAtomic(target, png.ToArray());
        }

        // Gibt es schon Verknüpfungen mit dem eigenen Symbol (.ico vorhanden), bekommen sie das neue. Hält ein anderes
        // Programm die .ico gerade offen, bleibt sie vorerst alt: Die PNG ist schon ersetzt und gilt, und die nächste
        // Verknüpfung schreibt die .ico neu (ShortcutIcon vergleicht die Bytes).
        if (File.Exists(Path.ChangeExtension(target, ".ico")))
        {
            try
            {
                _ = ShortcutIcon(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return name;
    }

    /// <summary>Löscht die PNG eines Spiels, falls vorhanden (ungültige Namen werden ignoriert). Die .ico für Verknüpfungen bleibt.</summary>
    public void Delete(string? fileName)
    {
        if (PathFor(fileName) is { } path && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// .ico für Verknüpfungen zur PNG eines eigenen Symbols (<paramref name="pngPath"/>, gleicher Name mit Endung .ico):
    /// Eine Verknüpfung kann keine PNG zeigen. Die .ico enthält die PNG unverändert als einziges Bild (PNG in ICO, seit
    /// Windows Vista). Geschrieben wird sie nur, wenn sie fehlt oder nicht mehr zur PNG passt; gelöscht wird sie nie
    /// (auch nicht von „Symbol zurücksetzen“), weil Verknüpfungen dauerhaft auf sie verweisen. Null, wenn die Datei keine
    /// PNG ist. Wirft IO-Ausnahmen.
    /// </summary>
    public string? ShortcutIcon(string pngPath)
    {
        var png = File.ReadAllBytes(pngPath);
        if (IcoWithPng(png) is not { } ico)
        {
            return null;
        }

        var target = Path.ChangeExtension(pngPath, ".ico");
        if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(ico))
        {
            WriteAtomic(target, ico);
        }

        return target;
    }

    /// <summary>
    /// ICO mit einer PNG als einzigem Bild: ICONDIR (6 Byte), ein ICONDIRENTRY (16 Byte, Breite und Höhe aus dem IHDR der
    /// PNG, 0 für 256 und mehr), dann die PNG. Null, wenn <paramref name="png"/> keine PNG ist.
    /// </summary>
    public static byte[]? IcoWithPng(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png[..8].SequenceEqual(signature) || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        static byte Edge(uint pixels) => pixels >= 256 ? (byte)0 : (byte)pixels;
        var ico = new byte[22 + png.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1); // Typ: Symbol
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), 1); // ein Bild
        ico[6] = Edge(BinaryPrimitives.ReadUInt32BigEndian(png[16..]));
        ico[7] = Edge(BinaryPrimitives.ReadUInt32BigEndian(png[20..]));
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(10), 1); // Ebenen
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(12), 32); // Bit je Pixel
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(14), (uint)png.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(18), 22);
        png.CopyTo(ico.AsSpan(22));
        return ico;
    }

    /// <summary>Schreibt über eine Temp-Datei im selben Ordner und ersetzt das Ziel dann in einem Schritt.</summary>
    private void WriteAtomic(string target, byte[] content)
    {
        var temp = Path.Combine(directory, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
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
