using System.Diagnostics;
using System.IO;
using HdrPilot.App.Services;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;

namespace HdrPilot.App.Setup;

/// <summary>Gemeinsame Hilfen von Setup und Entfernen: Sperre, Versionen, Fehlerzuordnung, Autostart-Wert.</summary>
public static class SetupSupport
{
    /// <summary>
    /// Sperre gegen ein zweites Setup oder Entfernen zur selben Zeit. Neuer Kernel-Name, deshalb schon in der Schreibweise
    /// HDRpilot (die alten Namen wie HdrPilot.Instance bleiben nur wegen laufender älterer Versionen).
    /// </summary>
    public const string MutexBaseName = @"Local\HDRpilot.Setup";

    /// <summary>Beschreibung der eigenen Verknüpfungen (Produktname, wird nicht übersetzt).</summary>
    public const string ProductName = "HDRpilot";

    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorDiskFull = 112;
    private const int ErrorUnableToRemoveReplaced = 1175;
    private const int ErrorUnableToMoveReplacement = 1176;
    private const int ErrorUnableToMoveReplacement2 = 1177;
    private const int ErrorUserMappedFile = 1224;

    /// <summary>
    /// Name der Sperre. Prüfläufe hängen den Hash ihres Testordners an: so kollidieren sie weder mit einem echten Setup
    /// noch miteinander (parallele Tests).
    /// </summary>
    public static string MutexNameFor(SetupLocations locations) =>
        locations.IsTest ? MutexBaseName + "." + locations.RegistryRoot[(locations.RegistryRoot.LastIndexOf('\\') + 1)..] : MutexBaseName;

