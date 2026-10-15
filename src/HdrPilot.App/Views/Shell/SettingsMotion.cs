using System.ComponentModel;
using System.Windows;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Dauer-Effekte der Einstellungen (Vorführkarte „Voll“, langsamer Zoom der Live-Vorschau) nach den Regeln der Effekte
/// (<see cref="EffectPolicy.ContinuousAllowed"/>): nur bei wirksamer Stufe „Voll“, sichtbarem, nicht minimiertem Fenster und
/// (mit „Im Hintergrund anhalten“) nur, solange es aktiv ist. Ändern sich Fenster oder Darstellung, wird gestartet bzw.
/// angehalten; beim Schließen angehalten und abgemeldet. Selbst läuft hier keine Bildschleife.
/// </summary>
public sealed class SettingsMotion : IDisposable
{
    private readonly Window _window;
    private readonly Func<AppearanceState> _state;
    private readonly INotifyPropertyChanged? _source;
    private readonly Action _start;
    private readonly Action _stop;
    private readonly Func<MotionContext>? _context;
    private bool _running;
    private bool _disposed;

    /// <param name="state">Wirksame Darstellung (Stufe und Pausen-Schalter).</param>
    /// <param name="source">Meldet Änderungen der Darstellung (Seite „Darstellung“); null: nur Fensterereignisse.</param>
    /// <param name="context">Lage des Fensters (Tests); Standard: aus dem Fenster gelesen.</param>
    public SettingsMotion(Window window, Func<AppearanceState> state, INotifyPropertyChanged? source, Action start, Action stop, Func<MotionContext>? context = null)
    {
        _context = context;
        _window = window;
        _state = state;
        _source = source;
        _start = start;
        _stop = stop;
        window.Activated += OnChanged;
        window.Deactivated += OnChanged;
        window.StateChanged += OnChanged;
        window.IsVisibleChanged += OnVisibleChanged;
        window.Closed += OnClosed;
        if (source is not null)
        {
            source.PropertyChanged += OnSourceChanged;
        }

        Update();
    }

    public bool IsRunning => _running;

    /// <summary>Lage des Fensters für die Effekte (ohne laufendes Spiel: die Einstellungen kennen keine Sitzungen).</summary>
    public MotionContext Context
    {
        get
        {
            if (_context is not null)
            {
                return _context();
            }

            var state = _state();
            return new MotionContext(
                _window.IsVisible,
                _window.WindowState == WindowState.Minimized,
                _window.IsActive,
                GameRunning: false,
                state.PauseInBackground,
                state.PauseWhilePlaying);
        }
    }

    /// <summary>Dürfen Einmal-Effekte (HDR-Moment der Vorschau) gerade spielen?</summary>
    public bool OneShotAllowed => !_disposed && EffectPolicy.OneShotAllowed(_state().EffectLevel, Context);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.Activated -= OnChanged;
        _window.Deactivated -= OnChanged;
        _window.StateChanged -= OnChanged;
        _window.IsVisibleChanged -= OnVisibleChanged;
        _window.Closed -= OnClosed;
        if (_source is not null)
        {
            _source.PropertyChanged -= OnSourceChanged;
        }

        if (_running)
        {
            _running = false;
            _stop();
        }
    }

    /// <summary>Neu bewerten (Fenster oder Darstellung geändert).</summary>
    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        var allowed = EffectPolicy.ContinuousAllowed(_state().EffectLevel, Context);
        if (allowed == _running)
        {
            return;
        }

        _running = allowed;
        if (allowed)
        {
            _start();
        }
        else
        {
            _stop();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Update();

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "State")
        {
            Update();
        }
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();
}
