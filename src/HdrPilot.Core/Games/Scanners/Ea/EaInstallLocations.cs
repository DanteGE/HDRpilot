using System.Xml;
using System.Xml.Linq;
using HdrPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.Games.Scanners.Ea;

/// <summary>
/// Liest unverschlüsselte Hinweise auf Installationsordner von EA-Spielen (alle am Zielrechner geprüft).
/// Die verschlüsselte Bibliothek der EA app unter %ProgramData%\EA Desktop wird bewusst nicht angefasst.
/// </summary>
internal static class EaInstallLocations
{
    internal const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    internal const string EaGamesKeyPath = @"SOFTWARE\EA Games";

    /// <summary>Uninstall-Sichten: 64 Bit, 32 Bit (WOW6432Node) und Benutzer.</summary>
    internal static readonly (RegistryHiveKind Hive, RegistryViewKind View)[] UninstallViews =
    [
        (RegistryHiveKind.LocalMachine, RegistryViewKind.Default),
        (RegistryHiveKind.LocalMachine, RegistryViewKind.Registry32),
        (RegistryHiveKind.CurrentUser, RegistryViewKind.Default),
    ];

    /// <summary>
    /// InstallLocation aller Uninstall-Einträge, die vom EA-Installer stammen (UninstallString nennt
    /// <c>…\Common Files\EAInstaller\…\Cleanup.exe</c>) oder als Herausgeber Electronic Arts nennen.
    /// </summary>
    public static IEnumerable<string> FromUninstallEntries(IRegistry registry, CancellationToken cancellationToken)
    {
        foreach (var (hive, view) in UninstallViews)
        {
            using var root = registry.OpenKey(hive, UninstallKeyPath, view);
            if (root is null)
            {
                continue;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var key = root.OpenSubKey(name);
                var location = key?.GetString("InstallLocation");
                if (key is null || string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var uninstall = key.GetString("UninstallString") + " " + key.GetString("QuietUninstallString");
                var publisher = key.GetString("Publisher") ?? string.Empty;
                if (uninstall.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase)
                    || publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase))
                {
                    yield return location;
                }
            }
        }
    }

    /// <summary>Wert „Install Dir“ unter HKLM\SOFTWARE\EA Games\&lt;Spiel&gt; (64- und 32-Bit-Sicht).</summary>
    public static IEnumerable<string> FromEaGamesKeys(IRegistry registry, CancellationToken cancellationToken)
    {
        foreach (var view in new[] { RegistryViewKind.Default, RegistryViewKind.Registry32 })
        {
            using var root = registry.OpenKey(RegistryHiveKind.LocalMachine, EaGamesKeyPath, view);
            if (root is null)
            {
                continue;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var key = root.OpenSubKey(name);
                var dir = key?.GetString("Install Dir");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    yield return dir;
                }
            }
        }
    }

    /// <summary>
    /// Standard-Download-Ordner des alten Origin-Clients aus %AppData%\Origin\local.xml
    /// (<c>&lt;Setting key="DownloadInPlaceDir" value="…"/&gt;</c>). Defekte oder fehlende Datei: null.
    /// </summary>
    public static string? FromOriginSettings(IFileSystem fileSystem, IAppEnvironment environment, ILogger logger)
    {
        var appData = environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            return null;
        }

        var path = Path.Combine(appData, "Origin", "local.xml");
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, CloseInput = true };
            using var reader = XmlReader.Create(new MemoryStream(fileSystem.ReadAllBytes(path), writable: false), settings);
            var doc = XDocument.Load(reader);
            return doc.Root?.Elements()
                .Where(e => string.Equals(e.Name.LocalName, "Setting", StringComparison.OrdinalIgnoreCase))
                .Where(e => string.Equals((string?)e.Attribute("key"), "DownloadInPlaceDir", StringComparison.OrdinalIgnoreCase))
                .Select(e => ((string?)e.Attribute("value"))?.Trim())
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            // Nur ein Zusatzhinweis auf einen Suchordner, kein Spieledatum: protokollieren statt als Scanfehler melden.
            logger.LogWarning("Origin-Einstellungen nicht lesbar ({Path}): {Message}", path, ex.Message);
            return null;
        }
    }
}
