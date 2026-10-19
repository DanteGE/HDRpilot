using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.Core.Config;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Rückfragen von „Einstellungen entfernen…“ (E90), je Spiel zusammengesetzt: was verloren geht (etwa „„HDR automatisch“
/// ist an“, „2 gemerkte EXE-Dateien“), eine Warnung, wenn das Laufwerk des Spiels nicht verbunden ist („vielleicht noch
/// installiert“), und ein Häkchen für die HDRpilot-Verknüpfungen, nur wenn es welche gibt. Für das Sammel-Entfernen in den
/// Einstellungen die Namen aller betroffenen Spiele, je mit dem, was verloren geht.
/// </summary>
public static class RemoveSettingsText
{
    /// <summary>Rückfrage für ein Spiel.</summary>
    /// <param name="missingDrive">Laufwerk des Spiels, wenn es nicht verbunden ist („F:“); sonst null.</param>
    /// <param name="shortcuts">Zahl der gefundenen HDRpilot-Verknüpfungen (0: kein Häkchen).</param>
    public static ConfirmRequest ForGame(GameConfig game, string? missingDrive, int shortcuts) =>
        new(LibraryText.Format(LibraryStrings.RemoveSettings_ConfirmTitle, game.Name), LibraryStrings.RemoveSettings_ConfirmText, LibraryStrings.RemoveSettings_Confirm, IsDanger: true)
        {
            DetailsHeader = LibraryStrings.RemoveSettings_LossHeader,
            Details = Losses(LibraryCleanup.LossOf(game)),
            Warning = missingDrive is null ? null : LibraryText.Format(LibraryStrings.RemoveSettings_DriveMissing, missingDrive),
            OptionText = ShortcutOption(shortcuts),
            OptionHint = shortcuts > 0 ? LibraryStrings.RemoveSettings_ShortcutHint : null,
        };

    /// <summary>Rückfrage für mehrere Spiele (Sammel-Entfernen): je Spiel Name und was verloren geht.</summary>
    /// <param name="missingDrives">Nicht verbundene Laufwerke der Spiele („F:“), ohne Doppelte; leer: keine Warnung.</param>
    /// <param name="shortcuts">Zahl der gefundenen HDRpilot-Verknüpfungen aller Spiele (0: kein Häkchen).</param>
    public static ConfirmRequest ForGames(IReadOnlyList<GameConfig> games, IReadOnlyList<string> missingDrives, int shortcuts) =>
        new(
            games.Count == 1 ? LibraryStrings.RemoveAll_ConfirmTitleOne : LibraryText.Format(LibraryStrings.RemoveAll_ConfirmTitleMany, games.Count),
            LibraryStrings.RemoveAll_ConfirmText,
            LibraryStrings.RemoveSettings_Confirm,
            IsDanger: true)
        {
            DetailsHeader = LibraryStrings.RemoveAll_DetailsHeader,
            Details = games.Select(GameLine).ToList(),
            Warning = missingDrives.Count == 0 ? null : LibraryText.Format(LibraryStrings.RemoveAll_DrivesMissing, string.Join(", ", missingDrives)),
            OptionText = ShortcutOption(shortcuts),
            OptionHint = shortcuts > 0 ? LibraryStrings.RemoveSettings_ShortcutHint : null,
        };

    /// <summary>Was verloren geht, in der Reihenfolge der Details; ohne eigene Einstellungen ein Satz, der das sagt.</summary>
    public static IReadOnlyList<string> Losses(SettingsLoss loss)
    {
        var list = new List<string>();
        if (loss.HdrAuto)
        {
            list.Add(LibraryStrings.RemoveSettings_LossHdrAuto);
        }

        if (loss.AutoHdr)
        {
            list.Add(LibraryStrings.RemoveSettings_LossAutoHdr);
        }

        if (loss.OwnDisplay)
        {
            list.Add(LibraryStrings.RemoveSettings_LossDisplay);
        }

        if (loss.NoRestore)
        {
            list.Add(LibraryStrings.RemoveSettings_LossNoRestore);
        }

        AddCount(list, loss.DetectedExecutables, LibraryStrings.RemoveSettings_LossDetectedOne, LibraryStrings.RemoveSettings_LossDetectedMany);
        AddCount(list, loss.ExtraExecutables, LibraryStrings.RemoveSettings_LossExtraOne, LibraryStrings.RemoveSettings_LossExtraMany);
        AddCount(list, loss.ExcludedExecutables, LibraryStrings.RemoveSettings_LossExcludedOne, LibraryStrings.RemoveSettings_LossExcludedMany);
        if (loss.CustomIcon)
        {
            list.Add(LibraryStrings.RemoveSettings_LossCustomIcon);
        }

        AddCount(list, loss.CustomImages, LibraryStrings.RemoveSettings_LossImagesOne, LibraryStrings.RemoveSettings_LossImagesMany);
        if (loss.LastPlayed is { } played)
        {
            list.Add(LibraryText.Format(LibraryStrings.RemoveSettings_LossLastPlayed, played.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
        }

        if (list.Count == 0)
        {
            list.Add(LibraryStrings.RemoveSettings_LossNothing);
        }

        return list;
    }

    /// <summary>Zeile des Sammel-Entfernens: „Name · was verloren geht“, ohne eigene Einstellungen nur der Name.</summary>
    private static string GameLine(GameConfig game)
    {
        var loss = LibraryCleanup.LossOf(game);
        return loss.IsEmpty ? game.Name : LibraryText.Format(LibraryStrings.RemoveAll_GameWithLoss, game.Name, string.Join(", ", Losses(loss)));
    }

    private static string? ShortcutOption(int shortcuts) => shortcuts switch
    {
        <= 0 => null,
        1 => LibraryStrings.RemoveSettings_ShortcutOne,
        _ => LibraryText.Format(LibraryStrings.RemoveSettings_ShortcutMany, shortcuts),
    };

    private static void AddCount(List<string> list, int count, string one, string many)
    {
        if (count == 1)
        {
            list.Add(one);
        }
        else if (count > 1)
        {
            list.Add(LibraryText.Format(many, count));
        }
    }
}
