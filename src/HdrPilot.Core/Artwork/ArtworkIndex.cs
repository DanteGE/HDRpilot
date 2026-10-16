using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Artwork;

/// <summary>Ein gefundenes Bild im Verzeichnis: Pfad, Größe in Pixeln (0: unbekannt), Cache und Variante.</summary>
public sealed class IndexedImage
{
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("w")]
    public int Width { get; set; }

    [JsonPropertyName("h")]
    public int Height { get; set; }

    public LauncherCache Source { get; set; }

    /// <summary>Steam-Cover: "600x900" oder "capsule".</summary>
    public string? Variant { get; set; }

    /// <summary>Hinweis für „Bild wählen…“ (etwa bei SplashScreen.png eines Xbox-Pakets): "grayscale" oder "titlecard".</summary>
    public string? Note { get; set; }
}

/// <summary>Gewähltes Symbol eines Spiels.</summary>
public sealed class IndexedIcon
{
    /// <summary>Datei des Symbols (Launcher, EXE oder eigenes Symbol); null: Monogramm.</summary>
    public string? Path { get; set; }

    /// <summary>Größte Kantenlänge in der Datei.</summary>
    public int Px { get; set; }

    /// <summary>"custom", "ico", "exe", "tile" (deckendes Kachelbild), "cache" (Launcher-Bildcache), "image" oder "monogram".</summary>
    public string Kind { get; set; } = "monogram";

    /// <summary>Deckendes Bild (Kachelbild), das erzeugte Bilder als Bildfläche statt als Symbol zeigen.</summary>
    public bool Opaque { get; set; }
}

/// <summary>Umgebungsfarben und Farbfeld eines Spiels.</summary>
public sealed class IndexedAmbient
{
    public string A1 { get; set; } = "#000000";

    public string A2 { get; set; } = "#000000";

    public string A3 { get; set; } = "#000000";

    [JsonPropertyName("lx")]
    public double LightX { get; set; }

    [JsonPropertyName("ly")]
    public double LightY { get; set; }

    [JsonPropertyName("hl")]
    public double Highlight { get; set; }

    public AmbientDullness Dull { get; set; }

    /// <summary>Farbfeld als „RRGGBB…“ zeilenweise (8×3 aus dem Titelbild, 6×6 aus dem Symbol).</summary>
    public string? Field { get; set; }

    [JsonPropertyName("fw")]
    public int FieldWidth { get; set; }

    [JsonPropertyName("fh")]
    public int FieldHeight { get; set; }

    /// <summary>Aus dem Titelbild (true) oder aus Symbol bzw. Kachelbild gelesen.</summary>
    public bool FromHero { get; set; }

    public AmbientColors ToColors() => new(A1, A2, A3, LightX, LightY, Highlight, Dull);

