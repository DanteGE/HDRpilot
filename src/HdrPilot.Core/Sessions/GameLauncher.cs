using System.ComponentModel;
using System.Diagnostics;
using HdrPilot.Core.Config;

namespace HdrPilot.Core.Sessions;

public sealed record GameLaunchResult(bool Success, string Message);

/// <summary>Startet ein Spiel über den Start-Befehl seiner Quelle.</summary>
public interface IGameLauncher
{
    GameLaunchResult Launch(GameConfig game);
}

/// <summary>
/// Startet über die Windows-Shell (ShellExecute): Protokoll-Links wie steam://, com.epicgames.launcher://,
/// uplay://, shell:AppsFolder\… und EXE-Pfade. Kein Eingriff in den gestarteten Prozess.
/// </summary>
public sealed class ShellGameLauncher : IGameLauncher
{
    public GameLaunchResult Launch(GameConfig game)
    {
        if (string.IsNullOrWhiteSpace(game.LaunchCommand))
        {
            return new GameLaunchResult(false, $"Für \"{game.Name}\" ist kein Start-Befehl bekannt.");
        }

        var command = game.LaunchCommand;
        var isShellTarget = command.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
        var info = isShellTarget
            ? new ProcessStartInfo("explorer.exe", command) { UseShellExecute = true }
            : new ProcessStartInfo(command) { UseShellExecute = true };
        if (!isShellTarget && !string.IsNullOrWhiteSpace(game.LaunchArguments))
        {
            info.Arguments = game.LaunchArguments;
        }

        if (!isShellTarget && !string.IsNullOrWhiteSpace(game.LaunchWorkingDirectory))
        {
            info.WorkingDirectory = game.LaunchWorkingDirectory;
        }

        try
        {
            using var process = Process.Start(info);
            return new GameLaunchResult(true, $"Start-Befehl ausgeführt: {command}");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new GameLaunchResult(false, $"Start von \"{game.Name}\" fehlgeschlagen: {ex.Message}");
        }
    }
}
