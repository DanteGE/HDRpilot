using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games.Scanners.BattleNet;
using HdrPilot.Core.Resources;
using HdrPilot.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games.Scanners;

/// <summary>
/// Battle.net über die Datei product.db des Battle.net-Agenten (%ProgramData%\Battle.net\Agent\product.db, Protobuf,
/// versteckt). Gelesen werden nur uid, Produktcode und Installationsordner; Anzeigename und EXE kommen aus dem
/// Uninstall-Eintrag mit derselben uid. Einträge des Agenten und der Battle.net-App selbst sind keine Spiele.
/// Details und Grenzen: docs\scanner\battlenet.md.
/// </summary>
public sealed class BattleNetScanner : IGameScanner
{
    /// <summary>Produktcodes ohne Spielcharakter (am Zielrechner gesehen: Agent und Battle.net-App).</summary>
    private static readonly HashSet<string> NonGameProductCodes = new(StringComparer.OrdinalIgnoreCase) { "agent", "bna" };

    /// <summary>uids ohne Spielcharakter (am Zielrechner gesehen).</summary>
    private static readonly HashSet<string> NonGameUids = new(StringComparer.OrdinalIgnoreCase) { "agent", "battle.net" };

    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly IAppEnvironment _environment;
    private readonly ILogger _logger;

    public BattleNetScanner(IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILogger? logger = null)
    {
        _fileSystem = fileSystem;
        _registry = registry;
        _environment = environment;
        _logger = logger ?? NullLogger.Instance;
    }

    public GameSource Source => GameSource.BattleNet;

    public string Name => "Battle.net";

    /// <summary>
    /// Start über das von Battle.net registrierte Protokoll (battlenet → Battle.net.exe --uri="%1").
    /// Mangels installiertem Blizzard-Spiel am Zielrechner nicht mit einem Spiel geprüft (siehe Doku).
    /// </summary>
    public static string CreateLaunchCommand(string productCode) => "battlenet://" + Uri.EscapeDataString(productCode);

    public ScanResult Scan(ScanOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var programData = _environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
        {
            return Result(IsBattleNetInstalled(null), [], []);
        }

        var agentDir = Path.Combine(programData, "Battle.net", "Agent");
        var productDbPath = Path.Combine(agentDir, "product.db");
        if (!_fileSystem.FileExists(productDbPath))
        {
            var detected = IsBattleNetInstalled(agentDir);
            if (detected)
            {
                _logger.LogInformation("Battle.net: {Path} fehlt, keine Spiele lesbar.", productDbPath);
            }

            return Result(detected, [], []);
        }

        byte[] bytes;
        try
        {
            bytes = _fileSystem.ReadAllBytes(productDbPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result(true, [], [new ScanError(ScanText.Format(ScanStrings.BattleNet_Unreadable, ex.Message), productDbPath)]);
        }

        var content = ProductDbReader.Read(bytes);
        var errors = content.Errors.Select(e => new ScanError(e, productDbPath)).ToList();
        var uninstallEntries = BlizzardUninstallEntries.Read(_registry, cancellationToken);
        var games = new List<GameEntry>();
        var seenUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var install in content.Installs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NonGameProductCodes.Contains(install.ProductCode) || NonGameUids.Contains(install.Uid))
            {
                continue;
            }

            var installDir = PathUtil.Normalize(install.InstallPath);
            if (installDir.Length == 0 || !IsFullyQualified(installDir) || !_fileSystem.DirectoryExists(installDir))
            {
                _logger.LogInformation("Battle.net: {Uid} ohne vorhandenen Installationsordner ({Path}), wird übersprungen.", install.Uid, install.InstallPath);
                continue;
            }

            if (IsDriveRoot(installDir))
            {
                // Später gilt jeder Prozess unter InstallDir als Spiel; ein ganzes Laufwerk wäre als Spielordner falsch.
                errors.Add(new ScanError(ScanText.Format(ScanStrings.BattleNet_DriveRoot, install.Uid, installDir), productDbPath));
                continue;
            }

            if (!seenUids.Add(install.Uid))
            {
                continue;
            }

            uninstallEntries.TryGetValue(install.Uid, out var uninstall);
            games.Add(new GameEntry
            {
                Source = Source,
                SourceId = install.Uid,
                Name = FirstNonEmpty(uninstall?.DisplayName, Path.GetFileName(installDir), install.ProductCode),
                InstallDir = installDir,
                Executables = IconExecutable(uninstall?.DisplayIcon, installDir) is { } exe ? [exe] : [],
                LaunchCommand = CreateLaunchCommand(install.ProductCode),
            });
        }

        return Result(true, games, errors);
    }

    private ScanResult Result(bool detected, IReadOnlyList<GameEntry> games, IReadOnlyList<ScanError> errors) => new()
    {
        Source = Source,
        ScannerName = Name,
        LauncherDetected = detected,
        Games = games,
        Errors = errors,
    };

    private bool IsBattleNetInstalled(string? agentDir)
    {
        if (agentDir is not null && _fileSystem.DirectoryExists(agentDir))
        {
            return true;
        }

        using var key = _registry.OpenKey(RegistryHiveKind.LocalMachine, @"SOFTWARE\Blizzard Entertainment\Battle.net", RegistryViewKind.Registry32);
        return key is not null;
    }

    /// <summary>DisplayIcon des Uninstall-Eintrags, wenn es eine vorhandene EXE unterhalb des Installationsordners ist.</summary>
    private string? IconExecutable(string? displayIcon, string installDir)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var exe = PathUtil.Normalize(StripIconIndex(displayIcon));
        return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && PathUtil.IsUnder(exe, installDir)
            && !PathUtil.PathEquals(exe, installDir)
            && _fileSystem.FileExists(exe)
            ? exe
            : null;
    }

    /// <summary>Entfernt Anführungszeichen und einen Symbolindex wie ",0" aus einem DisplayIcon-Wert.</summary>
    internal static string StripIconIndex(string icon)
    {
        var t = icon.Trim();
        if (t.StartsWith('"'))
        {
            var end = t.IndexOf('"', 1);
            return end > 0 ? t[1..end] : t.Trim('"');
        }

        var comma = t.LastIndexOf(',');
        return comma > 0 && int.TryParse(t[(comma + 1)..], out _) ? t[..comma] : t;
    }

    /// <summary>True für einen Laufwerks- oder Freigabe-Stamm wie <c>D:\</c> oder <c>\\server\share</c>.</summary>
    private static bool IsDriveRoot(string folder)
    {
        var root = Path.GetPathRoot(folder);
        return !string.IsNullOrEmpty(root) && PathUtil.PathEquals(folder, root);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static bool IsFullyQualified(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
