using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Theming;

/// <summary>
/// Farbüberblendung im OKLCH-Farbraum (E94): WPF mischt Farben in scRGB (linear), eine direkte Überblendung zwischen weit
/// entfernten Farbtönen läuft deshalb durch Grau. Hier liegen vier Schlüsselbilder auf dem OKLCH-Weg (25, 50, 75, 100 %,
/// kürzerer Bogen); ihre Zeiten (0,397 / 0,5 / 0,603 / 1 der Dauer) sind die Stellen, an denen CubicEase EaseInOut diese
/// Anteile erreicht, das erste Stück mit EaseIn und das letzte mit EaseOut (dort genau), dazwischen linear. Ohne „From“:
/// die Animation beginnt bei der Farbe auf dem Bildschirm, eine unterbrochene Überblendung läuft also weich weiter.
/// </summary>
public static class OklchFade
{
    /// <summary>Zeitanteile der Schlüsselbilder.</summary>
    public static IReadOnlyList<double> KeyTimes { get; } = [0.397, 0.5, 0.603, 1.0];

    /// <summary>Anteile des Farbwegs an den Schlüsselbildern.</summary>
    public static IReadOnlyList<double> PathFractions { get; } = [0.25, 0.5, 0.75, 1.0];

    /// <summary>
    /// Überblendung von <paramref name="from"/> (der Farbe auf dem Bildschirm) zu <paramref name="to"/>, eingefroren; null,
    /// wenn beide gleich sind oder die Dauer 0 ist (dann gilt der Wert sofort).
    /// </summary>
    public static ColorAnimationUsingKeyFrames? Create(Color from, Color to, TimeSpan duration)
    {
        if (from == to || duration <= TimeSpan.Zero)
        {
            return null;
        }

        Rgba8 a = new(from.R, from.G, from.B, from.A), b = new(to.R, to.G, to.B, to.A);
        var animation = new ColorAnimationUsingKeyFrames { Duration = new Duration(duration), FillBehavior = FillBehavior.HoldEnd };
        for (var i = 0; i < KeyTimes.Count; i++)
        {
            var color = i == KeyTimes.Count - 1 ? to : ToColor(ColorScience.Lerp(a, b, PathFractions[i]));
            var time = KeyTime.FromTimeSpan(TimeSpan.FromTicks((long)Math.Round(duration.Ticks * KeyTimes[i])));
            ColorKeyFrame frame = i switch
            {
                0 => new EasingColorKeyFrame(color, time, new CubicEase { EasingMode = EasingMode.EaseIn }),
                3 => new EasingColorKeyFrame(color, time, new CubicEase { EasingMode = EasingMode.EaseOut }),
                _ => new LinearColorKeyFrame(color, time),
            };
            animation.KeyFrames.Add(frame);
        }

        Timeline.SetDesiredFrameRate(animation, EffectPolicy.OneShotFrameRate);
        animation.Freeze();
        return animation;
    }

    private static Color ToColor(Rgba8 c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
