using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Processes;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners.Quiver;

/// <summary>
/// Lernt den Ordner des Quiver Launchers aus der Prozessüberwachung (E91): Startet <c>…\current\QuiverLauncher.exe</c>
/// (Velopack) oder <c>Quiver.exe</c> (bis 2.4.x) und trägt der Ordner darüber das Kennzeichen von Quiver
/// (<see cref="QuiverRoot.Detect"/>, Bibliothek darf noch fehlen), merkt HDRpilot ihn in der versteckten Einstellung
/// <c>quiverFolders</c> und meldet <see cref="Learned"/>; die App liest dann verzögert neu ein. Jeder EXE-Pfad wird nur
/// einmal geprüft (wie jeder Prozess nur einmal zugeordnet wird, E31), ein schon bekannter Ordner schreibt nichts. Die
/// MuiCache (Liste ausgeführter Programme) wird bewusst nicht gelesen. Wirft nie.
/// </summary>
public sealed class QuiverFolderLearner(IFileSystem fileSystem, ConfigService config, ILogger? logger = null)
{
    /// <summary>Höchstens so viele geprüfte Pfade werden gemerkt (danach beginnt die Liste neu).</summary>
    internal const int MaxRemembered = 64;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ein neuer Quiver-Ordner wurde gelernt und gespeichert (Argument: der Ordner). Kommt auf dem Thread der Überwachung.</summary>
    public event EventHandler<string>? Learned;

    /// <summary>Neu gestartete Prozesse eines Abfragezyklus.</summary>
    public void Observe(IReadOnlyList<ProcessInfo> started)
    {
        foreach (var process in started)
        {
            try
            {
                Observe(process);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Quiver: Ordner von {Path} nicht gelernt.", process.ExePath);
            }
        }
    }

    /// <summary>Ordner über der EXE von Quiver; null, wenn der Prozess nicht danach aussieht.</summary>
    internal static string? FolderOf(string exePath)
    {
        var path = PathUtil.Normalize(exePath);
        var name = Path.GetFileName(path);
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        if (string.Equals(name, QuiverRoot.MainExe, StringComparison.OrdinalIgnoreCase))
        {
            // Velopack startet …\current\QuiverLauncher.exe über den gleichnamigen Starter im Ordner darüber.
            return string.Equals(Path.GetFileName(directory), QuiverRoot.CurrentFolder, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(directory)
                : directory;
        }

        return string.Equals(name, QuiverRoot.LegacyExe, StringComparison.OrdinalIgnoreCase) ? directory : null;
    }

    private void Observe(ProcessInfo process)
    {
        if (process.ExePath is not { Length: > 0 } exePath
            || !(string.Equals(process.ExeName, QuiverRoot.MainExe, StringComparison.OrdinalIgnoreCase)
                || string.Equals(process.ExeName, QuiverRoot.LegacyExe, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (_checked.Count >= MaxRemembered)
        {
            _checked.Clear();
        }

        if (!_checked.Add(PathUtil.Normalize(exePath)) || FolderOf(exePath) is not { } folder)
        {
            return;
        }

        if (QuiverRoot.Detect(fileSystem, folder, requireLibrary: false) == QuiverLayout.None)
        {
            _logger.LogDebug("Quiver: {Path} läuft, aber {Folder} ist kein Quiver-Ordner.", exePath, folder);
            return;
        }

        if (Known(config.Current, folder))
        {
            return;
        }

        var added = false;
        config.Update(c =>
        {
            if (!Known(c, folder))
            {
                (c.Settings.QuiverFolders ??= []).Add(folder);
                added = true;
            }
        });
        if (added)
        {
            _logger.LogInformation("Quiver: Ordner {Folder} gelernt (Quiver gestartet).", folder);
            Learned?.Invoke(this, folder);
        }
    }

    private static bool Known(AppConfig config, string folder) =>
        config.Settings.QuiverFolders?.Any(f => PathUtil.PathEquals(f, folder)) == true;
}
