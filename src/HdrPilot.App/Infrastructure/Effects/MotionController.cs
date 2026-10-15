using System.Windows;
using System.Windows.Media.Animation;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Infrastructure.Effects;

/// <summary>Warum Dauer-Effekte gerade stillstehen (Statuszeile).</summary>
public enum MotionPause
{
    /// <summary>Sie laufen (oder sind auf dieser Stufe ohnehin aus).</summary>
    None,

    /// <summary>Fenster minimiert oder nicht sichtbar.</summary>
    Hidden,

    /// <summary>Fenster im Hintergrund (inaktiv) und „Im Hintergrund anhalten“ an.</summary>
    Background,

    /// <summary>Ein Spiel läuft und „Beim Spielen anhalten“ an.</summary>
    GameRunning,
}

/// <summary>
/// Ein Dauer-Effekt (langsamer Zoom, Lichtstrahlen, Lichtpunkte, Glanz, Auswahlring, Puls): ein Storyboard, das erst beim
/// ersten Start entsteht (Stufe „Reduziert“/„Aus“: nie), höchstens <see cref="EffectPolicy.ContinuousFrameRate"/> Bilder
/// pro Sekunde zeigt und vom <see cref="MotionController"/> angehalten, fortgesetzt und entfernt wird.
/// </summary>
public sealed class ContinuousEffect
{
    private readonly Func<Storyboard> _factory;
    private readonly FrameworkElement _target;
    private Storyboard? _board;

    internal ContinuousEffect(string name, FrameworkElement target, Func<Storyboard> factory)
    {
        Name = name;
        _target = target;
        _factory = factory;
    }

    public string Name { get; }

    /// <summary>Das Storyboard (null, solange der Effekt nie lief oder nachdem er entfernt wurde).</summary>
    public Storyboard? Board => _board;

    /// <summary>Wie oft das Storyboard erzeugt wurde (Tests: „Reduziert“ erzeugt keins).</summary>
    public int Created { get; private set; }

    public bool IsRunning { get; private set; }

    public bool IsPaused { get; private set; }

    /// <summary>Soll laufen (etwa nur, solange eine Kachel gewählt ist oder HDR an ist); der Controller prüft zusätzlich die Regeln.</summary>
    public bool Wanted { get; private set; } = true;

    internal Action? WantedChanged { get; set; }

    /// <summary>Effekt an- oder abmelden (zum Beispiel Puls nur bei HDR an).</summary>
    public void SetWanted(bool wanted)
    {
        if (Wanted != wanted)
        {
            Wanted = wanted;
            WantedChanged?.Invoke();
        }
    }

    internal void Run()
    {
        if (IsRunning && !IsPaused)
        {
            return;
        }

        if (_board is null)
        {
            _board = _factory();
            Timeline.SetDesiredFrameRate(_board, EffectPolicy.ContinuousFrameRate);
            Created++;
            _board.Begin(_target, isControllable: true);
        }
        else if (IsPaused)
        {
            _board.Resume(_target);
        }
        else
        {
            _board.Begin(_target, isControllable: true);
        }

        IsRunning = true;
        IsPaused = false;
    }

    /// <summary>Anhalten an der Stelle (sichtbares Fenster im Hintergrund: kein Sprung).</summary>
    internal void Pause()
    {
        if (_board is not null && IsRunning && !IsPaused)
        {
            _board.Pause(_target);
            IsPaused = true;
        }
    }

    /// <summary>Entfernen (minimiert, unsichtbar, Spiel läuft, Stufe geändert, Fenster zu): keine Uhr bleibt übrig.</summary>
    internal void Remove()
    {
        if (_board is not null)
        {
            _board.Remove(_target);
            _board = null;
        }

        IsRunning = false;
        IsPaused = false;
    }
}

/// <summary>
/// Steuerung der Effekte eines Fensters (E89, G10.6, G10.9): wirksame Stufe aus der Darstellung, Lage des Fensters
/// (sichtbar, minimiert, aktiv), laufendes Spiel. Dauer-Effekte laufen nur nach <see cref="EffectPolicy.ContinuousAllowed"/>:
/// im Hintergrund werden sie angehalten, minimiert, unsichtbar, beim Spielen und bei geänderter Stufe entfernt, beim
/// Schließen endgültig entfernt. Einmal-Effekte fragen <see cref="OneShotAllowed"/> und <see cref="Transition"/>. Kein
/// eigener Zeitgeber: alles hängt an Fenster-Ereignissen und Änderungen der Darstellung.
/// </summary>
public sealed class MotionController : IDisposable
{
    private readonly IAppearanceService _appearance;
    private readonly List<ContinuousEffect> _effects = [];
    private Window? _window;
    private bool _visible;
    private bool _minimized;
    private bool _active;
    private bool _gameRunning;
    private bool _disposed;
    private EffectLevel _level;
    private bool _continuousAllowed;

    public MotionController(IAppearanceService appearance)
    {
        _appearance = appearance;
        _level = appearance.Current.EffectLevel;
        appearance.Changed += OnAppearanceChanged;
    }

    /// <summary>Stufe, Erlaubnis oder Grund des Anhaltens haben sich geändert (Statuszeile, Ansicht).</summary>
    public event EventHandler? Changed;

    /// <summary>Wirksame Stufe (Windows-Animationseffekte und Hardware-Darstellung berücksichtigt).</summary>
    public EffectLevel Level => _level;

    public MotionContext Context => new(_visible, _minimized, _active, _gameRunning, _appearance.Current.PauseInBackground, _appearance.Current.PauseWhilePlaying);

    public bool ContinuousAllowed => _continuousAllowed;

