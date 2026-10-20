using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.Core.Config;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Rückfragen von „Einstellungen entfernen…“ (E90), je Spiel zusammengesetzt: was verloren geht (etwa „„HDR automatisch“
/// ist an“, „2 gemerkte EXE-Dateien“), eine Warnung, wenn das Spiel vielleicht noch installiert ist (Laufwerk nicht
/// verbunden, EXE noch da, bei Steam noch eingetragen oder nicht zu prüfen, <see cref="InstallTrace"/>), und ein Häkchen für
/// die HDRpilot-Verknüpfungen, nur wenn es welche gibt. Für das Sammel-Entfernen in den Einstellungen je betroffenes Spiel
/// ein Punkt: der Name und darunter, was verloren geht, angeführt von der Spur einer Installation.
/// </summary>
public static class RemoveSettingsText
{
    /// <summary>
    /// Trenner der Angaben unter dem Namen eines Spiels (Sammel-Entfernen); geschütztes Leerzeichen davor, damit keine Zeile
    /// mit dem Punkt beginnt.
    /// </summary>
    public const string NoteSeparator = " · ";

    /// <summary>Rückfrage für ein Spiel.</summary>
    /// <param name="trace">Spuren einer Installation (<see cref="UninstalledGameCleanup.CheckInstallAsync"/>).</param>
    /// <param name="shortcuts">Zahl der gefundenen HDRpilot-Verknüpfungen (0: kein Häkchen).</param>
    public static ConfirmRequest ForGame(GameConfig game, InstallTrace trace, int shortcuts) =>
        new(LibraryText.Format(LibraryStrings.RemoveSettings_ConfirmTitle, game.Name), LibraryStrings.RemoveSettings_ConfirmText, LibraryStrings.RemoveSettings_Confirm, IsDanger: true)
        {
            DetailsHeader = LibraryStrings.RemoveSettings_LossHeader,
            Details = [.. Losses(LibraryCleanup.LossOf(game)).Select(l => new ConfirmDetail(l))],
            Warning = trace.Sign switch
            {
                InstallSign.DriveMissing => LibraryText.Format(LibraryStrings.RemoveSettings_DriveMissing, trace.Drive),
                InstallSign.Program => LibraryStrings.RemoveSettings_ProgramPresent,
                InstallSign.SteamManifest => LibraryStrings.RemoveSettings_SteamListed,
                InstallSign.Unchecked => LibraryStrings.RemoveSettings_Unchecked,
                _ => null,
            },
            OptionText = ShortcutOption(shortcuts),
            OptionHint = ShortcutHint(shortcuts),
        };

    /// <summary>Rückfrage für mehrere Spiele (Sammel-Entfernen): je Spiel Name und was verloren geht.</summary>
    /// <param name="traces">Spuren einer Installation je Spiel, in derselben Reihenfolge wie <paramref name="games"/>.</param>
    /// <param name="shortcuts">Zahl der gefundenen HDRpilot-Verknüpfungen aller Spiele (0: kein Häkchen).</param>
    public static ConfirmRequest ForGames(IReadOnlyList<GameConfig> games, IReadOnlyList<InstallTrace> traces, int shortcuts)
    {
        var drives = traces.Where(t => t.Sign == InstallSign.DriveMissing).Select(t => t.Drive).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var unsure = traces.Count(t => t.MaybeInstalled && t.Sign != InstallSign.DriveMissing);
        var warnings = new List<string>();
        if (drives.Count > 0)
        {
            warnings.Add(drives.Count == 1
                ? LibraryText.Format(LibraryStrings.RemoveAll_DrivesMissingOne, drives[0])
                : LibraryText.Format(LibraryStrings.RemoveAll_DrivesMissingMany, string.Join(", ", drives)));
        }

        // EXE noch da, bei Steam noch eingetragen, nicht zu prüfen: Was genau, steht unter dem Namen des Spiels.
        if (unsure > 0)
        {
            warnings.Add(unsure == 1 ? LibraryStrings.RemoveAll_UnsureOne : LibraryText.Format(LibraryStrings.RemoveAll_UnsureMany, unsure));
        }

        return new(
            games.Count == 1 ? LibraryStrings.RemoveAll_ConfirmTitleOne : LibraryText.Format(LibraryStrings.RemoveAll_ConfirmTitleMany, games.Count),
            LibraryStrings.RemoveAll_ConfirmText,
            LibraryStrings.RemoveSettings_Confirm,
            IsDanger: true)
        {
            DetailsHeader = LibraryStrings.RemoveAll_DetailsHeader,
            Details = [.. games.Select((g, i) => GameLine(g, i < traces.Count ? traces[i] : InstallTrace.None))],
            Warning = warnings.Count == 0 ? null : string.Join(" ", warnings),
            OptionText = ShortcutOption(shortcuts),
            OptionHint = ShortcutHint(shortcuts),
        };
    }

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
        AddCount(list, loss.ChosenImages, LibraryStrings.RemoveSettings_LossChosenOne, LibraryStrings.RemoveSettings_LossChosenMany);
        if (loss.LastPlayed is { } played)
        {
            // Datum wie in Liste und Titelbereich („12. Sept. 2026“).
            list.Add(LibraryText.Format(LibraryStrings.RemoveSettings_LossLastPlayed, LibraryText.Format(LibraryStrings.When_Date, played.ToLocalTime())));
        }

        if (list.Count == 0)
        {
            list.Add(LibraryStrings.RemoveSettings_LossNothing);
        }

        return list;
    }

    /// <summary>
    /// Punkt des Sammel-Entfernens: der Name, darunter (durch „ · “ getrennt) die Spur einer Installation und was verloren
    /// geht; ohne beides nur der Name.
    /// </summary>
    private static ConfirmDetail GameLine(GameConfig game, InstallTrace trace)
    {
        var notes = new List<string>();
        var mark = trace.Sign switch
        {
            InstallSign.DriveMissing => LibraryText.Format(LibraryStrings.RemoveAll_MarkDriveMissing, trace.Drive),
            InstallSign.Program => LibraryStrings.RemoveAll_MarkProgramPresent,
            InstallSign.SteamManifest => LibraryStrings.RemoveAll_MarkSteamListed,
            InstallSign.Unchecked => LibraryStrings.RemoveAll_MarkUnchecked,
            _ => null,
        };
        if (mark is not null)
        {
            notes.Add(mark);
        }

        var loss = LibraryCleanup.LossOf(game);
        if (!loss.IsEmpty)
        {
            notes.AddRange(Losses(loss));
        }

        return new ConfirmDetail(game.Name, notes.Count == 0 ? null : string.Join(NoteSeparator, notes));
    }

    private static string? ShortcutOption(int shortcuts) => shortcuts switch
    {
        <= 0 => null,
        1 => LibraryStrings.RemoveSettings_ShortcutOne,
        _ => LibraryText.Format(LibraryStrings.RemoveSettings_ShortcutMany, shortcuts),
    };

    private static string? ShortcutHint(int shortcuts) => shortcuts switch
    {
        <= 0 => null,
        1 => LibraryStrings.RemoveSettings_ShortcutHintOne,
        _ => LibraryStrings.RemoveSettings_ShortcutHint,
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