    public static IndexedAmbient From(AmbientResult result, bool fromHero)
    {
        var c = result.Colors;
        var field = new StringBuilder(result.FieldWidth * result.FieldHeight * 6);
        for (var i = 0; i + 3 < result.Field.Length; i += 4)
        {
            field.Append(result.Field[i + 2].ToString("X2", System.Globalization.CultureInfo.InvariantCulture))
                .Append(result.Field[i + 1].ToString("X2", System.Globalization.CultureInfo.InvariantCulture))
                .Append(result.Field[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return new IndexedAmbient
        {
            A1 = c.Deep,
            A2 = c.Glow,
            A3 = c.Shine,
            LightX = c.LightX,
            LightY = c.LightY,
            Highlight = c.Highlight,
            Dull = c.Dullness,
            Field = field.ToString(),
            FieldWidth = result.FieldWidth,
            FieldHeight = result.FieldHeight,
            FromHero = fromHero,
        };
    }

    /// <summary>Farbfeld als BGRA (deckend); null, wenn es fehlt oder nicht zur Größe passt.</summary>
    public byte[]? FieldPixels()
    {
        if (Field is null || FieldWidth <= 0 || FieldHeight <= 0 || FieldWidth * FieldHeight > 64 || Field.Length != FieldWidth * FieldHeight * 6)
        {
            return null;
        }

        var pixels = new byte[FieldWidth * FieldHeight * 4];
        for (var i = 0; i < FieldWidth * FieldHeight; i++)
        {
            var (r, g, b) = PixelAnalysis.HexToRgb("#" + Field.Substring(i * 6, 6));
            pixels[i * 4] = (byte)b;
            pixels[(i * 4) + 1] = (byte)g;
            pixels[(i * 4) + 2] = (byte)r;
            pixels[(i * 4) + 3] = 255;
        }

        return pixels;
    }
}

/// <summary>Stand der Abfragen bei SteamGridDB für ein Spiel (bleibt bei geänderten Launcher-Bildern erhalten).</summary>
public sealed class IndexedSteamGridDb
{
    /// <summary>Letzte Abfrage je Bildart ("hero", "logo", "cover"); ohne Treffer wird erst nach 30 Tagen neu gefragt.</summary>
    public Dictionary<string, DateTimeOffset> Checked { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gespeicherte Bilder je Bildart (Dateiname in art\sgdb).</summary>
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);

    /// <summary>SteamGridDB-ID eines Spiels ohne Steam-AppID (aus der Suche), damit nicht jedes Mal gesucht wird.</summary>
    public long? GameId { get; set; }
}

/// <summary>Abgeleitete Daten eines Spiels; gilt, solange <see cref="Signature"/> zum aktuellen Stand passt.</summary>
public sealed class ArtworkIndexEntry
{
    public string Signature { get; set; } = string.Empty;

    /// <summary>Gefundene Launcher-Bilder je Bildart ("hero", "logo", "cover", "header", "field", "community", "cacheicon", "tile").</summary>
    public Dictionary<string, IndexedImage> Located { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Weitere lokale Bilder für „Bild wählen…“.</summary>
    public List<IndexedImage>? Alternatives { get; set; }

    public IndexedAmbient? Ambient { get; set; }

    public IndexedIcon? Icon { get; set; }

    /// <summary>Steam-Cover als verschmiert erkannt (null: noch nicht geprüft).</summary>
    public bool? CoverSmeared { get; set; }

    /// <summary>Kennzahlen der Prüfung (für den Bericht).</summary>
    public string? SmearMetrics { get; set; }

    /// <summary>Zugeschnittenes Logo (Dateiname in art\logo) samt Zuschnitt im Original (x, y, Breite, Höhe).</summary>
    public string? Logo { get; set; }

    public int[]? LogoTrim { get; set; }

    /// <summary>Erzeugte Bilder je Bildart ("hero", "cover", "icon"): Dateiname in art\gen.</summary>
    public Dictionary<string, string>? Generated { get; set; }

    /// <summary>Helle HDR-Fassungen je Breite: Dateiname in art\boost.</summary>
    public Dictionary<string, string>? Boost { get; set; }

    public IndexedSteamGridDb? SteamGridDb { get; set; }

    /// <summary>Alles außer SteamGridDB verwerfen (geänderter Stand).</summary>
    public void ResetDerived(string signature)
    {
        Signature = signature;
        Located = new Dictionary<string, IndexedImage>(StringComparer.Ordinal);
        Alternatives = null;
        Ambient = null;
        Icon = null;
        CoverSmeared = null;
        SmearMetrics = null;
        Logo = null;
        LogoTrim = null;
        Generated = null;
        Boost = null;
    }
}

/// <summary>Inhalt von <c>art\index.json</c>.</summary>
public sealed class ArtworkIndexData
{
    public int Version { get; set; } = ArtworkIndex.CurrentVersion;

    /// <summary>Letzter Lauf von SteamGridDB (höchstens einmal am Tag nach dem Start).</summary>
    public DateTimeOffset? SteamGridDbLastRun { get; set; }

    public Dictionary<string, ArtworkIndexEntry> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Verzeichnis der Spielbilder (<c>%LOCALAPPDATA%\HDRpilot\art\index.json</c>, E89): je Spiel gefundene Launcher-Bilder mit
/// Größe, Umgebungsfarben, Farbfeld, gewähltes Symbol, Ergebnis der Kapsel-Prüfung, Zuschnitt des Logos und die Namen der
/// erzeugten Dateien. Versioniert; eine unlesbare oder fremde Fassung gilt als leer (alles wird neu berechnet, die
/// Launcher-Caches bleiben unberührt). Geschrieben wird atomar.
/// </summary>
public sealed class ArtworkIndex(IFileSystem fileSystem, string path, ILogger? logger = null)
{
    /// <summary>Fassung des Formats; eine andere Fassung wird verworfen.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public string Path => path;

    /// <summary>Gelesener Stand; leer, wenn die Datei fehlt, unlesbar ist oder eine andere Fassung hat. Wirft nicht.</summary>
    public ArtworkIndexData Load()
    {
        try
        {
            if (!fileSystem.FileExists(path))
            {
                return new ArtworkIndexData();
            }

            var data = JsonSerializer.Deserialize<ArtworkIndexData>(fileSystem.ReadAllText(path), Options);
            if (data is null || data.Version != CurrentVersion)
            {
                _logger.LogInformation("Bilder: Verzeichnis {Path} hat eine andere Fassung und wird neu aufgebaut.", path);
                return new ArtworkIndexData();
            }

            data.Games = new Dictionary<string, ArtworkIndexEntry>(data.Games ?? [], StringComparer.OrdinalIgnoreCase);
            return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _logger.LogWarning("Bilder: Verzeichnis {Path} nicht lesbar ({Error}), es wird neu aufgebaut.", path, ex.GetType().Name);
            return new ArtworkIndexData();
        }
    }

    /// <summary>Schreibt den Stand atomar. Wirft IO-Ausnahmen.</summary>
    public void Save(ArtworkIndexData data)
    {
        data.Version = CurrentVersion;
        fileSystem.WriteAllTextAtomic(path, JsonSerializer.Serialize(data, Options));
    }

    /// <summary>
    /// Kennung des Stands eines Spiels: Name (steht in erzeugten Bildern), eigene Bildwahl, eigenes Symbol, Symbol-Hinweise,
    /// EXE-Listen, Installationsordner und eine eigene Ausschlussliste der Hilfsprogramme (Symbol aus der EXE) sowie Pfad,
    /// Größe und Änderungszeit jeder gefundenen Datei. Ändert sich etwas davon, werden die abgeleiteten Daten neu berechnet.
    /// </summary>
    /// <param name="stamp">Größe und Änderungszeit einer Datei als Text; null, wenn sie fehlt.</param>
    /// <param name="helperPatterns">Eigene Ausschlussliste aus den Einstellungen (null: Standardliste, zählt nicht mit).</param>
    public static string Signature(GameConfig game, LocatedArtwork located, Func<string, string?> stamp, IEnumerable<string>? extraFiles = null, IReadOnlyList<string>? helperPatterns = null)
    {
        var text = new StringBuilder(512);
        text.Append(CurrentVersion).Append('|').Append(game.Name).Append('|')
            .Append(game.Artwork?.Hero).Append('|').Append(game.Artwork?.Logo).Append('|').Append(game.Artwork?.Cover).Append('|')
            .Append(game.CustomIcon).Append('|').Append(game.InstallDir).Append('|');
        foreach (var list in new[] { game.IconCandidates, game.KnownExecutables, game.DetectedExecutables, game.ExcludedExecutables })
        {
            foreach (var item in list)
            {
                text.Append(item).Append(';');
            }

            text.Append('|');
        }

        foreach (var image in located.All())
        {
            text.Append(image.Kind).Append('=').Append(image.Path).Append('@').Append(stamp(image.Path)).Append('|');
        }

        foreach (var candidate in game.IconCandidates)
        {
            text.Append(stamp(candidate)).Append('|');
        }

        foreach (var file in extraFiles ?? [])
        {
            text.Append(file).Append('@').Append(stamp(file)).Append('|');
        }

        // Nur eine eigene Liste zählt mit: Mit der Standardliste bleiben bestehende Kennungen gültig.
        if (helperPatterns is not null)
        {
            text.Append("helper=").AppendJoin(';', helperPatterns).Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())), 0, 16);
    }
}
