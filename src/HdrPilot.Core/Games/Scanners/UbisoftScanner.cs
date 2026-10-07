using System.Globalization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.Ubisoft;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners;

/// <summary>
/// Findet installierte Spiele von Ubisoft Connect. Ablauf:
/// <list type="number">
/// <item>Launcher: HKLM\SOFTWARE\Ubisoft\Launcher über die 32-Bit-Sicht (unter x64 also WOW6432Node). Fehlt der Schlüssel,
/// gilt der Launcher als nicht installiert.</item>
/// <item>Je Unterschlüssel von <c>Launcher\Installs\&lt;ID&gt;</c> der Wert <c>InstallDir</c> (gemessen: <c>D:/Ubisoft-Spiele/AFOP/</c>).
/// Ohne InstallDir (gemessen bei über Steam installierten Ubisoft-Spielen) oder mit fehlendem Ordner entfällt der Eintrag;
/// eine Laufwerkswurzel als InstallDir wird als Fehler gemeldet und ebenfalls weggelassen.</item>
/// <item>Name aus dem Produkt-Cache <c>cache\configuration\configurations</c> (siehe <see cref="UbisoftConfigurationCache"/>),
/// sonst DisplayName des Uninstall-Eintrags „Uplay Install &lt;ID&gt;“ (32-Bit-Sicht), sonst der Ordnername.</item>
/// <item>EXE-Dateien: die im Produkt-Cache genannten Start-EXEs, sofern sie existieren.</item>
/// </list>
/// Start: <c>uplay://launch/&lt;ID&gt;/0</c> (so legt Ubisoft Connect seine Verknüpfungen an).
/// </summary>
public sealed class UbisoftScanner : IGameScanner
{
    internal const string LauncherKeyPath = @"SOFTWARE\Ubisoft\Launcher";
    internal const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly IAppEnvironment _environment;
    private readonly ILogger _logger;

