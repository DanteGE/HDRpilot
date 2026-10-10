namespace HdrPilot.Core.Games.Scanners.Xbox;

/// <summary>Ein Eintrag aus <c>ExecutableList</c>: <see cref="Name"/> ist relativ zum Content-Ordner, oft mit Schrägstrichen.</summary>
public sealed record GameConfigExecutable(string Name, string? Id, string? TargetDeviceFamily, string? OverrideDisplayName)
{
    /// <summary>True für PC-EXEs: TargetDeviceFamily fehlt oder ist „PC“ (nicht „Scarlett“/„XboxOne“ für Konsolen).</summary>
    public bool IsForPc => TargetDeviceFamily is null || string.Equals(TargetDeviceFamily, "PC", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Inhalt von <c>&lt;Spiel&gt;\Content\MicrosoftGame.config</c> (Microsoft GDK), soweit der Scanner ihn braucht:
/// <c>Identity</c> (Name, Publisher, Version), <c>ShellVisuals/@DefaultDisplayName</c> und <c>ExecutableList/Executable</c>.
/// <see cref="IsDownloadableContent"/> erkennt Zusatzinhalte (DLC): Laut GDK-Dokumentation („Packaging downloadable
/// content“) hat deren Config keine <c>ExecutableList</c>, dafür <c>TargetDeviceFamilyForDLC</c>,
/// <c>AllowedProducts</c> und <c>DesktopRegistration/MainPackageDependency</c>.
/// </summary>
public sealed record MicrosoftGameConfig(
    string IdentityName,
    string IdentityPublisher,
    string? IdentityVersion,
    string? DefaultDisplayName,
    IReadOnlyList<GameConfigExecutable> Executables,
    bool IsDownloadableContent = false)
{
    public const string FileName = "MicrosoftGame.config";

    /// <summary>
    /// Logos aus <c>ShellVisuals</c>, größtes zuerst (Square480x480Logo, Square150x150Logo, StoreLogo, Square44x44Logo),
    /// relativ zum Content-Ordner und so, wie sie in der Datei stehen (ohne Zusatz wie <c>.scale-200</c>, E86).
    /// </summary>
    public IReadOnlyList<string> Logos { get; init; } = [];

    /// <summary>Liest die Datei. Wirft <see cref="InvalidDataException"/> (deutsch) bei kaputtem XML oder fehlender Identity.</summary>
    public static MicrosoftGameConfig Parse(byte[] data)
    {
        var root = XmlDocuments.LoadRoot(data, "Game", FileName);
        var identity = root.Child("Identity")
            ?? throw new InvalidDataException(FileName + ": Element <Identity> fehlt.");
        var name = identity.Attr("Name")
            ?? throw new InvalidDataException(FileName + ": Identity/@Name (Paketname) fehlt.");
        var publisher = identity.Attr("Publisher")
            ?? throw new InvalidDataException(FileName + ": Identity/@Publisher fehlt.");

        var executables = root.Child("ExecutableList")?.Children("Executable")
            .Select(e => (Name: e.Attr("Name"), Element: e))
            .Where(e => e.Name is not null)
            .Select(e => new GameConfigExecutable(e.Name!, e.Element.Attr("Id"), e.Element.Attr("TargetDeviceFamily"), e.Element.Attr("OverrideDisplayName")))
            .ToList() ?? [];

        var isDlc = root.Child("TargetDeviceFamilyForDLC") is not null
            || root.Child("AllowedProducts") is not null
            || root.Child("DesktopRegistration")?.Child("MainPackageDependency") is not null;

        var visuals = root.Child("ShellVisuals");
        string?[] logos = [visuals.Attr("Square480x480Logo"), visuals.Attr("Square150x150Logo"), visuals.Attr("StoreLogo"), visuals.Attr("Square44x44Logo")];

        return new MicrosoftGameConfig(
            name,
            publisher,
            identity.Attr("Version"),
            visuals.Attr("DefaultDisplayName"),
            executables,
            isDlc)
        {
            Logos = logos.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }
}
