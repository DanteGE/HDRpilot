using System.Text.Json.Nodes;
using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Config;

/// <summary>
/// „Nicht installierte Spiele ausblenden“ (E90, G11.1): fehlt der Wert, ist es aus; die Rundreise stimmt; eine ältere
/// Konfiguration lädt unverändert, und nach dem Umschalten unterscheidet sich die Datei nur in diesem Wert.
/// </summary>
public sealed class UninstalledSettingsTests
{
    private const string Path = @"C:\Users\Test\AppData\Roaming\HdrPilot\config.json";

    /// <summary>Konfiguration wie von 1.4.0 geschrieben (alle damaligen Felder, ein nicht installiertes Spiel mit Einstellungen).</summary>
    private const string OldConfig = """
        {
          "version": 1,
          "settings": {
            "targetDisplays": [],
            "pollIntervalMs": 1000,
            "gracePeriodSeconds": 0,
            "launchTimeoutSeconds": 120,
            "launcherSignals": true,
            "startWithWindows": true,
            "notifications": false,
            "hotkeyFallback": false,
            "hdrListSync": true,
            "searchFolders": [],
            "logLevel": "Information",
            "librarySortOrder": "LastPlayed",
            "libraryDetailsRatio": 0.42
          },
          "games": [
            {
              "source": "Steam",
              "sourceId": "9990160",
              "name": "NACHTPROBE",
              "installDir": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Nachtprobe",
              "knownExecutables": [],
              "detectedExecutables": [],
              "excludedExecutables": [],
              "extraExecutables": [],
              "hdrAuto": true,
              "autoHdr": false,
              "restoreAfter": true,
              "isNew": false,
              "isInstalled": false,
              "firstSeen": "2026-10-01T18:00:00+00:00",
              "lastSeen": "2026-10-17T18:00:00+00:00",
              "iconCandidates": []
            },
            {
              "source": "Steam",
              "sourceId": "9990110",
              "name": "STERNENWACHT 2",
              "installDir": "D:\\Steam-Bibliothek\\steamapps\\common\\Sternenwacht 2",
              "knownExecutables": ["D:\\Steam-Bibliothek\\steamapps\\common\\Sternenwacht 2\\bin\\sternenwacht2.exe"],
              "detectedExecutables": [],
              "excludedExecutables": [],
              "extraExecutables": [],
              "hdrAuto": true,
              "autoHdr": false,
              "restoreAfter": false,
              "isNew": false,
              "isInstalled": true,
              "iconCandidates": []
            }
          ]
        }
        """;

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    private ConfigService Service() => new(new ConfigStore(_fs, Path, _clock), _clock);

    [Fact]
    public void Uninstalled_hide_setting_missing_means_off_and_survives_a_restart()
    {
        _fs.AddFile(Path, OldConfig);
        var service = Service();

        Assert.Null(service.LoadWarning);
        Assert.False(service.Current.Settings.HideUninstalledGames);
        Assert.Empty(_fs.EnumerateFiles(System.IO.Path.GetDirectoryName(Path)!, "*.bak")); // nichts als „defekt“ gesichert

        service.Update(c => c.Settings.HideUninstalledGames = true);
        Assert.Contains("\"hideUninstalledGames\": true", _fs.ReadAllText(Path), StringComparison.Ordinal);
        Assert.True(Service().Current.Settings.HideUninstalledGames);

        service.Update(c => c.Settings.HideUninstalledGames = false);
        Assert.False(Service().Current.Settings.HideUninstalledGames);
    }

    [Fact]
    public void Uninstalled_old_config_loads_unchanged_and_toggling_changes_only_the_hide_setting()
    {
        _fs.AddFile(Path, OldConfig);
        var service = Service();
        var games = service.Current.Games;
        Assert.Equal(2, games.Count);
        Assert.False(games[0].IsInstalled);
        Assert.True(games[0].HdrAuto);

        // Gespeichert mit dem neuen Stand, aber ohne Umschalten: Vergleichsstand (neue Felder mit Standardwerten).
        service.Update(_ => { });
        var baseline = JsonNode.Parse(_fs.ReadAllText(Path))!;

        service.Update(c => c.Settings.HideUninstalledGames = true);
        var toggled = JsonNode.Parse(_fs.ReadAllText(Path))!;

        Assert.Equal(["settings.hideUninstalledGames"], Differences(baseline, toggled, string.Empty));

        // Keine Einstellung der alten Datei geht verloren oder ändert sich: Jeder alte Wert steht unverändert in der neuen.
        var old = JsonNode.Parse(OldConfig)!;
        Assert.DoesNotContain(Differences(old, toggled, string.Empty), p => Exists(old, p));
    }

    /// <summary>Pfade, an denen sich zwei JSON-Bäume unterscheiden (fehlende Felder links zählen nur, wenn sie rechts stehen).</summary>
    private static List<string> Differences(JsonNode? left, JsonNode? right, string path)
    {
        if (left is JsonObject l && right is JsonObject r)
        {
            return l.Select(p => p.Key).Union(r.Select(p => p.Key))
                .SelectMany(k => Differences(l[k], r[k], path.Length == 0 ? k : path + "." + k)).ToList();
        }

        if (left is JsonArray la && right is JsonArray ra)
        {
            return la.Count != ra.Count
                ? [path]
                : la.Select((item, i) => Differences(item, ra[i], path + "[" + i + "]")).SelectMany(d => d).ToList();
        }

        return JsonNode.DeepEquals(left, right) ? [] : [path];
    }

    /// <summary>Gibt es den Pfad (aus <see cref="Differences"/>) im Baum?</summary>
    private static bool Exists(JsonNode root, string path)
    {
        JsonNode? node = root;
        foreach (var part in path.Split('.'))
        {
            var name = part;
            var index = -1;
            var bracket = part.IndexOf('[', StringComparison.Ordinal);
            if (bracket >= 0)
            {
                name = part[..bracket];
                index = int.Parse(part[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
            }

            node = node is JsonObject obj && obj.TryGetPropertyValue(name, out var child) ? child : null;
            if (index >= 0)
            {
                node = node is JsonArray array && index < array.Count ? array[index] : null;
            }

            if (node is null)
            {
                return false;
            }
        }

        return true;
    }
}