    public bool OneShotAllowed => !_disposed && EffectPolicy.OneShotAllowed(_level, Context);

    public IReadOnlyList<ContinuousEffect> Effects => _effects;

    public bool IsDisposed => _disposed;

    /// <summary>Grund, warum Dauer-Effekte gerade stillstehen (nur bei „Voll“ von Belang).</summary>
    public MotionPause Pause
    {
        get
        {
            if (_level != EffectLevel.Full || _continuousAllowed)
            {
                return MotionPause.None;
            }

            var context = Context;
            if (!context.WindowVisible || context.Minimized)
            {
                return MotionPause.Hidden;
            }

            return context.GameRunning && context.PauseWhilePlaying ? MotionPause.GameRunning : MotionPause.Background;
        }
    }

    /// <summary>
    /// Dauer eines Übergangs auf der wirksamen Stufe (Voll unverändert, Reduziert höchstens 0,3 s, Aus sofort). Ein
    /// unsichtbares oder minimiertes Fenster (und eines über einem laufenden Spiel) springt sofort an das Ziel: niemand sieht
    /// die Bewegung, und Bildschirmfotos und Prüfungen sehen gleich den Endzustand.
    /// </summary>
    public TimeSpan Transition(TimeSpan full) => OneShotAllowed ? EffectPolicy.Transition(_level, full) : TimeSpan.Zero;

    /// <summary>Hört auf Aktivieren, Deaktivieren, Minimieren, Sichtbarkeit und Schließen des Fensters.</summary>
    public void Attach(Window window)
    {
        if (_window is not null || _disposed)
        {
            return;
        }

        _window = window;
        window.Activated += OnWindowChanged;
        window.Deactivated += OnWindowChanged;
        window.StateChanged += OnWindowChanged;
        window.IsVisibleChanged += OnWindowVisibilityChanged;
        window.Closed += OnWindowClosed;
        ReadWindow();
    }

    /// <summary>Lage des Fensters von außen setzen (Tests; ein nie angezeigtes Fenster ist nie sichtbar oder aktiv).</summary>
    public void SetWindowState(bool visible, bool minimized, bool active)
    {
        _visible = visible;
        _minimized = minimized;
        _active = active;
        Evaluate();
    }

    /// <summary>Ein Spiel läuft (laufende Sitzung) oder nicht mehr.</summary>
    public void SetGameRunning(bool running)
    {
        if (_gameRunning != running)
        {
            _gameRunning = running;
            Evaluate();
        }
    }

    /// <summary>
    /// Meldet einen Dauer-Effekt an. Das Storyboard entsteht erst, wenn er laufen darf (<paramref name="factory"/> wird bei
    /// „Reduziert“ und „Aus“ nie aufgerufen) und bekommt höchstens 30 Bilder pro Sekunde.
    /// </summary>
    public ContinuousEffect Register(string name, FrameworkElement target, Func<Storyboard> factory, bool wanted = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var effect = new ContinuousEffect(name, target, factory);
        effect.SetWanted(wanted);
        effect.WantedChanged = () => Apply(effect);
        _effects.Add(effect);
        Apply(effect);
        return effect;
    }

    /// <summary>Effekt abmelden und sein Storyboard entfernen.</summary>
    public void Unregister(ContinuousEffect effect)
    {
        effect.Remove();
        effect.WantedChanged = null;
        _effects.Remove(effect);
    }

    /// <summary>Regeln neu auswerten und alle Effekte starten, anhalten oder entfernen.</summary>
    public void Evaluate()
    {
        if (_disposed)
        {
            return;
        }

        var level = _appearance.Current.EffectLevel;
        var levelChanged = level != _level;
        _level = level;
        var allowed = EffectPolicy.ContinuousAllowed(level, Context);
        var changed = levelChanged || allowed != _continuousAllowed;
        _continuousAllowed = allowed;
        foreach (var effect in _effects)
        {
            if (levelChanged)
            {
                effect.Remove(); // neue Stufe: frisch aufbauen (oder gar nicht)
            }

            Apply(effect);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _appearance.Changed -= OnAppearanceChanged;
        if (_window is { } window)
        {
            window.Activated -= OnWindowChanged;
            window.Deactivated -= OnWindowChanged;
            window.StateChanged -= OnWindowChanged;
            window.IsVisibleChanged -= OnWindowVisibilityChanged;
            window.Closed -= OnWindowClosed;
            _window = null;
        }

        foreach (var effect in _effects)
        {
            effect.Remove();
            effect.WantedChanged = null;
        }

        _effects.Clear();
        _continuousAllowed = false;
        Changed = null;
    }

    private void Apply(ContinuousEffect effect)
    {
        if (_disposed)
        {
            return;
        }

        if (_continuousAllowed && effect.Wanted)
        {
            effect.Run();
            return;
        }

        // Sichtbar, aber im Hintergrund: an der Stelle anhalten (kein Sprung). Sonst entfernen: keine Uhr bleibt stehen.
        var context = Context;
        var background = _level == EffectLevel.Full && effect.Wanted && context.WindowVisible && !context.Minimized
            && !(context.GameRunning && context.PauseWhilePlaying);
        if (background && effect.IsRunning)
        {
            effect.Pause();
        }
        else
        {
            effect.Remove();
        }
    }

    private void ReadWindow()
    {
        if (_window is { } window)
        {
            SetWindowState(window.IsVisible, window.WindowState == WindowState.Minimized, window.IsActive);
        }
    }

    private void OnWindowChanged(object? sender, EventArgs e) => ReadWindow();

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => ReadWindow();

    private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

    private void OnAppearanceChanged(object? sender, EventArgs e) => Evaluate();
}
