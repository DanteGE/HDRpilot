using HdrPilot.Core.Config;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.Sessions;

/// <summary>
/// Ordnet Prozesse Spielen zu. Treffer ist jeder Prozess, dessen EXE-Pfad unter dem Installationsordner eines
/// Spiels liegt (bei verschachtelten Ordnern gewinnt der genaueste), außer Hilfsprozessen (globale Ausschlussliste)
/// und vom Nutzer je Spiel ausgeschlossenen EXEs. Vom Nutzer ergänzte EXEs zählen auch außerhalb des Ordners.
/// Ist der Pfad nicht lesbar, wird über den EXE-Namen gegen gemerkte, bekannte und ergänzte EXEs abgeglichen,
/// aber nur bei eindeutigem Treffer.
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
            .Select(g => new Entry(g, UsableInstallDir(g.InstallDir, forbidden)))
            .OrderByDescending(e => e.InstallDir?.Length ?? 0)
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
            foreach (var e in _entries)
            {
                if (IsExcluded(e.Game, name, path))
                {
                    continue;
                }

                if (e.InstallDir is not null && PathUtil.IsUnder(path, e.InstallDir))
                {
                    return e.Game;
                }
            }

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

    /// <summary>Eintrag mit Pfadtrenner: Pfadvergleich; sonst Dateinamenvergleich.</summary>
    private static bool Matches(string entry, string name, string? path)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        if (entry.Contains('\\') || entry.Contains('/'))
        {
            return path is not null && PathUtil.PathEquals(entry, path);
        }

        return string.Equals(entry, name, StringComparison.OrdinalIgnoreCase);
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

    private sealed record Entry(GameConfig Game, string? InstallDir);
}