    public UbisoftScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? NullLogger.Instance;
    }

    public GameSource Source => GameSource.Ubisoft;

    public string Name => "Ubisoft Connect";

    /// <summary>Start-Befehl eines Ubisoft-Produkts.</summary>
    public static string LaunchCommandFor(uint productId) =>
        "uplay://launch/" + productId.ToString(CultureInfo.InvariantCulture) + "/0";

    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        using var launcherKey = _registry.OpenKey(RegistryHiveKind.LocalMachine, LauncherKeyPath, RegistryViewKind.Registry32);
        if (launcherKey is null)
        {
            _logger.LogDebug("Ubisoft Connect ist nicht installiert (kein Launcher-Schlüssel).");
            return new ScanResult { Source = Source, ScannerName = Name, LauncherDetected = false };
        }

        var errors = new List<ScanError>();
        var installs = ReadInstalls(launcherKey, errors, cancellationToken);
        var games = new List<GameEntry>();
        if (installs.Count > 0)
        {
            var configs = LoadConfigurations(launcherKey, installs, errors, cancellationToken);
            var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var install in installs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seenDirs.Add(install.Dir))
                {
                    _logger.LogInformation("Ubisoft: Ordner {Dir} gehört schon zu einem anderen Produkt; {Id} wird übersprungen.", install.Dir, install.Id);
                    continue;
                }

                configs.TryGetValue(install.Id, out var config);
                games.Add(new GameEntry
                {
                    Source = GameSource.Ubisoft,
                    SourceId = install.Id.ToString(CultureInfo.InvariantCulture),
                    Name = ResolveName(install, config),
                    InstallDir = install.Dir,
                    Executables = ResolveExecutables(install.Dir, config),
                    LaunchCommand = LaunchCommandFor(install.Id),
                });
            }
        }

        return new ScanResult
        {
            Source = Source,
            ScannerName = Name,
            LauncherDetected = true,
            Games = games,
            Errors = errors,
        };
    }

    private List<Install> ReadInstalls(IRegistryKey launcherKey, List<ScanError> errors, CancellationToken cancellationToken)
    {
        var result = new List<Install>();
        using var installsKey = launcherKey.OpenSubKey("Installs");
        if (installsKey is null)
        {
            _logger.LogInformation("Ubisoft: Schlüssel Installs fehlt, keine Spiele installiert.");
            return result;
        }

        var ids = new List<(uint Id, string KeyName)>();
        foreach (var keyName in installsKey.GetSubKeyNames())
        {
            if (uint.TryParse(keyName, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                ids.Add((id, keyName));
            }
            else
            {
                errors.Add(new ScanError($"Ubisoft-Installationseintrag \"{keyName}\" hat keine gültige Produkt-ID.", installsKey.Name + "\\" + keyName));
            }
        }

        foreach (var (id, keyName) in ids.OrderBy(i => i.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keyPath = installsKey.Name + "\\" + keyName;
            using var key = installsKey.OpenSubKey(keyName);
            if (key is null)
            {
                errors.Add(new ScanError($"Ubisoft-Installationseintrag {id} ist nicht lesbar.", keyPath));
                continue;
            }

            var raw = key.GetValue("InstallDir");
            if (raw is null)
            {
                // Gemessen: über Steam installierte Ubisoft-Spiele haben hier nur "language", aber keinen InstallDir.
                _logger.LogInformation("Ubisoft: {Id} hat keinen InstallDir (nicht über Ubisoft Connect installiert) und wird übersprungen.", id);
                continue;
            }

            if (raw is not string text)
            {
                errors.Add(new ScanError($"Ubisoft-Installationseintrag {id}: InstallDir hat den unerwarteten Typ {raw.GetType().Name}.", keyPath));
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogInformation("Ubisoft: InstallDir von {Id} ist leer, Eintrag wird übersprungen.", id);
                continue;
            }

            var dir = PathUtil.Normalize(text);
            if (!Path.IsPathFullyQualified(dir))
            {
                errors.Add(new ScanError($"Ubisoft-Installationseintrag {id}: InstallDir ist kein absoluter Pfad: {text}", keyPath));
                continue;
            }

            if (IsRoot(dir))
            {
                // Später gilt jeder Prozess unter InstallDir als Spiel; bei einer Laufwerkswurzel wäre das jeder Prozess
                // auf dem Laufwerk. Lieber melden und weglassen (manueller Eintrag möglich) als falsch zuordnen.
                errors.Add(new ScanError(
                    $"Ubisoft-Installationseintrag {id}: InstallDir ist die Wurzel eines Laufwerks bzw. einer Freigabe ({text}); " +
                    "jeder Prozess auf dem Laufwerk gälte als Spiel. Eintrag wird übersprungen, bitte den Spielordner manuell eintragen.",
                    keyPath));
                continue;
            }

            if (!_fileSystem.DirectoryExists(dir))
            {
                _logger.LogInformation("Ubisoft: Spielordner {Dir} von {Id} fehlt, Eintrag wird übersprungen.", dir, id);
                continue;
            }

            result.Add(new Install(id, dir));
        }

        return result;
    }

    private Dictionary<uint, UbisoftProductConfig> LoadConfigurations(
        IRegistryKey launcherKey, List<Install> installs, List<ScanError> errors, CancellationToken cancellationToken)
    {
        var configs = new Dictionary<uint, UbisoftProductConfig>();
        var launcherDir = PathUtil.Normalize(launcherKey.GetString("InstallDir") ?? string.Empty);
        if (!Path.IsPathFullyQualified(launcherDir))
        {
            // Ohne brauchbaren Registry-Wert: Standardordner unter Programme (x86).
            var programFilesX86 = PathUtil.Normalize(_environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            if (!Path.IsPathFullyQualified(programFilesX86))
            {
                return configs;
            }

            launcherDir = Path.Combine(programFilesX86, "Ubisoft", "Ubisoft Game Launcher");
        }

        var path = PathUtil.Normalize(Path.Combine(launcherDir, "cache", "configuration", "configurations"));
        if (!_fileSystem.FileExists(path))
        {
            _logger.LogInformation("Ubisoft: Produkt-Cache {Path} fehlt; Namen kommen aus den Uninstall-Einträgen.", path);
            return configs;
        }

        byte[] data;
        try
        {
            data = _fileSystem.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new ScanError("Ubisoft-Produkt-Cache nicht lesbar, Namen und EXE-Dateien fehlen: " + ex.Message, path));
            return configs;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var yamls = UbisoftConfigurationCache.Read(data, installs.Select(i => i.Id).ToHashSet(), out var cacheError);
        if (cacheError is not null)
        {
            errors.Add(new ScanError("Ubisoft-Produkt-Cache beschädigt, Namen und EXE-Dateien fehlen teilweise: " + cacheError, path));
        }

        foreach (var (id, yaml) in yamls)
        {
            try
            {
                configs[id] = UbisoftProductConfig.FromYaml(yaml);
            }
            catch (FormatException ex)
            {
                errors.Add(new ScanError($"Ubisoft-Produkt-Cache: Eintrag {id} ist defekt: {ex.Message}", path));
            }
        }

        return configs;
    }

    private string ResolveName(Install install, UbisoftProductConfig? config)
    {
        if (!string.IsNullOrWhiteSpace(config?.Name))
        {
            return config.Name;
        }

        using (var key = _registry.OpenKey(
            RegistryHiveKind.LocalMachine,
            UninstallKeyPath + @"\Uplay Install " + install.Id.ToString(CultureInfo.InvariantCulture),
            RegistryViewKind.Registry32))
        {
            // Nur REG_SZ: GetString machte aus einem falsch typisierten DWORD eine Zahl als "Namen".
            var displayName = (key?.GetValue("DisplayName") as string)?.Trim();
            if (!string.IsNullOrEmpty(displayName))
            {
                return displayName;
            }
        }

        var folder = Path.GetFileName(install.Dir);
        return string.IsNullOrEmpty(folder) ? install.Dir : folder;
    }

    /// <summary>
    /// Start-EXEs aus dem Produkt-Cache. <c>relative</c> bezieht sich auf den Arbeitsordner (InstallDir plus
    /// <c>working_directory.append</c>); fehlt die Datei dort, wird InstallDir selbst geprüft. Nur vorhandene Dateien.
    /// Ubisoft hängt <c>relative</c> an den Arbeitsordner an; ein führender Trenner (gemessen bei Watch Dogs 2:
    /// <c>'\bin\WatchDogs2.exe'</c>) ist deshalb kein absoluter Pfad und wird entfernt.
    /// </summary>
    private IReadOnlyList<string> ResolveExecutables(string installDir, UbisoftProductConfig? config)
    {
        if (config is null || config.Executables.Count == 0)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var exe in config.Executables)
        {
            var append = TrimLeadingSeparators(exe.WorkingDirectoryAppend);
            foreach (var raw in new[] { exe.RelativePath, exe.TrialRelativePath })
            {
                var relative = TrimLeadingSeparators(raw);
                if (relative is null || !IsPlainRelative(relative) || (append is not null && !IsPlainRelative(append)))
                {
                    continue;
                }

                var candidates = append is not null
                    ? new[] { Path.Combine(installDir, append, relative), Path.Combine(installDir, relative) }
                    : new[] { Path.Combine(installDir, relative) };
                foreach (var candidate in candidates)
                {
                    var path = PathUtil.Normalize(candidate);
                    if (_fileSystem.FileExists(path))
                    {
                        if (!result.Contains(path, StringComparer.OrdinalIgnoreCase))
                        {
                            result.Add(path);
                        }

                        break;
                    }
                }
            }
        }

        return result;
    }

    private static bool IsPlainRelative(string value) =>
        !Path.IsPathRooted(value)
        && !value.Contains(':', StringComparison.Ordinal)
        && value.Split('\\', '/').All(part => part.Length == 0 || part.Trim(' ', '.').Length > 0);

    /// <summary>Entfernt führende Trenner; null, wenn nichts übrig bleibt.</summary>
    private static string? TrimLeadingSeparators(string? value)
    {
        var trimmed = value?.TrimStart('\\', '/').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>True für eine Laufwerkswurzel ("D:\") oder die Wurzel einer Freigabe ("\\server\freigabe").</summary>
    private static bool IsRoot(string normalizedDir)
    {
        var root = Path.GetPathRoot(normalizedDir);
        return !string.IsNullOrEmpty(root) && PathUtil.PathEquals(root, normalizedDir);
    }

    private sealed record Install(uint Id, string Dir);
}
