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

/// <summary>Was vor der Rückfrage darauf hindeutet, dass ein nicht installiertes Spiel doch noch installiert ist (E90).</summary>
public enum InstallSign
{
    /// <summary>Keine Spur: kein Programm mehr da. Ein Restordner mit Spielständen, Einstellungen oder Protokollen zählt nicht.</summary>
    None,

    /// <summary>
    /// Das Laufwerk des Installationsordners oder einer gemerkten EXE ist nicht verbunden (<see cref="InstallTrace.Drive"/>,
    /// etwa „F:“).
    /// </summary>
    DriveMissing,

    /// <summary>Eine EXE des Spiels ist noch da: eine gemerkte oder hinzugefügte oder eine im Installationsordner.</summary>
    Program,

    /// <summary>Steam führt das Spiel noch als installiert (<c>appmanifest_&lt;ID&gt;.acf</c> in seiner Bibliothek).</summary>
    SteamManifest,

    /// <summary>Nicht zu klären: Laufwerk, Netzwerkfreigabe oder Ordner antworten nicht rechtzeitig oder sind nicht lesbar.</summary>
    Unchecked,
}

/// <summary>
/// Spur einer Installation, geprüft vor der Rückfrage (E90). Alles außer <see cref="InstallSign.None"/> heißt „vielleicht noch
/// installiert“: ein getrenntes Laufwerk, ein Launcher mitten in einem Update oder einer, den das Einlesen nicht fand.
/// </summary>
/// <param name="Drive">Bei <see cref="InstallSign.DriveMissing"/> das Laufwerk, etwa „F:“.</param>
public sealed record InstallTrace(InstallSign Sign, string? Drive = null)
{
    public static readonly InstallTrace None = new(InstallSign.None);

    public static InstallTrace MissingDrive(string drive) => new(InstallSign.DriveMissing, drive);

    public bool MaybeInstalled => Sign != InstallSign.None;
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
/// (Laufwerk nicht verbunden, EXE noch da, bei Steam noch eingetragen oder nicht zu klären, <see cref="CheckInstallsAsync"/>).
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
    /// <summary>
    /// Länger wartet die Rückfrage nicht auf die Prüfung aller Spiele zusammen (ein getrenntes Netzlaufwerk antwortet langsam);
    /// was bis dahin nicht geklärt ist, gilt als <see cref="InstallSign.Unchecked"/>.
    /// </summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Höchstens so viele Zugriffe der Prüfung laufen gleichzeitig (jeder hält, solange er hängt, einen Thread).</summary>
    public const int MaxParallelChecks = 4;

    /// <summary>So viele Ordnerebenen tief sucht die Prüfung im Installationsordner eine EXE (Unreal: Spiel\Binaries\Win64).</summary>
    public const int ExecutableSearchDepth = 4;

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

    /// <summary>Spur einer Installation für ein Spiel (siehe <see cref="CheckInstallsAsync"/>).</summary>
    public async Task<InstallTrace> CheckInstallAsync(GameConfig game) => (await CheckInstallsAsync([game]).ConfigureAwait(false))[0];

