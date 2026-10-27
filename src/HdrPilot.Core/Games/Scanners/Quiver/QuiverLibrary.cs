using System.Text.Json;
using HdrPilot.Core.Games.Scanners.Common;
using HdrPilot.Core.Resources;

namespace HdrPilot.Core.Games.Scanners.Quiver;

/// <summary>Ein Eintrag aus <c>apps.json</c> von Quiver (nur die Felder, die HDRpilot braucht).</summary>
/// <param name="FolderName">Ordner unter Apps, zugleich die ID in Quiver (Pflicht, eindeutig).</param>
/// <param name="InstallPath">Eigener Spielordner statt <c>&lt;Apps&gt;\&lt;folderName&gt;</c> (selten; null: Standard).</param>
/// <param name="IconUrl">Adresse des Covers (<c>appIconUrl</c>), Schlüssel des Bildzwischenspeichers von Quiver.</param>
internal sealed record QuiverApp(string? FolderName, string? Name, string? Project, string? CustomDisplayName, string? InstallPath, string? IconUrl)
{
    /// <summary>
    /// Name in der Bibliothek wie in Quiver: eigener Anzeigename, sonst Name, sonst Projekt, sonst der Ordnername.
    /// </summary>
    public string DisplayName =>
        FirstNonEmpty(CustomDisplayName, Name, Project) ?? FolderName ?? string.Empty;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}

/// <summary>Verknüpfung eines per Windows Installer installierten Spiels (<c>windows-installer.json</c> von Quiver).</summary>
internal sealed record QuiverInstallerReceipt(string? Phase, string? ExecutablePath);

/// <summary>
/// Liest die Bibliothek von Quiver (E91): <c>settings.json</c> (nur <c>AppsPath</c>), <c>apps.json</c> (Feld <c>apps</c>) und
/// <c>windows-installer.json</c> eines Spiels. Kodierung und Fehlermeldungen wie bei den anderen Scannern
/// (<see cref="ScannerJson"/>); Eigenschaften ohne Beachtung der Groß-/Kleinschreibung (Quiver schreibt apps.json in
/// camelCase, settings.json und die Verknüpfung in PascalCase).
/// </summary>
internal static class QuiverLibrary
{
    /// <summary>Größer ist keine Bibliothek (Schutz vor fremden Riesendateien).</summary>
    public const int MaxJsonBytes = 16 * 1024 * 1024;

    public const string InstallerReceiptFile = "windows-installer.json";

    /// <summary>Phasen der Verknüpfung, die Quiver selbst annimmt; nur „Linked“ hat eine verknüpfte EXE.</summary>
    private static readonly string[] KnownPhases = ["Installing", "NeedsExecutable", "Linked"];

    /// <summary><c>AppsPath</c> aus settings.json; leer oder fehlend: null. Wirft <see cref="InvalidDataException"/>.</summary>
    public static string? ReadAppsPath(byte[] bytes)
    {
        using var document = ScannerJson.ParseObject(bytes);
        var value = ScannerJson.GetString(document.RootElement, "AppsPath")?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Einträge aus apps.json in Dateireihenfolge. Ein Eintrag, der kein Objekt ist oder eine ungültige Zeichenfolge
    /// enthält, kommt als Fehlertext in <paramref name="problems"/> (Index ab 0); die übrigen bleiben. Wirft
    /// <see cref="InvalidDataException"/>, wenn die Datei kein JSON-Objekt mit dem Array <c>apps</c> ist.
    /// </summary>
    public static IReadOnlyList<QuiverApp> ReadApps(byte[] bytes, List<(int Index, string Problem)> problems)
    {
        using var document = ScannerJson.ParseObject(bytes);
        if (!ScannerJson.TryGetProperty(document.RootElement, "apps", out var apps) || apps.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(ScanStrings.Quiver_AppsMissing);
        }

        var result = new List<QuiverApp>();
        var index = -1;
        foreach (var app in apps.EnumerateArray())
        {
            index++;
            if (app.ValueKind != JsonValueKind.Object)
            {
                problems.Add((index, ScanStrings.Quiver_EntryNotObject));
                continue;
            }

            try
            {
                result.Add(new QuiverApp(
                    ScannerJson.GetString(app, "folderName"),
                    ScannerJson.GetString(app, "name"),
                    ScannerJson.GetString(app, "project"),
                    ScannerJson.GetString(app, "customDisplayName"),
                    ScannerJson.GetString(app, "installPath"),
                    ScannerJson.GetString(app, "appIconUrl")));
            }
            catch (InvalidDataException ex)
            {
                problems.Add((index, ex.Message));
            }
        }

        return result;
    }

    /// <summary>windows-installer.json; Phase unbekannt oder keine Zeichenfolge: <see cref="InvalidDataException"/> (wie in Quiver).</summary>
    public static QuiverInstallerReceipt ReadInstallerReceipt(byte[] bytes)
    {
        using var document = ScannerJson.ParseObject(bytes);
        var phase = ScannerJson.GetString(document.RootElement, "Phase");
        if (phase is null || !KnownPhases.Contains(phase, StringComparer.Ordinal))
        {
            throw new InvalidDataException(ScanText.Format(ScanStrings.Quiver_UnknownPhase, phase));
        }

        return new QuiverInstallerReceipt(phase, ScannerJson.GetString(document.RootElement, "ExecutablePath"));
    }
}
