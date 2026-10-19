using HdrPilot.App.Infrastructure.Effects;

namespace HdrPilot.App.Views.Library;

/// <summary>Kurven der Werkzeugleiste, wie in WPF und in der Vorschau (leiste.js, E).</summary>
public static class ToolbarEase
{
    public static readonly Func<double, double> Linear = t => t;

    public static readonly Func<double, double> CubicOut = t => 1 - Math.Pow(1 - t, 3);

    public static readonly Func<double, double> CubicIn = t => t * t * t;

    /// <summary>ExponentialEase Out mit Exponent 6 (die Kurve des Titelbereichs).</summary>
    public static readonly Func<double, double> ExpoOut6 = t => 1 - ((Math.Exp(6 * (1 - t)) - 1) / (Math.Exp(6) - 1));

    public static readonly Func<double, double> SineInOut = t => (1 - Math.Cos(Math.PI * t)) / 2;

    /// <summary>Hp.Ease.Spring: BackEase Out, Amplitude 0,35 (schwingt höchstens ein wenig über).</summary>
    public static readonly Func<double, double> Spring = t =>
    {
        var x = 1 - t;
        return 1 - ((x * x * x) - (x * 0.35 * Math.Sin(Math.PI * x)));
    };

    /// <summary>KeySpline der Lichtnaht (0.16, 0.84, 0.24, 1).</summary>
    public static readonly Func<double, double> Seam = Bezier(0.16, 0.84, 0.24, 1);

    /// <summary>Gleitende Fläche der Ansicht (0.22, 1, 0.36, 1).</summary>
    public static readonly Func<double, double> Segment = Bezier(0.22, 1, 0.36, 1);

    /// <summary>Kubische Bézierkurve wie KeySpline bzw. CSS cubic-bezier (Newton-Verfahren).</summary>
    public static Func<double, double> Bezier(double x1, double y1, double x2, double y2)
    {
        var cx = 3 * x1;
        var bx = (3 * (x2 - x1)) - cx;
        var ax = 1 - cx - bx;
        var cy = 3 * y1;
        var by = (3 * (y2 - y1)) - cy;
        var ay = 1 - cy - by;
        return x =>
        {
            if (x <= 0)
            {
                return 0;
            }

            if (x >= 1)
            {
                return 1;
            }

            var t = x;
            for (var i = 0; i < 10; i++)
            {
                var e = (((ax * t) + bx) * t + cx) * t - x;
                var d = (((3 * ax * t) + (2 * bx)) * t) + cx;
                if (Math.Abs(e) < 1e-6 || Math.Abs(d) < 1e-6)
                {
                    break;
                }

                t -= e / d;
            }

            t = Math.Clamp(t, 0, 1);
            return (((ay * t) + by) * t + cy) * t;
        };
    }
}

/// <summary>Stützstelle einer Bewegung mit Zwischenwerten: Anteil 0–1 und Wert (null an der ersten Stelle: der aktuelle Wert).</summary>
public readonly record struct MotionKey(double At, double? Value);

/// <summary>
/// Bewegung über benannte Kanäle (Deckkraft, Verschiebung, Breite …), wie die Zeitleiste der Vorschau (leiste.js anim/step):
/// Jede Bewegung startet beim aktuellen Wert ihres Kanals, eine neue ersetzt die laufende (SnapshotAndReplace), sodass sich
/// alles unterbrechen lässt und vom Augenblick aus weiterläuft. Getaktet wird über einen <see cref="IFrameClock"/> (in der App
/// eine Zeitleiste mit höchstens 60 Bildern pro Sekunde), angemeldet nur, solange etwas läuft: Danach gibt es keine Uhr mehr.
/// Die Zeit kommt von <see cref="IEffectTime"/> (Tests stellen sie selbst). Wer die Werte anwendet, hört auf
/// <see cref="Updated"/> (nach jedem Takt und jedem Setzen über <see cref="Apply"/>).
/// </summary>
public sealed class MotionChannels
{
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Motion> _running = new(StringComparer.Ordinal);
    private IFrameClock _clock;
    private IEffectTime _time;
    private bool _hooked;

    public MotionChannels(IFrameClock clock, IEffectTime time)
    {
        _clock = clock;
        _time = time;
    }

    /// <summary>Nach jedem Takt und nach <see cref="Apply"/>: Werte anwenden.</summary>
    public event EventHandler? Updated;

    /// <summary>Die letzte Bewegung ist zu Ende (auch nach <see cref="Finish"/>).</summary>
    public event EventHandler? Finished;

    /// <summary>Zahl der laufenden (oder noch wartenden) Bewegungen.</summary>
    public int Running => _running.Count;

    /// <summary>Am Bildtakt angemeldet (nur, solange etwas läuft).</summary>
    public bool IsHooked => _hooked;

    private double Now => _time.Now.TotalMilliseconds;

