using HdrPilot.Core.Games.Scanners.Common;

namespace HdrPilot.Core.Games.Scanners.Epic;

/// <summary>
/// Die für HdrPilot relevanten Felder einer Epic-Manifestdatei (<c>…\Data\Manifests\&lt;InstallationGuid&gt;.item</c>).
/// Fehlende oder falsch typisierte Felder sind null bzw. leer; ob das ein Fehler ist, entscheidet der Scanner.
/// </summary>
internal sealed record EpicManifest
{
    public string? DisplayName { get; init; }

    public string? InstallLocation { get; init; }

    /// <summary>Start-EXE relativ zu <see cref="InstallLocation"/>, oft mit Schrägstrichen ("newRoot/Game.exe").</summary>
    public string? LaunchExecutable { get; init; }

    public string? AppName { get; init; }

    /// <summary>AppName des Hauptspiels; weicht bei Zusatzinhalten (DLC) von <see cref="AppName"/> ab.</summary>
    public string? MainGameAppName { get; init; }

    public string? CatalogNamespace { get; init; }

    public string? CatalogItemId { get; init; }

    public bool IsIncompleteInstall { get; init; }

    /// <summary>bIsExecutable; null, wenn das Feld fehlt.</summary>
    public bool? IsExecutable { get; init; }

    /// <summary>Etwa ["public", "games", "applications"].</summary>
    public IReadOnlyList<string> AppCategories { get; init; } = [];

    /// <summary>Prozess des Hauptfensters, etwa "RDR2.exe" neben dem Starter "PlayRDR2.exe". Meist leer.</summary>
    public string? MainWindowProcessName { get; init; }

    public IReadOnlyList<string> ProcessNames { get; init; } = [];
}

internal static class EpicManifestReader
{
    /// <summary>Liest ein Manifest. Wirft <see cref="InvalidDataException"/> bei falscher Kodierung oder ungültigem JSON.</summary>
    public static EpicManifest Parse(byte[] bytes)
    {
        using var document = ScannerJson.ParseObject(bytes);
        var root = document.RootElement;
        return new EpicManifest
        {
            DisplayName = ScannerJson.GetString(root, "DisplayName"),
            InstallLocation = ScannerJson.GetString(root, "InstallLocation"),
            LaunchExecutable = ScannerJson.GetString(root, "LaunchExecutable"),
            AppName = ScannerJson.GetString(root, "AppName"),
            MainGameAppName = ScannerJson.GetString(root, "MainGameAppName"),
            CatalogNamespace = ScannerJson.GetString(root, "CatalogNamespace"),
            CatalogItemId = ScannerJson.GetString(root, "CatalogItemId"),
            IsIncompleteInstall = ScannerJson.GetBool(root, "bIsIncompleteInstall") ?? false,
            IsExecutable = ScannerJson.GetBool(root, "bIsExecutable"),
            AppCategories = ScannerJson.GetStringArray(root, "AppCategories"),
            MainWindowProcessName = ScannerJson.GetString(root, "MainWindowProcessName"),
            ProcessNames = ScannerJson.GetStringArray(root, "ProcessNames"),
        };
    }
}
