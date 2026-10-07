using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;

namespace HdrPilot.Core.Config;

public sealed record MergeSummary(int Added, int Updated, int NoLongerInstalled, int Reinstalled);

/// <summary>
/// Überträgt ein Einlese-Ergebnis in die Konfiguration. Einstellungen hängen am Schlüssel (Quelle:ID) und überleben
/// so das erneute Einlesen. Neue Spiele sind markiert und standardmäßig aus. Verschwundene Spiele bleiben als
/// „nicht installiert“ erhalten. Spiele eines abgestürzten Scanners werden nicht als verschwunden markiert.
/// </summary>
public static class LibraryMerger
{
    public static MergeSummary Apply(AppConfig config, LibraryScanReport report, IFileSystem fileSystem, DateTimeOffset now)
    {
        var added = 0;
        var updated = 0;
        var reinstalled = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in report.Games)
        {
            seen.Add(entry.Key);
            var game = config.FindGame(entry.Key);
            if (game is null)
            {
                config.Games.Add(new GameConfig
                {
                    Source = entry.Source,
                    SourceId = entry.SourceId,
                    Name = entry.Name,
                    InstallDir = entry.InstallDir,
                    KnownExecutables = [.. entry.Executables],
                    LaunchCommand = entry.LaunchCommand,
                    LaunchArguments = entry.LaunchArguments,
                    LaunchWorkingDirectory = entry.LaunchWorkingDirectory,
                    HdrAuto = false,
                    IsNew = true,
                    IsInstalled = true,
                    FirstSeen = now,
                    LastSeen = now,
                });
                added++;
                continue;
            }

            if (!game.IsInstalled)
            {
                reinstalled++;
            }

            game.Name = entry.Name;
            game.InstallDir = entry.InstallDir;
            game.KnownExecutables = [.. entry.Executables];
            game.LaunchCommand = entry.LaunchCommand;
            game.LaunchArguments = entry.LaunchArguments;
            game.LaunchWorkingDirectory = entry.LaunchWorkingDirectory;
            game.IsInstalled = true;
            game.LastSeen = now;
            updated++;
        }

        // Nur Quellen, deren Scanner ohne Absturz durchlief, dürfen Spiele als verschwunden markieren.
        var completedSources = report.Scanners
            .Where(s => !s.Result.Errors.Any(e => e.Message.StartsWith("Scanner abgestürzt", StringComparison.Ordinal)))
            .Select(s => s.Result.Source)
            .ToHashSet();

        var missing = 0;
        foreach (var game in config.Games)
        {
            if (game.IsManual)
            {
                var exists = fileSystem.DirectoryExists(game.InstallDir);
                if (game.IsInstalled && !exists)
                {
                    missing++;
                }
                else if (!game.IsInstalled && exists)
                {
                    reinstalled++;
                }

                game.IsInstalled = exists;
                continue;
            }

            if (!seen.Contains(game.Key) && completedSources.Contains(game.Source) && game.IsInstalled)
            {
                game.IsInstalled = false;
                missing++;
            }
        }

        return new MergeSummary(added, updated, missing, reinstalled);
    }
}
