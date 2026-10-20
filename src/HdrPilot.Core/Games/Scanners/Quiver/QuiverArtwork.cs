using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games.Scanners.Quiver;

/// <summary>
/// Cover eines Quiver-Spiels aus den lokalen Bildzwischenspeichern von Quiver (E91), nur lesend, ohne Netz. Quiver legt die
/// Bilder unter <c>&lt;Quiver&gt;\Cache</c> ab:
/// <list type="bullet">
/// <item>eigenes Cover des Nutzers: <c>CustomIcons\&lt;folderName&gt;_custom.&lt;Endung&gt;</c> (png, jpg, jpeg, webp, bmp, gif, ico);</item>
/// <item>gemeinsamer Zwischenspeicher: <c>Images\&lt;MD5&gt;</c> ohne Endung, MD5 der Bildadresse als Großbuchstaben-Hex (über
/// die ASCII-Bytes, wie der Bildlader von Quiver). Covers von SteamGridDB lädt Quiver als verkleinerte Kopie von
/// quiverlauncher.com; der Schlüssel ist dann deren Adresse
/// <c>https://quiverlauncher.com/cdn-cgi/image/width=480,quality=70,format=auto,fit=scale-down/&lt;appIconUrl&gt;</c> (Breite je
/// nach Anzeigeskalierung 240 bis 960, als Titelbild 1920 mit Qualität 85); sonst die Adresse selbst. Meist WebP.</item>
/// <item>älterer Zwischenspeicher je Spiel: <c>Icons\&lt;folderName&gt;_&lt;erste 16 Hex-Zeichen von SHA-256 der Adresse&gt;&lt;Endung&gt;</c>.</item>
/// </list>
/// Es zählen nur vorhandene Dateien mit einem Bildkopf (PNG, JPEG, GIF, BMP, ICO, WebP), höchstens
/// <see cref="IconFiles.MaxCandidates"/>, das eigene Cover zuerst, dann die größten Kopien. Dekodiert wird erst in der App.
/// Nachgebildet sind nur die Namen und Adressen, unter denen Quiver ablegt (gemessen am Zielrechner); Code von Quiver steckt
/// hier nicht.
/// </summary>
internal static class QuiverArtwork
{
    /// <summary>Bildumrechner von quiverlauncher.com, über den Quiver Bilder von SteamGridDB verkleinert lädt.</summary>
    internal const string ResizeService = "https://quiverlauncher.com/cdn-cgi/image/";

    /// <summary>Herkunft der Bilder, die der Bildumrechner kennt: Raster- und Titelbilder von SteamGridDB.</summary>
    internal const string SteamGridDbImages = "https://cdn2.steamgriddb.com/";

    internal const string ImagesFolder = "Images";

    internal const string CustomIconsFolder = "CustomIcons";

    internal const string IconsFolder = "Icons";

    /// <summary>Breiten der Kartenbilder, die Quiver je nach Anzeigeskalierung lädt, hier die größte zuerst.</summary>
    internal static readonly int[] CardCopyWidths = [960, 720, 480, 360, 240];

    /// <summary>JPEG/WebP-Qualität der Kartenbilder.</summary>
    internal const int CardCopyQuality = 70;

    /// <summary>Breite und Qualität der Kopie, die Quiver als Titelbild über die volle Breite zeigt.</summary>
    internal const int TitleCopyWidth = 1920;

    internal const int TitleCopyQuality = 85;

    /// <summary>Endungen eigener Cover in der Reihenfolge, in der Quiver sie sucht.</summary>
    internal static readonly string[] CustomExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico"];

    /// <summary>
    /// Kennungen am Dateianfang je Bildformat: jede Zeile eine Folge von (Versatz, Bytes), die alle passen müssen. PNG,
    /// JPEG (SOI und Marker), GIF (87a/89a), BMP, ICO (reserviert 0, Typ 1) und WebP (RIFF-Behälter mit Form „WEBP“).
    /// </summary>
    private static readonly (int Offset, byte[] Bytes)[][] ImageSignatures =
    [
        [(0, [0x89, 0x50, 0x4E, 0x47])],
        [(0, [0xFF, 0xD8, 0xFF])],
        [(0, "GIF8"u8.ToArray())],
        [(0, "BM"u8.ToArray())],
        [(0, [0x00, 0x00, 0x01, 0x00])],
        [(0, "RIFF"u8.ToArray()), (8, "WEBP"u8.ToArray())],
    ];

    /// <summary>So viele Bytes vom Dateianfang braucht <see cref="HasImageHeader"/> höchstens.</summary>
    internal static readonly int HeaderBytes = ImageSignatures.SelectMany(s => s).Max(p => p.Offset + p.Bytes.Length);

