using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HdrPilot.Core.Launch;

/// <summary>Weg zur laufenden HdrPilot-Instanz (in der App über die Einzelinstanz-Sperre und WM_COPYDATA).</summary>
public interface IWrapInstanceChannel
{
    /// <summary>True, wenn HdrPilot (die Tray-App) für diese Ablage läuft. Startet sie nie.</summary>
    bool IsInstanceRunning();

    /// <summary>Reicht Befehle an die laufende Instanz weiter. True, wenn sie angenommen wurden.</summary>
    bool TrySend(IReadOnlyList<string> args);
}

/// <summary>Startet das Spiel und gibt es sofort frei.</summary>
public interface IWrapGameStarter
{
    void Start(GameCommand game, string workingDirectory);
}

/// <summary>Umgebung des Wrapper-Prozesses (für Tests austauschbar).</summary>
public interface IWrapContext
{
    string? GetEnvironmentVariable(string name);

    string CurrentDirectory { get; }

    /// <summary>Ordner von HdrPilot selbst.</summary>
    string OwnFolder { get; }

    bool FileExists(string path);

    void Sleep(TimeSpan duration);

    DateTimeOffset Now { get; }
}

public sealed record WrapOptions
{
    /// <summary>So lange wartet der Wrapper höchstens auf die Rückmeldung von HdrPilot.</summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Nach „HDR bereit“ noch so lange warten: ein Spiel soll nicht starten, während der Bildschirm noch umschaltet.
    /// Starts über HdrPilot, die nie Probleme machten, hatten 2–3 s Vorlauf.
    /// </summary>
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromSeconds(1.5);

    public static WrapOptions Default { get; } = new();
}

/// <summary>Was ein Aufruf getan hat (für Protokoll und Tests).</summary>
public sealed record WrapOutcome
{
    /// <summary>Wert von SteamAppId (Rückfall SteamGameId), wie gesetzt.</summary>
    public string? SteamAppId { get; init; }

    public string? GameKey { get; init; }

    public bool Prepared { get; init; }

    /// <summary>Kurztext für das Protokoll, etwa "ja" oder "nein (HdrPilot läuft nicht)".</summary>
    public required string PrepareStatus { get; init; }

    /// <summary>Zeit vom Aufruf bis zum Spielstart (Vorbereitung und Nachlauf).</summary>
    public TimeSpan Waited { get; init; }

