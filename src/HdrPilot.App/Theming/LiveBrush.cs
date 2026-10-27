using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HdrPilot.App.Theming;

/// <summary>Zähler der Farbebene und der bleibenden Pinsel (E94, G15.7): je angewendetem Wechsel, ohne Zeitmessung.</summary>
public sealed class GameAccentStats
{
    /// <summary>Angewendete Wechsel (Ziel geändert).</summary>
    public int Applied { get; internal set; }

    /// <summary>Wechsel auf das schon gezeigte Ziel (nichts zu tun).</summary>
    public int SkippedEqual { get; internal set; }

    /// <summary>Ausgetauschte Farbwerte der Ebene (eine Invalidierung des Baums je Austausch).</summary>
    public int ColorPartSwaps { get; internal set; }

    /// <summary>Pinsel bzw. Verlaufsstopps mit neuem Ziel (Überblendung oder sofort).</summary>
    public int Retargets { get; internal set; }

    /// <summary>Gestartete Überblendungen.</summary>
    public int Animations { get; internal set; }

    /// <summary>Ersetzte Pinsel (anderer Aufbau, eingefroren oder Thema hell/dunkel).</summary>
    public int Replacements { get; internal set; }

    /// <summary>Wegen schneller Auswahl zurückgehaltene Wechsel (vom Fenster gezählt).</summary>
    public int Held { get; internal set; }

    /// <summary>Anfragen nach Spielfarben (vom Fenster gezählt).</summary>
    public int Requests { get; internal set; }

    /// <summary>Berechnete Zielpaletten der Farbebene (Schema oder Spiel, vom Fenster gezählt).</summary>
    public int Computed { get; internal set; }

    public override string ToString() =>
        $"angewendet {Applied}, gleich {SkippedEqual}, Farbwerte {ColorPartSwaps}, Ziele {Retargets}, Überblendungen {Animations}, ersetzt {Replacements}, gehalten {Held}, Anfragen {Requests}, berechnet {Computed}";
}

/// <summary>Bleibende (nicht eingefrorene) Pinsel für die Spielfarben (E94).</summary>
public static class LiveBrush
{
    /// <summary>Veränderbare Kopie mit den aktuellen Werten, nie eingefroren.</summary>
    public static T Unfrozen<T>(T template)
        where T : Brush
    {
        ArgumentNullException.ThrowIfNull(template);
        return (T)template.CloneCurrentValue();
    }

    /// <summary>
    /// Ob <paramref name="live"/> auf <paramref name="target"/> überblendet werden kann: nicht eingefroren, gleiche Art,
    /// gleich viele Verlaufsstopps an denselben Stellen. Sonst muss der Pinsel ersetzt werden.
    /// </summary>
    public static bool Compatible(Brush live, Brush target)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(target);
        if (live.IsFrozen || live.GetType() != target.GetType())
        {
            return false;
        }

