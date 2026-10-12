using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services;

/// <summary>
/// Gemerkte Symbolquelle eines Spiels (E86): Kennung der Eingaben, Datei (null: keine Quelle gefunden), Art der Quelle,
/// Größe und Änderungszeit der Datei, größte Kantenlänge im Symbol, ob es „gut“ war und wann gewählt wurde.
/// </summary>
public sealed record IconChoice(string Signature, string? Path, IconOrigin Origin, string? Stamp, int NativeSize, bool Good, DateTimeOffset Checked);

/// <summary>
/// Merkt sich je Spiel die gewählte Symbolquelle samt Größe und Änderungszeit der Datei in
/// <c>&lt;Datenordner&gt;\icon-cache.json</c>, damit das Öffnen der Bibliothek nach einem Neustart keine Spielordner
/// durchsucht (die EXE-Suche dauerte kalt 12,8 s für alle Spiele). Ungültig, sobald sich das Spiel oder die Wahlregeln
/// ändern (<see cref="IconSignature"/>), die Datei eine andere Größe oder Änderungszeit hat oder die EXE-Wahl sie nicht mehr
/// zulässt; eine Wahl ohne gutes Symbol gilt nur <see cref="GameIconService.RecheckAfter"/> lang und wird ohne Spielordner
/// gar nicht gemerkt. Gelesen erst beim ersten
/// Symbol, geschrieben gesammelt, wenn keine Symbole mehr anstehen; nach dem Schließen des Fensters nicht im Speicher.
/// Nur ein Zwischenspeicher: fehlt die Datei oder ist sie kaputt, wird neu gewählt.
/// </summary>
public sealed class IconChoiceCache(IFileSystem fileSystem, string? path, ILogger? logger = null)
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly object _gate = new();
    private Dictionary<string, IconChoice>? _entries;
    private bool _dirty;

    public IconChoice? Get(string gameKey)
    {
        lock (_gate)
        {
            return Entries().GetValueOrDefault(gameKey);
        }
    }

    public void Set(string gameKey, IconChoice choice)
    {
        lock (_gate)
        {
            Entries()[gameKey] = choice;
            _dirty = true;
        }
    }

    /// <summary>Vergisst die Wahl eines Spiels (beim nächsten Mal wird neu gewählt).</summary>
    public void Remove(string gameKey)
    {
        lock (_gate)
        {
            if (Entries().Remove(gameKey))
            {
                _dirty = true;
            }
        }
    }

    /// <summary>Schreibt ausstehende Änderungen (atomar). Fehler landen nur im Protokoll.</summary>
    public void Flush()
    {
        if (path is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!_dirty || _entries is null)
            {
                return;
            }

            try
            {
                fileSystem.WriteAllTextAtomic(path, JsonSerializer.Serialize(new CacheFile(CurrentVersion, _entries), Json));
                _dirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Symbol-Zwischenspeicher {Path} nicht geschrieben: {Message}", path, ex.Message);
            }
        }
    }

    /// <summary>Schreibt und gibt den Speicher frei (Fenster geschlossen).</summary>
    public void Release()
    {
        Flush();
        lock (_gate)
        {
            if (!_dirty)
            {
                _entries = null;
            }
        }
    }

    private Dictionary<string, IconChoice> Entries()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        _entries = new Dictionary<string, IconChoice>(StringComparer.OrdinalIgnoreCase);
        if (path is null)
        {
            return _entries;
        }

        try
        {
            if (fileSystem.FileExists(path)
                && JsonSerializer.Deserialize<CacheFile>(fileSystem.ReadAllText(path), Json) is { Version: CurrentVersion, Games: { } games })
            {
                foreach (var (key, choice) in games)
                {
                    if (choice is { Signature: not null })
                    {
                        _entries[key] = choice;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Nur ein Zwischenspeicher: kaputt oder gesperrt heißt neu suchen.
            _logger.LogDebug("Symbol-Zwischenspeicher {Path} nicht lesbar: {Message}", path, ex.Message);
        }

        return _entries;
    }

    private sealed record CacheFile(int Version, Dictionary<string, IconChoice>? Games);
}

/// <summary>
/// Kennung der Eingaben, aus denen das automatische Symbol eines Spiels folgt: Stand der Regeln, Installationsordner und ob
/// das Spiel installiert ist (eine Wahl von vor einer Neuinstallation gilt danach nicht mehr), Symbol-Hinweise, bekannte,
/// erkannte und ausgeschlossene EXEs sowie die Ausschlussliste für Hilfsprozesse. Ändert sich eine davon, wird neu gewählt.
/// </summary>
public static class IconSignature
{
    /// <summary>
    /// Stand der Wahlregeln (Reihenfolge, Ausschlussliste, Standardsymbole). Erhöhen, wenn sich eine Regel ändert: Dann
    /// gilt keine gemerkte Wahl mehr.
    /// </summary>
    public const int RulesVersion = 3;

    public static string Of(GameConfig game, IReadOnlyList<string>? helperPatterns)
    {
        var text = new StringBuilder();
        text.Append(RulesVersion).Append('\n');
        text.Append(game.InstallDir).Append('\n');
        text.Append(game.IsInstalled ? "installiert" : "nicht installiert").Append('\n');
        foreach (var list in new IEnumerable<string>[] { game.IconCandidates, game.KnownExecutables, game.DetectedExecutables, game.ExcludedExecutables, helperPatterns ?? ["<Standard>"] })
        {
            text.AppendJoin('|', list).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString().ToUpperInvariant())))[..16];
    }
}
