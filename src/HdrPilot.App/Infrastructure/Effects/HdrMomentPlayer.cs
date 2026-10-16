using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Infrastructure.Effects;

/// <summary>Lage des Moments beim Start: Größe des Titelbereichs, Spitzlicht und Farben der Partikel.</summary>
/// <param name="Width">Breite des Titelbereichs (px).</param>
/// <param name="Height">Höhe der Partikelfläche (Titelbereich ohne die oberen 96 px).</param>
/// <param name="LightX">Spitzlicht in Koordinaten der Partikelfläche.</param>
/// <param name="LightY">Spitzlicht in Koordinaten der Partikelfläche.</param>
/// <param name="SweepWidth">Breite des Lichtstreifens (38 % des Titelbereichs).</param>
public sealed record HdrMomentScene(double Width, double Height, double LightX, double LightY, double SweepWidth, IReadOnlyList<Color> ParticleColors);

/// <summary>
/// Elemente des HDR-Moments. <see cref="SweepBand"/> braucht eine <see cref="TranslateTransform"/>, <see cref="SweepLine"/>
/// eine <see cref="ScaleTransform"/> als RenderTransform. <see cref="Boost"/> liefert die Ebene der hellen Fassung im gerade
/// sichtbaren Bildplatz (null ohne Bild); gefragt wird erst beim Abspielen, denn der Titelbereich wechselt die Plätze und die
/// Bilder kommen erst nach dem Öffnen des Fensters.
/// </summary>
public sealed record HdrMomentParts(
    FrameworkElement Host,
    FrameworkElement SweepBand,
    FrameworkElement SweepLine,
    FrameworkElement Flash,
    FrameworkElement EdgeGlow,
    ParticleLayer Particles,
    Func<FrameworkElement?> Boost);

/// <summary>Was ein Moment gespielt hat (Tests, Protokoll).</summary>
public enum HdrMomentKind
{
    /// <summary>Nicht gespielt (Stufe „Aus“, Fenster minimiert, Spiel läuft …).</summary>
    None,

    /// <summary>„Reduziert“: nur die helle Fassung kurz einblenden.</summary>
    Reduced,

    /// <summary>„Voll“: Lichtstreifen, Blitz, helle Fassung, Partikel, leuchtender Rand.</summary>
    Full,
}

/// <summary>
/// HDR-Moment (E89, G10.6): Lichtstreifen durch das Titelbild (1,6 s), Lichtlinie, Blitz am Spitzlicht, helle Fassung,
/// leuchtender Fensterrand (1,8 s) und bis zu 160 Partikel, die nur 2,5 s über den Bildtakt gezeichnet werden; danach ist
/// der Spieler abgemeldet (0 Bildaufrufe). Einmal-Effekte dürfen 60 Bilder pro Sekunde haben (Storyboard und Bildtakt der
/// Partikel, <see cref="TimelineFrameClock"/>). Bei „Reduziert“ nur die helle
/// Fassung, höchstens 0,3 s; bei „Aus“ nichts. Für Bildschirmfotos lässt sich der Moment an einer Stelle anhalten
/// (<see cref="Freeze"/>, ohne Bildtakt).
/// </summary>
public sealed class HdrMomentPlayer : IDisposable
{
    /// <summary>Dauer des Lichtstreifens.</summary>
    public static readonly TimeSpan SweepDuration = TimeSpan.FromSeconds(1.6);

    /// <summary>Dauer des Pulses am Fensterrand.</summary>
    public static readonly TimeSpan EdgeDuration = TimeSpan.FromSeconds(1.8);

    private readonly HdrMomentParts _parts;
    private readonly IFrameClock _clock;
    private readonly IEffectTime _time;
    private readonly Func<int> _seed;
    private ParticleSystem? _system;
    private Storyboard? _board;
    private TimeSpan _start;
    private TimeSpan _last;
    private bool _hooked;
    private bool _disposed;

    public HdrMomentPlayer(HdrMomentParts parts, IFrameClock clock, IEffectTime time, Func<int>? seed = null)
    {
        _parts = parts;
        _clock = clock;
        _time = time;
        _seed = seed ?? (() => Environment.TickCount);
    }

    /// <summary>Gerade am Bildtakt angemeldet (Partikel laufen).</summary>
    public bool IsHooked => _hooked;

    public bool IsPlaying => _board is not null || _hooked;

    public HdrMomentKind LastKind { get; private set; }

