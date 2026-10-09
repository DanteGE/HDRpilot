using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Platform;

/// <summary>Ergebnis der Container-Prüfung (E84).</summary>
/// <param name="Folder">Ordner der Probe (%LOCALAPPDATA%).</param>
/// <param name="PackageFolder">
/// Ordner der App unter %LOCALAPPDATA%\Packages, in deren Container dieser Prozess läuft und Windows die Schreibzugriffe
/// umleitet (etwa „Claude_pzs8sxrjxfjjc“); null, wenn kein Container erkannt ist.
/// </param>
/// <param name="Problem">
/// Warum die Prüfung nichts sagen kann (Probe nicht schreibbar, Packages nicht lesbar) oder was beim Aufräumen
/// scheiterte; sonst null. Ohne <paramref name="PackageFolder"/> gilt dann „kein Container“: blockiert wird nur, was
/// sicher erkannt ist.
/// </param>
public sealed record ContainerRedirection(string Folder, string? PackageFolder, string? Problem = null)
{
    public bool Redirected => PackageFolder is not null;
}

/// <summary>Prüft, ob dieser Prozess im Container einer anderen App läuft, die Windows Schreibzugriffe umleiten lässt (E84).</summary>
public interface IContainerDetector
{
    /// <summary>Wirft nie; Fehler stehen in <see cref="ContainerRedirection.Problem"/>.</summary>
    ContainerRedirection Detect();
}

/// <summary>
/// Erkennt, ob dieser Prozess im MSIX-Container einer anderen App läuft (E84), etwa weil er aus der Claude-App heraus
/// gestartet wurde. Windows leitet dann HKCU\Software (Autostart, Eintrag unter „Installierte Apps“) und Neues unter
/// %APPDATA% und %LOCALAPPDATA% in den privaten Speicher dieser App um
/// (<c>%LOCALAPPDATA%\Packages\&lt;App&gt;\LocalCache\Local|Roaming\…</c>); im Container sieht alles normal aus, von außen
/// fehlt es. <c>GetCurrentPackageFullName</c> meldet für solche Prozesse kein Paket, deshalb eine Probe: eine eindeutige
/// Datei direkt in %LOCALAPPDATA% anlegen und unter <c>Packages\*\LocalCache\Local</c> am gleichen Namen suchen. Direkt
/// in der Wurzel, nicht im Datenordner: Windows leitet nur um, was in einem Ordner entsteht, den es im Container gibt
/// (die Wurzel gibt es dort immer); in einem Ordner, der nur außerhalb besteht (etwa ein schon vorhandenes
/// %LOCALAPPDATA%\HDRpilot), schreibt es echt. Eine Probe dort sähe unverdächtig aus, obwohl die Registry trotzdem nur
/// virtuell geändert würde. Die Probe wird immer gelöscht (im Container verschwindet damit auch die umgeleitete Kopie).
/// Kostet einmal eine Ordnerliste von Packages (gemessen 76 ms bei 168 Apps).
/// </summary>
public sealed class ContainerDetector : IContainerDetector
{
    /// <summary>Anfang des Dateinamens der Probe; der Rest ist eine GUID, damit nie eine fremde Datei zählt.</summary>
    public const string ProbePrefix = ".hdrpilot-probe-";

    private readonly IAppEnvironment _environment;
    private readonly Action<string>? _afterProbeWritten;

    /// <param name="environment">Liefert %LOCALAPPDATA% (Ort der Probe, darunter Packages).</param>
    /// <param name="afterProbeWritten">
    /// Nur für Tests: wird mit dem Pfad der Probe aufgerufen, bevor gesucht wird (bildet dort die Umleitung nach).
    /// </param>
    public ContainerDetector(IAppEnvironment environment, Action<string>? afterProbeWritten = null)
    {
        _environment = environment;
        _afterProbeWritten = afterProbeWritten;
    }

    public ContainerRedirection Detect()
    {
        string root;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        }
        catch (Exception ex)
        {
            return new ContainerRedirection("%LOCALAPPDATA%", null, Describe("Ordner bestimmen", ex));
        }

        var name = ProbePrefix + Guid.NewGuid().ToString("N") + ".tmp";
        var probe = Path.Combine(root, name);
        var written = false;
        string? package = null;
        string? problem = null;
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                written = true; // leere Datei genügt
            }

            _afterProbeWritten?.Invoke(probe);
            package = FindPackage(Path.Combine(root, "Packages"), Path.Combine("Local", name));
        }
        catch (Exception ex)
        {
            problem = Describe("Probe schreiben oder suchen", ex);
        }
        finally
        {
            if (written)
            {
                problem = Join(problem, Delete(probe));
            }
        }

        return new ContainerRedirection(root, package, problem);
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

    /// <summary>Löscht die Probe; liefert, was dabei scheiterte (sonst null).</summary>
    private static string? Delete(string probe)
    {
        try
        {
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return Describe("Probe löschen (" + probe + ")", ex);
        }
    }

    private static string? Join(string? a, string? b) => a is null ? b : b is null ? a : a + "; " + b;

    private static string Describe(string what, Exception ex) => what + ": " + ex.GetType().Name + ": " + ex.Message;
}
