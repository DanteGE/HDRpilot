using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HdrPilot.Core.Games;

/// <summary>
/// Namen der eigenen Symbole (Rechtsklick „Symbol ändern…“, E86): je Spiel eine PNG im Ordner icons des Datenordners.
/// Der Name ist aus dem Schlüssel abgeleitet (lesbar plus kurze Prüfsumme, damit sich zwei Schlüssel nach dem Ersetzen
/// unzulässiger Zeichen nie eine Datei teilen) und bleibt beim erneuten Ändern gleich.
/// </summary>
public static class CustomIcons
{
    /// <summary>Kantenlänge der gespeicherten PNG in Pixeln (die Liste zeigt 32, der Detailbereich 48 px).</summary>
    public const int StoredSize = 96;

    private const int MaxReadableLength = 80;

    /// <summary>Dateiname für ein Spiel, etwa „Steam_1091500_3F2A9C1B.png“.</summary>
    public static string FileNameFor(string gameKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameKey);
        var readable = new StringBuilder(gameKey.Length);
        foreach (var c in gameKey)
        {
            readable.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' ? c : '_');
        }

        var text = readable.ToString().Trim('.');
        if (text.Length > MaxReadableLength)
        {
            text = text[..MaxReadableLength];
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(gameKey.ToUpperInvariant()));
        return text + "_" + Convert.ToHexString(hash, 0, 4) + ".png";
    }

    /// <summary>
    /// True, wenn der Name das eigene Symbol dieses Spiels ist (<see cref="FileNameFor"/>). Nur dann löscht die App die Datei:
    /// Gibt eine von Hand geänderte config.json einem Spiel den Namen eines anderen, bleibt dessen Symbol erhalten.
    /// </summary>
    public static bool BelongsTo(string? fileName, string gameKey) =>
        !string.IsNullOrWhiteSpace(fileName)
        && !string.IsNullOrWhiteSpace(gameKey)
        && string.Equals(fileName, FileNameFor(gameKey), StringComparison.OrdinalIgnoreCase);

    /// <summary>True für einen schlichten PNG-Dateinamen ohne Ordner (nie „..“, nie ein Pfad).</summary>
    public static bool IsValidFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 128
        && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.StartsWith('.')
        && name.EndsWith(".png", true, CultureInfo.InvariantCulture);
}