    /// <summary>Name der Datei im gemeinsamen Zwischenspeicher für eine Bildadresse: MD5 der ASCII-Bytes, Hex in Großbuchstaben.</summary>
    public static string CacheName(string url) => Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(url)));

    /// <summary>
    /// True für ein Bild, das der Bildumrechner von quiverlauncher.com verkleinert anbietet: Adresse unter
    /// <see cref="SteamGridDbImages"/> im Ordner <c>grid</c> oder <c>hero</c>, Dateiname aus Hex-Ziffern und einer Endung aus
    /// Buchstaben, Ziffern oder _, ohne weitere Teile (Groß-/Kleinschreibung egal).
    /// </summary>
    internal static bool HasResizedCopies(string url)
    {
        if (!url.StartsWith(SteamGridDbImages, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = url[SteamGridDbImages.Length..].Split('/');
        if (parts is not [var kind, var file] || !(kind.Equals("grid", StringComparison.OrdinalIgnoreCase) || kind.Equals("hero", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var dot = file.IndexOf('.', StringComparison.Ordinal);
        return dot > 0
            && dot < file.Length - 1
            && file[..dot].All(Uri.IsHexDigit)
            && file[(dot + 1)..].All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    /// <summary>Adresse der verkleinerten Kopie, die Quiver für eine Breite anfordert; die Adresse selbst, wenn es keine gibt.</summary>
    public static string ResizedUrl(string url, int width, int quality) => HasResizedCopies(url)
        ? string.Create(CultureInfo.InvariantCulture, $"{ResizeService}width={width},quality={quality},format=auto,fit=scale-down/{url}")
        : url;

    /// <summary>Alle Adressen, unter denen Quiver das Cover abgelegt haben kann: das Original, das Titelbild, die Karten (groß zuerst).</summary>
    public static IReadOnlyList<string> CachedUrls(string url) =>
        new[] { url, ResizedUrl(url, TitleCopyWidth, TitleCopyQuality) }
            .Concat(CardCopyWidths.Select(w => ResizedUrl(url, w, CardCopyQuality)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Datei im älteren Zwischenspeicher je Spiel (siehe Klassenbeschreibung).</summary>
    public static string LegacyFileName(string folderName, string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
        var extension = Path.GetExtension(url);
        if (string.IsNullOrEmpty(extension) || extension.Length > 5 || extension.Contains('?', StringComparison.Ordinal))
        {
            extension = ".png";
        }

        return folderName + "_" + hash + extension;
    }

    /// <summary>Vorhandene Cover-Dateien mit Bildkopf, beste zuerst (siehe Klassenbeschreibung).</summary>
    public static IReadOnlyList<string> CoverCandidates(IFileSystem fileSystem, string root, string folderName, string? iconUrl)
    {
        var cache = Path.Combine(root, QuiverRoot.CacheFolder);
        var paths = new List<string>();
        paths.AddRange(CustomExtensions.Select(e => Path.Combine(cache, CustomIconsFolder, folderName + "_custom" + e)));
        var url = iconUrl?.Trim();
        if (!string.IsNullOrEmpty(url) && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            paths.AddRange(CachedUrls(url).Select(u => Path.Combine(cache, ImagesFolder, CacheName(u))));
            paths.Add(Path.Combine(cache, IconsFolder, LegacyFileName(folderName, url)));
        }

        var result = new List<string>();
        foreach (var path in paths)
        {
            if (result.Count >= IconFiles.MaxCandidates)
            {
                break;
            }

            var normalized = PathUtil.Normalize(path);
            if (IconFiles.IsLocalPath(normalized) && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase) && IsImageFile(fileSystem, normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    /// <summary>Vorhanden und mit Bildkopf (die ersten <see cref="HeaderBytes"/> Bytes, geteilt gelesen); sonst false. Wirft nicht.</summary>
    internal static bool IsImageFile(IFileSystem fileSystem, string path)
    {
        try
        {
            return fileSystem.FileExists(path) && HasImageHeader(fileSystem.ReadShared(path, HeaderBytes).Bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>True, wenn der Dateianfang zu einer der <see cref="ImageSignatures"/> passt.</summary>
    internal static bool HasImageHeader(ReadOnlySpan<byte> head)
    {
        foreach (var signature in ImageSignatures)
        {
            var matches = true;
            foreach (var (offset, bytes) in signature)
            {
                if (head.Length < offset + bytes.Length || !head.Slice(offset, bytes.Length).SequenceEqual(bytes))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }
}
