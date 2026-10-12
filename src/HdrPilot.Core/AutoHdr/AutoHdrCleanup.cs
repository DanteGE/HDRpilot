using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;

namespace HdrPilot.Core.AutoHdr;

/// <summary>Ergebnis von <see cref="AutoHdrCleanup.RestoreAll"/>.</summary>
/// <param name="Skipped">Grund, warum gemerkte Werte nicht angefasst wurden (anderer Schlüssel, nicht lesbar); sonst null.</param>
public sealed record AutoHdrCleanupResult(int Restored, int LeftAsIs, int Failed, string? Skipped = null)
{
    /// <summary>Alles zurückgenommen oder bewusst stehen gelassen; die Zustandsdateien sind gelöscht.</summary>
    public bool Complete => Skipped is null && Failed == 0;
}

/// <summary>
/// Auto-HDR-Werte ohne laufende App zurücknehmen (E88), für das Entfernen über den Setup: alle Werte aus
/// autohdr-state.json und den globalen Schalter aus autohdr-global.json, mit denselben Regeln wie die App (R1/R2/R3,
/// extern geänderte bleiben). Braucht keine Konfiguration. Gehören die Dateien zu einem anderen Schlüssel oder sind sie
/// nicht lesbar, bleibt alles, wie es ist. Danach werden die Dateien gelöscht; nicht Zurückgenommenes bleibt darin gemerkt.
/// </summary>
public static class AutoHdrCleanup
{
    /// <summary>Gibt es etwas zurückzunehmen (eine der beiden Zustandsdateien)?</summary>
    public static bool HasState(IFileSystem fileSystem, AppPaths paths) =>
        fileSystem.FileExists(paths.AutoHdrStateFile) || fileSystem.FileExists(paths.AutoHdrGlobalFile);

    /// <param name="log">Protokollzeilen (etwa das Setup-Protokoll).</param>
    public static AutoHdrCleanupResult RestoreAll(IGpuPreferenceStore store, IFileSystem fileSystem, AppPaths paths, Action<string> log)
    {
        var restored = 0;
        var leftAsIs = 0;
        var failed = 0;
        string? skipped = null;

        if (fileSystem.FileExists(paths.AutoHdrStateFile))
        {
            var load = AutoHdrFiles.Load<AutoHdrStateData>(fileSystem, paths.AutoHdrStateFile, d => d.IsValid(), clock: null);
            if (Problem(load, d => d.RegistryKey, store, paths.AutoHdrStateFile) is { } problem)
            {
                skipped = problem;
                log("Auto HDR: " + problem + ". Werte je Spiel bleiben unverändert, " + paths.AutoHdrStateFile + " bleibt.");
            }
            else if (load.Data is { } data)
            {
                var remaining = new List<AutoHdrStateEntry>();
                foreach (var entry in data.Entries)
                {
                    if (entry.Status == AutoHdrEntryStatus.ExternallyChanged)
                    {
                        leftAsIs++;
                        log("Auto HDR: " + entry.ValueName + " war extern geändert und bleibt, wie er ist.");
                        continue;
                    }

                    switch (TryRestore(store, entry.ValueName, entry.Original, entry.OriginalToken, entry.Written, GpuPreferenceText.PerGameOn, "Auto HDR: " + entry.ValueName, log))
                    {
                        case null:
                            failed++;
                            remaining.Add(entry);
                            break;
                        case AutoHdrRestoreOutcome.LeftAsIs:
                            leftAsIs++;
                            break;
                        default:
                            restored++;
                            break;
                    }
                }

                Finish(fileSystem, paths.AutoHdrStateFile, remaining.Count == 0 ? null : new AutoHdrStateData { RegistryKey = data.RegistryKey, Entries = remaining }, log);
            }
        }

        if (fileSystem.FileExists(paths.AutoHdrGlobalFile))
        {
            var load = AutoHdrFiles.Load<AutoHdrGlobalMarker>(fileSystem, paths.AutoHdrGlobalFile, m => m.IsValid(), clock: null);
            if (Problem(load, m => m.RegistryKey, store, paths.AutoHdrGlobalFile) is { } problem)
            {
                skipped ??= problem;
                log("Auto HDR global: " + problem + ". Der globale Schalter bleibt unverändert, " + paths.AutoHdrGlobalFile + " bleibt.");
            }
            else if (load.Data is { } marker)
            {
                switch (TryRestore(store, marker.ValueName, marker.Original, marker.OriginalToken, marker.Written, GpuPreferenceText.GlobalOn, "Auto HDR global: " + marker.ValueName, log))
                {
                    case null:
                        failed++;
                        break;
                    case AutoHdrRestoreOutcome.LeftAsIs:
                        leftAsIs++;
                        Finish(fileSystem, paths.AutoHdrGlobalFile, null, log);
                        break;
                    default:
                        restored++;
                        Finish(fileSystem, paths.AutoHdrGlobalFile, null, log);
                        break;
                }
            }
        }

        return new AutoHdrCleanupResult(restored, leftAsIs, failed, skipped);
    }

    /// <summary>Grund, die Datei nicht anzufassen (nicht lesbar, defekt, anderer Schlüssel); null, wenn sie fehlt oder passt.</summary>
    private static string? Problem<T>(AutoHdrFileLoad<T> load, Func<T, string> keyOf, IGpuPreferenceStore store, string path)
        where T : class => load.State switch
        {
            AutoHdrFileState.Unreadable => Path.GetFileName(path) + " nicht lesbar: " + load.Problem,
            AutoHdrFileState.Corrupt => Path.GetFileName(path) + " defekt: " + load.Problem,
            AutoHdrFileState.Loaded when !string.Equals(keyOf(load.Data!), store.KeyName, StringComparison.OrdinalIgnoreCase) =>
                "Zustand gehört zu " + keyOf(load.Data!) + ", nicht zu " + store.KeyName,
            _ => null,
        };

    private static AutoHdrRestoreOutcome? TryRestore(
        IGpuPreferenceStore store, string valueName, AutoHdrOriginalValue original, string? originalToken, string written, string onValue, string subject, Action<string> log)
    {
        try
        {
            var result = AutoHdrRestore.Apply(store, valueName, original, originalToken, written, onValue);
            log(AutoHdrRestore.Describe(result, subject, "Entfernen"));
            return result.Outcome;
        }
        catch (Exception ex)
        {
            log(subject + " ließ sich nicht zurücknehmen: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>Löscht die Datei (<paramref name="remaining"/> null) oder schreibt nur noch das nicht Zurückgenommene hinein.</summary>
    private static void Finish(IFileSystem fileSystem, string path, AutoHdrStateData? remaining, Action<string> log)
    {
        try
        {
            if (remaining is null)
            {
                fileSystem.DeleteFile(path);
                log("Gelöscht: " + path);
            }
            else
            {
                AutoHdrFiles.Save(fileSystem, path, remaining);
                log(remaining.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Auto-HDR-Werte bleiben gemerkt: " + path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log(path + " ließ sich nicht aktualisieren: " + ex.Message);
        }
    }
}
