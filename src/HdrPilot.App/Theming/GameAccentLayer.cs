using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace HdrPilot.App.Theming;

/// <summary>
/// Farbebene der Spielfarben im Bibliotheksfenster (E94): ein Wörterbuch, das das Fenster vor die Ressourcen der App legt
/// (nur bei eingeschaltetem Schalter und ohne hohen Kontrast). Es enthält die 25 Schlüssel des Akzents
/// (<see cref="ThemeResources.AddAccentEntries"/>): 14 bleibende Pinsel, die je Wechsel im OKLCH-Farbraum überblenden
/// (<see cref="LiveBrushSet"/>), und die 11 Farbwerte in einem Kind-Wörterbuch, das zu Beginn einer Überblendung einmal
/// ausgetauscht wird (wie die App-Ressourcen „Farben gelten sofort“). Flächen, Text, Zustände, Hintergrund und Schriften bleiben
/// beim Schema. Kein Wörterbuch wird je Spiel ausgetauscht; im Leerlauf läuft nichts.
/// </summary>
public sealed class GameAccentLayer : IDisposable
{
    /// <summary>Kennzeichen im Wörterbuch der Ebene (zum Wiederfinden, etwa in den Ressourcen eines Popups).</summary>
    public const string MarkerKey = "Hp.GameAccentLayer";

    /// <summary>Zuschlag nach dem Ende einer Überblendung, bevor die Grundwerte gesetzt und die Animationen entfernt werden.</summary>
    public static readonly TimeSpan SettleMargin = TimeSpan.FromMilliseconds(80);

    private readonly Dispatcher _dispatcher;
    private readonly LiveBrushSet _brushes = new();
    private readonly List<Action> _settled = [];
    private DispatcherTimer? _timer;
    private bool _pending;
    private bool _disposed;

