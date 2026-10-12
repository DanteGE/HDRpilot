using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;

namespace HdrPilot.Core.AutoHdr;

/// <summary>Ergebnis von <see cref="AutoHdrCleanup.RestoreAll"/>.</summary>
/// <param name="Skipped">Grund, warum der gemerkte Schalter nicht angefasst wurde (anderer Schlüssel, nicht lesbar); sonst null.</param>
public sealed record AutoHdrCleanupResult(int Restored, int LeftAsIs, int Failed, string? Skipped = null)
{
    /// <summary>Zurückgestellt oder bewusst stehen gelassen; der Merker ist gelöscht.</summary>
    public bool Complete => Skipped is null && Failed == 0;
}

/// <summary>
/// Globalen Auto-HDR-Schalter ohne laufende App zurückstellen (E88), für das Entfernen über den Setup: Hinterließ ein
/// Absturz von HDRpilot autohdr-global.json, gilt dieselbe Regel wie in der App (R1/R2/R3, eine Änderung durch den Nutzer
/// bleibt). Braucht keine Konfiguration. Gehört der Merker zu einem anderen Schlüssel oder ist er oder sein Kennzeichen
/// (autohdr-global.done) nicht lesbar, bleibt alles, wie es ist; ein erledigter (passendes Kennzeichen, aus Vorabversionen
/// <see cref="AutoHdrEntryStatus.Released"/>) wird nur gelöscht. Werte je Spiel-EXE schreibt HDRpilot nicht, also gibt es
/// davon nichts zurückzunehmen.
/// </summary>
public static class AutoHdrCleanup
{
    /// <summary>Gibt es etwas zurückzustellen (Merker vorhanden)?</summary>
    public static bool HasState(IFileSystem fileSystem, AppPaths paths) => fileSystem.FileExists(paths.AutoHdrGlobalFile);

    /// <param name="log">Protokollzeilen (etwa das Setup-Protokoll).</param>
    public static AutoHdrCleanupResult RestoreAll(IGpuPreferenceStore store, IFileSystem fileSystem, AppPaths paths, Action<string> log)
    {
        var path = paths.AutoHdrGlobalFile;
        if (!fileSystem.FileExists(path))
        {
            return new AutoHdrCleanupResult(0, 0, 0);
        }

        var load = AutoHdrFiles.Load<AutoHdrGlobalMarker>(fileSystem, path, m => m.IsValid(), clock: null);
        if (Problem(load, store, path) is { } problem)
        {
            log("Auto HDR global: " + problem + ". Der globale Schalter bleibt unverändert, " + path + " bleibt.");
            return new AutoHdrCleanupResult(0, 0, 0, problem);
        }

        if (load.Data is not { } marker)
        {
            return new AutoHdrCleanupResult(0, 0, 0);
        }

        var donePath = paths.AutoHdrGlobalDoneFile;
        var done = AutoHdrFiles.Load<AutoHdrGlobalDone>(fileSystem, donePath, d => d.IsValid(), clock: null);
        if (done.State is AutoHdrFileState.Unreadable or AutoHdrFileState.Corrupt)
        {
            var doneProblem = Path.GetFileName(donePath) + (done.State == AutoHdrFileState.Unreadable ? " nicht lesbar: " : " defekt: ") + done.Problem;
            log("Auto HDR global: " + doneProblem + ". Der globale Schalter bleibt unverändert, " + path + " bleibt.");
            return new AutoHdrCleanupResult(0, 0, 0, doneProblem);
        }

        if (done.Data?.Matches(marker) == true || marker.Status == AutoHdrEntryStatus.Released)
        {
            // Die App hatte schon nichts mehr zurückzustellen (etwa die Wahl des Nutzers), nur die Datei blieb: nur löschen.
            log("Auto HDR global: " + Path.GetFileName(path) + " ist erledigt, nichts zurückzustellen.");
            Discard(fileSystem, path, donePath, marker, log);
            return new AutoHdrCleanupResult(0, 0, 0);
        }

        AutoHdrRestoreResult result;
        try
        {
            result = AutoHdrRestore.Apply(store, marker);
        }
        catch (Exception ex)
        {
            log("Auto HDR global: " + marker.ValueName + " ließ sich nicht zurückstellen: " + ex.GetType().Name + ": " + ex.Message);
            return new AutoHdrCleanupResult(0, 0, 1);
        }

        log(AutoHdrRestore.Describe(result, "Auto HDR global: " + marker.ValueName, "Entfernen"));
        Discard(fileSystem, path, donePath, marker, log);
        return result.Outcome == AutoHdrRestoreOutcome.LeftAsIs ? new AutoHdrCleanupResult(0, 1, 0) : new AutoHdrCleanupResult(1, 0, 0);
    }

    /// <summary>
    /// Erledigten Merker löschen, danach sein Kennzeichen (ohne Merker gilt es nichts mehr). Bleibt der Merker gesperrt,
    /// hält autohdr-global.done fest, dass er erledigt ist, damit ein späterer Start ihn nie zurückstellt (eine eigene
    /// Datei: die Sperre verhindert unter NTFS auch das Ersetzen des Merkers).
    /// </summary>
    private static void Discard(IFileSystem fileSystem, string path, string donePath, AutoHdrGlobalMarker marker, Action<string> log)
    {
        if (!Delete(fileSystem, path, log))
        {
            try
            {
                AutoHdrFiles.Save(fileSystem, donePath, AutoHdrGlobalDone.Of(marker));
                log("Als erledigt vermerkt: " + donePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log(donePath + " ließ sich nicht speichern: " + ex.Message);
            }

            return;
        }

        if (fileSystem.FileExists(donePath))
        {
            Delete(fileSystem, donePath, log);
        }
    }

    private static bool Delete(IFileSystem fileSystem, string path, Action<string> log)
    {
        try
        {
            fileSystem.DeleteFile(path);
            log("Gelöscht: " + path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log(path + " ließ sich nicht löschen: " + ex.Message);
            return false;
        }
    }

    /// <summary>Grund, den Merker nicht anzufassen (nicht lesbar, defekt, anderer Schlüssel); null, wenn er fehlt oder passt.</summary>
    private static string? Problem(AutoHdrFileLoad<AutoHdrGlobalMarker> load, IGpuPreferenceStore store, string path) => load.State switch
    {
        AutoHdrFileState.Unreadable => Path.GetFileName(path) + " nicht lesbar: " + load.Problem,
        AutoHdrFileState.Corrupt => Path.GetFileName(path) + " defekt: " + load.Problem,
        AutoHdrFileState.Loaded when !string.Equals(load.Data!.RegistryKey, store.KeyName, StringComparison.OrdinalIgnoreCase) =>
            "Zustand gehört zu " + load.Data.RegistryKey + ", nicht zu " + store.KeyName,
        _ => null,
    };
}