    /// <summary>
    /// Andere Zeitquelle und anderen Bildtakt verwenden (Tests: gestellte Zeit, Takt von Hand). Laufende Bewegungen enden
    /// vorher an ihrem Ziel.
    /// </summary>
    public void UseTime(IFrameClock clock, IEffectTime time)
    {
        Finish();
        _clock = clock;
        _time = time;
    }

    /// <summary>Aktueller Wert eines Kanals (<paramref name="fallback"/>, solange er nie gesetzt wurde).</summary>
    public double Get(string key, double fallback = 0) => _values.TryGetValue(key, out var v) ? v : fallback;

    public bool Has(string key) => _values.ContainsKey(key);

    public bool IsRunning(string key) => _running.ContainsKey(key);

    /// <summary>Wohin ein Kanal geht: das Ende seiner laufenden Bewegung, sonst sein jetziger Wert.</summary>
    public double Heading(string key, double fallback = 0) => _running.TryGetValue(key, out var motion) ? motion.End : Get(key, fallback);

    /// <summary>Wie lange die Bewegung eines Kanals noch dauert (mit ihrer Wartezeit), in ms; 0, wenn keine läuft.</summary>
    public double Remaining(string key) => _running.TryGetValue(key, out var motion) ? Math.Max(0, motion.Start + motion.Duration - Now) : 0;

    /// <summary>Wert sofort setzen; eine laufende Bewegung des Kanals endet.</summary>
    public void Set(string key, double value)
    {
        _running.Remove(key);
        _values[key] = value;
    }

    /// <summary>Ziel einer laufenden Bewegung ändern (etwa die Breite der Kapsel, während sie noch wächst).</summary>
    public void Retarget(string key, double to)
    {
        if (_running.TryGetValue(key, out var motion))
        {
            _running[key] = motion with { To = to };
        }
    }

    /// <summary>
    /// Bewegung starten: vom aktuellen Wert (oder <paramref name="from"/>) nach <paramref name="to"/>, nach
    /// <paramref name="delayMs"/> in <paramref name="durationMs"/> mit der Kurve <paramref name="ease"/> (Standard CubicEase
    /// Out). Mit <paramref name="keys"/> laufen Zwischenwerte über die Kurve (die erste Stelle ohne Wert ist der aktuelle).
    /// </summary>
    public void Animate(string key, double to, double durationMs, Func<double, double>? ease = null, double delayMs = 0, double? from = null, MotionKey[]? keys = null)
    {
        if (from is { } f)
        {
            _values[key] = f;
        }

        var start = Get(key, to);
        _values.TryAdd(key, start);
        _running[key] = new Motion(start, to, Now + delayMs, Math.Max(1, durationMs), ease ?? ToolbarEase.CubicOut, keys);
        Hook();
    }

    /// <summary>Alle Bewegungen sofort an ihr Ende (Fenster minimiert, Stufe „Aus“); danach ist der Takt abgemeldet.</summary>
    public void Finish()
    {
        foreach (var (key, motion) in _running.ToList())
        {
            _values[key] = motion.End;
        }

        _running.Clear();
        Unhook();
        Updated?.Invoke(this, EventArgs.Empty);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Werte zum jetzigen Zeitpunkt rechnen und anwenden (ein Takt; Tests rufen ihn mit gestellter Zeit).</summary>
    public void Tick()
    {
        Step();
        Updated?.Invoke(this, EventArgs.Empty);
        if (_running.Count == 0)
        {
            Unhook();
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gesetzte Werte jetzt anwenden (ohne zu rechnen).</summary>
    public void Apply() => Updated?.Invoke(this, EventArgs.Empty);

    private void Step()
    {
        var now = Now;
        foreach (var (key, motion) in _running.ToList())
        {
            var t = (now - motion.Start) / motion.Duration;
            if (t < 0)
            {
                _values[key] = motion.Keys is { } k0 ? k0[0].Value ?? motion.From : motion.From;
                continue;
            }

            if (t >= 1)
            {
                _values[key] = motion.End;
                _running.Remove(key);
                continue;
            }

            var p = motion.Ease(t);
            _values[key] = motion.Keys is { } keys ? At(keys, motion.From, p) : motion.From + ((motion.To - motion.From) * p);
        }
    }

    private static double At(MotionKey[] keys, double from, double p)
    {
        for (var i = 1; i < keys.Length; i++)
        {
            if (p <= keys[i].At)
            {
                var a = keys[i - 1];
                var b = keys[i];
                var u = (p - a.At) / Math.Max(1e-6, b.At - a.At);
                var va = a.Value ?? from;
                var vb = b.Value ?? from;
                return va + ((vb - va) * u);
            }
        }

        return keys[^1].Value ?? from;
    }

    private void Hook()
    {
        if (!_hooked)
        {
            _hooked = true;
            _clock.Frame += OnFrame;
        }
    }

    private void Unhook()
    {
        if (_hooked)
        {
            _hooked = false;
            _clock.Frame -= OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e) => Tick();

    private sealed record Motion(double From, double To, double Start, double Duration, Func<double, double> Ease, MotionKey[]? Keys)
    {
        public double End => Keys is { } keys ? keys[^1].Value ?? From : To;
    }
}
