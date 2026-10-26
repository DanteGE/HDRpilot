using System.Text.Json;
using System.Text.Json.Serialization;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Cli;

/// <summary>Abhängigkeiten der Kommandozeile, damit Befehle mit Attrappen testbar sind.</summary>
public interface ICliServices
{
    IHdrController CreateHdrController(HdrControllerOptions options);

    IReadOnlyList<IGameScanner> CreateScanners();

    IFileSystem FileSystem => PhysicalFileSystem.Instance;

    IAppEnvironment Environment => SystemEnvironment.Instance;

    IClock Clock => SystemClock.Instance;

    AutomationHost CreateHost(AppPaths paths, ILoggerProvider consoleLog) => new(paths, [consoleLog]);
}

public sealed partial class CliApp(ICliServices services, TextWriter stdout, TextWriter stderr)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Sprache der Ausgaben (E95): die der Ablage (<c>--config-dir</c> oder die normale), ohne config.json nach der
    /// Anzeigesprache von Windows wie bei einer neuen Konfiguration (<see cref="UiLanguage.ForStartup"/>). Liest nur.
    /// </summary>
    internal static string LanguageFor(IReadOnlyList<string> args, IFileSystem fileSystem, IAppEnvironment environment, Func<string>? newConfigLanguage = null)
    {
        try
        {
            var paths = CliArguments.Parse(args, out _).Get("--config-dir") is { } dir
                ? AppPaths.InDirectory(Path.GetFullPath(dir))
                : AppPaths.Default(environment);
            return UiLanguage.ForStartup(fileSystem, paths.ConfigFile, newConfigLanguage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return (newConfigLanguage ?? UiLanguage.ForNewConfig)();
        }
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var parsed = CliArguments.Parse(args, out var parseError);
        if (parseError is not null)
        {
            return Usage(parseError);
        }

        if (parsed.Positional.Count == 0 || parsed.Has("--help") || parsed.Has("-h") || Is(parsed.Positional[0], "help"))
        {
            stdout.WriteLine(HelpText);
            return parsed.Positional.Count == 0 && !parsed.Has("--help") && !parsed.Has("-h") ? ExitCodes.Usage : ExitCodes.Ok;
        }

        try
        {
            return parsed.Positional[0].ToLowerInvariant() switch
            {
                "hdr" => await RunHdrAsync(parsed, cancellationToken).ConfigureAwait(false),
                "scan" => await RunScanAsync(parsed, cancellationToken).ConfigureAwait(false),
                "games" => await RunGamesAsync(parsed, cancellationToken).ConfigureAwait(false),
                "watch" => await RunWatchAsync(parsed, cancellationToken).ConfigureAwait(false),
                "run" => await RunLaunchAsync(parsed, cancellationToken).ConfigureAwait(false),
                "diag" => await RunDiagAsync(parsed, cancellationToken).ConfigureAwait(false),
                _ => Usage($"Unbekannter Befehl \"{parsed.Positional[0]}\"."),
            };
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("Abgebrochen.");
            return ExitCodes.Error;
        }
        catch (Exception ex)
        {
            stderr.WriteLine("Unerwarteter Fehler: " + ex.Message);
            return ExitCodes.Error;
        }
    }

    public const string HelpText = """
        HDRpilot Kommandozeile (Diagnose und Tests)

        hdrpilot hdr status [--json] [--api modern|legacy]
            Listet alle aktiven Bildschirme mit Name, HDR-fähig und HDR an/aus.
        hdrpilot hdr on|off|toggle [--display <auswahl>] [--api modern|legacy] [--hotkey-fallback] [--json]
            Schaltet HDR und bestätigt den Zustand durch erneutes Lesen.
            <auswahl>: primary (Standard), all, Nummer aus "hdr status", Teil des Namens oder monitorDevicePath.

        hdrpilot scan [--source <quelle>] [--search "<ordner>;<ordner>"] [--config-dir <ordner>]
            Gibt alle gefundenen Spiele und die Fehler je Scanner als JSON aus (nur lesen).
            <quelle>: Steam, Epic, Xbox, Gog, Ubisoft, EA, BattleNet, Quiver
            Mit --config-dir zählen Suchordner und gelernte Quiver-Ordner dieser Ablage mit.

        hdrpilot games list [--json]
        hdrpilot games import
            Liest alle Launcher ein und übernimmt das Ergebnis in die Konfiguration.
        hdrpilot games add --path <exe|ordner> [--name <name>] [--hdr on|off]
        hdrpilot games set <schlüssel> [--hdr on|off] [--restore on|off] [--display <monitorDevicePath>|global]
            Schlüssel: Quelle:ID, etwa Steam:1091500 (siehe "games list").
        hdrpilot watch [--verbose]
            Überwacht Spielstarts im Vordergrund (wie die App, ohne Oberfläche). Beenden mit Strg+C.
        hdrpilot run <schlüssel>
            Start über HDRpilot: HDR einschalten, bestätigen, Spiel starten, bis Spielende überwachen.
        scan, games, watch und run: --config-dir <ordner> (statt %APPDATA%/%LOCALAPPDATA%\HDRpilot, für Tests).

        Exit-Codes: 0 ok, 1 Fehler, 2 Aufruf falsch, 3 Bildschirm nicht gefunden,
                    4 Bildschirm ohne HDR, 5 Schalten fehlgeschlagen, 6 Spiel nicht gefunden
        """;

    private async Task<int> RunScanAsync(CliArguments args, CancellationToken cancellationToken)
    {
        var unknown = args.UnknownOptions("--source", "--search", "--config-dir").ToList();
        if (unknown.Count > 0 || args.Positional.Count > 1)
        {
            return Usage("Unbekannte Option oder Argument für scan.");
        }

        var scanners = services.CreateScanners();
        if (args.Get("--source") is { } sourceText)
        {
            if (!Enum.TryParse<GameSource>(sourceText, ignoreCase: true, out var source) || !Enum.IsDefined(source))
            {
                return Usage($"Unbekannte Quelle \"{sourceText}\".");
            }

            scanners = scanners.Where(s => s.Source == source).ToList();
        }

        IReadOnlyList<string> searchFolders = (args.Get("--search") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IReadOnlyList<string> quiverFolders = [];
        if (args.Get("--config-dir") is not null && ReadConfigReadOnly(ResolvePaths(args)) is { } config)
        {
            // Wie das Einlesen der App: Suchordner und gelernte Quiver-Ordner (E91) der Ablage, nur gelesen.
            searchFolders = [.. config.Settings.SearchFolders, .. searchFolders];
            quiverFolders = config.Settings.QuiverFolders ?? [];
        }

        var options = new ScanOptions { SearchFolders = searchFolders, QuiverFolders = quiverFolders };
        var report = await new LibraryScanner(scanners).ScanAsync(options, cancellationToken).ConfigureAwait(false);

        var output = new ScanOutput(
            report.Games.Select(g => new ScanGame(
                g.Key, g.Source, g.SourceId, g.Name, g.InstallDir, g.Executables, g.LaunchCommand, g.LaunchArguments, g.LaunchWorkingDirectory,
                g.IconCandidates, g.CoverCandidates, g.InstallKind, g.LastPlayed)).ToList(),
            report.Scanners.Select(s => new ScanScanner(
                s.Result.Source, s.Result.ScannerName, s.Result.LauncherDetected, s.Result.Games.Count,
                (long)s.Duration.TotalMilliseconds, s.Result.Errors)).ToList());
        stdout.WriteLine(JsonSerializer.Serialize(output, Json));
        return ExitCodes.Ok;
    }

    /// <summary>
    /// Konfiguration nur lesen (für scan): Fehlt die Datei oder ist sie nicht lesbar, null; anders als beim Öffnen über
    /// <see cref="ConfigService"/> wird nichts angelegt und nichts als „defekt“ gesichert.
    /// </summary>
    private AppConfig? ReadConfigReadOnly(AppPaths paths)
    {
        var text = new ConfigStore(services.FileSystem, paths.ConfigFile, services.Clock).TryReadText();
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(text, ConfigJson.Options);
        }
        catch (JsonException ex)
        {
            stderr.WriteLine($"Hinweis: Konfiguration {paths.ConfigFile} nicht lesbar ({ex.Message}); ohne ihre Ordner eingelesen.");
            return null;
        }
    }

    private sealed record ScanOutput(IReadOnlyList<ScanGame> Games, IReadOnlyList<ScanScanner> Scanners);

    private sealed record ScanGame(
        string Key, GameSource Source, string SourceId, string Name, string InstallDir, IReadOnlyList<string> Executables,
        string? LaunchCommand, string? LaunchArguments, string? LaunchWorkingDirectory, IReadOnlyList<string> IconCandidates,
        IReadOnlyList<string> CoverCandidates, string? InstallKind, DateTimeOffset? LastPlayed);

    private sealed record ScanScanner(
        GameSource Source, string Name, bool LauncherDetected, int GameCount, long DurationMs, IReadOnlyList<ScanError> Errors);

    private async Task<int> RunHdrAsync(CliArguments args, CancellationToken cancellationToken)
    {
        if (args.Positional.Count < 2)
        {
            return Usage("hdr braucht einen Unterbefehl: status, on, off oder toggle.");
        }

        var unknown = args.UnknownOptions("--api", "--display").ToList();
        if (unknown.Count > 0)
        {
            return Usage("Unbekannte Option: " + string.Join(", ", unknown));
        }

        HdrApi? forceApi = null;
        if (args.Get("--api") is { } apiText)
        {
            if (Is(apiText, "modern"))
            {
                forceApi = HdrApi.Modern;
            }
            else if (Is(apiText, "legacy"))
            {
                forceApi = HdrApi.Legacy;
            }
            else
            {
                return Usage("--api erwartet modern oder legacy.");
            }
        }

        var controller = services.CreateHdrController(new HdrControllerOptions
        {
            ForceApi = forceApi,
            HotkeyFallbackEnabled = args.Has("--hotkey-fallback"),
        });

        var sub = args.Positional[1].ToLowerInvariant();
        return sub switch
        {
            "status" => Status(controller, args.Has("--json")),
            "on" => await SwitchAsync(controller, args, target: true, cancellationToken).ConfigureAwait(false),
            "off" => await SwitchAsync(controller, args, target: false, cancellationToken).ConfigureAwait(false),
            "toggle" => await SwitchAsync(controller, args, target: null, cancellationToken).ConfigureAwait(false),
            _ => Usage($"Unbekannter Unterbefehl \"{args.Positional[1]}\"."),
        };
    }

    private int Status(IHdrController controller, bool json)
    {
        var displays = controller.GetDisplays();
        if (json)
        {
            var rows = displays.Select((d, i) => new StatusRow(
                i + 1, d.FriendlyName, d.MonitorDevicePath, d.GdiDeviceName, d.IsPrimary, d.HdrSupported, d.HdrEnabled,
                d.ActiveColorMode, d.Api, "0x" + d.RawColorFlags.ToString("X", System.Globalization.CultureInfo.InvariantCulture),
                d.BitsPerColorChannel, d.BlockedByPolicy));
            stdout.WriteLine(JsonSerializer.Serialize(rows, Json));
            return ExitCodes.Ok;
        }

        if (displays.Count == 0)
        {
            stdout.WriteLine("Keine aktiven Bildschirme gefunden.");
            return ExitCodes.DisplayNotFound;
        }

        for (var i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            var mode = d.ActiveColorMode is { } m ? $", aktiver Modus {m}" : string.Empty;
            stdout.WriteLine($"{i + 1}. {d.FriendlyName}{(d.IsPrimary ? " (Hauptbildschirm)" : string.Empty)}");
            stdout.WriteLine($"   HDR-fähig: {(d.HdrSupported ? "ja" : "nein")}   HDR: {(d.HdrEnabled ? "an" : "aus")}{mode}   API: {d.Api}");
            stdout.WriteLine($"   {d.MonitorDevicePath}");
        }

        return ExitCodes.Ok;
    }

    private async Task<int> SwitchAsync(IHdrController controller, CliArguments args, bool? target, CancellationToken cancellationToken)
    {
        var displays = controller.GetDisplays();
        var selection = DisplaySelector.Select(displays, args.Get("--display"), out var selectError);
        if (selection is null)
        {
            stderr.WriteLine(selectError);
            return selectError is not null && selectError.StartsWith("Mehrdeutig", StringComparison.Ordinal) ? ExitCodes.Usage : ExitCodes.DisplayNotFound;
        }

        var results = new List<HdrSetResult>();
        foreach (var display in selection)
        {
            var enable = target ?? !display.HdrEnabled;
            results.Add(await controller.SetHdrAsync(display.MonitorDevicePath, enable, allowHotkeyFallback: true, cancellationToken).ConfigureAwait(false));
        }

        if (args.Has("--json"))
        {
            stdout.WriteLine(JsonSerializer.Serialize(results, Json));
        }
        else
        {
            foreach (var r in results)
            {
                var writer = r.IsSuccess ? stdout : stderr;
                writer.WriteLine($"{r.FriendlyName ?? r.MonitorDevicePath}: {r.Message}");
            }
        }

        return results.Select(ToExitCode).DefaultIfEmpty(ExitCodes.Ok).Max();
    }

    internal static int ToExitCode(HdrSetResult result) => result.Status switch
    {
        HdrSetStatus.Success or HdrSetStatus.AlreadyInState => ExitCodes.Ok,
        HdrSetStatus.NotConnected => ExitCodes.DisplayNotFound,
        HdrSetStatus.NotHdrCapable => ExitCodes.NotHdrCapable,
        _ => ExitCodes.SwitchFailed,
    };

    private int Usage(string message)
    {
        stderr.WriteLine(message);
        stderr.WriteLine("Hilfe: hdrpilot help");
        return ExitCodes.Usage;
    }

    private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private sealed record StatusRow(
        int Index, string Name, string MonitorDevicePath, string? GdiDeviceName, bool Primary, bool HdrSupported, bool HdrEnabled,
        AdvancedColorMode? ActiveColorMode, HdrApi Api, string RawFlags, uint BitsPerColorChannel, bool BlockedByPolicy);
}
