using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Übergabe beim Entfernen (E83): Die installierte EXE kann ihren eigenen Ordner nicht löschen. Sie kopiert sich nach
/// %TEMP%\HDRpilot-Uninstall-&lt;guid&gt;\HDRpilot-Uninstall.exe und startet die Kopie mit
/// <c>--uninstall-continue --wait-pid</c>. Die Kopie wartet ohne Prozess-Handle (nur über die Prozessliste), bis die
/// installierte EXE beendet ist, und lässt ihren Temp-Ordner am Ende von einer verzögerten, unsichtbaren cmd.exe löschen.
/// Gelöscht wird nur ein solcher Ordner direkt im Temp-Ordner, nie etwas anderes.
/// </summary>
public static class UninstallHandoff
{
    public const string FolderPrefix = "HDRpilot-Uninstall-";
    public const string CopyName = "HDRpilot-Uninstall.exe";

    /// <summary>Abstand der Abfragen und längste Wartezeit auf das Ende der installierten EXE.</summary>
    public static readonly TimeSpan WaitInterval = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Kopiert <paramref name="sourceExe"/> in einen neuen Ordner im Temp-Ordner und startet die Kopie mit
    /// <paramref name="arguments"/> (Arbeitsordner: ihr eigener, damit der Programmordner frei wird). Wirft, wenn
    /// Kopieren oder Starten scheitert; ein angefangener Ordner wird dann wieder gelöscht.
    /// </summary>
    /// <returns>Pfad der gestarteten Kopie.</returns>
    public static string Start(string sourceExe, string tempRoot, IReadOnlyList<string> arguments, SetupLog log)
    {
        var copy = CreateTempCopy(sourceExe, tempRoot);
        try
        {
            var info = new ProcessStartInfo(copy)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(copy)!,
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info) ?? throw new InvalidOperationException("Kopie zum Entfernen wurde nicht gestartet.");
            log.Info(string.Create(CultureInfo.InvariantCulture, $"Entfernen geht weiter in {copy} (PID {process.Id}): {string.Join(' ', arguments)}"));
            return copy;
        }
        catch
        {
            TryDeleteFolder(Path.GetDirectoryName(copy)!, tempRoot);
            throw;
        }
    }

    /// <summary>Legt %TEMP%\HDRpilot-Uninstall-&lt;guid&gt;\HDRpilot-Uninstall.exe an (Kopie von <paramref name="sourceExe"/>).</summary>
    public static string CreateTempCopy(string sourceExe, string tempRoot)
    {
        var folder = Path.Combine(Path.GetFullPath(tempRoot), FolderPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, CopyName);
        try
        {
            File.Copy(sourceExe, target);
        }
        catch
        {
            TryDeleteFolder(folder, tempRoot);
            throw;
        }

        return target;
    }

    /// <summary>
    /// Ordner, den die Kopie am Ende löschen darf: nur wenn <paramref name="exePath"/> die Kopie
    /// (HDRpilot-Uninstall.exe) in einem Ordner HDRpilot-Uninstall-&lt;guid&gt; direkt in <paramref name="tempRoot"/> ist.
    /// Sonst null (etwa die installierte EXE, ein Entwicklungsstand oder ein Pfad, den cmd.exe falsch lesen könnte).
    /// </summary>
    public static string? RemovableTempFolder(string? exePath, string? tempRoot)
    {
        if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(tempRoot) || !Path.IsPathFullyQualified(exePath))
        {
            return null;
        }

        try
        {
            var exe = Path.GetFullPath(exePath);
            var folder = Path.GetDirectoryName(exe);
            var parent = folder is null ? null : Path.GetDirectoryName(folder);
            if (folder is null || parent is null || !string.Equals(Path.GetFileName(exe), CopyName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var name = Path.GetFileName(folder);
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempRoot));
            if (!string.Equals(Path.TrimEndingDirectorySeparator(parent), temp, StringComparison.OrdinalIgnoreCase)
                || !name.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParse(name.AsSpan(FolderPrefix.Length), out _))
            {
                return null;
            }

            // In Anführungszeichen liest cmd.exe nur %…% und " anders; solche Pfade bleiben lieber liegen.
            return folder.IndexOfAny(['%', '"']) >= 0 ? null : folder;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Befehl für cmd.exe: etwa 3 s warten (bis diese EXE beendet ist), dann den Ordner löschen.</summary>
    public static string RemovalArguments(string folder) => "/c ping -n 4 127.0.0.1 >nul & rd /s /q \"" + folder + "\"";

    /// <summary>
    /// Lässt den Temp-Ordner der Kopie nach dem Ende dieses Prozesses löschen (unsichtbare cmd.exe, nicht abgewartet).
    /// Tut nichts, wenn die EXE nicht aus einem solchen Ordner läuft. True, wenn das Löschen geplant ist.
    /// </summary>
    public static bool ScheduleSelfRemoval(string? exePath, string tempRoot, SetupLog log)
    {
        if (RemovableTempFolder(exePath, tempRoot) is not { } folder)
        {
            log.Info("Kein Temp-Ordner zum Aufräumen: " + exePath);
            return false;
        }

        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), RemovalArguments(folder))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(folder)!, // nie im Ordner selbst, sonst ließe er sich nicht löschen
        };
        try
        {
            using var process = Process.Start(info);
            log.Info("Temp-Ordner wird nach dem Ende gelöscht: " + folder);
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            log.Warn("Temp-Ordner bleibt liegen: " + folder + " | " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Wartet, bis <paramref name="isRunning"/> für die Prozess-ID false meldet (Abfrage alle <paramref name="interval"/>,
    /// höchstens <paramref name="timeout"/>). True, wenn der Prozess weg ist.
    /// </summary>
    public static async Task<bool> WaitUntilGoneAsync(int processId, Func<int, bool> isRunning, TimeSpan interval, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        while (isRunning(processId))
        {
            if (watch.Elapsed >= timeout)
            {
                return false;
            }

            await Task.Delay(interval, cancellationToken);
        }

        return true;
    }

    /// <summary>Fehlerart, wenn Kopieren oder Starten der Kopie scheitert (voller Datenträger, sonst unerwartet).</summary>
    public static SetupErrorKind ErrorKindFor(Exception ex) =>
        ex is IOException { HResult: unchecked((int)0x80070070) or unchecked((int)0x80070027) } ? SetupErrorKind.DiskFull : SetupErrorKind.Unexpected;

    private static void TryDeleteFolder(string folder, string tempRoot)
    {
        // Nur den eben angelegten Ordner (gleiche Prüfung wie beim Aufräumen am Ende).
        if (RemovableTempFolder(Path.Combine(folder, CopyName), tempRoot) is not { } removable)
        {
            return;
        }

        try
        {
            Directory.Delete(removable, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bleibt im Temp-Ordner liegen; Windows räumt ihn irgendwann auf.
        }
    }
}
