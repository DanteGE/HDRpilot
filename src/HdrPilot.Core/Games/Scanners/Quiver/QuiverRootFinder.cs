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
/// Ordner, ohne System- und Programmordner; sie endet beim ersten Treffer. Geprüft wird jeder Ordner nur an seinem
/// Kennzeichen (<see cref="QuiverRoot.Detect"/>), also mit zwei Dateiabfragen. Liest nur, schreibt nichts, kein Netz.
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

    /// <summary>Laufwerkspfade in einer Zeile des Protokolls: in Anführungszeichen (mit verdoppelten Backslashes) oder bis zum Zeilenende.</summary>
    private static readonly Regex LogPath = new(@"(?<![A-Za-z])[A-Za-z]:\\{1,2}[^""'\r\n<>|?*]*", RegexOptions.CultureInvariant);

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
                        next.AddRange(Directories(folder).Where(d => !IsSkipped(Path.GetFileName(d))));
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

    /// <summary>Kandidaten aus dem Text des Protokolls (siehe <see cref="VelopackLogFolders"/>).</summary>
    internal static IReadOnlyList<string> FoldersFromLog(string text)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in LogPath.Matches(text).Reverse())
        {
            var path = NormalizeFolder(match.Value.Replace(@"\\", @"\", StringComparison.Ordinal).TrimEnd(' ', '.', ',', ')', ';'));
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

        return result;
    }

    /// <summary>Grundordner der Suche ohne Hinweis: Downloads, Desktop, Dokumente, Benutzerordner, feste Laufwerke.</summary>
    private IEnumerable<string> SearchBases()
    {
        var bases = new List<string?>
        {
            DownloadsFolder(),
            environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        try
        {
            bases.AddRange(fileSystem.GetFixedDriveRoots());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug("Quiver: Laufwerke nicht lesbar: {Message}", ex.Message);
        }

        return bases.Select(NormalizeFolder).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);
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

    private List<string> Directories(string folder)
    {
        try
        {
            return fileSystem.EnumerateDirectories(folder).Select(PathUtil.Normalize).Order(StringComparer.OrdinalIgnoreCase).ToList();
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
