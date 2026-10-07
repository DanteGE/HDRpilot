using System.Text.Json;
using HdrPilot.Core.Games.Scanners.Common;

namespace HdrPilot.Core.Games.Scanners.Gog;

/// <summary>Eintrag aus <c>playTasks</c> einer <c>goggame-&lt;ID&gt;.info</c>. Pfade relativ zum Spielordner.</summary>
internal sealed record GogPlayTask
{
    public bool IsPrimary { get; init; }

    /// <summary>"FileTask" (Datei starten) oder "URLTask" (Link).</summary>
    public string? Type { get; init; }

    /// <summary>"launcher", "game", "document" oder fehlend (ältere Installer).</summary>
    public string? Category { get; init; }

    public string? Path { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDir { get; init; }

    /// <summary>Ältere Dateien lassen "type" teils weg; dann gilt der Eintrag als Datei-Aufgabe.</summary>
    public bool IsFileTask => Type is null || string.Equals(Type, "FileTask", StringComparison.OrdinalIgnoreCase);

    public bool IsGameCategory => string.Equals(Category, "game", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Inhalt von <c>goggame-&lt;ID&gt;.info</c> im Spielordner (JSON, von GOG geschrieben).</summary>
internal sealed record GogGameInfo
{
    public string? GameId { get; init; }

    /// <summary>ID des Hauptspiels; bei Zusatzinhalten ungleich <see cref="GameId"/>.</summary>
    public string? RootGameId { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<GogPlayTask> PlayTasks { get; init; } = [];

    public GogPlayTask? PrimaryTask => PlayTasks.FirstOrDefault(t => t.IsPrimary && t.IsFileTask);
}

internal static class GogGameInfoReader
{
    /// <summary>Liest eine Spielinfo. Wirft <see cref="InvalidDataException"/> bei falscher Kodierung oder ungültigem JSON.</summary>
    public static GogGameInfo Parse(byte[] bytes)
    {
        using var document = ScannerJson.ParseObject(bytes);
        var root = document.RootElement;
        var tasks = new List<GogPlayTask>();
        if (ScannerJson.TryGetProperty(root, "playTasks", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var task in array.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object))
            {
                tasks.Add(new GogPlayTask
                {
                    IsPrimary = ScannerJson.GetBool(task, "isPrimary") ?? false,
                    Type = ScannerJson.GetString(task, "type"),
                    Category = ScannerJson.GetString(task, "category"),
                    Path = ScannerJson.GetString(task, "path"),
                    Arguments = ScannerJson.GetString(task, "arguments"),
                    WorkingDir = ScannerJson.GetString(task, "workingDir"),
                });
            }
        }

        return new GogGameInfo
        {
            GameId = ScannerJson.GetId(root, "gameId"),
            RootGameId = ScannerJson.GetId(root, "rootGameId"),
            Name = ScannerJson.GetString(root, "name"),
            PlayTasks = tasks,
        };
    }
}
