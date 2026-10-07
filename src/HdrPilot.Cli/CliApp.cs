using System.Text.Json;
using System.Text.Json.Serialization;
using HdrPilot.Core.Hdr;

namespace HdrPilot.Cli;

/// <summary>Abhängigkeiten der Kommandozeile, damit Befehle mit Attrappen testbar sind.</summary>
public interface ICliServices
{
    IHdrController CreateHdrController(HdrControllerOptions options);
}

public sealed class CliApp(ICliServices services, TextWriter stdout, TextWriter stderr)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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
        HdrPilot Kommandozeile (Diagnose und Tests)

        hdrpilot hdr status [--json] [--api modern|legacy]
            Listet alle aktiven Bildschirme mit Name, HDR-fähig und HDR an/aus.
        hdrpilot hdr on|off|toggle [--display <auswahl>] [--api modern|legacy] [--hotkey-fallback] [--json]
            Schaltet HDR und bestätigt den Zustand durch erneutes Lesen.
            <auswahl>: primary (Standard), all, Nummer aus "hdr status", Teil des Namens oder monitorDevicePath.

        Exit-Codes: 0 ok, 1 Fehler, 2 Aufruf falsch, 3 Bildschirm nicht gefunden,
                    4 Bildschirm ohne HDR, 5 Schalten fehlgeschlagen
        """;

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
            results.Add(await controller.SetHdrAsync(display.MonitorDevicePath, enable, cancellationToken).ConfigureAwait(false));
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
