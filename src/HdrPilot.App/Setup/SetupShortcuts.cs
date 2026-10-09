using System.IO;
using System.Runtime.InteropServices;
using HdrPilot.App.Services;
using HdrPilot.Core.Util;

namespace HdrPilot.App.Setup;

/// <summary>Verknüpfungen für Setup und Entfernen.</summary>
public interface ISetupShortcuts
{
    /// <summary>Schreibt eine Verknüpfung auf <paramref name="targetPath"/> (Symbol der EXE); vorhandene wird ersetzt.</summary>
    void Write(string shortcutPath, string targetPath, string arguments, string workingDirectory, string description);

    /// <summary>Ziel einer Verknüpfung oder null (fehlt oder nicht lesbar).</summary>
    string? ReadTarget(string shortcutPath);
}

public static class SetupShortcutExtensions
{
    /// <summary>True, wenn die Verknüpfung existiert und auf <paramref name="exePath"/> zeigt.</summary>
    public static bool PointsTo(this ISetupShortcuts shortcuts, string shortcutPath, string exePath) =>
        shortcuts.ReadTarget(shortcutPath) is { } target && PathUtil.PathEquals(target, exePath);
}

/// <summary>
/// Verknüpfungen über die Windows-Shell (<see cref="ShortcutService.Write"/>/<see cref="ShortcutService.Read"/>, COM).
/// COM braucht ein STA; Setup und Entfernen laufen auf einem Hintergrund-Thread, deshalb geht jeder Aufruf dann über
/// einen eigenen kurzen STA-Thread (<see cref="SetupSupport.OnSta"/>).
/// </summary>
public sealed class ShellSetupShortcuts : ISetupShortcuts
{
    public static ShellSetupShortcuts Instance { get; } = new();

    private const string StaThreadName = "HDRpilot Setup Verknüpfungen";

    public void Write(string shortcutPath, string targetPath, string arguments, string workingDirectory, string description) =>
        SetupSupport.OnSta(StaThreadName, () =>
        {
            ShortcutService.Write(shortcutPath, targetPath, arguments, workingDirectory, targetPath, 0, description);
            return true;
        });

    public string? ReadTarget(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
        {
            return null;
        }

        return SetupSupport.OnSta<string?>(StaThreadName, () =>
        {
            try
            {
                var target = ShortcutService.Read(shortcutPath).TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null; // kaputte oder fremde Datei mit .lnk-Endung
            }
        });
    }
}
