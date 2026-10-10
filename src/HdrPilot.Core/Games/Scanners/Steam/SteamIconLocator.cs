using System.Globalization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Core.Games.Scanners.Steam;

/// <summary>
/// Offizielles Steam-Symbol eines Spiels (Entscheidung des Nutzers, E86), nur lesend und ohne Netz. Es zählt die erste
/// vorhandene Quelle:
/// <list type="number">
/// <item><c>steam\games\&lt;clienticon&gt;.ico</c>, Name aus appinfo.vdf <c>common/clienticon</c> (gemessen bei 609 von 633
/// Spielen).</item>
/// <item><c>DisplayIcon</c> des Uninstall-Eintrags „Steam App &lt;AppID&gt;“ unter HKLM in der 64-Bit-Sicht. Der Steam-Client
/// ist inzwischen ein 64-Bit-Programm und schreibt alle Einträge dorthin (gemessen 798, in WOW6432Node keiner). Hilft
/// bei geändertem clienticon: Der Eintrag zeigt dann noch auf die ältere, vorhandene .ico.</item>
/// <item><c>IconFile</c> einer Steam-Verknüpfung (.url mit <c>steam://rungameid/&lt;AppID&gt;</c>) im Startmenü oder auf dem
/// Desktop (auch eine Ebene tiefer, etwa „Desktop\Games“). Gelesen erst, wenn ein Spiel sie braucht, dann einmal.</item>
/// <item><c>appcache\librarycache\&lt;AppID&gt;\&lt;icon&gt;.jpg</c> (Name aus <c>common/icon</c>, sonst das einzige Hex-JPEG im
/// Ordner, sonst das ältere <c>&lt;AppID&gt;_icon.jpg</c>): immer 32 px ohne Transparenz, deshalb nur ohne jede .ico.</item>
/// </list>
/// </summary>
internal sealed class SteamIconLocator(string steamPath, IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger logger)
{
    internal const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Höchstens so viele .url-Dateien werden gelesen (gemessen: 1606 am Zielrechner).</summary>
    internal const int MaxShortcutFiles = 4000;

    private Dictionary<uint, List<string>>? _shortcutIcons;

    public IReadOnlyList<string> Find(uint appId, SteamAppInfo? info)
    {
        var result = new List<string>(1);
        if (IsHash(info?.ClientIcon)
            && IconFiles.AddExisting(result, fileSystem, Path.Combine(steamPath, "steam", "games", info!.ClientIcon!.ToLowerInvariant() + ".ico")))
        {
            return result;
        }

        if (IconFiles.AddExisting(result, fileSystem, FromUninstallEntry(appId)))
        {
            return result;
        }

        foreach (var icon in FromShortcuts(appId))
        {
            if (IconFiles.AddExisting(result, fileSystem, icon))
            {
                return result;
            }
        }

        IconFiles.AddExisting(result, fileSystem, FromLibraryCache(appId, info?.Icon));
        return result;
    }

    /// <summary>Steam benennt Symbole nach ihrem SHA-1: genau 40 Hex-Zeichen (alles andere könnte ein Pfad sein).</summary>
    internal static bool IsHash(string? value) => value is { Length: 40 } && value.All(char.IsAsciiHexDigit);

    private string? FromUninstallEntry(uint appId)
    {
        using var key = registry.OpenKey(
            RegistryHiveKind.LocalMachine,
            UninstallKeyPath + @"\Steam App " + appId.ToString(CultureInfo.InvariantCulture),
            RegistryViewKind.Registry64);
        return IconFiles.ParseIconLocation(key?.GetValue("DisplayIcon") as string);
    }

    private IReadOnlyList<string> FromShortcuts(uint appId)
    {
        _shortcutIcons ??= ReadShortcuts();
        return _shortcutIcons.TryGetValue(appId, out var icons) ? icons : [];
    }

