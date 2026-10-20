using System.ComponentModel;
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
        var own = element.RenderTransform as TranslateTransform;
        var borrowed = own is { IsFrozen: false };
        var shift = borrowed ? own! : new TranslateTransform();
        var previous = element.ReadLocalValue(UIElement.RenderTransformProperty);
        if (!borrowed)
        {
            element.RenderTransform = shift;
        }

        var move = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        var property = offsetX != 0 ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var start = offsetX != 0 ? offsetX : offsetY;
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(delay)));
        move.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay + duration), ease));
        move.Completed += (_, _) =>
        {
            // Aufräumen (Listen-Container werden wiederverwendet); eine neuere Einblendung behält ihre Verschiebung.
            if (!borrowed && ReferenceEquals(element.RenderTransform, shift))
            {
                if (previous == DependencyProperty.UnsetValue)
                {
                    element.ClearValue(UIElement.RenderTransformProperty);
                }
                else
                {
                    element.SetValue(UIElement.RenderTransformProperty, previous);
                }
            }
        };

        element.BeginAnimation(UIElement.OpacityProperty, opacity);
        shift.BeginAnimation(property, move);
    }

    private void Raise()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFull)));
    }
}
