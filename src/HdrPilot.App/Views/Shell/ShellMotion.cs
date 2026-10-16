using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Bewegung im Shell-Bereich (Einstellungen, Dialoge, Tray-Menü). Sie folgt der wirksamen Effektstufe der Darstellung
/// (<see cref="Level"/>, E89: „Aus“ heißt keine Bewegung; ob Windows „Animationseffekte“ beachtet wird, steckt schon in der
/// Stufe). Ohne Darstellung (Setup) gilt die Windows-Einstellung (<see cref="SystemParameters.ClientAreaAnimation"/>). Immer
/// nur, wenn die Grafikkarte rendert (RenderCapability.Tier &gt; 0); sonst stehen alle Elemente sofort im Endzustand. Es
/// gibt keine Dauer-Animationen außer über <see cref="AmbientAnimation"/>, die nur bei sichtbarem und aktivem Fenster läuft.
/// </summary>
public static class ShellMotion
{
    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(ShellMotion), new PropertyMetadata(false, OnHoverLiftChanged));

    private static readonly Duration EnterDuration = new(TimeSpan.FromMilliseconds(170));
    private static readonly Duration LeaveDuration = new(TimeSpan.FromMilliseconds(240));
    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Deckkraft des Leuchtens hinter einer überfahrenen Karte.</summary>
    public const double HoverGlowOpacity = 0.22;

    private static bool? _override;
    private static Theming.EffectLevel? _level;

    static ShellMotion()
    {
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            {
                RaiseIsEnabledChanged();
            }
        };
    }

    /// <summary>Für Bindungen an <see cref="IsEnabled"/> (WPF erkennt dieses Ereignis an statischen Eigenschaften).</summary>
    public static event EventHandler<PropertyChangedEventArgs>? StaticPropertyChanged;

    /// <summary>True, wenn Zier-Animationen laufen dürfen.</summary>
    public static bool IsEnabled => _override ?? Allowed(_level, SystemParameters.ClientAreaAnimation, (RenderCapability.Tier >> 16) > 0);

    /// <summary>
    /// Wirksame Effektstufe der Darstellung (setzt die App bei jeder Änderung); null ohne Darstellung (Setup): dann die
    /// Windows-Einstellung.
    /// </summary>
    public static Theming.EffectLevel? Level
    {
        get => _level;
        set
        {
            if (_level != value)
            {
                _level = value;
                RaiseIsEnabledChanged();
            }
        }
    }

    /// <summary>Regel hinter <see cref="IsEnabled"/> (für Tests offen): Stufe nicht „Aus“, ohne Stufe Windows; nie ohne Hardware.</summary>
    public static bool Allowed(Theming.EffectLevel? level, bool windowsAnimations, bool hardwareRendering) =>
        hardwareRendering && (level is { } known ? known != Theming.EffectLevel.Off : windowsAnimations);

    /// <summary>Erzwingt Animationen an oder aus (Tests, Bildschirmfotos). Null: Windows-Einstellung.</summary>
    public static bool? Override
    {
        get => _override;
        set
        {
            if (_override != value)
            {
                _override = value;
                RaiseIsEnabledChanged();
            }
        }
    }

    public static bool GetHoverLift(DependencyObject element) => (bool)element.GetValue(HoverLiftProperty);

    public static void SetHoverLift(DependencyObject element, bool value) => element.SetValue(HoverLiftProperty, value);

    /// <summary>
    /// Öffnen: Inhalt blendet weich ein und gleitet nach oben (240 ms); die ersten <paramref name="staggered"/>
    /// Elemente folgen gestaffelt. Alle anderen erscheinen mit dem Inhalt.
    /// </summary>
    public static void PlayEntrance(UIElement content, IReadOnlyList<UIElement> staggered)
    {
        if (!IsEnabled)
        {
            return;
        }

        FadeSlideIn(content, TimeSpan.Zero, 10, 240);
        for (var i = 0; i < staggered.Count; i++)
        {
            FadeSlideIn(staggered[i], TimeSpan.FromMilliseconds(70 + (i * 45)), 16, 280);
        }
    }

    /// <summary>Blendet ein Element aus dem Nichts ein und gleitet von unten in seine Lage. Räumt danach auf.</summary>
    public static void FadeSlideIn(UIElement element, TimeSpan delay, double offset, int milliseconds)
    {
        var translate = new TranslateTransform(0, offset);
        element.RenderTransform = translate;
        element.Opacity = 0;
        var duration = new Duration(TimeSpan.FromMilliseconds(milliseconds));
        var fade = new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = EaseOut };
        var slide = new DoubleAnimation(offset, 0, duration) { BeginTime = delay, EasingFunction = EaseOut };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            element.RenderTransform = Transform.Identity;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control)
        {
            return;
        }

        control.MouseEnter -= OnMouseEnter;
        control.MouseLeave -= OnMouseLeave;
        if ((bool)e.NewValue)
        {
            control.MouseEnter += OnMouseEnter;
            control.MouseLeave += OnMouseLeave;
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e) => SetHover((Control)sender, hover: true);

    private static void OnMouseLeave(object sender, MouseEventArgs e) => SetHover((Control)sender, hover: false);

    /// <summary>
    /// Karte anheben (2 px), Verlaufsrahmen und Leuchten einblenden. Das Leuchten ist ein vorab gezeichneter radialer
    /// Verlauf (E89: kein BlurEffect); ohne Animationseffekte erscheint nur der Rahmen, sofort und ohne Anheben.
    /// </summary>
    private static void SetHover(Control control, bool hover)
    {
        if (control.Template is null)
        {
            return;
        }

        var animate = IsEnabled;
        var duration = hover ? EnterDuration : LeaveDuration;
        if (control.Template.FindName("PART_Ring", control) is UIElement ring)
        {
            To(ring, UIElement.OpacityProperty, hover ? 1 : 0, duration, animate);
        }

        if (control.Template.FindName("PART_Glow", control) is UIElement glow)
        {
            To(glow, UIElement.OpacityProperty, hover && animate ? HoverGlowOpacity : 0, duration, animate);
        }

        if (control.Template.FindName("Root", control) is UIElement root)
        {
            if (root.RenderTransform is not TranslateTransform { IsFrozen: false } lift)
            {
                lift = new TranslateTransform();
                root.RenderTransform = lift;
            }

            To(lift, TranslateTransform.YProperty, hover && animate ? -2 : 0, duration, animate);
        }
    }

    private static void To(IAnimatable target, DependencyProperty property, double value, Duration duration, bool animate, Action? onCompleted = null)
    {
        if (!animate)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, value);
            onCompleted?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(value, duration) { EasingFunction = EaseOut };
        if (onCompleted is not null)
        {
            animation.Completed += (_, _) =>
            {
                // Nur aufräumen, wenn inzwischen keine neue Animation (erneutes Überfahren) begonnen hat.
                if (Math.Abs((double)((DependencyObject)target).GetValue(property) - value) < 0.001)
                {
                    onCompleted();
                }
            };
        }

        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void RaiseIsEnabledChanged() => StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(IsEnabled)));

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>
/// Dauer-Zierde (z. B. Schimmer im Kopf) nur, solange das Fenster sichtbar, aktiv und nicht minimiert ist und Windows
/// Animationen erlaubt. Bei Deaktivieren, Minimieren, Verstecken oder Schließen wird angehalten.
/// </summary>
public sealed class AmbientAnimation : IDisposable
{
    private readonly Window _window;
    private readonly Action _start;
    private readonly Action _stop;
    private bool _running;
    private bool _disposed;

