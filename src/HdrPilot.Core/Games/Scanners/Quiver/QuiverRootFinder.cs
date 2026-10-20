using System.Text;
using System.Text.RegularExpressions;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.Games.Scanners.Quiver;

/// <summary>Ein gefundener Quiver-Ordner und woher HDRpilot ihn kennt (für das Protokoll).</summary>
internal sealed record QuiverRootHit(string Folder, QuiverLayout Layout, string Origin);

/// <summary>
/// Sucht den Ordner des Quiver Launchers (E91). Quiver ist portabel und hinterlässt keinen Registry-Eintrag; gesucht wird
/// deshalb in dieser Reihenfolge, die günstigsten Quellen zuerst:
/// <list type="number">
/// <item>gelernte und genannte Ordner (<see cref="ScanOptions.QuiverFolders"/>, versteckte Einstellung <c>quiverFolders</c>);</item>
/// <item>das Protokoll von Velopack <c>%LOCALAPPDATA%\velopack\velopack_QuiverLauncher.log</c> (nur das Ende, geteilt
/// gelesen; jeder darin genannte Pfad und seine zwei übergeordneten Ordner, die jüngsten zuerst);</item>
/// <item>die Suchordner des Nutzers und ihre direkten Unterordner;</item>
/// <item><c>%LOCALAPPDATA%\QuiverLauncher</c> (Standardordner eines Velopack-Installers) und ein Eintrag
/// <c>HKCU\…\Uninstall\QuiverLauncher</c> (InstallLocation), falls es je einen Installer gibt.</item>
/// </list>
/// Erst wenn das nichts findet, folgt eine begrenzte Suche: Downloads (bekannter Ordner aus der Registry), Desktop,
/// Dokumente, Benutzerordner und die festen Laufwerke, je bis zwei Ebenen tief, höchstens <see cref="MaxSearchedFolders"/>
/// Ordner, ohne System- und Programmordner; sie endet beim ersten Treffer. Sie bleibt auf festen Laufwerken (ein umgeleiteter
/// Ordner auf einer Netzfreigabe oder einem Wechseldatenträger fällt heraus) und folgt keinem verknüpften Ordner (Junction,
/// symbolische Verknüpfung), damit sie nie über eine Verknüpfung auf eine Netzfreigabe gerät. Geprüft wird jeder Ordner nur
/// an seinem Kennzeichen (<see cref="QuiverRoot.Detect"/>), also mit zwei Dateiabfragen. Liest nur, schreibt nichts, kein Netz.
/// </summary>
internal sealed class QuiverRootFinder(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger logger)
{
    /// <summary>Höchstens so viele Ordner prüft die Suche ohne Hinweis (alle Grundordner zusammen).</summary>
    public const int MaxSearchedFolders = 1500;

    /// <summary>Höchstens so viele Unterordner je Suchordner.</summary>
    public const int MaxSearchFolderChildren = 400;

    /// <summary>Vom Protokoll wird höchstens das Ende gelesen (es wächst mit jedem Start von Quiver).</summary>
    public const int MaxLogBytes = 256 * 1024;

    /// <summary>Höchstens so viele Ordner aus dem Protokoll.</summary>
    public const int MaxLogCandidates = 32;

    public const string VelopackLogFolder = "velopack";

    public const string VelopackLogFile = "velopack_QuiverLauncher.log";

    internal const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\QuiverLauncher";

    internal const string UserShellFoldersKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

    /// <summary>Bekannter Ordner „Downloads“ (FOLDERID_Downloads) als Wertname unter User Shell Folders.</summary>
    internal const string DownloadsValueName = "{374DE290-123F-4565-9164-39C4925E467B}";

    /// <summary>
    /// Laufwerkspfad in einfachen Anführungszeichen, bis zum letzten ' der Zeile (ein Pfad wie C:\Users\O'Brien enthält
    /// selbst eins; Velopack setzt je Zeile höchstens einen solchen Pfad).
    /// </summary>
    private static readonly Regex SingleQuotedPath = new(@"'(?<path>[A-Za-z]:\\[^\r\n]*)'", RegexOptions.CultureInvariant);

    /// <summary>
    /// Übriger Laufwerkspfad, auch in doppelten Anführungszeichen (Velopack verdoppelt dort die Backslashes): bis zum
    /// Zeilenende, bis vor ein " oder bis vor den nächsten Laufwerkspfad; ein ' darf darin stehen.
    /// </summary>
    private static readonly Regex BarePath = new(@"(?<![A-Za-z])[A-Za-z]:\\(?:(?! [A-Za-z]:\\)[^""\r\n<>|?*])*", RegexOptions.CultureInvariant);

