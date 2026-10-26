using System.Globalization;
using System.Text.Json.Serialization;

namespace HdrPilot.Core.Artwork;

/// <summary>Woher der Akzent eines Spiels stammt (E94).</summary>
public enum GameColorSource
{
    None,
    Logo,
    Cover,
    Hero,
}

/// <summary>Bildart, die <see cref="GameColorAnalysis.Analyze"/> liest (Logo mit eigenen Regeln für Kontur und Leuchten).</summary>
public enum GameColorKind
{
    Logo,
    Cover,
    Hero,
}

/// <summary>Eine Kandidatenfarbe („#RRGGBB“, roh aus dem Bild, noch nicht eingepasst) und ihre Herkunft.</summary>
public readonly record struct GameSwatch(string Hex, GameColorSource Source);

/// <summary>
/// Farben eines Spiels unabhängig vom Thema, einmal je Bildstand gelesen (E94). <see cref="Accent"/> null: keine brauchbare
/// Farbe (graue Bilder); dann gilt das Farbschema. Eingepasst wird erst in der App, damit eine geänderte Regel dort nie die
/// gespeicherten Werte ungültig macht.
/// </summary>
/// <param name="Confidence">Sicherheit der Akzentquelle 0–1, auf zwei Stellen gerundet (bleibt auch bei Ablehnung, fürs Protokoll).</param>
/// <param name="Atmospheres">Bis zu drei Kandidaten für die Atmosphäre, der beste zuerst.</param>
/// <param name="Shines">Bis zu drei Kandidaten für den Glanz (nur Tritonus), der beste zuerst.</param>
public sealed record GameColors(
    string? Accent,
    GameColorSource AccentSource,
    double Confidence,
    IReadOnlyList<GameSwatch> Atmospheres,
    IReadOnlyList<GameSwatch> Shines)
{
    /// <summary>Mindestsicherheit, ab der eine Quelle den Akzent liefert.</summary>
    public const double Accept = 0.5;

    public bool IsUsable => Accent is not null && Confidence >= Accept;

    /// <summary>Keine Farben (graue oder fehlende Bilder, Fehler).</summary>
    public static GameColors None { get; } = new(null, GameColorSource.None, 0, [], []);

    /// <summary>Gleichheit nach Wert, auch der Listen (Records vergleichen Listen sonst nach Verweis).</summary>
    public bool Equals(GameColors? other) =>
        other is not null
        && Accent == other.Accent
        && AccentSource == other.AccentSource
        && Confidence.Equals(other.Confidence)
        && Atmospheres.SequenceEqual(other.Atmospheres)
        && Shines.SequenceEqual(other.Shines);

    public override int GetHashCode() => HashCode.Combine(Accent, AccentSource, Confidence, Atmospheres.Count, Shines.Count);

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Accent ?? "-"} aus {AccentSource} ({Confidence:0.00}); Atmosphäre {string.Join(",", Atmospheres.Select(s => s.Hex))}; Glanz {string.Join(",", Shines.Select(s => s.Hex))}");
}

/// <summary>Kandidatenfarbe im Verzeichnis: Farbe und Herkunft als schlichter Text.</summary>
public sealed class IndexedSwatch
{
    [JsonPropertyName("c")]
    public string Color { get; set; } = string.Empty;

    [JsonPropertyName("s")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Gespeicherte Spielfarben (<c>colors</c> in <c>art\index.json</c>, E94). Herkünfte sind schlichter Text statt Aufzählung:
/// Ein unbekannter Wert würde mit dem Aufzählungs-Konverter das ganze Verzeichnis verwerfen; so gilt nur der Eintrag als
/// fehlend und wird neu berechnet. Höchstens 260 Bytes (3 + 3 Kandidaten).
/// </summary>
public sealed class IndexedGameColors
{
    /// <summary>Fassung des Verfahrens (<see cref="GameColorAnalysis.Version"/>); eine andere gilt als fehlend.</summary>
    [JsonPropertyName("v")]
    public int Version { get; set; }

    [JsonPropertyName("acc")]
    public string? Accent { get; set; }

    [JsonPropertyName("src")]
    public string Source { get; set; } = nameof(GameColorSource.None);

    [JsonPropertyName("conf")]
    public double Confidence { get; set; }

    [JsonPropertyName("atm")]
    public List<IndexedSwatch>? Atmospheres { get; set; }

    [JsonPropertyName("sh")]
    public List<IndexedSwatch>? Shines { get; set; }

    /// <summary>
    /// Gelesene Farben; null, wenn der Eintrag nicht brauchbar ist (andere Fassung, ungültige Farbe, unbekannte Herkunft bei
    /// gesetztem Akzent). Kandidaten mit unbekannter Herkunft oder ungültiger Farbe fallen weg.
    /// </summary>
    public GameColors? ToColors()
    {
        if (Version != GameColorAnalysis.Version || !Enum.TryParse<GameColorSource>(Source, ignoreCase: false, out var source)
            || !Enum.IsDefined(source))
        {
            return null;
        }

        if (Accent is not null && (source == GameColorSource.None || Rgba8.TryParse(Accent) is null))
        {
            return null;
        }

        if (Accent is null && source != GameColorSource.None)
        {
            return null;
        }

        return new GameColors(Accent, source, Math.Round(Math.Clamp(Confidence, 0, 1), 2), Swatches(Atmospheres), Swatches(Shines));
    }

    public static IndexedGameColors From(GameColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        return new IndexedGameColors
        {
            Version = GameColorAnalysis.Version,
            Accent = colors.Accent,
            Source = colors.AccentSource.ToString(),
            Confidence = Math.Round(colors.Confidence, 2),
            Atmospheres = colors.Atmospheres.Count == 0 ? null : colors.Atmospheres.Select(Indexed).ToList(),
            Shines = colors.Shines.Count == 0 ? null : colors.Shines.Select(Indexed).ToList(),
        };
    }

    private static IndexedSwatch Indexed(GameSwatch s) => new() { Color = s.Hex, Source = s.Source.ToString() };

    private static GameSwatch[] Swatches(List<IndexedSwatch>? list)
    {
        if (list is null)
        {
            return [];
        }

        var result = new List<GameSwatch>(list.Count);
        foreach (var item in list)
        {
            if (item is not null && Rgba8.TryParse(item.Color) is not null
                && Enum.TryParse<GameColorSource>(item.Source, ignoreCase: false, out var s) && Enum.IsDefined(s) && s != GameColorSource.None)
            {
                result.Add(new GameSwatch(item.Color, s));
            }
        }

        return [.. result.Take(3)];
    }
}
