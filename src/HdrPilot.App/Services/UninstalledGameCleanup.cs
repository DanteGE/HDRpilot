using System.IO;
using HdrPilot.App.Services.Artwork;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.App.Services;

/// <summary>Was nach dem Entfernen eines Eintrags gelöscht wurde und was sich nicht löschen ließ (volle Pfade).</summary>
public sealed record CleanupResult(IReadOnlyList<string> Deleted, IReadOnlyList<string> Failed, int DeletedShortcuts);

/// <summary>
/// Aufräumen nach „Einstellungen entfernen…“ (E90), nachdem der Eintrag aus der Konfiguration genommen ist
/// (<see cref="LibraryCleanup.RemoveNotInstalled"/>): eigenes Symbol (die PNG mit dem Namen dieses Spiels im Ordner icons),
/// die gemerkte Symbolwahl in icon-cache.json, eigene Bilder (art\custom, nur mit dem Stamm dieses Spiels) und, nur wenn
/// gewünscht, die genau passenden HDRpilot-Verknüpfungen samt ihrer .ico (nur die eigene des Spiels im Ordner icons).
/// Gelöscht wird nur über Pfade, die aus dem Schlüssel folgen, nie ein Pfad aus der Konfiguration: Spiel-, Launcher- und
/// Steam-Dateien sind nie darunter. Erzeugte Bilder, helle Fassungen und Einträge im Bildverzeichnis sind nur
/// Zwischenspeicher; sie räumt der Bilddienst nach dem Schließen des Fensters auf (<see cref="ArtworkService.PruneRemovedGames"/>).
/// Dazu die Prüfungen vor der Rückfrage: welche Verknüpfungen es gibt und ob das Laufwerk des Spiels verbunden ist.
/// </summary>
public sealed class UninstalledGameCleanup(
    IFileSystem fileSystem,
    string iconDirectory,
    string customArtDirectory,
    IGameIconService icons,
    IArtworkService artwork,
    IShortcutService shortcuts,
    ILogger? logger = null)
{
    /// <summary>Länger wartet die Rückfrage nicht auf ein Laufwerk (ein getrenntes Netzlaufwerk antwortet langsam).</summary>
    public static readonly TimeSpan DriveCheckTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>HDRpilot-Verknüpfungen des Spiels (nur lesen, nicht auf dem UI-Thread); nicht lesbar: keine.</summary>
    public IReadOnlyList<GameShortcut> FindShortcuts(string gameKey)
    {
        try
        {
            return shortcuts.FindFor(gameKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning("Verknüpfungen von {Game} nicht lesbar: {Message}", gameKey, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Laufwerk des Installationsordners, wenn es nicht verbunden ist („F:“), sonst null. Nur Laufwerksbuchstaben (ein
    /// UNC-Pfad hat keinen); antwortet es nicht innerhalb von <see cref="DriveCheckTimeout"/>, gilt es als nicht verbunden.
    /// </summary>
    public async Task<string?> MissingDriveAsync(string? installDir)
    {
        if (DriveOf(installDir) is not { } drive)
        {
            return null;
        }

        var root = drive + "\\";
        var check = Task.Run(() =>
        {
            try
            {
                return fileSystem.DirectoryExists(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        });
        var finished = await Task.WhenAny(check, Task.Delay(DriveCheckTimeout)).ConfigureAwait(false);
        return ReferenceEquals(finished, check) && check.Result ? null : drive;
    }

    /// <summary>„F:“ für einen Pfad mit Laufwerksbuchstaben, sonst null.</summary>
    public static string? DriveOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/'))
        {
            return null;
        }

        return char.ToUpperInvariant(path[0]) + ":";
    }

    /// <summary>
    /// Nach dem Entfernen des Eintrags: Symbol im Speicher und in icon-cache.json vergessen, eigene PNG und eigene Bilder
    /// dieses Spiels löschen, Bilder im Speicher vergessen; mit <paramref name="shortcutsToDelete"/> diese Verknüpfungen
    /// (beim Löschen erneut geprüft) und danach die eigene .ico des Spiels, wenn eine gelöschte Verknüpfung sie zeigte und
    /// keine verbliebene mehr. Nicht auf dem UI-Thread; wirft nicht.
    /// </summary>
    public CleanupResult DeleteData(GameConfig removed, IReadOnlyCollection<GameShortcut> shortcutsToDelete)
    {
        var key = removed.Key;
        var deleted = new List<string>();
        var failed = new List<string>();
        void Delete(string path)
        {
            try
            {
                if (fileSystem.FileExists(path))
                {
                    fileSystem.DeleteFile(path);
                    deleted.Add(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(path);
                _logger.LogWarning("Datei von {Game} nicht gelöscht: {Path} ({Message})", key, path, ex.Message);
            }
        }

        icons.ForgetGame(key);
        var ownIcon = CustomIcons.FileNameFor(key);
        Delete(Path.Combine(iconDirectory, ownIcon));
        foreach (var kind in new[] { ArtworkKind.Hero, ArtworkKind.Logo, ArtworkKind.Cover })
        {
            Delete(Path.Combine(customArtDirectory, ArtworkFiles.CustomFileName(key, kind)));
        }

        artwork.Forget(key);

        var deletedShortcuts = 0;
        if (shortcutsToDelete.Count > 0)
        {
            var links = shortcuts.Delete(key, shortcutsToDelete);
            deletedShortcuts = links.Count;
            deleted.AddRange(links.Select(l => l.Path));
            var kept = shortcutsToDelete.Where(s => !links.Any(l => PathUtil.PathEquals(l.Path, s.Path))).ToList();
            failed.AddRange(kept.Select(s => s.Path));
            foreach (var link in kept)
            {
                _logger.LogWarning("Verknüpfung von {Game} nicht gelöscht (gesperrt, geändert oder nicht mehr da): {Path}", key, link.Path);
            }

            // Die .ico entstand nur für die Verknüpfungen (neben der PNG des eigenen Symbols): weg, wenn keine mehr sie zeigt.
            var ownIco = Path.Combine(iconDirectory, Path.ChangeExtension(ownIcon, ".ico"));
            if (links.Any(l => PathUtil.PathEquals(l.IconPath, ownIco)) && !kept.Any(s => PathUtil.PathEquals(s.IconPath, ownIco)))
            {
                Delete(ownIco);
            }
        }

        _logger.LogInformation(
            "Einstellungen von {Game} ({Key}) entfernt: {Deleted} Dateien gelöscht ({Shortcuts} Verknüpfungen), {Failed} nicht löschbar.",
            removed.Name, key, deleted.Count, deletedShortcuts, failed.Count);
        foreach (var path in deleted)
        {
            _logger.LogInformation("Gelöscht: {Path}", path);
        }

        return new CleanupResult(deleted, failed, deletedShortcuts);
    }
}