    /// <summary>Ordner, in die die Suche ohne Hinweis nicht hinabsteigt (Windows, Programme, große Datenordner, Verknüpfungen).</summary>
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "System Volume Information", "Recovery", "PerfLogs",
        "AppData", "WindowsApps", "Documents and Settings", "node_modules", "steamapps", "$Recycle.Bin",
    };

    public IReadOnlyList<QuiverRootHit> Find(ScanOptions options, CancellationToken cancellationToken)
    {
        var hits = new List<QuiverRootHit>();
        var probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var budget = MaxSearchedFolders;

        bool Probe(string? raw, string origin)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = NormalizeFolder(raw);
            if (folder is null || !probed.Add(folder))
            {
                return false;
            }

            var layout = QuiverRoot.Detect(fileSystem, folder);
            if (layout == QuiverLayout.None)
            {
                return false;
            }

            logger.LogInformation("Quiver: Ordner {Folder} gefunden ({Origin}, Aufbau {Layout}).", folder, origin, layout);
            hits.Add(new QuiverRootHit(folder, layout, origin));
            return true;
        }

        foreach (var folder in options.QuiverFolders)
        {
            Probe(folder, "gelernt");
        }

        foreach (var folder in VelopackLogFolders())
        {
            Probe(folder, "Velopack-Protokoll");
        }

        foreach (var searchFolder in options.SearchFolders)
        {
            Probe(searchFolder, "Suchordner");
            if (NormalizeFolder(searchFolder) is { } root)
            {
                foreach (var child in Directories(root).Take(MaxSearchFolderChildren))
                {
                    Probe(child, "Suchordner");
                }
            }
        }

        var localAppData = environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            Probe(Path.Combine(localAppData, QuiverRoot.AppId), "LOCALAPPDATA");
        }

        Probe(UninstallLocation(), "Uninstall-Eintrag");
        if (hits.Count > 0)
        {
            return hits;
        }

        // Ohne Hinweis: begrenzte Suche bis zwei Ebenen tief, beim ersten Treffer Schluss. Jeder Ordner wird einmal geprüft und
        // einmal aufgelistet, auch wenn er unter mehreren Grundordnern liegt (Downloads im Benutzerordner).
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseFolder in SearchBases())
        {
            var level = new List<string> { baseFolder };
            for (var depth = 0; depth <= 2 && level.Count > 0; depth++)
            {
                var next = new List<string>();
                foreach (var folder in level)
                {
                    if (!probed.Contains(folder))
                    {
                        if (budget-- <= 0)
                        {
                            logger.LogDebug("Quiver: Suche nach {Count} Ordnern beendet, kein Quiver-Ordner gefunden.", MaxSearchedFolders);
                            return hits;
                        }

                        if (Probe(folder, "Suche"))
                        {
                            return hits;
                        }
                    }

                    if (depth < 2 && expanded.Add(folder))
                    {
                        next.AddRange(Directories(folder, withoutLinks: true).Where(d => !IsSkipped(Path.GetFileName(d))));
                    }
                }

                level = next;
            }
        }

        logger.LogDebug("Quiver: kein Quiver-Ordner gefunden ({Count} Ordner geprüft).", MaxSearchedFolders - Math.Max(budget, 0));
        return hits;
    }

    /// <summary>
    /// Ordner aus dem Protokoll von Velopack: jeder genannte Laufwerkspfad (verdoppelte Backslashes einfach) samt seinen
    /// zwei übergeordneten Ordnern, die jüngsten Zeilen zuerst, höchstens <see cref="MaxLogCandidates"/>. Fehlt das
    /// Protokoll oder ist es nicht lesbar: keine.
    /// </summary>
    internal IReadOnlyList<string> VelopackLogFolders()
    {
        var localAppData = environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return [];
        }

        var path = Path.Combine(localAppData, VelopackLogFolder, VelopackLogFile);
        string text;
        try
        {
            if (!fileSystem.FileExists(path))
            {
                return [];
            }

            text = Encoding.UTF8.GetString(fileSystem.ReadShared(path, MaxLogBytes, fromEnd: true).Bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            logger.LogDebug("Quiver: Velopack-Protokoll {Path} nicht lesbar: {Message}", path, ex.Message);
            return [];
        }

        return FoldersFromLog(text);
    }

    /// <summary>
    /// Kandidaten aus dem Text des Protokolls (siehe <see cref="VelopackLogFolders"/>): je Zeile, die jüngste zuerst, erst
    /// die Pfade in einfachen Anführungszeichen, dann die übrigen; jeder Fund wird in der Zeile gelöscht, bevor die nächste
    /// Art gesucht wird. So bleibt ein ' im Pfad erhalten, und zwei Pfade einer Zeile werden nicht eins.
    /// </summary>
    internal static IReadOnlyList<string> FoldersFromLog(string text)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n').Reverse())
        {
            var rest = line;
            var found = new List<(int Index, string Path)>();
            foreach (var pattern in new[] { SingleQuotedPath, BarePath })
            {
                foreach (Match match in pattern.Matches(rest))
                {
                    var group = match.Groups["path"].Success ? match.Groups["path"] : match.Groups[0];
                    found.Add((group.Index, group.Value));
                }

                rest = pattern.Replace(rest, m => new string(' ', m.Length));
            }

            // In der Zeile hinten zuerst, wie die Zeilen selbst.
            foreach (var (_, raw) in found.OrderByDescending(f => f.Index))
            {
                var path = NormalizeFolder(raw.Replace(@"\\", @"\", StringComparison.Ordinal).TrimEnd(' ', '.', ',', ')', ';'));
                for (var i = 0; i < 3 && path is not null; i++)
                {
                    if (seen.Add(path))
                    {
                        result.Add(path);
                        if (result.Count >= MaxLogCandidates)
                        {
                            return result;
                        }
                    }

                    path = Path.GetDirectoryName(path) is { } parent && parent.Length > 3 ? parent : null;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Grundordner der Suche ohne Hinweis: Downloads, Desktop, Dokumente, Benutzerordner, feste Laufwerke; nur solche auf
    /// einem festen Laufwerk (ein auf eine Netzfreigabe umgeleiteter Ordner fällt heraus).
    /// </summary>
    private IEnumerable<string> SearchBases()
    {
        IReadOnlyList<string> drives;
        try
        {
            drives = fileSystem.GetFixedDriveRoots();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug("Quiver: Laufwerke nicht lesbar: {Message}", ex.Message);
            drives = [];
        }

        var fixedRoots = new HashSet<string>(drives.Select(PathUtil.Normalize), StringComparer.OrdinalIgnoreCase);
        var bases = new List<string?>
        {
            DownloadsFolder(),
            environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        bases.AddRange(drives);
        return bases.Select(NormalizeFolder)
            .OfType<string>()
            .Where(b => Path.GetPathRoot(b) is { } root && fixedRoots.Contains(PathUtil.Normalize(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ordner „Downloads“: der bekannte Ordner aus <c>HKCU\…\Explorer\User Shell Folders</c> (der Nutzer kann ihn
    /// verschieben, etwa nach D:\Downloads), sonst <c>%USERPROFILE%\Downloads</c>.
    /// </summary>
    internal string? DownloadsFolder()
    {
        using (var key = registry.OpenKey(RegistryHiveKind.CurrentUser, UserShellFoldersKeyPath))
        {
            if (key?.GetString(DownloadsValueName) is { Length: > 0 } raw && NormalizeFolder(environment.ExpandEnvironmentVariables(raw)) is { } fromRegistry)
            {
                return fromRegistry;
            }
        }

        var profile = environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? null : Path.Combine(profile, "Downloads");
    }

    private string? UninstallLocation()
    {
        using var key = registry.OpenKey(RegistryHiveKind.CurrentUser, UninstallKeyPath);
        return key?.GetString("InstallLocation");
    }

    /// <summary>Unterordner, sortiert; mit <paramref name="withoutLinks"/> ohne verknüpfte Ordner (Suche ohne Hinweis).</summary>
    private List<string> Directories(string folder, bool withoutLinks = false)
    {
        try
        {
            var children = withoutLinks ? fileSystem.EnumerateDirectoriesWithoutLinks(folder) : fileSystem.EnumerateDirectories(folder);
            return children.Select(PathUtil.Normalize).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            logger.LogDebug("Quiver: Ordner {Folder} nicht lesbar: {Message}", folder, ex.Message);
            return [];
        }
    }

    private static bool IsSkipped(string name) => name.StartsWith('$') || name.StartsWith('.') || SkippedFolders.Contains(name);

    /// <summary>Absoluter, vereinheitlichter Ordner; leer, relativ oder ungültig: null.</summary>
    private static string? NormalizeFolder(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var folder = PathUtil.Normalize(raw);
        try
        {
            return folder.Length > 0 && folder.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(folder) ? folder : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
