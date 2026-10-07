using System.Text.Json;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Logging;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Cli;

/// <summary>Befehle für Spielliste, Überwachung und Start über HdrPilot.</summary>
public sealed partial class CliApp
{
    private AppPaths ResolvePaths(CliArguments args) => args.Get("--config-dir") is { } dir
        ? AppPaths.InDirectory(Path.GetFullPath(dir))
        : AppPaths.Default(services.Environment);

    private ConfigService OpenConfig(AppPaths paths) =>
        new(new ConfigStore(services.FileSystem, paths.ConfigFile, services.Clock), services.Clock);

    private async Task<int> RunGamesAsync(CliArguments args, CancellationToken cancellationToken)
    {
        var unknown = args.UnknownOptions("--config-dir", "--path", "--name", "--hdr", "--restore", "--display").ToList();
        if (unknown.Count > 0)
        {
            return Usage("Unbekannte Option: " + string.Join(", ", unknown));
        }

        var sub = args.Positional.Count > 1 ? args.Positional[1].ToLowerInvariant() : string.Empty;
        var paths = ResolvePaths(args);
        switch (sub)
        {
            case "list":
            {
                var config = OpenConfig(paths).Current;
                if (args.Has("--json"))
                {
                    stdout.WriteLine(JsonSerializer.Serialize(config.Games, ConfigJson.Options));
                    return ExitCodes.Ok;
                }

                foreach (var g in config.Games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var flags = (g.HdrAuto ? "HDR auto" : "        ") + (g.IsInstalled ? string.Empty : "  nicht installiert") + (g.IsNew ? "  neu" : string.Empty);
                    stdout.WriteLine($"{g.Key,-45} {flags}  {g.Name}");
                }

                stdout.WriteLine($"{config.Games.Count} Spiele, davon {config.Games.Count(g => g.HdrAuto)} mit HDR automatisch.");
                return ExitCodes.Ok;
            }

            case "import":
            {
                var config = OpenConfig(paths);
                var report = await new LibraryScanner(services.CreateScanners())
                    .ScanAsync(new ScanOptions { SearchFolders = config.Current.Settings.SearchFolders }, cancellationToken)
                    .ConfigureAwait(false);
                var summary = config.ApplyScan(report, services.FileSystem);
                stdout.WriteLine($"{report.Games.Count} Spiele eingelesen: {summary.Added} neu, {summary.Updated} aktualisiert, {summary.NoLongerInstalled} nicht mehr installiert.");
                foreach (var error in report.AllErrors)
                {
                    stderr.WriteLine($"Hinweis: {error.Message} {error.Path}");
                }

                return ExitCodes.Ok;
            }

            case "add":
            {
                if (args.Get("--path") is not { } path)
                {
                    return Usage("games add braucht --path <exe|ordner>.");
                }

                if (!TryOnOff(args.Get("--hdr"), false, out var hdr))
                {
                    return Usage("--hdr erwartet on oder off.");
                }

                GameEntry entry;
                try
                {
                    entry = ManualGames.Create(services.FileSystem, Path.GetFullPath(path), args.Get("--name"));
                }
                catch (ArgumentException ex)
                {
                    stderr.WriteLine(ex.Message);
                    return ExitCodes.GameNotFound;
                }

                var config = OpenConfig(paths);
                config.Update(c => c.Games.Add(new GameConfig
                {
                    Source = entry.Source,
                    SourceId = entry.SourceId,
                    Name = entry.Name,
                    InstallDir = entry.InstallDir,
                    KnownExecutables = [.. entry.Executables],
                    LaunchCommand = entry.LaunchCommand,
                    LaunchArguments = entry.LaunchArguments,
                    LaunchWorkingDirectory = entry.LaunchWorkingDirectory,
                    HdrAuto = hdr,
                    IsNew = false,
                    FirstSeen = services.Clock.UtcNow,
                }));
                stdout.WriteLine(entry.Key);
                return ExitCodes.Ok;
            }

            case "set":
            {
                if (args.Positional.Count < 3)
                {
                    return Usage("games set braucht einen Schlüssel (Quelle:ID).");
                }

                var key = args.Positional[2];
                var config = OpenConfig(paths);
                if (config.Current.FindGame(key) is null)
                {
                    stderr.WriteLine($"Spiel {key} ist nicht in der Konfiguration.");
                    return ExitCodes.GameNotFound;
                }

                if (!TryOnOff(args.Get("--hdr"), null, out var hdr) || !TryOnOff(args.Get("--restore"), null, out var restore))
                {
                    return Usage("--hdr und --restore erwarten on oder off.");
                }

                var display = args.Get("--display");
                config.UpdateGame(key, g =>
                {
                    if (hdr is { } h)
                    {
                        g.HdrAuto = h;
                    }

                    if (restore is { } r)
                    {
                        g.RestoreAfter = r;
                    }

                    if (display is not null)
                    {
                        g.Display = display.Equals("global", StringComparison.OrdinalIgnoreCase) ? null : display;
                    }

                    if (args.Get("--name") is { } name)
                    {
                        g.Name = name;
                    }

                    g.IsNew = false;
                });
                stdout.WriteLine($"{key} gespeichert.");
                return ExitCodes.Ok;
            }

            default:
                return Usage("games braucht einen Unterbefehl: list, import, add oder set.");
        }
    }

