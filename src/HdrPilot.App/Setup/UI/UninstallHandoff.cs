using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using HdrPilot.App.Resources;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Übergabe beim Entfernen (E83): Die installierte EXE kann ihren eigenen Ordner nicht löschen. Sie kopiert sich nach
/// %TEMP%\HDRpilot-Uninstall-&lt;guid&gt;\HDRpilot-Uninstall.exe und startet die Kopie mit
/// <c>--uninstall-continue --wait-pid</c>. Die Kopie wartet ohne Prozess-Handle (nur über die Prozessliste), bis die
/// installierte EXE beendet ist, und lässt am Ende von einer verzögerten, unsichtbaren cmd.exe ihren Temp-Ordner und,
/// sobald der weg ist, einmal den Entpack-Ordner ihrer nativen Bibliotheken (%TEMP%\.net\HDRpilot-Uninstall) löschen
/// (<see cref="RemovalArguments"/>). Gelöscht wird nur ein solcher Ordner direkt im Temp-Ordner bzw. genau dieser
/// Entpack-Ordner, nie etwas anderes (nie der Entpack-Ordner der App, %TEMP%\.net\HDRpilot, oder eines Setups,
/// %TEMP%\.net\HDRpilot-Setup-&lt;Version&gt;).
/// </summary>
public static partial class UninstallHandoff
{
    public const string FolderPrefix = "HDRpilot-Uninstall-";
    public const string CopyName = "HDRpilot-Uninstall.exe";

    /// <summary>Entpack-Ordner der Kopie: Die Einzeldatei-EXE entpackt nach &lt;Temp&gt;\.net\&lt;Name der EXE&gt;\&lt;Kennung&gt;.</summary>
    public const string ExtractionFolderName = "HDRpilot-Uninstall";
    public const string ExtractionBaseName = ".net";

    /// <summary>Wie oft (etwa im Sekundenabstand) die cmd.exe das Löschen versucht, bis die Ordner weg sind.</summary>
    public const int RemovalAttempts = 30;

    /// <summary>Native WPF-Bibliotheken, die die Einzeldatei-EXE entpackt; ihr Pfad verrät den Entpack-Ordner.</summary>
    private static readonly string[] NativeLibraries = ["wpfgfx_cor3.dll", "PresentationNative_cor3.dll", "vcruntime140_cor3.dll", "PenImc_cor3.dll", "D3DCompiler_47_cor3.dll"];

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

            using var process = Process.Start(info) ?? throw new InvalidOperationException(SetupStrings.UninstallCopyNotStarted);
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