    private string FromLibraryCache(uint appId, string? icon)
    {
        var cache = Path.Combine(steamPath, "appcache", "librarycache");
        var dir = Path.Combine(cache, appId.ToString(CultureInfo.InvariantCulture));
        if (IsHash(icon))
        {
            var named = Path.Combine(dir, icon!.ToLowerInvariant() + ".jpg");
            if (SafeExists(named))
            {
                return named;
            }
        }

        try
        {
            var hashed = fileSystem.EnumerateFiles(dir, "*.jpg").Where(f => IsHash(Path.GetFileNameWithoutExtension(f))).Take(2).ToList();
            if (hashed.Count == 1)
            {
                return hashed[0];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            logger.LogDebug("Steam: librarycache von {AppId} nicht lesbar: {Message}", appId, ex.Message);
        }

        return Path.Combine(cache, appId.ToString(CultureInfo.InvariantCulture) + "_icon.jpg");
    }

    /// <summary>
    /// Alle Steam-Verknüpfungen: Startmenü (Benutzer und alle Benutzer, mit Unterordnern) und Desktop (Benutzer mit einer
    /// Ebene Unterordner, öffentlicher Desktop). Je AppID die IconFile-Werte in Fundreihenfolge.
    /// </summary>
    private Dictionary<uint, List<string>> ReadShortcuts()
    {
        var result = new Dictionary<uint, List<string>>();
        var files = new List<string>();
        AddUrlFiles(files, environment.GetFolderPath(Environment.SpecialFolder.Programs), recursive: true);
        AddUrlFiles(files, environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), recursive: true);
        var desktop = environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
        {
            desktop = environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        AddUrlFiles(files, desktop, recursive: false);
        foreach (var sub in SafeDirectories(desktop))
        {
            AddUrlFiles(files, sub, recursive: false);
        }

        AddUrlFiles(files, environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), recursive: false);

        foreach (var file in files)
        {
            string text;
            try
            {
                text = fileSystem.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            if (ParseShortcut(text) is { } shortcut)
            {
                if (!result.TryGetValue(shortcut.AppId, out var icons))
                {
                    icons = [];
                    result[shortcut.AppId] = icons;
                }

                icons.Add(shortcut.IconFile);
            }
        }

        logger.LogDebug("Steam: {Files} Verknüpfungen gelesen, Symbole für {Apps} AppIDs.", files.Count, result.Count);
        return result;
    }

    /// <summary>
    /// Liest eine Internet-Verknüpfung (INI-Format): <c>URL=steam://rungameid/&lt;AppID&gt;</c> und <c>IconFile=…</c>.
    /// Null bei anderen Zielen, Nicht-Steam-Spielen (64-Bit-IDs) oder fehlendem Symbol.
    /// </summary>
    internal static (uint AppId, string IconFile)? ParseShortcut(string text)
    {
        uint? appId = null;
        string? icon = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                const string prefix = "steam://rungameid/";
                var url = line[4..].Trim();
                if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && uint.TryParse(url[prefix.Length..].TrimEnd('/'), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    && id > 0)
                {
                    appId = id;
                }
            }
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
            {
                icon = IconFiles.ParseIconLocation(line[9..]);
            }
        }

        return appId is { } a && icon is not null ? (a, icon) : null;
    }

    private void AddUrlFiles(List<string> files, string? directory, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(directory) || files.Count >= MaxShortcutFiles)
        {
            return;
        }

        try
        {
            files.AddRange(fileSystem.EnumerateFiles(PathUtil.Normalize(directory), "*.url", recursive).Take(MaxShortcutFiles - files.Count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            logger.LogDebug("Steam: Verknüpfungen in {Dir} nicht lesbar: {Message}", directory, ex.Message);
        }
    }

    private List<string> SafeDirectories(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return [];
        }

        try
        {
            return fileSystem.EnumerateDirectories(PathUtil.Normalize(directory)).Take(64).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return [];
        }
    }

    private bool SafeExists(string path)
    {
        try
        {
            return fileSystem.FileExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
