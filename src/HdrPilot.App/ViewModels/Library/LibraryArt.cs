using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    // Zugeschnittene Logos je Bild (lebt so lange wie das Bild im Bilddienst).
    private static readonly ConditionalWeakTable<ImageSource, ImageSource> Trimmed = new();

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

    /// <summary>
    /// Logo ohne durchsichtigen Rand (Prototyp trimLogo, WPF CroppedBitmap): Launcher legen Logos oft mit viel leerem Rand
    /// ab (Steam 640 × 360), sonst stünde das Logo klein in der Ecke. Ein Bildpunkt zählt ab Deckkraft 21 von 255; ganz
    /// durchsichtige oder randlose Bilder bleiben, wie sie sind. Ergebnis eingefroren und je Bild gemerkt.
    /// </summary>
    public static ImageSource? TrimTransparent(ImageSource? image) =>
        image is BitmapSource { PixelWidth: > 1, PixelHeight: > 1 } bitmap ? Trimmed.GetValue(image, _ => Trim(bitmap)) : image;

    /// <summary>„#RRGGBB“ in eine Farbe; Unlesbares wird ein neutrales Dunkel.</summary>
    public static Color Parse(string? hex) => ColorMath.TryParseHex(hex, out var c) ? c : Color.FromRgb(0x20, 0x22, 0x2C);

    private static ImageSource Trim(BitmapSource bitmap)
    {
        BitmapSource source = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int w = source.PixelWidth, h = source.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        source.CopyPixels(pixels, stride, 0);
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] > 20)
                {
                    x0 = Math.Min(x0, x);
                    x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y);
                    y1 = Math.Max(y1, y);
                }
            }
        }

        if (x1 < 0 || (x0 == 0 && y0 == 0 && x1 == w - 1 && y1 == h - 1))
        {
            return bitmap;
        }

        var crop = new CroppedBitmap(bitmap, new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
        crop.Freeze();
        return crop;
    }
}