    public GameAccentLayer(Dispatcher dispatcher, ThemePalette initial)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(initial);
        _dispatcher = dispatcher;
        Target = initial;
        Dictionary[MarkerKey] = true;
        var values = Entries(initial);
        Dictionary.MergedDictionaries.Add(ColorPart(values));
        foreach (var key in BrushKeys)
        {
            Dictionary[key] = _brushes.Track(null, (Brush)values[key], TimeSpan.Zero);
        }
    }

    /// <summary>Die 14 Pinsel der Ebene (19 Farben).</summary>
    public static IReadOnlyList<object> BrushKeys { get; } =
    [
        ThemeKeys.BrushTextOnAccent, ThemeKeys.BrushAccent, ThemeKeys.BrushAccentShine, ThemeKeys.BrushAccentSolid, ThemeKeys.BrushAccentText,
        ThemeKeys.BrushAccentSoft, ThemeKeys.BrushAccentStrong, ThemeKeys.BrushAtmosphereSoft, ThemeKeys.BrushShine, ThemeKeys.BrushSelectionFill,
        ThemeKeys.BrushSelectionRing, ThemeKeys.BrushFocusRing, ThemeKeys.BrushHdrOn, ThemeKeys.BrushToggleOn,
    ];

    /// <summary>Die 11 Farbwerte der Ebene (Kind-Wörterbuch).</summary>
    public static IReadOnlyList<object> ColorKeys { get; } =
    [
        ThemeKeys.ColorC1, ThemeKeys.ColorC2, ThemeKeys.ColorC3, ThemeKeys.ColorOnAccent, ThemeKeys.ColorAccentText, ThemeKeys.ColorAccent1,
        ThemeKeys.ColorAccent2, ThemeKeys.ColorAccent3, ThemeKeys.ColorGlow, ThemeKeys.ColorAmbient2, ThemeKeys.ColorAmbient3,
    ];

    /// <summary>
    /// Schlüssel, die mit dem Akzent ihren Wert ändern, aber bewusst beim Schema bleiben: Grund und Leuchten des Fensters
    /// (folgen dem Hintergrund, nicht dem Akzent).
    /// </summary>
    public static IReadOnlyList<object> ExcludedKeys { get; } =
    [
        ThemeKeys.ColorBackground, ThemeKeys.ColorAmbient1, ThemeKeys.ColorShadow, ThemeKeys.BrushWindowBackground, ThemeKeys.BrushBackgroundGlow,
        ThemeKeys.BrushBackgroundGlow2,
    ];

    /// <summary>Das Wörterbuch der Ebene (Pinsel direkt, Farbwerte in <c>MergedDictionaries[0]</c>).</summary>
    public ResourceDictionary Dictionary { get; } = new();

    /// <summary>Zuletzt angesteuerte Palette.</summary>
    public ThemePalette Target { get; private set; }

    /// <summary>Zähler (Pinsel ersetzt, Überblendungen, Austausch der Farbwerte).</summary>
    public GameAccentStats Stats => _brushes.Stats;

    /// <summary>Animierte Farben, die noch nicht beruhigt sind (0 im Leerlauf).</summary>
    public int Animating => _brushes.Animating;

    /// <summary>Nur für Tests: statt eines DispatcherTimer (Verzögerung, Rückruf zum Beruhigen).</summary>
    public Action<TimeSpan, Action>? TimerHookForTest { get; set; }

    /// <summary>
    /// Überblendet zur Palette <paramref name="target"/> in <paramref name="duration"/> (0: sofort). Die Farbwerte wechseln zu
    /// Beginn; <paramref name="onSettled"/> läuft, sobald alles beruhigt ist (bei gleichem Ziel ohne Arbeit sofort).
    /// </summary>
    public void TransitionTo(ThemePalette target, TimeSpan duration, Action? onSettled = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_disposed)
        {
            return;
        }

        if (target == Target)
        {
            // Nichts Neues: ohne laufende Überblendung sofort fertig, sonst mit ihr (bei Dauer 0 jetzt).
            Stats.SkippedEqual++;
            if (!_pending)
            {
                onSettled?.Invoke();
                return;
            }

            if (onSettled is not null)
            {
                _settled.Add(onSettled);
            }

            if (duration <= TimeSpan.Zero)
            {
                Settle();
            }

            return;
        }

        Target = target;
        Stats.Applied++;
        var values = Entries(target);
        var part = Dictionary.MergedDictionaries[0];
        if (ColorKeys.Any(k => !Equals(part[k], values[k])))
        {
            Dictionary.MergedDictionaries[0] = ColorPart(values);
            Stats.ColorPartSwaps++;
        }

        foreach (var key in BrushKeys)
        {
            var live = (Brush)Dictionary[key];
            var result = _brushes.Track(live, (Brush)values[key], duration);
            if (!ReferenceEquals(result, live))
            {
                Dictionary[key] = result;
            }
        }

        if (onSettled is not null)
        {
            _settled.Add(onSettled);
        }

        if (duration <= TimeSpan.Zero)
        {
            Settle();
            return;
        }

        _pending = true;
        var delay = duration + SettleMargin;
        if (TimerHookForTest is { } hook)
        {
            hook(delay, Settle);
            return;
        }

        _timer ??= new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
        _timer.Stop();
        _timer.Tick -= OnTimer;
        _timer.Tick += OnTimer;
        _timer.Interval = delay;
        _timer.Start();
    }

    /// <summary>Grundwerte setzen, Animationen entfernen, alle wartenden Rückrufe ausführen.</summary>
    public void Settle()
    {
        _timer?.Stop();
        _brushes.Settle();
        _pending = false;
        if (_settled.Count == 0)
        {
            return;
        }

        var callbacks = _settled.ToArray();
        _settled.Clear();
        foreach (var callback in callbacks)
        {
            callback();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimer;
        }

        _brushes.Settle();
        _settled.Clear();
        _pending = false;
    }

    private static ResourceDictionary Entries(ThemePalette palette)
    {
        var values = new ResourceDictionary();
        ThemeResources.AddAccentEntries(values, palette);
        return values;
    }

    private static ResourceDictionary ColorPart(ResourceDictionary values)
    {
        var part = new ResourceDictionary();
        foreach (var key in ColorKeys)
        {
            part[key] = values[key];
        }

        return part;
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (!_disposed)
        {
            Settle();
        }
    }
}
