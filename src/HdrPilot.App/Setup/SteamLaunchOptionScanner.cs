using System.Globalization;
using System.IO;
using System.Text.Json;
using HdrPilot.App.Resources;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners.Steam;
using HdrPilot.Core.Util;

namespace HdrPilot.App.Setup;

/// <summary>Steam-Spiele, deren Startoption noch HDRpilot aufruft (Warnung beim Entfernen).</summary>
public interface ISteamLaunchOptionScanner
{
    /// <summary>Namen der Spiele (sonst „Steam-App &lt;ID&gt;“, <c>SetupSteamAppFallback</c>), sortiert. Wirft nicht.</summary>
    IReadOnlyList<string> FindGamesWithLaunchOption();
}

/// <summary>
/// Liest nur: <c>&lt;Steam&gt;\userdata\&lt;Konto&gt;\config\localconfig.vdf</c> jedes Kontos (KeyValues, derselbe Leser wie
/// der Steam-Scanner). Treffer sind Apps, deren LaunchOptions den EXE-Namen von HDRpilot und <c>--wrap</c> enthalten
/// (siehe <see cref="Services.SteamLaunchOption"/>). Namen kommen aus der config.json der App (Schlüssel „Steam:&lt;ID&gt;“).
/// </summary>
public sealed class SteamLaunchOptionScanner : ISteamLaunchOptionScanner
{
    public const string SteamKeyPath = @"Software\Valve\Steam";

    private readonly Func<string?> _steamPath;
    private readonly string _configFile;
    private readonly SetupLog? _log;
    private readonly string _exeFileName;

    /// <summary>Steam-Ordner aus HKCU\Software\Valve\Steam, Wert SteamPath.</summary>
    public SteamLaunchOptionScanner(IRegistry registry, string configFile, SetupLog? log = null)
        : this(() => ReadSteamPath(registry), configFile, log)
    {
    }

    /// <param name="steamPath">Liefert den Steam-Ordner (Prüfläufe: ein Ordner im Testordner).</param>
    public SteamLaunchOptionScanner(Func<string?> steamPath, string configFile, SetupLog? log = null, string exeFileName = SetupLocations.ExeName)
    {
        _steamPath = steamPath;
        _configFile = configFile;
        _log = log;
        _exeFileName = exeFileName;
    }

    public static string? ReadSteamPath(IRegistry registry)
    {
        using var key = registry.OpenKey(RegistryHiveKind.CurrentUser, SteamKeyPath);
        return key?.GetString("SteamPath");
    }

    public IReadOnlyList<string> FindGamesWithLaunchOption()
    {
        var ids = FindAppIds();
        if (ids.Count == 0)
        {
            return [];
        }

        var names = ReadSteamNames();
        return ids
            .Select(id => names.TryGetValue(id, out var name) ? name : string.Format(CultureInfo.CurrentCulture, SetupStrings.SetupSteamAppFallback, id))
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>App-IDs mit HDRpilot in der Startoption, über alle Steam-Konten, ohne Doppelte.</summary>
    public IReadOnlyList<string> FindAppIds()
    {
        string? userdata;
        try
        {
            var steam = _steamPath();
            userdata = string.IsNullOrWhiteSpace(steam) ? null : Path.Combine(PathUtil.Normalize(steam), "userdata");
            if (userdata is null || !Directory.Exists(userdata))
            {
                return [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            _log?.Warn("Steam-Ordner nicht lesbar: " + ex.Message);
            return [];
        }

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var account in Directory.EnumerateDirectories(userdata))
            {
                var file = Path.Combine(account, "config", "localconfig.vdf");
                if (!File.Exists(file))
                {
                    continue;
                }

                try
                {
                    var root = KeyValuesParser.Parse(SteamTextDecoder.Decode(ReadShared(file), out _));
                    Collect(root, ids, seen);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyValuesException)
                {
                    _log?.Warn("Steam-Startoptionen in " + file + " nicht lesbar: " + ex.Message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Warn("Steam-Konten nicht lesbar: " + ex.Message);
        }

        return ids;
    }

    private void Collect(KeyValueNode node, List<string> ids, HashSet<string> seen)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsBlock)
            {
                continue;
            }

            // Steam: UserLocalConfigStore > Software > Valve > Steam > apps > <AppID> > LaunchOptions. Gesucht wird in
            // jeder Tiefe, weil Steam die Groß-/Kleinschreibung der Zwischenebenen nicht festhält.
            if (IsAppId(child.Key) && child.GetString("LaunchOptions") is { } options && StartsThroughWrapper(options) && seen.Add(child.Key))
            {
                ids.Add(child.Key);
            }

            Collect(child, ids, seen);
        }
    }

    private bool StartsThroughWrapper(string options) =>
        options.Contains(_exeFileName, StringComparison.OrdinalIgnoreCase) && options.Contains("--wrap", StringComparison.OrdinalIgnoreCase);

    private static bool IsAppId(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);

    /// <summary>Spielenamen aus der config.json (nur lesen); bei Fehlern leer.</summary>
    private Dictionary<string, string> ReadSteamNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(_configFile))
            {
                return names;
            }

            var config = JsonSerializer.Deserialize<AppConfig>(ReadSharedText(_configFile), ConfigJson.Options);
            foreach (var game in config?.Games ?? [])
            {
                if (game is { Source: GameSource.Steam } && !string.IsNullOrWhiteSpace(game.Name) && !string.IsNullOrWhiteSpace(game.SourceId))
                {
                    names.TryAdd(game.SourceId.Trim(), game.Name.Trim());
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _log?.Warn("Spielenamen aus " + _configFile + " nicht lesbar: " + ex.Message);
        }

        return names;
    }

    /// <summary>Liest eine Datei, ohne Steam oder der App das Schreiben zu verwehren.</summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