    /// <summary>Das Storyboard des laufenden Moments (Tests: Bildrate, Dauer).</summary>
    public Storyboard? Board => _board;

    /// <summary>Partikel des laufenden Moments (null ohne Partikel).</summary>
    public ParticleSystem? System => _system;

    /// <summary>
    /// Spielt den Moment, wenn die Regeln es erlauben. <paramref name="transientBoost"/>: die helle Fassung nur für die
    /// Dauer des Moments zeigen (Prüfung ohne echte HDR-Schaltung); sonst lässt der Moment ihre Ebene unberührt, sie bleibt,
    /// solange HDR an ist (Sache des Titelbereichs).
    /// </summary>
    public HdrMomentKind Play(EffectLevel level, MotionContext context, HdrMomentScene scene, bool transientBoost)
    {
        Stop();
        if (_disposed || !EffectPolicy.OneShotAllowed(level, context))
        {
            LastKind = HdrMomentKind.None;
            return LastKind;
        }

        if (level == EffectLevel.Reduced)
        {
            _board = BuildReduced(transientBoost);
            Begin();
            LastKind = HdrMomentKind.Reduced;
            return LastKind;
        }

        _board = BuildFull(scene, transientBoost);
        Begin();
        StartParticles(scene);
        _start = _last = _time.Now;
        _clock.Frame += OnFrame;
        _hooked = true;
        LastKind = HdrMomentKind.Full;
        return LastKind;
    }

    /// <summary>Bildschirmfoto: Moment bei diesem Anteil (0–1) der 2,5 s anhalten; Partikel nach festem Startwert, ohne Bildtakt.</summary>
    public void Freeze(HdrMomentScene scene, double fraction, bool transientBoost)
    {
        Stop();
        if (_disposed)
        {
            return;
        }

        var at = TimeSpan.FromSeconds(EffectPolicy.HdrMomentDuration.TotalSeconds * Math.Clamp(fraction, 0, 1));
        _board = BuildFull(scene, transientBoost);
        _board.Begin(_parts.Host, isControllable: true);
        _board.Seek(_parts.Host, at, TimeSeekOrigin.BeginTime);
        _board.Pause(_parts.Host);

        _parts.Particles.SetColors(scene.ParticleColors);
        _system = new ParticleSystem(new Random(4711), _parts.Particles.ColorCount);
        _system.Start(scene.Width, scene.Height, scene.LightX, scene.LightY);
        for (var t = 0.0; t + 1e-9 < at.TotalSeconds; t += 1.0 / 60)
        {
            _system.Step(1.0 / 60);
        }

        _parts.Particles.Render(_system.Particles, _system.FadeOut);
        LastKind = HdrMomentKind.Full;
    }

    /// <summary>Sofort beenden: Storyboard entfernen, vom Bildtakt abmelden, Partikel löschen.</summary>
    public void Stop()
    {
        if (_hooked)
        {
            _clock.Frame -= OnFrame;
            _hooked = false;
        }

        if (_board is not null)
        {
            _board.Remove(_parts.Host);
            _board = null;
        }

        _system = null;
        _parts.Particles.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }

    private void Begin()
    {
        var board = _board!;
        board.Completed += (_, _) =>
        {
            if (ReferenceEquals(_board, board))
            {
                _board.Remove(_parts.Host);
                _board = null;
            }
        };
        board.Begin(_parts.Host, isControllable: true);
    }

    private void StartParticles(HdrMomentScene scene)
    {
        _parts.Particles.SetColors(scene.ParticleColors);
        _system = new ParticleSystem(new Random(_seed()), _parts.Particles.ColorCount);
        _system.Start(scene.Width, scene.Height, scene.LightX, scene.LightY);
        _parts.Particles.Render(_system.Particles, 1);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_system is not { } system)
        {
            Unhook();
            return;
        }

        var now = _time.Now;
        var dt = (now - _last).TotalSeconds;
        _last = now;

        // Wie lange der Moment schon läuft, zählt die Uhr; die Partikel rechnen höchstens 0,05 s je Bild.
        var alive = system.Step(dt) && now - _start < EffectPolicy.HdrMomentDuration;
        if (!alive)
        {
            Unhook();
            return;
        }

