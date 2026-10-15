using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Umgebungsfarben, wie das Fenster sie anwendet (Prototyp applyAmbient/palUse): <see cref="A1"/> dunkler Grundton,
/// <see cref="A2"/> Leuchtfarbe, <see cref="A3"/> helle Zweitfarbe, <see cref="Background"/> Fenstergrund, dazu das
/// Spitzlicht des Titelbilds.
/// </summary>
public readonly record struct AmbientPalette(Color A1, Color A2, Color A3, Color Background, double LightX, double LightY, double Highlight)
{
    private static readonly Color Ink = Color.FromRgb(0x05, 0x06, 0x0A);
    private static readonly Color Paper = Color.FromRgb(0xED, 0xEF, 0xF4);

    /// <summary>
    /// Farben für ein Spiel: folgt der Hintergrund dem Spiel, die Farben des Bildes (fast graue Bilder zur Hälfte mit dem
    /// Schema gemischt), sonst die Umgebung des Farbschemas (a1 = Atmosphäre 82 % abgedunkelt, a2 = Atmosphäre, a3 = Glanz).
    /// Grund dunkel: 34 % a1 auf #05060A; hell: 9 % a2 auf #EDEFF4 (OKLab).
    /// </summary>
    public static AmbientPalette Resolve(AmbientColors? game, PaletteColors scheme, bool followGame, bool dark)
    {
        var schemeA1 = ColorMath.Mix(scheme.Atmosphere, Ink, 0.82);
        Color a1, a2, a3;
        if (followGame && game is not null)
        {
            a1 = LibraryArt.Parse(game.Deep);
            a2 = LibraryArt.Parse(game.Glow);
            a3 = LibraryArt.Parse(game.Shine);
            if (game.Dullness == AmbientDullness.Gray)
            {
                a1 = ColorMath.MixOklab(a1, ColorMath.Mix(scheme.Atmosphere, Ink, 0.8), 0.5);
                a2 = ColorMath.MixOklab(a2, scheme.Atmosphere, 0.5);
                a3 = ColorMath.MixOklab(a3, scheme.Shine, 0.5);
            }
        }
        else
        {
            a1 = schemeA1;
            a2 = scheme.Atmosphere;
            a3 = scheme.Shine;
        }

        var background = dark ? ColorMath.MixOklab(Ink, a1, 0.34) : ColorMath.MixOklab(Paper, a2, 0.09);
        return new AmbientPalette(
            Opaque(a1),
            Opaque(a2),
            Opaque(a3),
            Opaque(background),
            game?.LightX ?? 0.62,
            game?.LightY ?? 0.45,
            game?.Highlight ?? 0.55);
    }

    /// <summary>Farben der Partikel im HDR-Moment: Spielfarben bzw. Schema, dazu Weiß und ein heller Glanz.</summary>
    public IReadOnlyList<Color> ParticleColors(PaletteColors scheme, bool followGame) => followGame
        ? [A3, A2, Colors.White, ColorMath.Mix(A3, Colors.White, 0.5), scheme.Shine]
        : [scheme.Shine, scheme.Accent, Colors.White, ColorMath.Mix(scheme.Shine, Colors.White, 0.5), scheme.Atmosphere];

    /// <summary>Farbe mit Deckkraft 0–1.</summary>
    public static Color Alpha(Color color, double alpha) => ColorMath.WithAlpha(color, alpha);

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);
}

/// <summary>
/// Bildausschnitt wie CSS <c>object-fit: cover</c> mit <c>object-position</c>: Welcher Teil des Bildes (relativ 0–1) füllt
/// eine Fläche, ohne zu verzerren. Für ImageBrush.Viewbox (Titelbild „50 % 28 %“, Ausschnitt um das Spitzlicht).
/// </summary>
public static class CoverCrop
{
    public static Rect Compute(double imageWidth, double imageHeight, double boxWidth, double boxHeight, double positionX = 0.5, double positionY = 0.5)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || boxWidth <= 0 || boxHeight <= 0
            || !double.IsFinite(imageWidth + imageHeight + boxWidth + boxHeight))
        {
            return new Rect(0, 0, 1, 1);
        }

        var imageAspect = imageWidth / imageHeight;
        var boxAspect = boxWidth / boxHeight;
        double w = 1, h = 1;
        if (imageAspect > boxAspect)
        {
            w = boxAspect / imageAspect; // Bild breiter: links und rechts abschneiden
        }
        else
        {
            h = imageAspect / boxAspect; // Bild höher: oben und unten abschneiden
        }

        var x = (1 - w) * Math.Clamp(positionX, 0, 1);
        var y = (1 - h) * Math.Clamp(positionY, 0, 1);
        return new Rect(x, y, w, h);
    }
}
