namespace HdrPilot.App.Theming;

/// <summary>Was das Fenster mit einem Ereignis der Spielfarben tun soll.</summary>
public enum GameAccentAction
{
    /// <summary>Nichts (etwa Farben kommen an, während gehalten wird).</summary>
    None,

    /// <summary>Jetzt anwenden.</summary>
    ApplyNow,

    /// <summary>Gezeigte Farben behalten und zum Zeitpunkt <see cref="GameAccentDecision.DueAt"/> erneut fragen.</summary>
    Hold,
}

/// <summary>Entscheidung des <see cref="GameAccentScheduler"/>.</summary>
public readonly record struct GameAccentDecision(GameAccentAction Action, TimeSpan DueAt)
{
    public static GameAccentDecision None { get; } = new(GameAccentAction.None, TimeSpan.Zero);

    public static GameAccentDecision ApplyNow { get; } = new(GameAccentAction.ApplyNow, TimeSpan.Zero);

    public static GameAccentDecision Hold(TimeSpan dueAt) => new(GameAccentAction.Hold, dueAt);
}

/// <summary>
/// Entprellung der Spielfarben (E94) mit vorderer Flanke, ohne eigene Zeitgeber: Ein einzelner Wechsel gilt sofort. Folgen
/// Wechsel schneller als <see cref="Quiet"/> aufeinander oder ist eine Taste gehalten, bleiben die gezeigten Farben stehen, und
/// das zuletzt gewählte Spiel wird <see cref="Quiet"/> nach dem letzten Wechsel angewendet. Gehaltene Pfeiltaste (Windows:
/// etwa 500 ms Verzögerung, dann alle 33 ms): eine Überblendung zu Beginn und eine am Ende.
/// </summary>
public sealed class GameAccentScheduler
{
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private TimeSpan? _last;
    private TimeSpan _dueAt;

    /// <summary>Gezeigte Farben werden gerade gehalten.</summary>
    public bool IsHolding { get; private set; }

    /// <summary>Auswahl hat gewechselt; <paramref name="repeat"/>: ausgelöst von einer gehaltenen Taste.</summary>
    public GameAccentDecision Selected(TimeSpan now, bool repeat)
    {
        var fast = repeat || (_last is { } last && now - last < Quiet);
        _last = now;
        if (!fast)
        {
            IsHolding = false;
            return GameAccentDecision.ApplyNow;
        }

        IsHolding = true;
        _dueAt = now + Quiet;
        return GameAccentDecision.Hold(_dueAt);
    }

    /// <summary>Farben oder Bilder der aktuellen Auswahl sind angekommen.</summary>
    public GameAccentDecision ColorsArrived() => IsHolding ? GameAccentDecision.None : GameAccentDecision.ApplyNow;

    /// <summary>Der Zeitgeber des Fensters ist abgelaufen.</summary>
    public GameAccentDecision Elapsed(TimeSpan now)
    {
        if (!IsHolding)
        {
            return GameAccentDecision.None;
        }

        if (now >= _dueAt)
        {
            IsHolding = false;
            return GameAccentDecision.ApplyNow;
        }

        return GameAccentDecision.Hold(_dueAt);
    }
}
