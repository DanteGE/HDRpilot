using System.IO;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.App.ViewModels.Library;

public enum FolderRisk
{
    /// <summary>Normaler Spielordner.</summary>
    Ok,

    /// <summary>Systemordner oder Ordner, der Systemordner enthält: nur nach Rückfrage.</summary>
    Wide,

    /// <summary>Laufwerkswurzel: nie (jeder Prozess darauf gälte als Spiel).</summary>
    DriveRoot,
}

/// <summary>
/// Warnt vor sehr weiten Ordnern als Installationsordner eines manuellen Spiels: Laufwerkswurzel, Windows (auch
/// darunter), Programme, Benutzerprofil und dessen Standardordner sowie jeder Ordner, der einen davon enthält.
/// </summary>
public static class FolderSafety
{
    private static readonly Environment.SpecialFolder[] SystemFolders =
    [
        Environment.SpecialFolder.ProgramFiles,
        Environment.SpecialFolder.ProgramFilesX86,
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolder.Desktop,
        Environment.SpecialFolder.DesktopDirectory,
        Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolder.ApplicationData,
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.CommonApplicationData,
        Environment.SpecialFolder.Programs,
        Environment.SpecialFolder.StartMenu,
    ];

    public static FolderRisk Classify(string folder, IAppEnvironment environment)
    {
        var dir = PathUtil.Normalize(folder);
        if (dir.Length <= 3 || PathUtil.PathEquals(Path.GetPathRoot(dir) ?? string.Empty, dir))
        {
            return FolderRisk.DriveRoot;
        }

        var windows = environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows) && (PathUtil.IsUnder(dir, windows) || PathUtil.IsUnder(windows, dir)))
        {
            return FolderRisk.Wide;
        }

        foreach (var special in SystemFolders)
        {
            var path = environment.GetFolderPath(special);
            if (!string.IsNullOrWhiteSpace(path) && PathUtil.IsUnder(path, dir))
            {
                // Gleich dem Systemordner oder ein Ordner darüber (etwa C:\Users).
                return FolderRisk.Wide;
            }
        }

        return FolderRisk.Ok;
    }
}