    /// <summary>
    /// Spur einer Installation je Spiel, in derselben Reihenfolge (vor der Rückfrage, nur lesend, nur Namen). Als Spur zählt
    /// nur, was auf ein Programm deutet, nicht ein Restordner mit Spielständen oder Protokollen:
    /// <list type="number">
    /// <item>Das Laufwerk des Installationsordners ist nicht verbunden (<see cref="InstallSign.DriveMissing"/>); je Laufwerk
    /// und Netzwerkfreigabe wird einmal gefragt, Ordner darauf nur, wenn es antwortet.</item>
    /// <item>Eine gemerkte oder hinzugefügte EXE (voller Pfad) ist noch da, oder im Installationsordner liegt bis
    /// <see cref="ExecutableSearchDepth"/> Ebenen tief eine EXE, ohne verknüpften Ordnern zu folgen (<see cref="InstallSign.Program"/>).</item>
    /// <item>Bei Steam steht <c>appmanifest_&lt;ID&gt;.acf</c> noch in der Bibliothek (<see cref="InstallSign.SteamManifest"/>).</item>
    /// <item>Sonst: Eine gemerkte EXE liegt auf einem nicht verbundenen Laufwerk (<see cref="InstallSign.DriveMissing"/>).</item>
    /// </list>
    /// Was nicht antwortet (Laufwerk, Freigabe, Ordner) oder nicht lesbar ist, gilt als <see cref="InstallSign.Unchecked"/>,
    /// nicht als „keine Spur“: Ein getrenntes Netzlaufwerk soll die Warnung bekommen, ebenso ein Installationsordner, den
    /// Windows nicht auflisten lässt, oder eine gemerkte EXE in einem nicht lesbaren Ordner. Höchstens
    /// <see cref="MaxParallelChecks"/> Zugriffe gleichzeitig, alle zusammen höchstens <see cref="CheckTimeout"/> lang.
    /// </summary>
    public async Task<IReadOnlyList<InstallTrace>> CheckInstallsAsync(IReadOnlyList<GameConfig> games)
    {
        using var deadline = new CancellationTokenSource(CheckTimeout);
        var probes = new Probes(deadline.Token);
        var roots = new Dictionary<string, Task<bool?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in games.SelectMany(g => RememberedExecutables(g).Append(FullPath(g.InstallDir))).Select(RootOf).OfType<string>())
        {
            if (!roots.ContainsKey(root))
            {
                roots[root] = probes.AskAsync(() => fileSystem.DirectoryExists(root + "\\"));
            }
        }

        var traces = await Task.WhenAll(games.Select(g => CheckAsync(g, roots, probes))).ConfigureAwait(false);
        for (var i = 0; i < games.Count; i++)
        {
            if (traces[i].MaybeInstalled)
            {
                _logger.LogInformation("Vielleicht noch installiert: {Game} ({Key}): {Sign} {Drive}", games[i].Name, games[i].Key, traces[i].Sign, traces[i].Drive);
            }
        }