    public GameCommand? Game { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool Started { get; init; }

    public string? StartError { get; init; }
}

/// <summary>
/// Steam-Startoption <c>"…\HdrPilot.exe" --wrap -- %command%</c>: HDR ist an, bevor das Spiel startet, auch beim Start
/// aus der Steam-Bibliothek. Läuft HdrPilot, bittet der Wrapper die laufende Instanz, HDR für <c>Steam:&lt;SteamAppId&gt;</c>
/// vorzubereiten, wartet auf die Rückmeldung (höchstens 8 s) und kurz auf den Bildschirm (1,5 s), startet dann das Spiel
/// und endet sofort. Läuft HdrPilot nicht, fehlt die SteamAppId oder kennt HdrPilot das Spiel nicht, startet das Spiel
/// sofort ohne HDR.
/// <list type="bullet">
/// <item>Startet nie die Tray-App: Steam zählt alle Nachkommen des Gestarteten zum Spiel.</item>
/// <item>Hält kein Handle auf das Spiel (Anti-Cheat): starten, sofort freigeben, beenden. Kein Warten auf das Spielende.</item>
/// <item>Der Spielstart scheitert nie am Wrapper: bei jedem Fehler wird trotzdem gestartet.</item>
/// <item>Schreibt je Aufruf genau eine Zeile in <see cref="WrapLog"/>.</item>
/// </list>
/// </summary>
public sealed class SteamWrapper(
    IWrapInstanceChannel channel, IWrapGameStarter starter, IWrapContext context, WrapLog? log, WrapOptions? options = null)
{
    private readonly WrapOptions _options = options ?? WrapOptions.Default;

    public WrapOutcome Run(WrapRequest request)
    {
        var watch = Stopwatch.StartNew();
        GameCommand? game = null;
        string? workingDirectory = null;
        string? rawAppId = null;
        string? key = null;
        var prepared = false;
        string status;
        try
        {
            game = WrapCommandLine.SplitGameCommand(request.GameCommand, context.FileExists);
            if (game is null)
            {
                status = "nein (kein Spielbefehl)";
            }
            else
            {
                workingDirectory = WrapCommandLine.ChooseWorkingDirectory(SafeCurrentDirectory(), context.OwnFolder, game.ExePath);
                key = WrapCommandLine.SteamGameKey(context.GetEnvironmentVariable, out rawAppId);
                status = key is not null
                    ? Prepare(key, out prepared)
                    : rawAppId is null ? "nein (keine SteamAppId)" : "nein (SteamAppId ungültig)";
            }
        }
        catch (Exception ex)
        {
            status = "nein (Fehler: " + ex.Message + ")";
        }

        var waited = watch.Elapsed;
        var started = false;
        string? startError = null;
        if (game is not null)
        {
            try
            {
                starter.Start(game, workingDirectory ?? SafeCurrentDirectory());
                started = true;
            }
            catch (Exception ex)
            {
                startError = ex.Message;
            }
        }

        var outcome = new WrapOutcome
        {
            SteamAppId = rawAppId,
            GameKey = key,
            Prepared = prepared,
            PrepareStatus = status,
            Waited = waited,
            Game = game,
            WorkingDirectory = workingDirectory,
            Started = started,
            StartError = startError,
        };
        log?.TryAppend(FormatLine(request, outcome));
        return outcome;
    }

    /// <summary>Eine Protokollzeile: Zeit, SteamAppId, Ergebnis, Wartezeit, Arbeitsordner, Start, rohe Befehlszeile.</summary>
    public string FormatLine(WrapRequest request, WrapOutcome outcome)
    {
        var line = new StringBuilder(256)
            .Append(context.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" | SteamAppId: ").Append(outcome.SteamAppId ?? "-")
            .Append(" | HDR vorbereitet: ").Append(outcome.PrepareStatus)
            .Append(" | gewartet: ").Append(((long)outcome.Waited.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append(" ms")
            .Append(" | Arbeitsordner: ").Append(outcome.WorkingDirectory ?? "-")
            .Append(" | Spiel gestartet: ")
            .Append(outcome.Started ? "ja" : outcome.Game is null ? "nein (kein Spielbefehl)" : "nein (" + outcome.StartError + ")");
        if (!request.SeparatorFound)
        {
            line.Append(" | Hinweis: Startoption ohne \"--\" eingetragen");
        }

        return line.Append(" | Befehlszeile: ").Append(request.RawCommandLine).ToString();
    }

    private string Prepare(string key, out bool prepared)
    {
        prepared = false;
        if (!channel.IsInstanceRunning())
        {
            return "nein (HdrPilot läuft nicht)";
        }

        using var waiter = new PrepareWaiter();
        if (!channel.TrySend(["--prepare", key, "--ready-event", waiter.Name]))
        {
            return "nein (HdrPilot hat den Befehl nicht angenommen)";
        }

        switch (waiter.Wait(_options.ReadyTimeout))
        {
            case PrepareWaitResult.Ready:
                prepared = true;
                context.Sleep(_options.SettleTime);
                return "ja";
            case PrepareWaitResult.Skipped:
                return "nein (HdrPilot hat nichts vorbereitet, etwa weil das Spiel unbekannt ist)";
            default:
                return "nein (keine Antwort nach " + _options.ReadyTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s)";
        }
    }

    private string SafeCurrentDirectory()
    {
        try
        {
            return context.CurrentDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Startet das Spiel direkt, wie Steam es getan hätte: Umgebung (SteamAppId usw.) unverändert geerbt, Argumente
/// unverändert. Der Prozess wird sofort freigegeben: kein Handle, kein Warten (Anti-Cheat).
/// </summary>
public sealed class DirectGameStarter : IWrapGameStarter
{
    public void Start(GameCommand game, string workingDirectory)
    {
        var info = new ProcessStartInfo(game.ExePath)
        {
            UseShellExecute = false,
            Arguments = game.Arguments,

            // Ein fehlender Ordner ließe CreateProcess scheitern: dann den geerbten nehmen.
            WorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory) ? workingDirectory : string.Empty,
        };
        using var process = Process.Start(info);
    }
}

/// <summary>Die echte Umgebung des Wrapper-Prozesses.</summary>
public sealed class SystemWrapContext : IWrapContext
{
    public static SystemWrapContext Instance { get; } = new();

    public string CurrentDirectory => Environment.CurrentDirectory;

    public string OwnFolder => AppContext.BaseDirectory;

    public DateTimeOffset Now => DateTimeOffset.Now;

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);

    public void Sleep(TimeSpan duration) => Thread.Sleep(duration);
}