    private async Task<int> RunWatchAsync(CliArguments args, CancellationToken cancellationToken)
    {
        var unknown = args.UnknownOptions("--config-dir").ToList();
        if (unknown.Count > 0)
        {
            return Usage("Unbekannte Option: " + string.Join(", ", unknown));
        }

        var level = args.Has("--verbose") ? LogLevel.Debug : LogLevel.Information;
        await using var host = services.CreateHost(ResolvePaths(args), new TextWriterLoggerProvider(stdout, () => level));
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        var s = host.Config.Current.Settings;
        stdout.WriteLine($"Überwachung läuft (Intervall {s.PollIntervalMs} ms, Nachlaufzeit {s.GracePeriodSeconds} s, Protokoll {host.Paths.LogDirectory}). Beenden mit Strg+C.");
        stdout.Flush();
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Strg+C: normales Ende.
        }

        return ExitCodes.Ok;
    }

    private async Task<int> RunLaunchAsync(CliArguments args, CancellationToken cancellationToken)
    {
        if (args.Positional.Count < 2)
        {
            return Usage("run braucht einen Schlüssel (Quelle:ID).");
        }

        var key = args.Positional[1];
        var level = args.Has("--verbose") ? LogLevel.Debug : LogLevel.Information;
        await using var host = services.CreateHost(ResolvePaths(args), new TextWriterLoggerProvider(stdout, () => level));
        if (host.Config.Current.FindGame(key) is null)
        {
            stderr.WriteLine($"Spiel {key} ist nicht in der Konfiguration.");
            return ExitCodes.GameNotFound;
        }

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        var outcome = await host.LaunchAsync(key, cancellationToken).ConfigureAwait(false);
        if (!outcome.Started)
        {
            stderr.WriteLine(outcome.Message);
            return ExitCodes.Error;
        }

        // Bis zum Spielende weiter überwachen, damit die Nachlaufzeit und das Zurückstellen stattfinden.
        try
        {
            while (host.Sessions.Sessions.Any(s => string.Equals(s.GameKey, key, StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return ExitCodes.Ok;
    }

    private static bool TryOnOff(string? text, bool? fallback, out bool? value)
    {
        value = fallback;
        if (text is null)
        {
            return true;
        }

        if (text.Equals("on", StringComparison.OrdinalIgnoreCase) || text.Equals("an", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (text.Equals("off", StringComparison.OrdinalIgnoreCase) || text.Equals("aus", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryOnOff(string? text, bool fallback, out bool value)
    {
        var ok = TryOnOff(text, (bool?)fallback, out var v);
        value = v ?? fallback;
        return ok;
    }
}
