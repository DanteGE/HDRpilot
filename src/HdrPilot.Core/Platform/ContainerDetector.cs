using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Platform;

/// <summary>Ergebnis der Container-Prüfung (E84).</summary>
/// <param name="Folder">Geprüfter Ordner.</param>
/// <param name="PackageFolder">
/// Ordner der App unter %LOCALAPPDATA%\Packages, in deren Container Windows die Schreibzugriffe umleitet (etwa
/// „Claude_pzs8sxrjxfjjc“); null, wenn nicht umgeleitet oder unbekannt.
/// </param>
/// <param name="Problem">
/// Warum die Prüfung nichts sagen kann (Probe nicht schreibbar, Packages nicht lesbar) oder was beim Aufräumen
/// scheiterte; sonst null. Ohne <paramref name="PackageFolder"/> gilt dann „nicht umgeleitet“: blockiert wird nur, was
/// sicher erkannt ist.
/// </param>
public sealed record ContainerRedirection(string Folder, string? PackageFolder, string? Problem = null)
{
    public bool Redirected => PackageFolder is not null;
}

/// <summary>Prüft, ob Windows Schreibzugriffe auf einen Ordner in den Container einer anderen App umleitet (E84).</summary>
public interface IContainerDetector
{
    /// <summary>Wirft nie; Fehler stehen in <see cref="ContainerRedirection.Problem"/>.</summary>
    ContainerRedirection Detect(string folder);
}

/// <summary>
/// Erkennt, ob dieser Prozess im MSIX-Container einer anderen App läuft (E84), etwa weil er aus der Claude-App heraus
/// gestartet wurde. Windows leitet dann neue Dateien unter %APPDATA% und %LOCALAPPDATA% (und HKCU\Software) in den
/// privaten Speicher dieser App um (<c>%LOCALAPPDATA%\Packages\&lt;App&gt;\LocalCache\Local|Roaming\…</c>); im Container
/// sieht alles normal aus, von außen fehlt es. <c>GetCurrentPackageFullName</c> meldet für solche Prozesse kein Paket,
/// deshalb eine Probe: eine eindeutige Datei in den Ordner schreiben und unter <c>Packages\*\LocalCache</c> am gleichen
/// relativen Pfad suchen. Die Probe wird immer gelöscht (im Container verschwindet damit auch die umgeleitete Kopie),
/// selbst angelegte Ordner ebenso, sobald sie leer sind. Ordner außerhalb von %APPDATA% und %LOCALAPPDATA% gelten ohne
/// Probe als nicht umgeleitet; %TEMP% liegt darunter und wird geprüft (Windows leitet ihn nicht um, die Probe findet sich
/// dann nirgends). Kostet einmal eine Ordnerliste von Packages (gemessen 76 ms bei 168 Apps).
/// </summary>
public sealed class ContainerDetector : IContainerDetector
{
    /// <summary>Anfang des Dateinamens der Probe; der Rest ist eine GUID, damit nie eine fremde Datei zählt.</summary>
    public const string ProbePrefix = ".hdrpilot-probe-";

    private readonly IAppEnvironment _environment;
    private readonly Action<string>? _afterProbeWritten;

    /// <param name="environment">Liefert %LOCALAPPDATA% (mit Packages) und %APPDATA%.</param>
    /// <param name="afterProbeWritten">
    /// Nur für Tests: wird mit dem Pfad der Probe aufgerufen, bevor gesucht wird (bildet dort die Umleitung nach).
    /// </param>
    public ContainerDetector(IAppEnvironment environment, Action<string>? afterProbeWritten = null)
    {
        _environment = environment;
        _afterProbeWritten = afterProbeWritten;
    }

