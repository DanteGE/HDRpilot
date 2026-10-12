namespace HdrPilot.Core.Games.Scanners.Xbox;

/// <summary>Eine App des Pakets: <see cref="Id"/> ist der Teil hinter „!“ in der AppUserModelId.</summary>
public sealed record AppxApplication(string Id, string? Executable);

/// <summary>
/// Inhalt von <c>&lt;Spiel&gt;\Content\appxmanifest.xml</c>, soweit der Scanner ihn braucht. Das ist das Manifest des
/// registrierten Pakets: Der Paketordner unter WindowsApps ist am Zielrechner eine Verknüpfung (Junction) auf den Content-Ordner.
/// <see cref="MainPackageDependency"/> ist bei optionalen Paketen (Zusatzinhalte) der Paketname des Hauptspiels
/// (<c>Dependencies/uap3:MainPackageDependency/@Name</c>).
/// </summary>
public sealed record AppxManifest(
    string? IdentityName,
    string? IdentityPublisher,
    string? DisplayName,
    IReadOnlyList<AppxApplication> Applications,
    string? MainPackageDependency = null)
{
    public const string FileName = "appxmanifest.xml";

    /// <summary>
    /// Logos der ersten App (<c>VisualElements</c>: Square150x150Logo, Square44x44Logo) und des Pakets
    /// (<c>Properties/Logo</c>), relativ zum Paketordner wie in der Datei, ohne Zusatz wie <c>.scale-200</c> (E86).
    /// </summary>
    public IReadOnlyList<string> Logos { get; init; } = [];

    /// <summary>Liest die Datei. Wirft <see cref="InvalidDataException"/> (deutsch) bei kaputtem XML.</summary>
    public static AppxManifest Parse(byte[] data)
    {
        var root = XmlDocuments.LoadRoot(data, "Package", FileName);
        var identity = root.Child("Identity");
        var applications = root.Child("Applications")?.Children("Application")
            .Select(a => (Id: a.Attr("Id"), Executable: a.Attr("Executable")))
            .Where(a => a.Id is not null)
            .Select(a => new AppxApplication(a.Id!, a.Executable))
            .ToList() ?? [];

        var mainPackage = root.Child("Dependencies")?.Child("MainPackageDependency");
        var visuals = root.Child("Applications")?.Child("Application")?.Child("VisualElements");
        string?[] logos = [visuals.Attr("Square150x150Logo"), visuals.Attr("Square44x44Logo"), root.Child("Properties")?.Child("Logo").Text()];

        return new AppxManifest(
            identity.Attr("Name"),
            identity.Attr("Publisher"),
            root.Child("Properties")?.Child("DisplayName").Text(),
            applications,
            mainPackage is null ? null : mainPackage.Attr("Name") ?? string.Empty)
        {
            Logos = logos.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }
}
