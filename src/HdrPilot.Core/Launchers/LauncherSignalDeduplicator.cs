using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Launchers;

/// <summary>
/// Ein Signal je Startvorgang. Steam schreibt zu einem Start viele Zeilen mit derselben (AppID, ActionID); Epic kann
/// denselben Start über URI und Bibliothek melden. Nicht threadsicher (der Watcher ruft es nur unter seiner Sperre auf).
/// </summary>
public sealed class LauncherSignalDeduplicator(IClock clock)
{
    /// <summary>Epic: weitere Meldungen desselben Spiels innerhalb dieser Zeit gehören zum selben Start.</summary>
    public static readonly TimeSpan EpicWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Steam: Ein Startvorgang bleibt bekannt, solange höchstens so lange keine Zeile mehr dazu kam (gleitend). Steam zählt
    /// die ActionID nach einem Neustart wieder von vorn; ohne Ablauf ginge ein späterer Start mit derselben Nummer verloren.
    /// Wartet Steam länger auf eine Antwort (etwa „anderes Gerät abmelden“), meldet die nächste Zeile den Start erneut:
    /// Bis dahin hat die Startfrist HDR schon zurückgestellt, so kommt es kurz vor dem Spielprozess wieder.
    /// </summary>
    public static readonly TimeSpan SteamIdle = TimeSpan.FromMinutes(2);

    /// <summary>Bekannte Starts mit ihrem Ablaufzeitpunkt.</summary>
    private readonly Dictionary<string, DateTimeOffset> _expires = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True, wenn das Signal einen neuen Start meldet und weitergegeben werden soll.</summary>
    public bool IsNew(LauncherSignal signal)
    {
        var now = clock.UtcNow;
        Prune(now);
        if (signal.ActionId is { } actionId)
        {
            var key = signal.GameKey + "#" + actionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var known = _expires.ContainsKey(key);
            _expires[key] = now + SteamIdle;
            return !known;
        }

        if (_expires.ContainsKey(signal.GameKey))
        {
            return false;
        }

        _expires[signal.GameKey] = now + EpicWindow;
        return true;
    }

    private void Prune(DateTimeOffset now)
    {
        if (_expires.Count == 0)
        {
            return;
        }

        foreach (var (key, expires) in _expires.ToList())
        {
            if (expires <= now)
            {
                _expires.Remove(key);
            }
        }
    }
}
