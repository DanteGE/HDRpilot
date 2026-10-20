using System.Globalization;

namespace HdrPilot.Core.Artwork;

/// <summary>Bildart eines Spiels (E89).</summary>
public enum ArtworkKind
{
    /// <summary>Breites Titelbild (Steam library_hero, etwa 3,1:1).</summary>
    Hero,

    /// <summary>Schriftzug mit durchsichtigem Hintergrund, auf den sichtbaren Teil zugeschnitten.</summary>
    Logo,

    /// <summary>Hochformat 2:3 (Steam library_600x900 bzw. Kapsel).</summary>
    Cover,

    /// <summary>Querformat-Kopfbild (Steam header), für kleine Vorschaubilder der Liste.</summary>
    Header,

    /// <summary>Weiches Farbfeld hinter dem Titelbild (Steam library_hero_blur oder ein 6×6-Bild, gestreckt).</summary>
    Field,

    /// <summary>Symbol des Spiels (größtes vorhandenes Bild, höchstens 256 px; sonst ein Monogramm).</summary>
    Icon,
}

/// <summary>Woher ein angezeigtes Bild stammt.</summary>
public enum ArtworkOrigin
{
    /// <summary>Kein Bild (nur bei <see cref="ArtworkKind.Logo"/>: der Name steht dann in der Titelschrift).</summary>
    None,

    /// <summary>Lokaler Cache des Launchers (Steam, GOG Galaxy, Ubisoft, Xbox-Paket) oder Symbol aus der EXE.</summary>
    Launcher,

    /// <summary>Von HDRpilot erzeugt (Farbfeld, Monogramm, Ausschnitt aus dem Titelbild).</summary>
    Generated,

    /// <summary>Anderes lokales Bild, vom Nutzer gewählt (etwa SplashScreen.png eines Xbox-Pakets).</summary>
    Alternative,

    /// <summary>Eigene Datei des Nutzers („Bild wählen…“).</summary>
    Custom,

    /// <summary>Von SteamGridDB geladen (nur mit eigenem Schlüssel, nur für fehlende Bilder).</summary>
    SteamGridDb,
}

/// <summary>Art einer eigenen Bildwahl (<see cref="ArtworkOverride"/>).</summary>
public enum ArtworkOverrideKind
{
    Automatic,
    Generated,
    Alternative,
    Custom,
}

/// <summary>
/// Kennzeichen der eigenen Bildwahl in <see cref="Config.GameArtworkChoice"/>: null (automatisch), "gen",
/// "alt:&lt;Dateiname&gt;" oder "custom:&lt;Dateiname&gt;". Dateinamen sind schlichte Namen ohne Ordner (nie ein Pfad), damit
/// eine von Hand geänderte config.json nie auf fremde Dateien zeigt.
/// </summary>
public static class ArtworkOverride
{
    public const string Generated = "gen";
    public const string AlternativePrefix = "alt:";
    public const string CustomPrefix = "custom:";

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>True für null (automatisch) und jedes gültige Kennzeichen.</summary>
    public static bool IsValid(string? token) => token is null || KindOf(token) is not ArtworkOverrideKind.Automatic;

    public static ArtworkOverrideKind KindOf(string? token)
    {
        if (token is null)
        {
            return ArtworkOverrideKind.Automatic;
        }

        if (string.Equals(token, Generated, StringComparison.Ordinal))
        {
            return ArtworkOverrideKind.Generated;
        }

        if (token.StartsWith(AlternativePrefix, StringComparison.Ordinal) && IsPlainImageName(token[AlternativePrefix.Length..]))
        {
            return ArtworkOverrideKind.Alternative;
        }

        if (token.StartsWith(CustomPrefix, StringComparison.Ordinal) && IsPlainImageName(token[CustomPrefix.Length..]))
        {
            return ArtworkOverrideKind.Custom;
        }

        return ArtworkOverrideKind.Automatic;
    }

    /// <summary>Dateiname hinter "alt:" bzw. "custom:"; sonst null.</summary>
    public static string? FileOf(string? token) => KindOf(token) switch
    {
        ArtworkOverrideKind.Alternative => token![AlternativePrefix.Length..],
        ArtworkOverrideKind.Custom => token![CustomPrefix.Length..],
        _ => null,
    };

    public static string Alternative(string fileName) => IsPlainImageName(fileName)
        ? AlternativePrefix + fileName
        : throw new ArgumentException("Nur ein schlichter Bild-Dateiname: " + fileName, nameof(fileName));

    public static string Custom(string fileName) => IsPlainImageName(fileName)
        ? CustomPrefix + fileName
        : throw new ArgumentException("Nur ein schlichter Bild-Dateiname: " + fileName, nameof(fileName));

    /// <summary>Schlichter Bild-Dateiname ohne Ordner, höchstens 128 Zeichen, nie mit Punkt am Anfang.</summary>
    public static bool IsPlainImageName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 128
        && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.StartsWith('.')
        && ImageExtensions.Any(e => name.EndsWith(e, true, CultureInfo.InvariantCulture));
}

/// <summary>Wie blass die Farben eines Bildes sind (bestimmt, ob beim Anwenden die Schemafarbe beigemischt wird).</summary>
public enum AmbientDullness
{
    None,

    /// <summary>Blass oder sehr dunkel: Mindest-Chroma 0,08 (OKLCH) schon beim Auslesen.</summary>
    Pale,

    /// <summary>Fast grau: beim Anwenden zur Hälfte mit der Schemafarbe mischen.</summary>
    Gray,
}

/// <summary>
/// Umgebungsfarben eines Bildes, einmal je Bild ausgelesen und zwischengespeichert (Farben als "#RRGGBB"):
/// <see cref="Deep"/> dunkler Grundton (a1), <see cref="Glow"/> kräftige Leuchtfarbe (a2), <see cref="Shine"/> helle
/// Zweitfarbe (a3), dazu die hellste Stelle des Titelbilds (<see cref="LightX"/>/<see cref="LightY"/> als Anteil
/// 0–1, <see cref="Highlight"/> ihre Helligkeit 0–1) für Lichtpunkt und Leuchten im HDR-Moment.
/// </summary>
public sealed record AmbientColors(
    string Deep,
    string Glow,
    string Shine,
    double LightX,
    double LightY,
    double Highlight,
    AmbientDullness Dullness)
{
    /// <summary>Farben aus dem Namen (ohne Bild): fester Farbton je Name, wie im Prototyp (namePal).</summary>
    public static AmbientColors FromName(string name)
    {
        var hue = NameHue(name);
        return new AmbientColors(Hsl(hue, 0.55, 0.16), Hsl(hue + 18, 0.78, 0.55), Hsl(hue - 36, 0.85, 0.74), 0.62, 0.45, 0.55, AmbientDullness.None);
    }

    /// <summary>Farbton 0–359 aus dem Namen (FNV-1a), damit dasselbe Spiel immer dieselben Ersatzfarben bekommt.</summary>
    public static int NameHue(string name)
    {
        var h = 2166136261u;
        foreach (var c in name ?? string.Empty)
        {
            h ^= c;
            h *= 16777619u;
        }

        return (int)(h % 360);
    }

    private static string Hsl(double h, double s, double l)
    {
        h = (((h % 360) + 360) % 360) / 360;
        double F(double n)
        {
            var k = (n + (h * 12)) % 12;
            var a = s * Math.Min(l, 1 - l);
            return l - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }

        static int B(double v) => (int)Math.Clamp(Math.Round(v * 255), 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{B(F(0)):X2}{B(F(8)):X2}{B(F(4)):X2}");
    }
}
