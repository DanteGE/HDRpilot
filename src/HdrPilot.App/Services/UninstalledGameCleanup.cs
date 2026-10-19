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
/// Spuren einer Installation, geprüft vor der Rückfrage (E90): Das Laufwerk des Installationsordners ist nicht verbunden
/// (<see cref="MissingDrive"/>, etwa „F:“), oder der Ordner ist noch da und nicht leer (<see cref="FolderPresent"/>). Beides
/// heißt „vielleicht noch installiert“: ein getrenntes Laufwerk, ein Launcher mitten in einem Update oder einer, den das
/// Einlesen nicht fand. <see cref="None"/>: keine Spur (oder nicht rechtzeitig zu klären).
/// </summary>
public sealed record InstallTrace(string? MissingDrive, bool FolderPresent)
{
    public static readonly InstallTrace None = new(null, false);

    public bool MaybeInstalled => MissingDrive is not null || FolderPresent;
}

/// <summary>
/// Aufräumen nach „Einstellungen entfernen…“ (E90), nachdem der Eintrag aus der Konfiguration genommen ist
/// (<see cref="LibraryCleanup.RemoveNotInstalled"/>): eigenes Symbol (die PNG mit dem Namen dieses Spiels im Ordner icons),
/// die gemerkte Symbolwahl in icon-cache.json, eigene Bilder (art\custom, nur mit dem Stamm dieses Spiels) und, nur wenn
/// gewünscht, die genau passenden HDRpilot-Verknüpfungen samt ihrer .ico (nur die eigene des Spiels im Ordner icons).
/// Gelöscht wird nur über Pfade, die aus dem Schlüssel folgen, nie ein Pfad aus der Konfiguration: Spiel-, Launcher- und
/// Steam-Dateien sind nie darunter; eine Datei, die ein verbliebener Eintrag noch nennt (von Hand geänderte config.json),
/// bleibt. Erzeugte Bilder, helle Fassungen und Einträge im Bildverzeichnis sind nur Zwischenspeicher; sie räumt der
/// Bilddienst nach dem Schließen des Fensters auf (<see cref="ArtworkService.PruneRemovedGames"/>).
/// Dazu die Prüfungen vor der Rückfrage: welche Verknüpfungen es gibt und ob das Spiel vielleicht noch installiert ist
/// (Laufwerk nicht verbunden oder Ordner noch da, <see cref="CheckInstallsAsync"/>).
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

        return await ExistsWithinAsync(drive + "\\").ConfigureAwait(false) == true ? null : drive;
    }

    /// <summary>Spuren einer Installation für ein Spiel (siehe <see cref="CheckInstallsAsync"/>).</summary>
    public async Task<InstallTrace> CheckInstallAsync(string? installDir) => (await CheckInstallsAsync([installDir]).ConfigureAwait(false))[0];

    /// <summary>
    /// Spuren einer Installation je Installationsordner, in derselben Reihenfolge (vor der Rückfrage): je Laufwerk einmal, ob
    /// es verbunden ist (wie <see cref="MissingDriveAsync"/>), und nur auf einem verbundenen (oder bei UNC-Pfaden), ob der
    /// Ordner noch da und nicht leer ist. Die Laufwerke gleichzeitig, jede Prüfung höchstens <see cref="DriveCheckTimeout"/>
    /// lang; ein Ordner, der nicht rechtzeitig antwortet, gilt als ohne Spur. Ordner auf einem nicht verbundenen Laufwerk
    /// fragt sie gar nicht erst an (jede Anfrage hielte dort einen Thread fest).
    /// </summary>
    public async Task<IReadOnlyList<InstallTrace>> CheckInstallsAsync(IReadOnlyList<string?> installDirs)
    {
        var traces = Enumerable.Repeat(InstallTrace.None, installDirs.Count).ToArray();
        var groups = Enumerable.Range(0, installDirs.Count)
            .Where(i => !string.IsNullOrWhiteSpace(installDirs[i]) && Path.IsPathFullyQualified(installDirs[i]!))
            .GroupBy(i => DriveOf(installDirs[i]) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await Task.WhenAll(groups.Select(async group =>
        {
            if (group.Key.Length > 0 && await ExistsWithinAsync(group.Key + "\\").ConfigureAwait(false) != true)
            {
                foreach (var i in group)
                {
                    traces[i] = new InstallTrace(group.Key, false);
                }

                return;
            }

            var indices = group.ToList();
            var present = await Task.WhenAll(indices.Select(i => HasContentWithinAsync(installDirs[i]!))).ConfigureAwait(false);
            for (var n = 0; n < indices.Count; n++)
            {
                if (present[n] == true)
                {
                    traces[indices[n]] = new InstallTrace(null, true);
                }
            }
        })).ConfigureAwait(false);
        return traces;
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

    /// <summary>Gibt es den Ordner? null, wenn die Antwort nicht innerhalb von <see cref="DriveCheckTimeout"/> kommt.</summary>
    private Task<bool?> ExistsWithinAsync(string path) => WithinAsync(() => fileSystem.DirectoryExists(path));

    /// <summary>Gibt es den Ordner, und liegt etwas darin? null, wenn die Antwort nicht rechtzeitig kommt.</summary>
    private Task<bool?> HasContentWithinAsync(string path) => WithinAsync(() =>
        fileSystem.DirectoryExists(path) && (fileSystem.EnumerateFiles(path).Any() || fileSystem.EnumerateDirectories(path).Any()));

    private static async Task<bool?> WithinAsync(Func<bool> probe)
    {
        var check = Task.Run(() =>
        {
            try
            {
                return probe();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                return false;
            }
        });
        var finished = await Task.WhenAny(check, Task.Delay(DriveCheckTimeout)).ConfigureAwait(false);
        return ReferenceEquals(finished, check) ? check.Result : null;
    }

    /// <summary>
    /// Nach dem Entfernen des Eintrags: Symbol im Speicher und in icon-cache.json vergessen, eigene PNG und eigene Bilder
    /// dieses Spiels löschen (außer einer Datei, die ein Eintrag in <paramref name="remaining"/> noch nennt), Bilder im
    /// Speicher vergessen; mit <paramref name="shortcutsToDelete"/> diese Verknüpfungen (beim Löschen erneut geprüft) und
    /// danach die eigene .ico des Spiels, wenn eine gelöschte Verknüpfung sie zeigte und keine andere Verknüpfung mehr
    /// (<see cref="IShortcutService.IsIconInUse"/>, auch in Unterordnern und in der Taskleiste). Nicht auf dem UI-Thread;
    /// wirft nicht.
    /// </summary>
    /// <param name="remaining">Die Konfiguration nach dem Entfernen.</param>
    public CleanupResult DeleteData(GameConfig removed, IReadOnlyCollection<GameShortcut> shortcutsToDelete, AppConfig remaining)
    {
        var key = removed.Key;
        var deleted = new List<string>();
        var failed = new List<string>();
        var (iconsInUse, imagesInUse) = FilesInUse(remaining);
        void Delete(string path, HashSet<string>? inUse = null)
        {
            if (inUse?.Contains(Path.GetFileName(path)) == true)
            {
                _logger.LogWarning("Datei von {Game} bleibt, ein anderer Eintrag nennt sie: {Path}", key, path);
                return;
            }

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
        Delete(Path.Combine(iconDirectory, ownIcon), iconsInUse);
        foreach (var kind in new[] { ArtworkKind.Hero, ArtworkKind.Logo, ArtworkKind.Cover })
        {
            Delete(Path.Combine(customArtDirectory, ArtworkFiles.CustomFileName(key, kind)), imagesInUse);
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

            // Die .ico entstand nur für die Verknüpfungen (neben der PNG des eigenen Symbols): weg, wenn eine gelöschte sie
            // zeigte und keine andere Verknüpfung mehr, auch keine verbliebene, keine in einem Unterordner und keine angeheftete.
            var ownIco = Path.Combine(iconDirectory, Path.ChangeExtension(ownIcon, ".ico"));
            if (links.Any(l => PathUtil.PathEquals(l.IconPath, ownIco)))
            {
                if (kept.Any(s => PathUtil.PathEquals(s.IconPath, ownIco)) || IconInUse(ownIco))
                {
                    _logger.LogInformation("Symboldatei von {Game} bleibt, eine andere Verknüpfung zeigt sie: {Path}", key, ownIco);
                }
                else
                {
                    Delete(ownIco);
                }
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

    /// <summary>
    /// Eigene Symbole (Ordner icons) und eigene Bilder (art\custom), die Einträge der Konfiguration nennen: Nennt ein
    /// verbliebener Eintrag die Datei des entfernten Spiels (nur in einer von Hand geänderten config.json), bleibt sie.
    /// </summary>
    private static (HashSet<string> Icons, HashSet<string> Images) FilesInUse(AppConfig remaining)
    {
        var iconNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in remaining.Games)
        {
            if (CustomIcons.IsValidFileName(game.CustomIcon))
            {
                iconNames.Add(game.CustomIcon!);
            }

            foreach (var token in new[] { game.Artwork?.Hero, game.Artwork?.Logo, game.Artwork?.Cover })
            {
                if (ArtworkOverride.KindOf(token) == ArtworkOverrideKind.Custom && ArtworkOverride.FileOf(token) is { } file)
                {
                    imageNames.Add(file);
                }
            }
        }

        return (iconNames, imageNames);
    }

    /// <summary>Zeigt noch eine Verknüpfung die Symboldatei? Lässt es sich nicht klären, gilt sie als benutzt (bleibt).</summary>
    private bool IconInUse(string iconPath)
    {
        try
        {
            return shortcuts.IsIconInUse(iconPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning("Verknüpfungen mit {Path} nicht lesbar, die Symboldatei bleibt: {Message}", iconPath, ex.Message);
            return true;
        }
    }
}
