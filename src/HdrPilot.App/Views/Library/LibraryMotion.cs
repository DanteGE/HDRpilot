using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Ob Animationen laufen dürfen: die wirksame Effektstufe der Darstellung (E89, vom Bibliotheksfenster gesetzt; ob die
/// Windows-Einstellung „Animationseffekte“ beachtet wird, steckt schon darin) und die Hardware-Darstellung (über
/// Remotedesktop oder ohne Grafikbeschleunigung keine Animationen). So gelten für Anheben, Glanz, Einblenden und Hinweise
/// dieselben Regeln wie für Zoom und HDR-Moment. XAML-Trigger binden an <see cref="IsEnabled"/> (kurze Übergänge: Stufe
/// „Voll“ oder „Reduziert“) bzw. <see cref="IsFull"/> (Anheben der Kacheln, Glanz: nur „Voll“); Code fragt
/// <see cref="Enabled"/>.
/// </summary>
public sealed class LibraryMotion : INotifyPropertyChanged
{
    /// <summary>Jüngste Einblendung je Element (nur sie setzt am Ende die Verschiebung zurück).</summary>
    private static readonly ConditionalWeakTable<UIElement, FadeRun> Runs = new();

    private EffectLevel _level = EffectLevel.Full;

    private LibraryMotion()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static LibraryMotion Instance { get; } = new();

    /// <summary>Kurze Übergänge erlaubt (Effektstufe nicht „Aus“, Hardware-Darstellung).</summary>
    public static bool Enabled => HardwareRendering && Instance._level != EffectLevel.Off;

    /// <summary>Die Grafikkarte zeichnet (sonst keine Animationen, wie bei der wirksamen Stufe „Aus“).</summary>
    public static bool HardwareRendering => (RenderCapability.Tier >> 16) > 0;

    public bool IsEnabled => Enabled;

    /// <summary>Stufe „Voll“: Anheben und Glanz der Kacheln, gestaffeltes Einblenden.</summary>
    public bool IsFull => Enabled && _level == EffectLevel.Full;

    public EffectLevel Level => _level;

    /// <summary>Wirksame Effektstufe der Darstellung übernehmen (Bibliotheksfenster).</summary>
    public void SetLevel(EffectLevel level)
    {
        if (_level != level)
        {
            _level = level;
            Raise();
        }
    }

    /// <summary>Einblenden: Deckkraft 0→1 und Verschieben, mit Verzögerung. Startwert gilt sofort (kein Aufblitzen).</summary>
    public static void FadeIn(UIElement element, TimeSpan delay, double offsetX = 0, double offsetY = 14, double durationMs = 260)
    {
        if (!Enabled)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(Instance._level == EffectLevel.Full ? durationMs : Math.Min(durationMs, 300));
        var opacity = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + duration), ease));
        opacity.Freeze();

        // Grundwert 0: Nach dem Ende (FillBehavior.Stop) steht das Element ohne Sprung an seinem Platz. Eine eigene
        // Verschiebung des Elements (Schublade, Titelbereich) wird mitbenutzt und bleibt; sonst kommt eine vorübergehende.
        // Läuft auf dem Element noch eine Einblendung, übernimmt die neue deren vorübergehende Verschiebung samt dem Wert
        // davor: Zurückgesetzt wird erst am Ende der jüngsten (eine abgelöste meldet ihr Ende trotzdem).
        var own = element.RenderTransform as TranslateTransform;
        FadeRun run;
        if (Runs.TryGetValue(element, out var earlier) && earlier.Temporary is { } inherited && ReferenceEquals(own, inherited))
        {
            run = new FadeRun(inherited, earlier.Previous);
        }
        else if (own is { IsFrozen: false })
        {
            run = new FadeRun(null, null);
        }
        else
        {
            run = new FadeRun(new TranslateTransform(), element.ReadLocalValue(UIElement.RenderTransformProperty));
            element.RenderTransform = run.Temporary;
        }

        Runs.AddOrUpdate(element, run);
        var shift = run.Temporary ?? own!;

        var move = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        var property = offsetX != 0 ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var start = offsetX != 0 ? offsetX : offsetY;
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(delay)));
        move.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay + duration), ease));
        move.Freeze();

        var opacityClock = opacity.CreateClock();
        var moveClock = move.CreateClock();
        moveClock.Completed += (_, _) =>
        {
            // Beendete Uhren abnehmen (FillBehavior.Stop allein lässt sie an Element und Verschiebung): danach läuft und hängt
            // nichts mehr. Nur die eigenen; die einer neueren Einblendung laufen weiter.
            opacityClock.Controller?.Remove();
            moveClock.Controller?.Remove();
            if (!Runs.TryGetValue(element, out var newest) || !ReferenceEquals(newest, run))
            {
                return;
            }

            Runs.Remove(element);

            // Aufräumen (Listen-Container werden wiederverwendet); eine eigene Verschiebung bleibt.
            if (run.Temporary is { } temporary && ReferenceEquals(element.RenderTransform, temporary))
            {
                if (run.Previous == DependencyProperty.UnsetValue)
                {
                    element.ClearValue(UIElement.RenderTransformProperty);
                }
                else
                {
                    element.SetValue(UIElement.RenderTransformProperty, run.Previous);
                }
            }
        };

        element.ApplyAnimationClock(UIElement.OpacityProperty, opacityClock);
        shift.ApplyAnimationClock(property, moveClock);
    }

    private void Raise()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFull)));
    }

    /// <summary>
    /// Einblendung je Element: <paramref name="Temporary"/> ist die vorübergehende Verschiebung (null: die eigene des Elements),
    /// <paramref name="Previous"/> der Wert von RenderTransform davor.
    /// </summary>
    private sealed record FadeRun(TranslateTransform? Temporary, object? Previous);
}
