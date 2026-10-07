using System.Buffers.Binary;
using System.Text;

namespace HdrPilot.Core.Games.Scanners.Xbox;

/// <summary>Ergebnis beim Lesen einer .GamingRoot-Datei. Bei <see cref="Error"/> ungleich null sind die bis dahin vollständigen Einträge trotzdem gesetzt.</summary>
public sealed record GamingRootContent(IReadOnlyList<string> Folders, uint HeaderValue, string? Error);

/// <summary>
/// Liest die versteckte Datei <c>.GamingRoot</c> in einer Laufwerkswurzel, mit der die Xbox-App ihren Spieleordner
/// auf diesem Laufwerk markiert. Gemessenes Format (C: und D: am Zielrechner):
/// 4 Byte Kennung <c>RGBX</c>, UInt32 little-endian (gemessen immer 1), danach nullterminierte UTF-16LE-Ordnernamen
/// relativ zur Laufwerkswurzel. Ob mehrere Einträge folgen können, war am Zielrechner nicht prüfbar; gelesen werden
/// deshalb alle nullterminierten Namen bis Dateiende, der UInt32 dient nur zur Kontrolle.
/// </summary>
public static class GamingRootFile
{
    public const string FileName = ".GamingRoot";

    private const int HeaderLength = 8;
    private const int MaxFolderLength = 260;

    public static ReadOnlySpan<byte> Magic => "RGBX"u8;

    public static GamingRootContent Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength)
        {
            return new GamingRootContent([], 0, $"Datei ist zu kurz ({data.Length} Byte, erwartet mindestens {HeaderLength}).");
        }

        if (!data[..4].SequenceEqual(Magic))
        {
            return new GamingRootContent([], 0, "Kennung \"RGBX\" fehlt, die Datei hat ein unbekanntes Format.");
        }

        var header = BinaryPrimitives.ReadUInt32LittleEndian(data[4..HeaderLength]);
        var folders = new List<string>();
        var current = new StringBuilder();
        var body = data[HeaderLength..];
        var evenLength = body.Length - (body.Length % 2);
        for (var i = 0; i < evenLength; i += 2)
        {
            var c = (char)BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(i, 2));
            if (c != '\0')
            {
                current.Append(c);
                continue;
            }

            if (current.Length > 0)
            {
                var name = current.ToString();
                current.Clear();
                if (!IsPlausibleFolderName(name))
                {
                    return new GamingRootContent(folders, header, "Ungültiger Ordnername in der Datei (Steuerzeichen oder unzulässige Zeichen).");
                }

                folders.Add(name);
            }
        }

        if (current.Length > 0 || (body.Length % 2 != 0 && body[^1] != 0))
        {
            return new GamingRootContent(folders, header, "Datei ist abgeschnitten: der letzte Ordnername endet nicht mit einem Nullzeichen.");
        }

        return folders.Count == 0
            ? new GamingRootContent(folders, header, "Datei enthält keinen Ordnernamen.")
            : new GamingRootContent(folders, header, null);
    }

    private static bool IsPlausibleFolderName(string name)
    {
        if (name.Length > MaxFolderLength || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c < 0x20 || c is '<' or '>' or '"' or '|' or '?' or '*' or '�' or '￿')
            {
                return false;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }
}
