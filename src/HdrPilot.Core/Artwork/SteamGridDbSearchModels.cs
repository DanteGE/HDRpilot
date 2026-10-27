using System.Globalization;

namespace HdrPilot.Core.Artwork;

/// <summary>
/// Eine Seite einer Bilderliste für „Bild wählen…“ (E93). <see cref="RawCount"/> zählt die Einträge in <c>data</c> vor dem
/// Filtern, <see cref="Skipped"/> die verworfenen.
/// </summary>
public sealed record SteamGridDbAssetPage(IReadOnlyList<SteamGridDbAsset> Items, int Page, int RawCount, int Skipped, bool GameUnknown)
{
    /// <summary>Die Liste geht weiter, bis eine Seite leer zurückkommt (oder 404 ab Seite 1).</summary>
    public bool HasMore => !GameUnknown && RawCount > 0;

    /// <summary>Spiel bei SteamGridDB unbekannt (404 auf Seite 0).</summary>
    public static SteamGridDbAssetPage Unknown() => new([], 0, 0, 0, true);

    /// <summary>Ende der Liste (404 ab Seite 1).</summary>
    public static SteamGridDbAssetPage End(int page) => new([], page, 0, 0, false);
}

/// <summary>
/// Ein Treffer der Suche (search/autocomplete) oder von games/id. Der Name ist bereinigt wie ein Autorenname;
/// <see cref="ReleaseYear"/> stammt aus release_date (Unix-Sekunden, UTC) und ist null ohne Angabe oder außerhalb 1950–2100.
/// </summary>
public sealed record SteamGridDbGameHit(long Id, string Name, int? ReleaseYear, bool Verified);

/// <summary>
/// Ziel einer Bilderabfrage: genau eins von Steam-AppID (nur ASCII-Ziffern, nicht leer) oder SteamGridDB-ID (&gt; 0).
/// Kommas kommen so nie in den Pfad (eine Liste lieferte HTTP 207 mit nur einem Bild je Spiel).
/// </summary>
public readonly record struct SteamGridDbTarget
{
    private readonly string? _segment;

    private SteamGridDbTarget(string? steamAppId, long? gameId, string segment)
    {
        SteamAppId = steamAppId;
        GameId = gameId;
        _segment = segment;
    }

    /// <summary>Steam-AppID oder null.</summary>
    public string? SteamAppId { get; }

    /// <summary>SteamGridDB-ID oder null.</summary>
    public long? GameId { get; }

    /// <summary>„steam/&lt;AppID&gt;“ oder „game/&lt;ID&gt;“.</summary>
    internal string PathSegment => _segment ?? throw new InvalidOperationException("Leeres Ziel.");

    /// <summary>Ziel über die Steam-AppID; <see cref="ArgumentException"/>, wenn sie nicht nur aus ASCII-Ziffern besteht.</summary>
    public static SteamGridDbTarget Steam(string appId)
    {
        if (!IsSteamAppId(appId))
        {
            throw new ArgumentException("Steam-AppID muss aus Ziffern bestehen.", nameof(appId));
        }

        return new SteamGridDbTarget(appId, null, "steam/" + appId);
    }

    /// <summary>Ziel über die SteamGridDB-ID; <see cref="ArgumentOutOfRangeException"/> für eine ID &lt;= 0.</summary>
    public static SteamGridDbTarget Game(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        return new SteamGridDbTarget(null, id, "game/" + id.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>True für eine nicht leere Folge von ASCII-Ziffern (auch der Schutz gegen eine leere AppID, §19.1).</summary>
    public static bool IsSteamAppId(string? appId) => !string.IsNullOrEmpty(appId) && appId.All(char.IsAsciiDigit);

    public override string ToString() => _segment ?? string.Empty;
}

/// <summary>
/// Zähler einer Suche in „Bild wählen…“ (threadsicher): Anfragen an die Schnittstelle, Bildabrufe und geladene
/// Vorschau-Bytes getrennt. Volle Bilder begrenzt die Sitzung über ihre Anzahl.
/// </summary>
public sealed class SteamGridDbBudget(int maxApiRequests, int maxImageRequests, long maxPreviewBytes)
{
    private int _api;
    private int _images;
    private long _previewBytes;

    public int ApiRequests => Volatile.Read(ref _api);

    public int ImageRequests => Volatile.Read(ref _images);

    public long PreviewBytes => Interlocked.Read(ref _previewBytes);

    internal bool TryTakeApi() => TryTake(ref _api, maxApiRequests);

    internal bool TryTakeImage() => TryTake(ref _images, maxImageRequests);

    /// <summary>Platz einer Anfrage zurückgeben, die nie gesendet wurde (Abbruch beim Warten auf den Abstand).</summary>
    internal void ReturnApi() => Interlocked.Decrement(ref _api);

    internal void ReturnImage() => Interlocked.Decrement(ref _images);

    /// <summary>False, sobald die Summe <c>maxPreviewBytes</c> überschritte (dann wird nichts gezählt).</summary>
    internal bool TryAddPreviewBytes(long count)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _previewBytes);
            if (current + count > maxPreviewBytes)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _previewBytes, current + count, current) == current)
            {
                return true;
            }
        }
    }

    private static bool TryTake(ref int counter, int max)
    {
        while (true)
        {
            var current = Volatile.Read(ref counter);
            if (current >= max)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref counter, current + 1, current) == current)
            {
                return true;
            }
        }
    }
}

/// <summary>Zeitgrenzen eines Clients (§4.8 der Spezifikation zu E93). Tests verkürzen sie.</summary>
public sealed record SteamGridDbTimeouts(TimeSpan Headers, TimeSpan Idle, TimeSpan TotalJson, TimeSpan TotalPreview, TimeSpan TotalPicked)
{
    /// <summary>15 s bis zu den Kopfzeilen, 15 s ohne Daten, insgesamt 30 s (JSON, Vorschau) bzw. 90 s (volles Bild).</summary>
    public static SteamGridDbTimeouts Default { get; } = new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90));
}
