using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
/// </summary>
internal static class QuiverArtwork
{
    internal const string Resizer = "https://quiverlauncher.com/cdn-cgi/image/";

    internal const string ImagesFolder = "Images";

    internal const string CustomIconsFolder = "CustomIcons";

    internal const string IconsFolder = "Icons";

    /// <summary>Breiten der Karten-Kopien, die Quiver anfordert, hier größte zuerst.</summary>
    internal static readonly int[] CardWidths = [960, 720, 480, 360, 240];

    internal const int CardQuality = 70;

    internal const int BannerWidth = 1920;

    internal const int BannerQuality = 85;

    /// <summary>Endungen eigener Cover in der Reihenfolge, in der Quiver sie sucht.</summary>
    internal static readonly string[] CustomExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico"];

    /// <summary>Bilder von SteamGridDB, für die quiverlauncher.com verkleinerte Kopien macht.</summary>
    private static readonly Regex ResizableArt =
        new(@"^https://cdn2\.steamgriddb\.com/(?:grid|hero)/[0-9a-f]+\.\w+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Name der Datei im gemeinsamen Zwischenspeicher für eine Bildadresse: MD5 der ASCII-Bytes, Hex in Großbuchstaben.</summary>
    public static string CacheName(string url) => Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(url)));

    /// <summary>Adresse der verkleinerten Kopie, die Quiver für eine Breite anfordert; die Adresse selbst, wenn es keine gibt.</summary>
    public static string ResizedUrl(string url, int width, int quality) => ResizableArt.IsMatch(url)
        ? string.Create(CultureInfo.InvariantCulture, $"{Resizer}width={width},quality={quality},format=auto,fit=scale-down/{url}")
        : url;

    /// <summary>Alle Adressen, unter denen Quiver das Cover abgelegt haben kann: das Original, das Titelbild, die Karten (groß zuerst).</summary>
    public static IReadOnlyList<string> CachedUrls(string url) =>
        new[] { url, ResizedUrl(url, BannerWidth, BannerQuality) }
            .Concat(CardWidths.Select(w => ResizedUrl(url, w, CardQuality)))
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

    /// <summary>Vorhanden und mit Bildkopf (die ersten 12 Bytes, geteilt gelesen); sonst false. Wirft nicht.</summary>
    internal static bool IsImageFile(IFileSystem fileSystem, string path)
    {
        try
        {
            return fileSystem.FileExists(path) && HasImageHeader(fileSystem.ReadShared(path, 12).Bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Kennung am Dateianfang: PNG, JPEG, GIF, BMP, ICO oder WebP (RIFF…WEBP).</summary>
    internal static bool HasImageHeader(ReadOnlySpan<byte> head) =>
        head.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G'])
        || head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])
        || head.StartsWith("GIF8"u8)
        || head.StartsWith("BM"u8)
        || head.StartsWith((ReadOnlySpan<byte>)[0, 0, 1, 0])
        || (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8));
}
