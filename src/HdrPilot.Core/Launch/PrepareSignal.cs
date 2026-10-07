using System.Text.RegularExpressions;

namespace HdrPilot.Core.Launch;

public enum PrepareWaitResult
{
    /// <summary>HdrPilot hat HDR für das Spiel vorbereitet.</summary>
    Ready,

    /// <summary>HdrPilot hat nichts vorbereitet (etwa unbekanntes Spiel): sofort starten.</summary>
    Skipped,

    /// <summary>Keine Antwort in der Wartezeit.</summary>
    TimedOut,
}

/// <summary>
/// Rückmeldung der laufenden Instanz an den Wrapper über zwei benannte Ereignisse der Sitzung (rein lokal, kein Netz):
/// <c>Local\HdrPilot.Prepared.&lt;guid&gt;</c> heißt „HDR ist bereit“, dasselbe mit <c>.Skip</c> dahinter „nichts
/// vorbereitet“. Der Wrapper legt beide an und reicht nur den ersten Namen weiter (<c>--ready-event</c>).
/// </summary>
public static partial class PrepareSignal
{
    public const string Prefix = @"Local\HdrPilot.Prepared.";

    public const string SkipSuffix = ".Skip";

    /// <summary>Neuer, eindeutiger Name für das Bereit-Ereignis.</summary>
    public static string NewName() => Prefix + Guid.NewGuid().ToString("N");

    /// <summary>Nur Namen in genau diesem Aufbau werden gesetzt, nie beliebige fremde Ereignisse.</summary>
    public static bool IsValidName(string? name) => name is not null && ValidName().IsMatch(name);

    public static string SkipNameFor(string readyName) => readyName + SkipSuffix;

    /// <summary>
    /// Laufende Instanz: meldet „bereit“ (<paramref name="prepared"/>) oder „nichts vorbereitet“. False, wenn der Name
    /// ungültig ist oder der Wrapper nicht mehr wartet (Ereignis existiert nicht mehr).
    /// </summary>
    public static bool TrySignal(string readyName, bool prepared)
    {
        if (!IsValidName(readyName))
        {
            return false;
        }

        try
        {
            if (!EventWaitHandle.TryOpenExisting(prepared ? readyName : SkipNameFor(readyName), out var handle))
            {
                return false;
            }

            using (handle)
            {
                return handle.Set();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^Local\\HdrPilot\.Prepared\.[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();
}

/// <summary>Wrapper: legt die beiden Ereignisse an und wartet auf eines davon.</summary>
public sealed class PrepareWaiter : IDisposable
{
    private readonly EventWaitHandle _ready;
    private readonly EventWaitHandle _skip;

    public PrepareWaiter()
    {
        Name = PrepareSignal.NewName();
        _ready = new EventWaitHandle(false, EventResetMode.ManualReset, Name);
        _skip = new EventWaitHandle(false, EventResetMode.ManualReset, PrepareSignal.SkipNameFor(Name));
    }

    /// <summary>Name des Bereit-Ereignisses für <c>--ready-event</c>.</summary>
    public string Name { get; }

    public PrepareWaitResult Wait(TimeSpan timeout) => WaitHandle.WaitAny([_ready, _skip], timeout) switch
    {
        0 => PrepareWaitResult.Ready,
        1 => PrepareWaitResult.Skipped,
        _ => PrepareWaitResult.TimedOut,
    };

    public void Dispose()
    {
        _ready.Dispose();
        _skip.Dispose();
    }
}