    public ContainerRedirection Detect(string folder)
    {
        string full;
        string localAppData;
        string? cacheRelative;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            localAppData = _environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            cacheRelative = RedirectedRelative(full, localAppData, "Local")
                ?? RedirectedRelative(full, _environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Roaming");
        }
        catch (Exception ex)
        {
            return new ContainerRedirection(folder, null, Describe("Ordner bestimmen", ex));
        }

        if (cacheRelative is null)
        {
            return new ContainerRedirection(full, null); // nicht unter AppData: keine Umleitung möglich
        }

        var created = MissingFolders(full);
        var probe = Path.Combine(full, ProbePrefix + Guid.NewGuid().ToString("N") + ".tmp");
        string? package = null;
        string? problem = null;
        try
        {
            Directory.CreateDirectory(full);
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                // leere Datei genügt
            }

            _afterProbeWritten?.Invoke(probe);
            package = FindPackage(Path.Combine(localAppData, "Packages"), Path.Combine(cacheRelative, Path.GetFileName(probe)));
        }
        catch (Exception ex)
        {
            problem = Describe("Probe schreiben oder suchen", ex);
        }
        finally
        {
            problem = Join(problem, Cleanup(probe, created));
        }

        return new ContainerRedirection(full, package, problem);
    }

    /// <summary>
    /// Pfad unter <c>LocalCache</c> (<paramref name="kind"/> = Local oder Roaming plus relativer Pfad), wenn
    /// <paramref name="full"/> unter <paramref name="root"/> liegt; sonst null.
    /// </summary>
    private static string? RedirectedRelative(string full, string root, string kind)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (string.Equals(full, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return kind;
        }

        return full.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(kind, full[(normalizedRoot.Length + 1)..])
            : null;
    }

    /// <summary>Erste App unter <paramref name="packagesRoot"/>, deren LocalCache die Probe enthält; sonst null.</summary>
    private static string? FindPackage(string packagesRoot, string relativeProbe)
    {
        if (!Directory.Exists(packagesRoot))
        {
            return null;
        }

        foreach (var package in Directory.EnumerateDirectories(packagesRoot))
        {
            if (File.Exists(Path.Combine(package, "LocalCache", relativeProbe)))
            {
                return Path.GetFileName(package);
            }
        }

        return null;
    }

    /// <summary>Ordner, die es vor der Probe noch nicht gibt (der Ordner selbst und fehlende übergeordnete), tiefste zuerst.</summary>
    private static List<string> MissingFolders(string full)
    {
        var missing = new List<string>();
        try
        {
            for (var dir = full; !string.IsNullOrEmpty(dir) && !Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
            {
                missing.Add(dir);
            }
        }
        catch (Exception)
        {
            // Ungültiger Pfad: das Anlegen scheitert gleich mit einer eigenen Meldung.
        }

        return missing;
    }

    /// <summary>Probe löschen, dann die selbst angelegten Ordner, wenn leer. Liefert, was dabei scheiterte (sonst null).</summary>
    private static string? Cleanup(string probe, IReadOnlyList<string> createdFolders)
    {
        string? problem = null;
        try
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
        catch (Exception ex)
        {
            problem = Describe("Probe löschen (" + probe + ")", ex);
        }

        foreach (var dir in createdFolders)
        {
            try
            {
                if (!RemoveIfEmpty(dir))
                {
                    problem = Join(problem, "Ordner bleibt, weil nicht leer: " + dir + " (" + string.Join(", ", Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName)) + ")");
                }
            }
            catch (Exception ex)
            {
                problem = Join(problem, Describe("Ordner entfernen (" + dir + ")", ex));
            }
        }

        return problem;
    }

    /// <summary>
    /// Entfernt einen selbst angelegten Ordner, sobald er leer ist. Eine eben gelöschte Probe kann noch kurz darin stehen
    /// (etwa solange ein Virenscanner sie offen hält); deshalb höchstens eine halbe Sekunde lang erneut nachsehen.
    /// False, wenn er danach noch etwas enthält (dann bleibt er).
    /// </summary>
    private static bool RemoveIfEmpty(string dir)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (!Directory.Exists(dir))
            {
                return true;
            }

            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                return true;
            }

            if (attempt == 20)
            {
                return false;
            }

            Thread.Sleep(25);
        }
    }

    private static string? Join(string? a, string? b) => a is null ? b : b is null ? a : a + "; " + b;

    private static string Describe(string what, Exception ex) => what + ": " + ex.GetType().Name + ": " + ex.Message;
}
