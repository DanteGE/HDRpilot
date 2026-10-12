using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Util;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Gewünschter Stand aller Spiele mit „Windows Auto HDR“: je Ziel-EXE die Spiele, die sie wollen, und je Spiel seine
/// Ziel-EXEs in fester Reihenfolge. <see cref="Signature"/> ändert sich genau dann, wenn sich Ziele oder ihre Spiele ändern.
/// </summary>
public sealed class AutoHdrDesired
{
    internal AutoHdrDesired(
        IReadOnlyList<string> targetOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> targets,
        IReadOnlyDictionary<string, IReadOnlyList<string>> byGame,
        string signature)
    {
        TargetOrder = targetOrder;
        Targets = targets;
        ByGame = byGame;
        Signature = signature;
    }

    /// <summary>Alle Ziel-EXEs in der Reihenfolge der Spiele und ihrer EXEs.</summary>
    public IReadOnlyList<string> TargetOrder { get; }

    /// <summary>Ziel-EXE (Wertname) → Schlüssel der Spiele, die sie wollen. Ohne Groß-/Kleinschreibung.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Targets { get; }

    /// <summary>Spiel mit Option an → seine Ziel-EXEs (leer: noch keine Spiel-EXE bekannt). Ohne Groß-/Kleinschreibung.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ByGame { get; }

    public string Signature { get; }
}

/// <summary>
/// Welche EXEs eines Spiels den Auto-HDR-Wert bekommen (E88). Rein rechnerisch, ohne Dateisystem: Ob eine EXE existiert,
/// spielt keine Rolle (ein Wert für eine fehlende EXE stört nicht und wirkt, sobald sie wieder da ist).
/// </summary>
public static class AutoHdrTargets
{
    /// <summary>
    /// Ziel-EXEs eines Spiels: gemerkte, vom Scanner genannte und vom Nutzer ergänzte EXEs (in dieser Reihenfolge, ohne
    /// doppelte), nur volle Pfade mit Endung .exe. Ausgenommen sind vom Nutzer entfernte EXEs (wie bei der Zuordnung),
    /// Hilfsprozesse (<see cref="HelperExecutables"/>: Absturzmelder, Installer, Redistributables, Launcher-Fenster) und
    /// die Ausschlüsse der Symbole (<see cref="IconExcludedExecutables"/>: Anti-Cheat-Starter, EOS-Bootstrapper, Werkzeuge,
    /// Ordner der Redistributables). Bewusst keine allgemeine „Launcher“-Regel: Ein Wert am Starter stört nicht, fehlte er
    /// an der EXE, die rendert, wäre die Option wirkungslos.
    /// </summary>
    /// <param name="helperPatterns">Muster der Hilfsprozesse aus der Konfiguration (null: Standardliste).</param>
    public static IReadOnlyList<string> For(GameConfig game, IReadOnlyList<string>? helperPatterns)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in game.DetectedExecutables.Concat(game.KnownExecutables).Concat(game.ExtraExecutables))
        {
            if (string.IsNullOrWhiteSpace(raw) || (!raw.Contains('\\', StringComparison.Ordinal) && !raw.Contains('/', StringComparison.Ordinal)))
            {
                continue; // nur ein Dateiname (etwa in den ergänzten EXEs): kein Wertname möglich
            }

            var path = PathUtil.Normalize(raw);
            if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !seen.Add(path))
            {
                continue;
            }

            if (ExcludedByUser(game, path)
                || HelperExecutables.IsHelper(path, helperPatterns)
                || IconExcludedExecutables.IsExcluded(path, game.InstallDir))
            {
                continue;
            }

            result.Add(path);
        }

        return result;
    }

    /// <summary>Gewünschter Stand aller Spiele mit <see cref="GameConfig.AutoHdr"/> (installiert oder nicht).</summary>
    public static AutoHdrDesired Desired(AppConfig config)
    {
        var order = new List<string>();
        var targets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var byGame = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var patterns = config.Settings.HelperExecutablePatterns;
        foreach (var game in config.Games.Where(g => g.AutoHdr))
        {
            var exes = For(game, patterns);
            byGame[game.Key] = exes;
            foreach (var exe in exes)
            {
                if (!targets.TryGetValue(exe, out var games))
                {
                    games = [];
                    targets[exe] = games;
                    order.Add(exe);
                }

                if (!games.Contains(game.Key, StringComparer.OrdinalIgnoreCase))
                {
                    games.Add(game.Key);
                }
            }
        }

        var signature = string.Join(
            "\n",
            order.Select(exe => exe.ToUpperInvariant() + "|" + string.Join(",", targets[exe].Select(k => k.ToUpperInvariant()).Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal));
        return new AutoHdrDesired(
            order,
            targets.ToDictionary(t => t.Key, t => (IReadOnlyList<string>)t.Value, StringComparer.OrdinalIgnoreCase),
            byGame,
            signature);
    }

    /// <summary>Wie die Zuordnung (GameMatcher): Eintrag mit Pfadtrenner per Pfadvergleich, sonst per Dateiname.</summary>
    private static bool ExcludedByUser(GameConfig game, string path)
    {
        var name = Path.GetFileName(path);
        foreach (var entry in game.ExcludedExecutables)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var matches = entry.Contains('\\', StringComparison.Ordinal) || entry.Contains('/', StringComparison.Ordinal)
                ? PathUtil.PathEquals(entry, path)
                : string.Equals(entry, name, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                return true;
            }
        }

        return false;
    }
}
