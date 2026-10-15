using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Ob Animationen laufen dürfen: Windows-Einstellung „Animationseffekte“ (SystemParameters.ClientAreaAnimation),
/// Hardware-Darstellung (über Remotedesktop oder ohne Grafikbeschleunigung keine Animationen) und die Effektstufe der
/// Darstellung (E89, vom Bibliotheksfenster gesetzt). XAML-Trigger binden an <see cref="IsEnabled"/> (kurze Übergänge:
/// Stufe „Voll“ oder „Reduziert“) bzw. <see cref="IsFull"/> (Anheben der Kacheln, Glanz: nur „Voll“); Code fragt
/// <see cref="Enabled"/>.
/// </summary>
public sealed class LibraryMotion : INotifyPropertyChanged
{
    private EffectLevel _level = EffectLevel.Full;

    private LibraryMotion()
    {
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            {
                Raise();
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static LibraryMotion Instance { get; } = new();

    /// <summary>Kurze Übergänge erlaubt (Windows und Effektstufe).</summary>
    public static bool Enabled => SystemEnabled && Instance._level != EffectLevel.Off;

    /// <summary>Windows-Animationseffekte an und Hardware-Darstellung.</summary>
    public static bool SystemEnabled => SystemParameters.ClientAreaAnimation && (RenderCapability.Tier >> 16) > 0;

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

        // Grundwert 0: Nach dem Ende (FillBehavior.Stop) steht das Element ohne Sprung an seinem Platz.
        var shift = new TranslateTransform();
        element.RenderTransform = shift;
        var move = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        var property = offsetX != 0 ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var start = offsetX != 0 ? offsetX : offsetY;
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(delay)));
        move.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay + duration), ease));
        move.Completed += (_, _) =>
        {
            // Aufräumen (Listen-Container werden wiederverwendet); eine neuere Einblendung behält ihre Verschiebung.
            if (ReferenceEquals(element.RenderTransform, shift))
            {
                element.ClearValue(UIElement.RenderTransformProperty);
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
