using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Theming;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Bildgrößen und Ersatzflächen der Bibliothek. Jedes Bild wird höchstens in der angezeigten Pixelbreite angefragt
/// (Listenvorschau 128 px, Kachel-Cover in doppelter größter Kachelbreite), nie größer.
/// </summary>
public static class LibraryArt
{
    /// <summary>Breite der Listenvorschau (64 × 36 angezeigt, doppelte Dichte).</summary>
    public const int ThumbnailWidth = 128;

    /// <summary>Breite der Cover für Raster und Regal (größte Kachel 220 bzw. 227 px).</summary>
    public const int CoverWidth = 440;

    /// <summary>Kleine Vorschau im Detailbereich und im Kontextmenü.</summary>
    public const int PreviewWidth = 320;

    /// <summary>Breitestes Titelbild (Vollbild bis 3840 px).</summary>
    public const int MaxHeroWidth = 3840;

    /// <summary>Höchstens so viele Cover hält die Bibliothek selbst (mehr als je gleichzeitig sichtbar, auch bei 3440×1440).</summary>
    public const int CoverCacheLimit = 96;

    private static readonly Color Ink = Color.FromRgb(0x05, 0x06, 0x0A);

    /// <summary>
    /// Farbfeld aus den Namensfarben (Prototyp nameField): schräger Verlauf vom dunklen Grundton über die abgedunkelte
    /// Leuchtfarbe zur abgedunkelten Zweitfarbe. Eingefroren, fester Farbton je Name.
    /// </summary>
    public static Brush NameField(string name) => Field(AmbientColors.FromName(name ?? string.Empty));

    /// <summary>Farbfeld aus Umgebungsfarben (Ersatz-Titelbild, Ersatz-Cover).</summary>
    public static Brush Field(AmbientColors colors)
    {
        var a1 = Parse(colors.Deep);
        var a2 = ColorMath.Mix(Parse(colors.Glow), Ink, 0.35);
        var a3 = ColorMath.Mix(Parse(colors.Shine), Ink, 0.45);
        var angle = 120 * Math.PI / 180;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5 - (Math.Sin(angle) / 2), 0.5 + (Math.Cos(angle) / 2)),
            EndPoint = new Point(0.5 + (Math.Sin(angle) / 2), 0.5 - (Math.Cos(angle) / 2)),
            GradientStops = { new GradientStop(a1, 0), new GradientStop(a2, 0.55), new GradientStop(a3, 1) },
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>„#RRGGBB“ in eine Farbe; Unlesbares wird ein neutrales Dunkel.</summary>
    public static Color Parse(string? hex) => ColorMath.TryParseHex(hex, out var c) ? c : Color.FromRgb(0x20, 0x22, 0x2C);
}