        _parts.Particles.Render(system.Particles, system.FadeOut);
    }

    private void Unhook()
    {
        if (_hooked)
        {
            _clock.Frame -= OnFrame;
            _hooked = false;
        }

        _system = null;
        _parts.Particles.Clear();
    }

    private Storyboard BuildReduced(bool transientBoost)
    {
        var board = new Storyboard();
        Timeline.SetDesiredFrameRate(board, EffectPolicy.OneShotFrameRate);
        if (_parts.Boost() is { } boost && transientBoost)
        {
            var fade = EffectPolicy.ReducedTransition;
            var hold = EffectPolicy.HdrMomentDuration - fade;
            var opacity = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(fade)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(hold)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(EffectPolicy.HdrMomentDuration)));
            Add(board, opacity, boost, UIElement.OpacityProperty);
        }

        return board;
    }

    private Storyboard BuildFull(HdrMomentScene scene, bool transientBoost)
    {
        var board = new Storyboard();
        Timeline.SetDesiredFrameRate(board, EffectPolicy.OneShotFrameRate);
        var ease = new KeySpline(0.5, 0.1, 0.2, 1);

        // Lichtstreifen: von links außerhalb quer durch das Bild, mit kräftiger Mitte (Prototyp sw1).
        var travel = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        travel.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.2 * scene.SweepWidth, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        travel.KeyFrames.Add(new SplineDoubleKeyFrame(3.3 * scene.SweepWidth, KeyTime.FromTimeSpan(SweepDuration), ease));
        Add(board, travel, _parts.SweepBand, "(UIElement.RenderTransform).(TranslateTransform.X)");
        Add(board, Pulse(SweepDuration, 0.95, (0.10, 0.80)), _parts.SweepBand, UIElement.OpacityProperty);

        // Lichtlinie auf Höhe des Spitzlichts: wächst aus der Mitte, verglimmt breiter und rasch (Prototyp sk1: ease-out),
        // damit sie nicht als harte Linie stehen bleibt.
        var lineBegin = TimeSpan.FromSeconds(0.2);
        var lineDuration = TimeSpan.FromSeconds(1.3);
        var scale = new DoubleAnimationUsingKeyFrames { BeginTime = lineBegin, FillBehavior = FillBehavior.Stop };
        scale.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.05, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scale.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(lineDuration * 0.25), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(lineDuration), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        Add(board, scale, _parts.SweepLine, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        var lineOpacity = Pulse(lineDuration, 1, (0.25, 0.25), EasingMode.EaseOut);
        lineOpacity.BeginTime = lineBegin;
        Add(board, lineOpacity, _parts.SweepLine, UIElement.OpacityProperty);

        // Blitz am Spitzlicht und leuchtender Rand in den Spielfarben.
        Add(board, Pulse(TimeSpan.FromSeconds(1.1), 1, (0.18, 0.18)), _parts.Flash, UIElement.OpacityProperty);
        Add(board, Pulse(EdgeDuration, 1, (0.22, 0.22)), _parts.EdgeGlow, UIElement.OpacityProperty);

        // Helle Fassung nur für die Prüfung: blendet in 1,2 s ein und danach wieder aus. Bei echtem HDR gehört die Ebene dem
        // Titelbereich (er blendet sie ebenso in 1,2 s ein und hält sie, solange HDR an ist); nähme der Moment sie, fiele sie
        // beim Entfernen seines Storyboards (Ende, Abbruch) auf ihren eigenen Wert zurück, auch bei HDR an.
        if (transientBoost && _parts.Boost() is { } boost)
        {
            var opacity = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2)), new SineEase()));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(EffectPolicy.HdrMomentDuration)));
            Add(board, opacity, boost, UIElement.OpacityProperty);
        }

        return board;
    }

    /// <summary>0 → Spitze (ab Anteil <paramref name="hold"/>.Item1) → gehalten bis Item2 → 0 am Ende (Abklingen nach <paramref name="fall"/>).</summary>
    private static DoubleAnimationUsingKeyFrames Pulse(TimeSpan duration, double peak, (double Rise, double Fall) hold, EasingMode fall = EasingMode.EaseIn)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(duration * hold.Rise), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        if (hold.Fall > hold.Rise)
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(duration * hold.Fall)));
        }

        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration), new QuadraticEase { EasingMode = fall }));
        return animation;
    }

    private static void Add(Storyboard board, Timeline animation, DependencyObject target, DependencyProperty property) =>
        Add(board, animation, target, new PropertyPath(property));

    private static void Add(Storyboard board, Timeline animation, DependencyObject target, string path) =>
        Add(board, animation, target, new PropertyPath(path));

    private static void Add(Storyboard board, Timeline animation, DependencyObject target, PropertyPath path)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, path);
        board.Children.Add(animation);
    }
}
