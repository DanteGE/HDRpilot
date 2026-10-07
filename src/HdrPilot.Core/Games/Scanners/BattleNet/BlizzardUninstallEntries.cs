using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Games.Scanners.BattleNet;

/// <summary>Uninstall-Eintrag eines Battle.net-Produkts, verknüpft über <c>--uid=</c> im UninstallString.</summary>
internal sealed record BlizzardUninstallEntry(string Uid, string? DisplayName, string? DisplayIcon);

/// <summary>
/// Liest Uninstall-Einträge, deren UninstallString den Blizzard Uninstaller mit <c>--uid=&lt;uid&gt;</c> aufruft
/// (am Zielrechner geprüft am Eintrag „Battle.net“: <c>"…\Blizzard Uninstaller.exe" --lang=deDE --uid=battle.net …</c>).
/// </summary>
internal static class BlizzardUninstallEntries
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UidOption = "--uid=";

    private static readonly (RegistryHiveKind Hive, RegistryViewKind View)[] Views =
    [
        (RegistryHiveKind.LocalMachine, RegistryViewKind.Registry32),
        (RegistryHiveKind.LocalMachine, RegistryViewKind.Default),
        (RegistryHiveKind.CurrentUser, RegistryViewKind.Default),
    ];

    /// <summary>Einträge je uid (ohne Groß-/Kleinschreibung); bei mehreren gewinnt der erste.</summary>
    public static IReadOnlyDictionary<string, BlizzardUninstallEntry> Read(IRegistry registry, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, BlizzardUninstallEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in Views)
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
                var uninstall = key?.GetString("UninstallString");
                if (key is null || uninstall is null || !uninstall.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ParseUid(uninstall) is { } uid && !result.ContainsKey(uid))
                {
                    result[uid] = new BlizzardUninstallEntry(uid, key.GetString("DisplayName")?.Trim(), key.GetString("DisplayIcon"));
                }
            }
        }

        return result;
    }

    /// <summary>Wert der Option <c>--uid=</c>, mit oder ohne Anführungszeichen; null, wenn sie fehlt oder leer ist.</summary>
    internal static string? ParseUid(string uninstallString)
    {
        var start = uninstallString.IndexOf(UidOption, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var rest = uninstallString[(start + UidOption.Length)..];
        string value;
        if (rest.StartsWith('"'))
        {
            var end = rest.IndexOf('"', 1);
            value = end > 0 ? rest[1..end] : rest[1..];
        }
        else
        {
            var end = rest.IndexOfAny([' ', '\t', '"']);
            value = end >= 0 ? rest[..end] : rest;
        }

        value = value.Trim();
        return value.Length > 0 ? value : null;
    }
}
