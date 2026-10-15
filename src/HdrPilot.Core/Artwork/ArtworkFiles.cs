using System.Text.RegularExpressions;
using HdrPilot.Core.Games;

namespace HdrPilot.Core.Artwork;

/// <summary>
/// Namen der Dateien im Ordner <c>art</c> des Datenordners (E89). Je Spiel ein lesbarer Stamm mit Prüfsumme wie bei den
/// eigenen Symbolen (<see cref="CustomIcons.FileNameFor"/>, etwa „Steam_553850_3F2A9C1B“), dahinter die Bildart:
/// <list type="bullet">
/// <item><c>custom\&lt;Stamm&gt;-hero.jpg|-logo.png|-cover.jpg</c>: eigene Bilder („Bild wählen…“);</item>
/// <item><c>gen\&lt;Stamm&gt;-hero.jpg|-cover.jpg|-icon.png</c>: erzeugte Ersatzbilder;</item>
/// <item><c>logo\&lt;Stamm&gt;.png</c>: auf den sichtbaren Teil zugeschnittene Logos;</item>
/// <item><c>boost\&lt;Stamm&gt;-&lt;Breite&gt;.jpg</c>: helle HDR-Fassungen der Titelbilder;</item>
/// <item><c>sgdb\&lt;Stamm&gt;-hero|-logo|-cover.&lt;Endung&gt;</c>: Bilder von SteamGridDB.</item>
/// </list>
/// </summary>
public static partial class ArtworkFiles
{
    public const string CustomFolder = "custom";
    public const string GeneratedFolder = "gen";
    public const string LogoFolder = "logo";
    public const string BoostFolder = "boost";
    public const string SteamGridDbFolder = "sgdb";

    /// <summary>Stamm der Dateinamen eines Spiels (lesbarer Schlüssel und 8 Hex-Zeichen Prüfsumme).</summary>
    public static string Stem(string gameKey) => Path.GetFileNameWithoutExtension(CustomIcons.FileNameFor(gameKey));

    /// <summary>Name einer eigenen Bilddatei: Titelbild und Cover als JPEG, Logo als PNG (Durchsichtigkeit bleibt).</summary>
    public static string CustomFileName(string gameKey, ArtworkKind kind) => kind switch
    {
        ArtworkKind.Hero => Stem(gameKey) + "-hero.jpg",
        ArtworkKind.Logo => Stem(gameKey) + "-logo.png",
        ArtworkKind.Cover => Stem(gameKey) + "-cover.jpg",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Eigene Bilder gibt es für Titelbild, Logo und Cover."),
    };

    /// <summary>
    /// True für den Namen einer von HDRpilot angelegten eigenen Bilddatei (<see cref="CustomFileName"/>). Nur solche löscht die
    /// App; ein von Hand in config.json eingetragener fremder Name bleibt unangetastet.
    /// </summary>
    public static bool IsOwnCustomFile(string? fileName) => fileName is not null && OwnCustomName().IsMatch(fileName);

    /// <summary>Name eines erzeugten Ersatzbilds (Kopfbild: Vorschaubild der Liste ohne Steam-Kopfbild und ohne Titelbild).</summary>
    public static string GeneratedFileName(string gameKey, ArtworkKind kind) => kind switch
    {
        ArtworkKind.Hero => Stem(gameKey) + "-hero.jpg",
        ArtworkKind.Cover => Stem(gameKey) + "-cover.jpg",
        ArtworkKind.Header => Stem(gameKey) + "-header.jpg",
        ArtworkKind.Icon => Stem(gameKey) + "-icon.png",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Erzeugt werden Titelbild, Cover, Kopfbild und Monogramm."),
    };

    /// <summary>Name des zugeschnittenen Logos.</summary>
    public static string LogoFileName(string gameKey) => Stem(gameKey) + ".png";

    /// <summary>Name der hellen HDR-Fassung in einer Breite.</summary>
    public static string BoostFileName(string gameKey, int width) =>
        Stem(gameKey) + "-" + width.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg";

    /// <summary>Name eines Bildes von SteamGridDB (Endung aus der Antwort: .png, .jpg oder .webp).</summary>
    public static string SteamGridDbFileName(string gameKey, ArtworkKind kind, string extension)
    {
        var suffix = kind switch
        {
            ArtworkKind.Hero => "-hero",
            ArtworkKind.Logo => "-logo",
            ArtworkKind.Cover => "-cover",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "SteamGridDB füllt Titelbild, Logo und Cover."),
        };
        var ext = extension.ToLowerInvariant() switch
        {
            ".png" or "png" => ".png",
            ".webp" or "webp" => ".webp",
            _ => ".jpg",
        };
        return Stem(gameKey) + suffix + ext;
    }

    /// <summary>Breite der gespeicherten Fassung zu einer angefragten Breite (Stufen, damit nicht jede Fenstergröße eine Datei bekommt).</summary>
    public static int WidthStep(int requested)
    {
        foreach (var step in (int[])[160, 320, 480, 640, 960, 1280, 1600, 1920, 2560, 3200, 3840])
        {
            if (requested <= step)
            {
                return step;
            }
        }

        return 3840;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,90}_[0-9A-F]{8}-(hero|logo|cover)\.(jpg|png)$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnCustomName();
}
