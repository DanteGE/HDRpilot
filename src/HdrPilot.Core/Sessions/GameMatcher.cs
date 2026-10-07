using HdrPilot.Core.Config;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Sessions;

/// <summary>
/// Ordnet Prozesse Spielen zu. Treffer ist jeder Prozess, dessen EXE-Pfad unter dem Installationsordner eines
/// Spiels liegt (bei verschachtelten Ordnern gewinnt der genaueste, bei gleichem Ordner ein ausgewähltes und dann
/// ein installiertes Spiel), außer Hilfsprozessen (globale Ausschlussliste) und vom Nutzer je Spiel ausgeschlossenen
/// EXEs. Genau bekannte EXE-Pfade (gemerkt, vom Scanner genannt, vom Nutzer ergänzt) zählen immer, auch wenn ihr
/// Ordner als Installationsordner zu weit wäre. Ist der Pfad nicht lesbar, wird über den EXE-Namen gegen diese EXEs
/// abgeglichen, aber nur bei eindeutigem Treffer.
/// </summary>
public sealed class GameMatcher
{
    private readonly List<Entry> _entries;
    private readonly Func<string, bool> _isHelper;

    /// <param name="games">Spiele, gegen die abgeglichen wird.</param>
    /// <param name="isHelper">Prüft einen EXE-Dateinamen gegen die Ausschlussliste.</param>
    /// <param name="forbiddenRoots">Ordner, die nie als Installationsordner gelten (Laufwerkswurzeln, Windows, Programme).</param>
    public GameMatcher(IEnumerable<GameConfig> games, Func<string, bool> isHelper, IEnumerable<string>? forbiddenRoots = null)
    {
        _isHelper = isHelper;
        var forbidden = new HashSet<string>((forbiddenRoots ?? []).Select(PathUtil.Normalize), StringComparer.OrdinalIgnoreCase);
        _entries = games
            .Select(g => new Entry(g, UsableInstallDir(g.InstallDir, forbidden), ExactPaths(g)))
            .OrderByDescending(e => e.InstallDir?.Length ?? 0)
            .ThenByDescending(e => e.Game.HdrAuto)
            .ThenByDescending(e => e.Game.IsInstalled)
            .ToList();
    }

    public GameConfig? Match(ProcessInfo process)
    {
        var name = process.ExeName;
        if (string.IsNullOrEmpty(name) || _isHelper(name))
        {
            return null;
        }

        var path = process.ExePath is null ? null : PathUtil.Normalize(process.ExePath);
        if (path is not null)
        {
            // 1. Genau bekannter EXE-Pfad (unabhängig davon, ob der Ordner als Installationsordner taugt).
            foreach (var e in _entries)
            {
                if (e.ExactPaths.Contains(path) && !IsExcluded(e.Game, name, path))
                {
                    return e.Game;
                }
            }

            // 2. Unter dem Installationsordner.
            foreach (var e in _entries)
            {
                if (e.InstallDir is not null && PathUtil.IsUnder(path, e.InstallDir) && !IsExcluded(e.Game, name, path))
                {
                    return e.Game;
                }
            }

            // 3. Vom Nutzer ergänzte EXE per Name.
            foreach (var e in _entries)
            {
                if (!IsExcluded(e.Game, name, path) && e.Game.ExtraExecutables.Any(x => Matches(x, name, path)))
                {
                    return e.Game;
                }
            }

            return null;
        }

        // Pfad nicht lesbar: nur über den Namen, nur eindeutig.
        GameConfig? found = null;
        foreach (var e in _entries)
        {
            if (IsExcluded(e.Game, name, null))
            {
                continue;
            }

            var named = e.Game.DetectedExecutables.Concat(e.Game.KnownExecutables).Concat(e.Game.ExtraExecutables)
                .Any(x => string.Equals(Path.GetFileName(x), name, StringComparison.OrdinalIgnoreCase));
            if (named)
            {
                if (found is not null && !ReferenceEquals(found, e.Game))
                {
                    return null;
                }

                found = e.Game;
            }
        }

        return found;
    }

    private static bool IsExcluded(GameConfig game, string name, string? path) =>
        game.ExcludedExecutables.Any(x => Matches(x, name, path));

    /// <summary>
    /// Eintrag mit Pfadtrenner: Pfadvergleich (bei unbekanntem Prozesspfad Vergleich des Dateinamens);
    /// sonst Dateinamenvergleich.
    /// </summary>
    private static bool Matches(string entry, string name, string? path)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        if (entry.Contains('\\') || entry.Contains('/'))
        {
            return path is not null
                ? PathUtil.PathEquals(entry, path)
                : string.Equals(Path.GetFileName(entry.Replace('/', '\\')), name, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(entry, name, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ExactPaths(GameConfig game)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in game.DetectedExecutables.Concat(game.KnownExecutables).Concat(game.ExtraExecutables))
        {
            if (!string.IsNullOrWhiteSpace(exe) && (exe.Contains('\\') || exe.Contains('/')))
            {
                var p = PathUtil.Normalize(exe);
                if (Path.IsPathFullyQualified(p))
                {
                    set.Add(p);
                }
            }
        }

        return set;
    }

    private static string? UsableInstallDir(string installDir, HashSet<string> forbidden)
    {
        if (string.IsNullOrWhiteSpace(installDir))
        {
            return null;
        }

        var dir = PathUtil.Normalize(installDir);
        if (!Path.IsPathFullyQualified(dir) || dir.Length <= 3 || forbidden.Contains(dir))
        {
            return null; // Laufwerkswurzel oder Systemordner würde alles treffen.
        }

        return dir;
    }

    private sealed record Entry(GameConfig Game, string? InstallDir, HashSet<string> ExactPaths);
}