        return traces;
    }

    /// <summary>Spur eines Spiels; die Wurzeln (Laufwerke, Freigaben) sind schon angefragt.</summary>
    private async Task<InstallTrace> CheckAsync(GameConfig game, IReadOnlyDictionary<string, Task<bool?>> roots, Probes probes)
    {
        var installDir = FullPath(game.InstallDir);
        if (RootOf(installDir) is { } installRoot)
        {
            var answer = await roots[installRoot].ConfigureAwait(false);
            if (answer == false && installRoot.Length == 2)
            {
                return InstallTrace.MissingDrive(installRoot);
            }

            if (answer != true)
            {
                return new InstallTrace(InstallSign.Unchecked); // antwortet nicht, oder die Freigabe ist nicht erreichbar
            }
        }

        var unclear = false;
        string? missingDrive = null;
        async Task<bool> Found(string path, Func<bool> probe)
        {
            if (RootOf(path) is { } root)
            {
                // Eine gemerkte EXE auf einem anderen Laufwerk: Ist es nicht verbunden, ist sie vielleicht noch dort; antwortet
                // es nicht oder ist die Freigabe nicht erreichbar, ist es nicht zu klären.
                var answer = await roots[root].ConfigureAwait(false);
                if (answer == false && root.Length == 2)
                {
                    missingDrive ??= root;
                    return false;
                }

                if (answer != true)
                {
                    unclear = true;
                    return false;
                }
            }

            var found = await probes.AskAsync(probe).ConfigureAwait(false);
            unclear |= found is null;
            return found == true;
        }

        // Nicht lesbar heißt „nicht zu klären“, nicht „nicht da“: FileExistsOrThrow und EnumerateFilesUpTo werfen dann.
        foreach (var exe in RememberedExecutables(game))
        {
            if (await Found(exe, () => fileSystem.FileExistsOrThrow(exe)).ConfigureAwait(false))
            {
                return new InstallTrace(InstallSign.Program);
            }
        }

        if (installDir is not null && await Found(installDir, () => fileSystem.EnumerateFilesUpTo(installDir, "*.exe", ExecutableSearchDepth).Any()).ConfigureAwait(false))
        {
            return new InstallTrace(InstallSign.Program);
        }

        if (SteamManifestOf(game, installDir) is { } manifest && await Found(manifest, () => fileSystem.FileExistsOrThrow(manifest)).ConfigureAwait(false))
        {
            return new InstallTrace(InstallSign.SteamManifest);
        }

        return missingDrive is not null ? InstallTrace.MissingDrive(missingDrive)
            : unclear ? new InstallTrace(InstallSign.Unchecked)
            : InstallTrace.None;
    }

    /// <summary>Gemerkte und hinzugefügte EXEs mit vollem Pfad (ein bloßer Dateiname sagt nichts über die Installation).</summary>
    private static IEnumerable<string> RememberedExecutables(GameConfig game) =>
        game.DetectedExecutables.Concat(game.ExtraExecutables).Select(FullPath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Der Pfad, wenn er vollständig ist (mit Laufwerk oder UNC), sonst null.</summary>
    private static string? FullPath(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? path : null;

    /// <summary>„F:“ für einen Pfad mit Laufwerksbuchstaben, „\\server\freigabe“ für einen UNC-Pfad, sonst null.</summary>
    private static string? RootOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/')
        {
            return char.ToUpperInvariant(path[0]) + ":";
        }

        // \\server\freigabe\…, nicht \\?\ oder \\.\ (dort entscheidet das Laufwerk dahinter, das prüft dann der Zugriff selbst)
        var parts = path.Replace('/', '\\').Split('\\');
        return path.Length > 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/' && parts.Length >= 4 && parts[2] is { Length: > 0 } server
            && server is not ("?" or ".") && parts[3].Length > 0
                ? @"\\" + server + @"\" + parts[3]
                : null;
    }

    /// <summary>
    /// <c>&lt;Bibliothek&gt;\steamapps\appmanifest_&lt;ID&gt;.acf</c> eines Steam-Spiels, dessen Ordner unter
    /// <c>steamapps\common</c> liegt (wie sie der Steam-Scanner einträgt); sonst null. Nur Ziffern als ID, nie ein Pfad.
    /// </summary>
    private static string? SteamManifestOf(GameConfig game, string? installDir)
    {
        if (game.Source != GameSource.Steam || installDir is null || game.SourceId.Length == 0 || !game.SourceId.All(char.IsAsciiDigit))
        {
            return null;
        }

        const string Common = @"\steamapps\common\";
        var at = installDir.Replace('/', '\\').LastIndexOf(Common, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : Path.Combine(installDir[..(at + @"\steamapps".Length)], "appmanifest_" + game.SourceId + ".acf");
    }

    /// <summary>
    /// Zugriffe der Prüfung mit einer gemeinsamen Frist und höchstens <see cref="MaxParallelChecks"/> zugleich. Ein Zugriff,
    /// der hängt (getrenntes Netzlaufwerk), hält seinen Platz, bis Windows antwortet; nach der Frist beginnt keiner mehr.
    /// </summary>
    private sealed class Probes(CancellationToken deadline)
    {
        private readonly SemaphoreSlim _slots = new(MaxParallelChecks);
        private readonly Task _expired = Task.Delay(Timeout.InfiniteTimeSpan, deadline);

        /// <summary>Antwort des Zugriffs; null: keine Antwort bis zur Frist oder nicht lesbar.</summary>
        public async Task<bool?> AskAsync(Func<bool> probe)
        {
            try
            {
                await _slots.WaitAsync(deadline).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            var check = Task.Run(() =>
            {
                try
                {
                    return (bool?)probe();
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or ArgumentException or NotSupportedException)
                {
                    return false; // nicht (mehr) da, oder kein gültiger Pfad: dort liegt nichts
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    return null; // nicht lesbar: nicht zu klären
                }
                finally
                {
                    _slots.Release();
                }
            });
            return await Task.WhenAny(check, _expired).ConfigureAwait(false) == check ? check.Result : null;
        }
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
