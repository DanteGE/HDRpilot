using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Ob Animationen laufen dürfen: Windows-Einstellung „Animationseffekte“ (SystemParameters.ClientAreaAnimation) und
/// Hardware-Darstellung (über Remotedesktop oder ohne Grafikbeschleunigung keine Animationen). XAML-Trigger binden an
/// <see cref="IsEnabled"/>; Code fragt <see cref="Enabled"/>.
/// </summary>
public sealed class LibraryMotion : INotifyPropertyChanged
{
    private LibraryMotion()
    {
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static LibraryMotion Instance { get; } = new();

    public static bool Enabled => SystemParameters.ClientAreaAnimation && (RenderCapability.Tier >> 16) > 0;

    public bool IsEnabled => Enabled;

    /// <summary>Einblenden: Deckkraft 0→1 und Verschieben, mit Verzögerung. Startwert gilt sofort (kein Aufblitzen).</summary>
    public static void FadeIn(UIElement element, TimeSpan delay, double offsetX = 0, double offsetY = 14, double durationMs = 260)
    {
        if (!Enabled)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(durationMs);
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
}