    /// <summary>Testordner eines Prüflaufs (<see cref="SetupLocations.ForTestRoot"/> legt „Config“ direkt darunter) oder null.</summary>
    public static string? TestRootOf(SetupLocations locations) =>
        locations.ConfigDirectoryArgument is { } config ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(config)) : null;

    /// <summary>Nur Major.Minor.Build zählen (die vierte Stelle setzt der Build nicht).</summary>
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    /// <summary>Dateiversion einer EXE (Major.Minor.Build). Ohne Versionsangabe 0.0.0: gilt dann als ältere Version.</summary>
    public static Version ReadFileVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }

    /// <summary>Programmpfad einer Befehlszeile: in Anführungszeichen, sonst bis „.exe“, sonst alles.</summary>
    public static string? CommandTarget(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end < 0 ? text[1..] : text[1..end];
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe < 0 ? text : text[..(exe + 4)];
    }

    /// <summary>True, wenn die Befehlszeile <paramref name="exePath"/> startet (ohne Groß-/Kleinschreibung).</summary>
    public static bool PointsTo(string? command, string exePath) =>
        CommandTarget(command) is { } target && PathUtil.PathEquals(target, exePath);

    /// <summary>Autostart-Wert (Run-Schlüssel dieser Installation) oder null.</summary>
    public static string? ReadAutostart(IRegistry registry, SetupLocations locations)
    {
        using var key = registry.OpenKey(RegistryHiveKind.CurrentUser, locations.RunKeyPath);
        return key?.GetValue(AutostartService.ValueName) as string;
    }

    public static bool AutostartPointsToExe(IRegistry registry, SetupLocations locations) =>
        PointsTo(ReadAutostart(registry, locations), locations.ExePath);

    public static void EnableAutostart(IRegistry registry, SetupLocations locations)
    {
        using var key = registry.CreateKey(RegistryHiveKind.CurrentUser, locations.RunKeyPath);
        key.SetValue(AutostartService.ValueName, AutostartService.BuildCommand(locations.ExePath));
    }

    /// <summary>Entfernt den Autostart-Wert nur, wenn er auf die eigene EXE zeigt. True, wenn er entfernt wurde.</summary>
    public static bool RemoveOwnAutostart(IRegistry registry, SetupLocations locations)
    {
        if (!AutostartPointsToExe(registry, locations))
        {
            return false;
        }

        using var key = registry.CreateKey(RegistryHiveKind.CurrentUser, locations.RunKeyPath);
        key.DeleteValue(AutostartService.ValueName);
        return true;
    }

    /// <summary>Setzt den Autostart-Wert wieder auf einen früheren Stand (null: Wert entfernen).</summary>
    public static void RestoreAutostart(IRegistry registry, SetupLocations locations, string? value)
    {
        if (value is null)
        {
            if (ReadAutostart(registry, locations) is null)
            {
                return;
            }

            using var existing = registry.CreateKey(RegistryHiveKind.CurrentUser, locations.RunKeyPath);
            existing.DeleteValue(AutostartService.ValueName);
            return;
        }

        using var key = registry.CreateKey(RegistryHiveKind.CurrentUser, locations.RunKeyPath);
        key.SetValue(AutostartService.ValueName, value);
    }

    /// <summary>
    /// Ordnet eine Ausnahme einer Fehlerart zu. <paramref name="existingFile"/>: Die Ausnahme kam beim Ersetzen oder
    /// Löschen einer vorhandenen Datei in einem Ordner, in den gerade noch geschrieben werden konnte; „Zugriff
    /// verweigert“ heißt dann fast immer, dass die Datei läuft (eine gestartete EXE lässt sich nicht löschen).
    /// </summary>
    public static SetupErrorKind Classify(Exception ex, bool existingFile = false) => ex switch
    {
        UnauthorizedAccessException when existingFile => SetupErrorKind.FilesInUse,
        UnauthorizedAccessException => SetupErrorKind.AccessDenied,
        IOException io when IsDiskFull(io) => SetupErrorKind.DiskFull,
        IOException io when IsInUse(io) => SetupErrorKind.FilesInUse,
        _ => SetupErrorKind.Unexpected,
    };

    /// <summary>Kurzer technischer Text für Protokoll und Ergebnis.</summary>
    public static string Describe(string what, Exception ex) => what + ": " + ex.GetType().Name + ": " + ex.Message;

    public static bool IsDiskFull(Exception ex) => Win32Code(ex) is ErrorDiskFull or ErrorHandleDiskFull;

    private static bool IsInUse(Exception ex) => Win32Code(ex) is ErrorSharingViolation or ErrorLockViolation or ErrorUnableToRemoveReplaced
        or ErrorUnableToMoveReplacement or ErrorUnableToMoveReplacement2 or ErrorUserMappedFile;

    /// <summary>Win32-Fehlercode aus einem HRESULT der Form 0x8007xxxx, sonst -1.</summary>
    private static int Win32Code(Exception ex) => (ex.HResult & 0xFFFF0000) == 0x80070000 ? ex.HResult & 0xFFFF : -1;
}

/// <summary>Benannte Sperre für Setup und Entfernen; wird auf demselben Thread freigegeben, der sie hält.</summary>
internal sealed class SetupLock : IDisposable
{
    private readonly Mutex _mutex;

    private SetupLock(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>Die Sperre, wenn sie frei war; sonst null (ein anderes Setup oder Entfernen läuft).</summary>
    public static SetupLock? TryAcquire(string name)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
        }
        catch (UnauthorizedAccessException)
        {
            return null; // vorhanden, gehört einem anderen Benutzerkontext
        }

        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // Ein früheres Setup ist abgestürzt: die Sperre gehört jetzt diesem.
        }

        return new SetupLock(mutex);
    }

    public void Dispose()
    {
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Nicht vom aktuellen Thread gehalten: beim Aufräumen unkritisch.
        }

        _mutex.Dispose();
    }
}

/// <summary>Merkt sich den ersten Fehler eines Laufs, der trotz Fehlern weitermacht (Entfernen).</summary>
internal sealed class FirstError
{
    public SetupErrorKind? Kind { get; private set; }

    public string? Detail { get; private set; }

    public void Add(SetupErrorKind kind, string detail)
    {
        if (Kind is null)
        {
            Kind = kind;
            Detail = detail;
        }
    }
}
