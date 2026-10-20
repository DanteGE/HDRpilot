using HdrPilot.Core.Games;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Artwork;

/// <summary>
/// Ordnet eine geänderte Datei in einem beobachteten Bildcache den betroffenen Spielen zu (E89, neue Bilder erscheinen von
/// selbst): Steam <c>librarycache\&lt;appid&gt;\…</c> → Steam:&lt;appid&gt;, GOG Galaxy <c>webcache\&lt;Benutzer&gt;\gog\&lt;ID&gt;\…</c>
/// → Gog:&lt;ID&gt;, Ubisoft <c>cache\assets\&lt;Datei&gt;</c> → die Produkte, deren Produkt-Cache die Datei nennt. Dateien ohne
/// Zuordnung (etwa Steams assetcache.vdf) betreffen kein Spiel.
/// </summary>
public static class ArtworkWatchMap
{
    /// <param name="ubisoftOwners">Ubisoft-Bilddatei → Produkt-IDs (<see cref="ArtworkLocator.UbisoftAssetOwners"/>).</param>
    public static IReadOnlyList<string> GameKeysFor(string changedPath, ArtworkRoots roots, IReadOnlyDictionary<string, IReadOnlyList<string>>? ubisoftOwners = null)
    {
        if (string.IsNullOrWhiteSpace(changedPath))
        {
            return [];
        }

        var path = PathUtil.Normalize(changedPath);
        if (roots.SteamLibraryCache is { } steam && Relative(path, steam) is { Length: >= 1 } steamParts && steamParts[0].All(char.IsAsciiDigit))
        {
            return [GameKey.Create(GameSource.Steam, steamParts[0])];
        }

        if (roots.GogWebCache is { } gog && Relative(path, gog) is { Length: >= 3 } gogParts
            && string.Equals(gogParts[1], "gog", StringComparison.OrdinalIgnoreCase) && gogParts[2].All(char.IsAsciiDigit))
        {
            return [GameKey.Create(GameSource.Gog, gogParts[2])];
        }

        if (roots.UbisoftAssets is { } assets && Relative(path, assets) is { Length: 1 } ubiParts
            && ubisoftOwners is not null && ubisoftOwners.TryGetValue(ubiParts[0], out var owners))
        {
            return owners.Select(id => GameKey.Create(GameSource.Ubisoft, id)).ToList();
        }

        return [];
    }

    /// <summary>Teile des Pfads unter <paramref name="root"/>; null, wenn er nicht darunter liegt.</summary>
    private static string[]? Relative(string path, string root)
    {
        var normalizedRoot = PathUtil.Normalize(root);
        if (!PathUtil.IsUnder(path, normalizedRoot) || PathUtil.PathEquals(path, normalizedRoot))
        {
            return null;
        }

        return path[normalizedRoot.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }
}
