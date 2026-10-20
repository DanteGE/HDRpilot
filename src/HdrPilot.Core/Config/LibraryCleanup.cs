using HdrPilot.Core.Artwork;

namespace HdrPilot.Core.Config;

/// <summary>
/// Was beim Entfernen der HDRpilot-Daten eines Spiels verloren geht (für die Rückfrage, E90): eigene Einstellungen und was
/// HDRpilot über das Spiel gelernt hat. Steht alles auf Standard, ist <see cref="IsEmpty"/> wahr.
/// </summary>
/// <param name="HdrAuto">„HDR automatisch“ ist an.</param>
/// <param name="AutoHdr">„Windows Auto HDR“ ist an.</param>
/// <param name="OwnDisplay">Ein eigener Bildschirm statt der globalen Einstellung ist gewählt.</param>
/// <param name="NoRestore">„Nach dem Spiel zurückstellen“ ist aus.</param>
/// <param name="DetectedExecutables">Beim Spielen gemerkte EXE-Dateien.</param>
/// <param name="ExtraExecutables">Vom Nutzer hinzugefügte EXE-Dateien.</param>
/// <param name="ExcludedExecutables">Vom Nutzer ausgeschlossene EXE-Dateien.</param>
/// <param name="CustomIcon">Ein eigenes Symbol ist gesetzt.</param>
/// <param name="CustomImages">Eigene Bilder („Bild wählen…“ → „Eigene Datei…“).</param>
/// <param name="ChosenImages">Per „Bild wählen…“ festgelegte Bilder ohne eigene Datei (ein anderes Launcher-Bild oder das erzeugte).</param>
/// <param name="LastPlayed">Zuletzt gespielt, wie HDRpilot es sich gemerkt hat (null: nie).</param>
public sealed record SettingsLoss(
    bool HdrAuto,
    bool AutoHdr,
    bool OwnDisplay,
    bool NoRestore,
    int DetectedExecutables,
    int ExtraExecutables,
    int ExcludedExecutables,
    bool CustomIcon,
    int CustomImages,
    int ChosenImages,
    DateTimeOffset? LastPlayed)
{
    public bool IsEmpty => !HdrAuto && !AutoHdr && !OwnDisplay && !NoRestore && DetectedExecutables == 0 && ExtraExecutables == 0
        && ExcludedExecutables == 0 && !CustomIcon && CustomImages == 0 && ChosenImages == 0 && LastPlayed is null;
}

/// <summary>
/// Daten nicht installierter Spiele entfernen (Wunsch des Nutzers 2026-10-18, E90). Entfernbar ist ein Spiel nur, wenn es
/// beim letzten Einlesen nicht mehr gefunden wurde (<see cref="GameConfig.IsInstalled"/> aus) und von einem Launcher stammt;
/// eigene Spiele haben dafür „Aus der Bibliothek entfernen“. Entfernt wird nie von selbst, nur nach einer Rückfrage, und
/// geprüft wird erst beim Schreiben (<see cref="RemoveNotInstalled"/> im Update der Konfiguration): Findet ein Einlesen das
/// Spiel dazwischen wieder oder läuft dafür inzwischen eine Sitzung, bleibt alles. Wird es später wieder installiert,
/// erscheint es beim nächsten Einlesen als neu, „HDR automatisch“ ist dann aus (<see cref="LibraryMerger"/>).
/// </summary>
public static class LibraryCleanup
{
    /// <summary>Nicht installiert und von einem Launcher (nicht selbst hinzugefügt).</summary>
    public static bool IsRemovable(GameConfig game) => !game.IsInstalled && !game.IsManual;

    /// <summary>
    /// Nicht installierte Spiele, auch selbst hinzugefügte mit fehlendem Ordner: so viele blendet „Nicht installierte Spiele
    /// ausblenden“ aus.
    /// </summary>
    public static int CountNotInstalled(AppConfig config) => config.Games.Count(g => !g.IsInstalled);

    /// <summary>Entfernbare Spiele (<see cref="IsRemovable"/>), nach Namen geordnet (Sammel-Entfernen in den Einstellungen).</summary>
    public static IReadOnlyList<GameConfig> Removable(AppConfig config) =>
        config.Games.Where(IsRemovable).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Entfernt die Einträge der genannten Spiele, die jetzt noch entfernbar sind und für die keine Sitzung läuft oder
    /// vorbereitet ist (<paramref name="hasSession"/>). Im Update der Konfiguration aufrufen, damit erst beim Schreiben
    /// geprüft wird. Liefert die entfernten Einträge (für das Aufräumen ihrer Dateien); alle anderen bleiben unverändert.
    /// </summary>
    public static IReadOnlyList<GameConfig> RemoveNotInstalled(AppConfig config, IEnumerable<string> keys, Func<string, bool>? hasSession = null)
    {
        var wanted = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var removed = new List<GameConfig>();
        config.Games.RemoveAll(game =>
        {
            if (!wanted.Contains(game.Key) || !IsRemovable(game) || hasSession?.Invoke(game.Key) == true)
            {
                return false;
            }

            removed.Add(game);
            return true;
        });
        return removed;
    }

    /// <summary>Was mit dem Eintrag eines Spiels verloren ginge (Rückfrage).</summary>
    public static SettingsLoss LossOf(GameConfig game) => new(
        game.HdrAuto,
        game.AutoHdr,
        !string.IsNullOrWhiteSpace(game.Display),
        !game.RestoreAfter,
        Count(game.DetectedExecutables),
        Count(game.ExtraExecutables),
        Count(game.ExcludedExecutables),
        !string.IsNullOrWhiteSpace(game.CustomIcon),
        Images(game, ArtworkOverrideKind.Custom),
        Images(game, ArtworkOverrideKind.Alternative) + Images(game, ArtworkOverrideKind.Generated),
        game.LastPlayed);

    private static int Count(IEnumerable<string> entries) => entries.Count(e => !string.IsNullOrWhiteSpace(e));

    /// <summary>Titelbild, Logo und Cover mit dieser Art der eigenen Bildwahl.</summary>
    private static int Images(GameConfig game, ArtworkOverrideKind kind) =>
        new[] { game.Artwork?.Hero, game.Artwork?.Logo, game.Artwork?.Cover }.Count(t => ArtworkOverride.KindOf(t) == kind);
}