        if (live is GradientBrush a && target is GradientBrush b)
        {
            if (a.GradientStops.Count != b.GradientStops.Count)
            {
                return false;
            }

            for (var i = 0; i < a.GradientStops.Count; i++)
            {
                if (!a.GradientStops[i].Offset.Equals(b.GradientStops[i].Offset) || a.GradientStops[i].IsFrozen)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>
/// Führt bleibende Pinsel zu neuen Zielen (E94): je Pinsel bzw. Verlaufsstopp wird das zuletzt gesetzte Ziel gemerkt. Gleiches
/// Ziel: nichts tun (eine laufende Überblendung bleibt). Neues Ziel: <see cref="OklchFade"/> von der Farbe auf dem Bildschirm
/// mit <see cref="HandoffBehavior.SnapshotAndReplace"/>, bei Dauer 0 sofort. Lage, Größe und Deckkraft gelten sofort. Ein Pinsel
/// mit anderem Aufbau wird ersetzt (gezählt). <see cref="Settle"/> setzt am Ende die Grundwerte und entfernt die Animationen.
/// </summary>
public sealed class LiveBrushSet
{
    private readonly ConditionalWeakTable<Animatable, StrongBox<Color>> _targets = [];
    private readonly List<(Animatable Owner, DependencyProperty Property)> _animated = [];

    public GameAccentStats Stats { get; } = new();

    /// <summary>Animierte Eigenschaften, die noch nicht beruhigt sind.</summary>
    public int Animating => _animated.Count;

    /// <summary>
    /// <paramref name="live"/> auf das Ziel führen; Ergebnis ist der Pinsel, der gelten soll (<paramref name="live"/> selbst oder
    /// eine neue, nicht eingefrorene Kopie von <paramref name="target"/>, wenn er fehlt oder nicht passt).
    /// </summary>
    public Brush Track(Brush? live, Brush target, TimeSpan duration) => Track(live, target, _ => duration);

    /// <summary>
    /// Wie <see cref="Track(Brush?, Brush, TimeSpan)"/>, mit eigener Dauer je Verlaufsstopp (etwa die Fensterkante: Stopps der
    /// Umgebung sofort, der Akzent-Stopp mit der Überblendung).
    /// </summary>
    public Brush Track(Brush? live, Brush target, Func<int, TimeSpan> durationOfStop)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(durationOfStop);
        if (live is null || !LiveBrush.Compatible(live, target))
        {
            if (live is not null)
            {
                Stats.Replacements++;
            }

            var fresh = LiveBrush.Unfrozen(target);
            Remember(fresh);
            return fresh;
        }

        live.Opacity = target.Opacity;
        switch (live, target)
        {
            case (SolidColorBrush solid, SolidColorBrush to):
                Retarget(solid, SolidColorBrush.ColorProperty, to.Color, durationOfStop(0));
                break;
            case (GradientBrush gradient, GradientBrush to):
                Geometry(gradient, to);
                for (var i = 0; i < gradient.GradientStops.Count; i++)
                {
                    Retarget(gradient.GradientStops[i], GradientStop.ColorProperty, to.GradientStops[i].Color, durationOfStop(i));
                }

                break;
        }

        return live;
    }

    /// <summary>Grundwerte = Ziele, Animationen entfernt; die Pinsel bleiben veränderbar.</summary>
    public void Settle()
    {
        foreach (var (owner, property) in _animated)
        {
            if (_targets.TryGetValue(owner, out var target))
            {
                owner.SetValue(property, target.Value);
            }

            owner.BeginAnimation(property, null);
        }

        _animated.Clear();
    }

    private void Remember(Brush brush)
    {
        switch (brush)
        {
            case SolidColorBrush solid:
                _targets.AddOrUpdate(solid, new StrongBox<Color>(solid.Color));
                break;
            case GradientBrush gradient:
                foreach (var stop in gradient.GradientStops)
                {
                    _targets.AddOrUpdate(stop, new StrongBox<Color>(stop.Color));
                }

                break;
        }
    }

    private void Retarget(Animatable owner, DependencyProperty property, Color target, TimeSpan duration)
    {
        if (_targets.TryGetValue(owner, out var known) && known.Value == target)
        {
            return;
        }

        _targets.AddOrUpdate(owner, new StrongBox<Color>(target));
        Stats.Retargets++;
        var current = (Color)owner.GetValue(property);
        var fade = OklchFade.Create(current, target, duration);
        if (fade is null)
        {
            owner.SetValue(property, target);
            owner.BeginAnimation(property, null);
            _animated.RemoveAll(a => ReferenceEquals(a.Owner, owner) && a.Property == property);
            return;
        }

        owner.BeginAnimation(property, fade, HandoffBehavior.SnapshotAndReplace);
        Stats.Animations++;
        if (!_animated.Exists(a => ReferenceEquals(a.Owner, owner) && a.Property == property))
        {
            _animated.Add((owner, property));
        }
    }

    private static void Geometry(GradientBrush live, GradientBrush target)
    {
        live.MappingMode = target.MappingMode;
        live.SpreadMethod = target.SpreadMethod;
        live.ColorInterpolationMode = target.ColorInterpolationMode;
        switch (live, target)
        {
            case (LinearGradientBrush l, LinearGradientBrush t):
                if (l.StartPoint != t.StartPoint)
                {
                    l.StartPoint = t.StartPoint;
                }

                if (l.EndPoint != t.EndPoint)
                {
                    l.EndPoint = t.EndPoint;
                }

                break;
            case (RadialGradientBrush r, RadialGradientBrush t):
                r.Center = t.Center;
                r.GradientOrigin = t.GradientOrigin;
                r.RadiusX = t.RadiusX;
                r.RadiusY = t.RadiusY;
                break;
        }
    }
}