            return SafeForCmd(folder) ? folder : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Entpack-Ordner, den die Kopie am Ende löschen darf: nur genau &lt;tempRoot&gt;\.net\HDRpilot-Uninstall (Name exakt,
    /// direkt unter einem Ordner .net im Temp-Ordner). Sonst null, etwa für den Ordner der App (.net\HDRpilot), eines
    /// Setups (.net\HDRpilot-Setup-&lt;Version&gt;), einen eigenen Entpack-Ort (DOTNET_BUNDLE_EXTRACT_BASE_DIR) oder einen
    /// Pfad, den cmd.exe falsch lesen könnte.
    /// </summary>
    public static string? RemovableExtractionFolder(string? folder, string? tempRoot)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(tempRoot) || !Path.IsPathFullyQualified(folder))
        {
            return null;
        }

        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var dotnet = Path.GetDirectoryName(full);
            var parent = dotnet is null ? null : Path.GetDirectoryName(dotnet);
            if (dotnet is null || parent is null
                || !string.Equals(Path.GetFileName(full), ExtractionFolderName, StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(dotnet), ExtractionBaseName, StringComparison.Ordinal)
                || !string.Equals(Path.TrimEndingDirectorySeparator(parent), Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempRoot)), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return SafeForCmd(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Entpack-Ordner dieses Prozesses (Kopie zum Entfernen): aus dem Pfad einer geladenen nativen WPF-Bibliothek
    /// (&lt;Temp&gt;\.net\HDRpilot-Uninstall\&lt;Kennung&gt;\wpfgfx_cor3.dll), sonst aus den Suchordnern für native
    /// Bibliotheken, die der Einzeldatei-Start setzt. Nur, was <see cref="RemovableExtractionFolder"/> erlaubt; sonst null.
    /// </summary>
    public static string? OwnExtractionFolder(string tempRoot)
    {
        foreach (var library in NativeLibraries)
        {
            if (LoadedModulePath(library) is { } path && RemovableExtractionFolder(Path.GetDirectoryName(Path.GetDirectoryName(path)), tempRoot) is { } folder)
            {
                return folder;
            }
        }

        var searchDirectories = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? string.Empty;
        foreach (var directory in searchDirectories.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (RemovableExtractionFolder(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory)), tempRoot) is { } folder)
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>
    /// Befehl für cmd.exe (ohne AutoRun und ohne verzögerte Erweiterung): etwa 3 s warten, bis diese EXE beendet ist, dann
    /// bis zu <see cref="RemovalAttempts"/>-mal im Sekundenabstand den Temp-Ordner löschen. Sobald er weg ist, ist die
    /// Kopie beendet (ihre EXE lag darin): dann einmal den Entpack-Ordner löschen und aufhören. Nicht früher und nicht
    /// öfter: Während eines späteren Versuchs könnte dort schon eine neue Kopie zum Entfernen entpackt haben. ping mit
    /// vollem Pfad (nicht über PATH gesucht).
    /// </summary>
    public static string RemovalArguments(string folder, string? extractionFolder = null)
    {
        var ping = "\"" + Path.Combine(Environment.SystemDirectory, "PING.EXE") + "\"";
        var whenGone = extractionFolder is null ? "exit" : "(rd /s /q \"" + extractionFolder + "\" 2>nul & exit)";
        return string.Create(CultureInfo.InvariantCulture,
            $"/d /v:off /c ({ping} -n 4 127.0.0.1 >nul) & for /l %i in (1,1,{RemovalAttempts}) do @(rd /s /q \"{folder}\" 2>nul & (if not exist \"{folder}\\\" {whenGone}) & {ping} -n 2 127.0.0.1 >nul)");
    }

    /// <summary>
    /// Lässt den Temp-Ordner der Kopie und ihren Entpack-Ordner (<paramref name="extractionFolder"/>, falls erlaubt) nach
    /// dem Ende dieses Prozesses löschen (unsichtbare cmd.exe, nicht abgewartet). Tut nichts, wenn die EXE nicht aus einem
    /// solchen Temp-Ordner läuft. True, wenn das Löschen geplant ist.
    /// </summary>
    public static bool ScheduleSelfRemoval(string? exePath, string tempRoot, SetupLog log, string? extractionFolder = null)
    {
        if (RemovableTempFolder(exePath, tempRoot) is not { } folder)
        {
            log.Info("Kein Temp-Ordner zum Aufräumen: " + exePath);
            return false;
        }

        var extraction = RemovableExtractionFolder(extractionFolder, tempRoot);
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), RemovalArguments(folder, extraction))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory, // nie in einem der Ordner, sonst ließe er sich nicht löschen
        };
        try
        {
            using var process = Process.Start(info);
            log.Info("Temp-Ordner wird nach dem Ende gelöscht: " + folder + "; Entpack-Ordner: " + (extraction ?? "keiner erkannt (" + extractionFolder + "), bleibt liegen"));
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            log.Warn("Temp-Ordner bleibt liegen: " + folder + " | " + ex.Message);
            return false;
        }
    }

    /// <summary>In Anführungszeichen liest cmd.exe nur %…%, " und (mit verzögerter Erweiterung) ! anders; solche Pfade bleiben lieber liegen.</summary>
    private static bool SafeForCmd(string path) => path.IndexOfAny(['%', '"', '!']) < 0;

    /// <summary>Pfad einer in diesen Prozess geladenen Bibliothek (nur dieser Prozess, kein fremder) oder null.</summary>
    private static unsafe string? LoadedModulePath(string name)
    {
        var module = GetModuleHandleW(name);
        if (module == IntPtr.Zero)
        {
            return null;
        }

        const int Capacity = 32768;
        var buffer = new char[Capacity];
        fixed (char* start = buffer)
        {
            var length = GetModuleFileNameW(module, start, Capacity);
            return length == 0 || length >= Capacity ? null : new string(start, 0, (int)length);
        }
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string moduleName);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial uint GetModuleFileNameW(IntPtr module, char* fileName, uint size);

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