    public AmbientAnimation(Window window, Action start, Action stop)
    {
        _window = window;
        _start = start;
        _stop = stop;
        window.Activated += OnChanged;
        window.Deactivated += OnChanged;
        window.StateChanged += OnChanged;
        window.IsVisibleChanged += OnVisibleChanged;
        window.Closed += OnClosed;

        // Windows-Einstellung „Animationseffekte“ wirkt sofort, auch bei offenem Fenster (statisch: im Dispose abmelden).
        ShellMotion.StaticPropertyChanged += OnMotionSettingChanged;
        Update();
    }

    public bool IsRunning => _running;

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
        ShellMotion.StaticPropertyChanged -= OnMotionSettingChanged;
        if (_running)
        {
            _running = false;
            _stop();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Update();

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void OnMotionSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SystemParameters melden sich auf dem UI-Thread des Fensters; sicherheitshalber dorthin wechseln.
        if (_window.Dispatcher.CheckAccess())
        {
            Update();
        }
        else
        {
            _window.Dispatcher.BeginInvoke(Update);
        }
    }

    private void Update()
    {
        if (_disposed)
        {
            return;
        }

        var shouldRun = ShellMotion.IsEnabled && _window.IsVisible && _window.IsActive && _window.WindowState != WindowState.Minimized;
        if (shouldRun == _running)
        {
            return;
        }

        _running = shouldRun;
        if (shouldRun)
        {
            _start();
        }
        else
        {
            _stop();
        }
    }
}
