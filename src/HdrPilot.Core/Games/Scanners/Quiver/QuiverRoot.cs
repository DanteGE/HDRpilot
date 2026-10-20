using System.Xml;
using System.Xml.Linq;
using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Games.Scanners.Quiver;

/// <summary>Aufbau eines Quiver-Ordners.</summary>
internal enum QuiverLayout
{
    /// <summary>Kein Quiver-Ordner.</summary>
    None,

    /// <summary>Ab 3.0 (Velopack): <c>current\sq.version</c> mit der Kennung QuiverLauncher, Bibliothek daneben.</summary>
    Velopack,

    /// <summary>Bis 2.4.x: <c>Quiver.exe</c>, <c>apps.json</c> und <c>Apps\</c> im selben Ordner.</summary>
    Legacy,
}

/// <summary>
/// Erkennt den Ordner des Quiver Launchers (E91) an seinem eigenen Kennzeichen, nicht am Namen: Velopack legt in jedem
/// Programm <c>current\sq.version</c> an (eine kleine NuSpec-Datei); nur mit <c>&lt;id&gt;QuiverLauncher&lt;/id&gt;</c> ist es
/// Quiver, ein anderes Velopack-Programm im selben Ordner fällt so heraus. Daneben liegt die Bibliothek
/// (<c>apps.json</c>, <c>settings.json</c>). Die alte Struktur bis 2.4.x hat statt dessen <c>Quiver.exe</c>, <c>apps.json</c>
/// und <c>Apps\</c> (laut MIGRATING.md von Quiver). Nur lesend, Dateien geteilt geöffnet.
/// </summary>
internal static class QuiverRoot
{
    public const string AppId = "QuiverLauncher";

    public const string CurrentFolder = "current";

    public const string ManifestFile = "sq.version";

    public const string MainExe = "QuiverLauncher.exe";

    public const string LegacyExe = "Quiver.exe";

    public const string AppsJson = "apps.json";

    public const string SettingsJson = "settings.json";

    public const string DefaultAppsFolder = "Apps";

    public const string CacheFolder = "Cache";

    /// <summary>sq.version ist wenige Hundert Byte groß; mehr wird nie gelesen.</summary>
    public const int MaxManifestBytes = 64 * 1024;

    /// <summary>
    /// Aufbau von <paramref name="folder"/>. Mit <paramref name="requireLibrary"/> zählt ein Velopack-Ordner nur mit
    /// <c>apps.json</c> oder <c>settings.json</c> (sonst gibt es nichts zu lesen); ohne genügt das Kennzeichen (etwa für
    /// den Ordner eines gerade gestarteten Quiver, der seine Bibliothek noch nicht angelegt hat). Wirft nicht.
    /// </summary>
    public static QuiverLayout Detect(IFileSystem fileSystem, string folder, bool requireLibrary = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            {
                return QuiverLayout.None;
            }

            var manifest = Path.Combine(folder, CurrentFolder, ManifestFile);
            if (fileSystem.FileExists(manifest) && IsQuiverManifest(fileSystem.ReadShared(manifest, MaxManifestBytes)))
            {
                return !requireLibrary || fileSystem.FileExists(Path.Combine(folder, AppsJson)) || fileSystem.FileExists(Path.Combine(folder, SettingsJson))
                    ? QuiverLayout.Velopack
                    : QuiverLayout.None;
            }

            return fileSystem.FileExists(Path.Combine(folder, LegacyExe))
                && fileSystem.FileExists(Path.Combine(folder, AppsJson))
                && fileSystem.DirectoryExists(Path.Combine(folder, DefaultAppsFolder))
                ? QuiverLayout.Legacy
                : QuiverLayout.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return QuiverLayout.None;
        }
    }

    /// <summary>
    /// True, wenn die NuSpec-Datei von Velopack (<c>&lt;package&gt;&lt;metadata&gt;&lt;id&gt;</c>, Namensraum egal) Quiver nennt.
    /// Unvollständig gelesen, kein XML oder mit DTD: false.
    /// </summary>
    internal static bool IsQuiverManifest(SharedRead manifest)
    {
        if (!manifest.IsComplete || manifest.Bytes.Length == 0)
        {
            return false;
        }

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = true };
            using var reader = XmlReader.Create(new MemoryStream(manifest.Bytes, writable: false), settings);
            var root = XDocument.Load(reader).Root;
            var id = root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")?.Elements().FirstOrDefault(e => e.Name.LocalName == "id");
            return root?.Name.LocalName == "package" && string.Equals(id?.Value.Trim(), AppId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or System.Text.DecoderFallbackException)
        {
            return false;
        }
    }
}
