using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.Core.Artwork;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Zeichnet die Ersatzbilder (E89, G10.3) einmal mit <see cref="DrawingVisual"/> und <see cref="RenderTargetBitmap"/>; danach
/// sind sie normale Bilder im Ordner art\gen. Formen und Werte wie im Prototyp (<c>genBanner</c>, <c>coverGen</c>, <c>.gico</c>
/// in app.css), umgerechnet auf die gespeicherte Größe; ohne BitmapEffects. Schrift: Bahnschrift fett, sonst Segoe UI.
/// Nur auf einem STA-Thread aufrufen (die Arbeits-Threads des Bilderdienstes sind STA).
/// </summary>
public static class ArtworkRenderer
{
    public const int HeroWidth = 1920;
    public const int HeroHeight = 620;
    public const int CoverWidth = 600;
    public const int CoverHeight = 900;
    public const int HeaderWidth = 460;
    public const int HeaderHeight = 215;
    public const int IconSize = 256;

    /// <summary>Symbole werden höchstens so stark vergrößert (nie ein 32-px-Bild aufblasen).</summary>
    public const double MaxIconUpscale = 1.5;

    private static readonly Color Ink = Color.FromRgb(5, 6, 10);
    private static readonly Color Back = Color.FromRgb(7, 8, 12);
    private static readonly FontFamily Display = new("Bahnschrift, Segoe UI");

    /// <summary>Schrift der Titel in erzeugten Bildern.</summary>
    public static Typeface DisplayTypeface { get; } = new(Display, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <summary>
    /// Titelbild 1920×620: Farbfeld (gestreckt, 3 % über den Rand), Abdunklung (Kreisverlauf rechts, Verlauf von links), der
    /// Name als Umriss-Muster (zwei Zeilen, Großbuchstaben, −6° gedreht, links ausgeblendet) und bei einem deckenden
    /// Kachelbild (Xbox) das Bild rechts der Mitte mit weichen Kanten. Kein Symbol in der Mitte: Symbol und Name zeigt die
    /// Oberfläche im Titelbereich.
    /// </summary>
    public static BitmapSource Hero(string name, BgraImage field, BitmapSource? tile)
    {
        return Render(HeroWidth, HeroHeight, dc =>
        {
            const double w = HeroWidth, h = HeroHeight;
            dc.DrawRectangle(new SolidColorBrush(Back), null, new Rect(0, 0, w, h));
            DrawField(dc, field, new Rect(-0.03 * w, -0.03 * h, w * 1.06, h * 1.06));

            // .gshade: radial-gradient(60% 110% at 74% 40%, transparent, rgba(5,6,10,.42) 75%) und von links abgedunkelt.
            var radial = new RadialGradientBrush
            {
                Center = new Point(0.74, 0.40),
                GradientOrigin = new Point(0.74, 0.40),
                RadiusX = 0.60,
                RadiusY = 1.10,
                GradientStops = { Stop(Colors.Transparent, 0), Stop(WithAlpha(Ink, 0.42), 0.75) },
            };
            dc.DrawRectangle(radial, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Gradient(90, Stop(WithAlpha(Ink, 0.5), 0), Stop(WithAlpha(Ink, 0.12), 0.55), Stop(Colors.Transparent, 1)), null, new Rect(0, 0, w, h));
            DrawNamePattern(dc, name, w, h);
            if (tile is not null)
            {
                // .gart (über Abdunklung und Muster): quadratisch in voller Höhe rechts der Mitte, links und rechts weich ausgeblendet.
                dc.PushOpacityMask(Gradient(90, Stop(Colors.Transparent, 0), Stop(Colors.Black, 0.38), Stop(Colors.Black, 0.78), Stop(Colors.Transparent, 1)));
                dc.DrawImage(tile, new Rect(0.37 * w, 0, h, h));
                dc.Pop();
            }
        });
    }

    /// <summary>
    /// Cover 600×900 aus dem Titelbild (verschmierte Steam-Kapsel oder kein Cover): Ausschnitt um die hellste Stelle
    /// (<paramref name="lightX"/>, auf 0,2–0,8 begrenzt, senkrecht bei 30 %), unten abgedunkelt, darauf das Logo (unten,
    /// höchstens 80 % breit und 30 % hoch) oder der Name.
    /// </summary>
    public static BitmapSource CoverFromHero(BitmapSource hero, double lightX, BitmapSource? logo, string name)
    {
        var crop = ArtworkImaging.CoverFill(hero, CoverWidth, CoverHeight, Math.Clamp(lightX, 0.2, 0.8), 0.3);
        return Render(CoverWidth, CoverHeight, dc =>
        {
            const double w = CoverWidth, h = CoverHeight;
            dc.DrawImage(crop, new Rect(0, 0, w, h));
            dc.DrawRectangle(Gradient(0, Stop(WithAlpha(Ink, 0.88), 0), Stop(WithAlpha(Ink, 0.25), 0.46), Stop(Colors.Transparent, 0.66)), null, new Rect(0, 0, w, h));
            if (logo is not null)
            {
                var box = new Rect(0.10 * w, h - (0.09 * h) - (0.30 * h), 0.80 * w, 0.30 * h);
                var fit = Fit(logo.PixelWidth, logo.PixelHeight, box.Width, box.Height, allowUpscale: true);
                dc.DrawImage(logo, new Rect(box.X + ((box.Width - fit.Width) / 2), box.Bottom - fit.Height, fit.Width, fit.Height));
            }
            else
            {
                DrawCoverName(dc, name, w, h);
            }
        });
    }

    /// <summary>
    /// Cover 600×900 ohne Titelbild: Farbfeld mit Licht und Streifen, das Symbol auf einer Glaskachel (14 % von oben, 46 %
    /// breit; kleine Symbole höchstens 1,5-fach) oder das Monogramm, ein deckendes Kachelbild (Xbox) stattdessen oben über die
    /// ganze Breite; unten der Name (höchstens drei Zeilen).
    /// </summary>
    public static BitmapSource CoverFromField(string name, BgraImage field, BitmapSource? icon, int iconPx, bool iconIsTile, string monogram)
    {
        return Render(CoverWidth, CoverHeight, dc =>
        {
            const double w = CoverWidth, h = CoverHeight;
            dc.DrawRectangle(new SolidColorBrush(Back), null, new Rect(0, 0, w, h));
            DrawField(dc, field, new Rect(-0.04 * w, -0.04 * h, w * 1.08, h * 1.08));
            var light = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.3),
                GradientOrigin = new Point(0.5, 0.3),
                RadiusX = 0.9,
                RadiusY = 0.6,
                GradientStops = { Stop(Color.FromArgb(20, 255, 255, 255), 0), Stop(Colors.Transparent, 0.7) },
            };
            dc.DrawRectangle(light, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Gradient(180, Stop(WithAlpha(Ink, 0.05), 0), Stop(WithAlpha(Ink, 0.25), 0.48), Stop(WithAlpha(Ink, 0.86), 1)), null, new Rect(0, 0, w, h));
            DrawStripes(dc, w, h);
            if (icon is not null && iconIsTile)
            {
                dc.PushOpacityMask(Gradient(180, Stop(Colors.Black, 0.62), Stop(Colors.Transparent, 1)));
                dc.DrawImage(ArtworkImaging.CoverFill(icon, (int)w, (int)w), new Rect(0, 0, w, w));
                dc.Pop();
            }
            else
            {
                var tileRect = new Rect(0.27 * w, 0.14 * h, 0.46 * w, 0.46 * w);
                DrawGlassTile(dc, tileRect);
                if (icon is not null)
                {
                    DrawIcon(dc, icon, iconPx, tileRect, 0.8);
                }
                else
                {
                    DrawCentered(dc, monogram, tileRect, 0.15 * w * 1.0, Brushes.White);
                }
            }

            DrawCoverName(dc, name, w, h);
        });
    }

    /// <summary>
    /// Vorschaubild der Liste ohne Kopf- und Titelbild (460×215): Farbfeld mit Symbol oder Monogramm; ein Kachelbild (Xbox) oder
    /// quadratisches Cover (Quiver) füllt stattdessen die ganze Fläche (Mitte, oben und unten beschnitten).
    /// </summary>
    public static BitmapSource Header(BgraImage field, BitmapSource? icon, int iconPx, bool iconIsTile, string monogram)
    {
        return Render(HeaderWidth, HeaderHeight, dc =>
        {
            const double w = HeaderWidth, h = HeaderHeight;
            dc.DrawRectangle(new SolidColorBrush(Back), null, new Rect(0, 0, w, h));
            DrawField(dc, field, new Rect(-0.04 * w, -0.04 * h, w * 1.08, h * 1.08));
            if (icon is not null && iconIsTile)
            {
                dc.DrawImage(ArtworkImaging.CoverFill(icon, (int)w, (int)h), new Rect(0, 0, w, h));
                return;
            }

            dc.DrawRectangle(Gradient(180, Stop(Colors.Transparent, 0.4), Stop(WithAlpha(Ink, 0.35), 1)), null, new Rect(0, 0, w, h));
            var box = new Rect((w - (0.62 * h)) / 2, 0.19 * h, 0.62 * h, 0.62 * h);
            if (icon is not null)
            {
                DrawIcon(dc, icon, iconPx, box, 1.0);
            }
            else
            {
                DrawCentered(dc, monogram, box, 0.42 * h, Brushes.White);
            }
        });
    }

    /// <summary>
    /// Monogramm-Symbol 256×256 (<c>.gico.mono</c>): abgerundete Kachel im Verlauf der Namensfarben (unabhängig vom
    /// Farbschema), heller Innenrand, zwei Buchstaben in Weiß.
    /// </summary>
    public static BitmapSource Monogram(string text, AmbientColors colors)
    {
        return Render(IconSize, IconSize, dc =>
        {
            const double s = IconSize;
            var tile = new Rect(0, 0, s, s);
            var radius = 0.24 * s;
            dc.DrawRoundedRectangle(Css(145, s, s, Stop(ParseColor(colors.Glow), 0), Stop(ParseColor(colors.Deep), 1)), null, tile, radius, radius);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), 3), new Rect(1.5, 1.5, s - 3, s - 3), radius - 1.5, radius - 1.5);
            dc.DrawRoundedRectangle(Gradient(180, Stop(Color.FromArgb(46, 255, 255, 255), 0), Stop(Colors.Transparent, 0.45)), null, tile, radius, radius);
            DrawCentered(dc, text, tile, 0.36 * s, Brushes.White);
        });
    }

    /// <summary>Farbfeld (kleines BGRA-Bild) bilinear auf das Ziel gestreckt (weiche Fläche ohne Weichzeichner).</summary>
    private static void DrawField(DrawingContext dc, BgraImage field, Rect target)
    {
        int width = Math.Max(1, (int)Math.Ceiling(target.Width / 4)), height = Math.Max(1, (int)Math.Ceiling(target.Height / 4));
        var smooth = PixelAnalysis.Resample(field.Pixels, field.Width, field.Height, 0, 0, field.Width, field.Height, width, height);
        var bitmap = new BgraImage(width, height, smooth).ToBitmap();
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.Linear);
        group.Children.Add(new ImageDrawing(bitmap, target));
        dc.DrawDrawing(group);
    }

    /// <summary>Name als Umriss in zwei Zeilen (.gpat): links 30 %, rechts −10 %, −6° gedreht, Kontur Weiß 10 % bzw. 6 %.</summary>
    private static void DrawNamePattern(DrawingContext dc, string name, double w, double h)
    {
        var text = name.ToUpper(CultureInfo.CurrentCulture);
        var size = Math.Clamp(h * 0.52, 110, 190);
        var line = size * 0.86;
        var box = new Rect(0.30 * w, (h / 2) - line, 0.80 * w, 2 * line);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        dc.PushTransform(new RotateTransform(-6, box.X + (box.Width / 2), box.Y + (box.Height / 2)));
        dc.PushClip(new RectangleGeometry(box));
        dc.PushOpacityMask(new LinearGradientBrush(Colors.Transparent, Colors.Black, new Point(box.Left, 0), new Point(box.Left + (0.3 * box.Width), 0))
        {
            MappingMode = BrushMappingMode.Absolute,
        });
        for (var row = 0; row < 2; row++)
        {
            var formatted = Formatted(text, size, Brushes.Transparent);
            var x = box.X - (row == 1 ? 1.6 * size : 0);
            var y = box.Y + (row * line) + ((line - formatted.Height) / 2);
            var geometry = formatted.BuildGeometry(new Point(x, y));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(row == 0 ? 26 : 15), 255, 255, 255)), 1.5) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, geometry);
        }

        dc.Pop();
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Name unten auf dem Cover (.gn): 9 % Rand, unten 7 %, höchstens drei Zeilen, dunkler weicher Rand für Lesbarkeit.</summary>
    private static void DrawCoverName(DrawingContext dc, string name, double w, double h)
    {
        var maxWidth = 0.82 * w;
        var size = 0.105 * w;
        FormattedText formatted;
        do
        {
            formatted = Formatted(name, size, Brushes.White);
            formatted.MaxTextWidth = maxWidth;
            formatted.LineHeight = size * 1.12;
            formatted.MaxLineCount = 3;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
            if (formatted.Height <= size * 1.12 * 3.05 || size <= 0.07 * w)
            {
                break;
            }

            size -= 2;
        }
        while (true);

        var origin = new Point(0.09 * w, h - (0.07 * h) - formatted.Height);
        var geometry = formatted.BuildGeometry(origin);
        foreach (var (thickness, alpha) in new[] { (16.0, 0.10), (9.0, 0.16), (4.0, 0.22) })
        {
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(WithAlpha(Colors.Black, alpha)), thickness) { LineJoin = PenLineJoin.Round }, geometry);
        }

        dc.DrawGeometry(Brushes.White, null, geometry);
    }

    /// <summary>Diagonale Streifen (repeating-linear-gradient 135°, Weiß 4 %).</summary>
    private static void DrawStripes(DrawingContext dc, double w, double h)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)), 5);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        for (var d = -h; d < w + h; d += 62)
        {
            dc.DrawLine(pen, new Point(d, 0), new Point(d + h, h));
        }

        dc.Pop();
    }

    /// <summary>Glaskachel (.gic): Verlauf Weiß 18 % → 5 %, heller Innenrand, weicher Schatten darunter.</summary>
    private static void DrawGlassTile(DrawingContext dc, Rect tile)
    {
        var radius = 0.24 * tile.Width;
        for (var i = 3; i >= 1; i--)
        {
            var grow = i * 7;
            var shadow = new Rect(tile.X - (grow / 2.0), tile.Y + 22 + (grow / 2.0), tile.Width + grow, tile.Height);
            dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(Colors.Black, 0.10)), null, shadow, radius + grow, radius + grow);
        }

        dc.DrawRoundedRectangle(Css(145, tile.Width, tile.Height, Stop(Color.FromArgb(46, 255, 255, 255), 0), Stop(Color.FromArgb(13, 255, 255, 255), 1), tile.TopLeft), null, tile, radius, radius);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(61, 255, 255, 255)), 3), new Rect(tile.X + 1.5, tile.Y + 1.5, tile.Width - 3, tile.Height - 3), radius - 1.5, radius - 1.5);
    }

    /// <summary>Symbol mittig in einem Rahmen: höchstens <paramref name="fill"/> des Rahmens und höchstens 1,5-fach vergrößert.</summary>
    private static void DrawIcon(DrawingContext dc, BitmapSource icon, int iconPx, Rect box, double fill)
    {
        var native = Math.Max(16, iconPx > 0 ? iconPx : icon.PixelWidth);
        var size = Math.Min(box.Width * fill, native * MaxIconUpscale);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        group.Children.Add(new ImageDrawing(icon, new Rect(box.X + ((box.Width - size) / 2), box.Y + ((box.Height - size) / 2), size, size)));
        dc.DrawDrawing(group);
    }

    private static void DrawCentered(DrawingContext dc, string text, Rect box, double size, Brush brush)
    {
        var formatted = Formatted(text, size, brush);
        var geometry = formatted.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        dc.PushTransform(new TranslateTransform(box.X + ((box.Width - bounds.Width) / 2) - bounds.X, box.Y + ((box.Height - bounds.Height) / 2) - bounds.Y));
        dc.DrawGeometry(brush, null, geometry);
        dc.Pop();
    }

    private static FormattedText Formatted(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, DisplayTypeface, size, brush, 1.0);

    private static Size Fit(double width, double height, double maxWidth, double maxHeight, bool allowUpscale)
    {
        var scale = Math.Min(maxWidth / Math.Max(1, width), maxHeight / Math.Max(1, height));
        if (!allowUpscale)
        {
            scale = Math.Min(1, scale);
        }

        return new Size(width * scale, height * scale);
    }

    /// <summary>Linearer Verlauf über das Rechteck: 0° von unten nach oben, 90° von links nach rechts, 180° von oben nach unten.</summary>
    private static LinearGradientBrush Gradient(double cssAngle, params GradientStop[] stops)
    {
        var radians = cssAngle * Math.PI / 180;
        double dx = Math.Sin(radians) / 2, dy = -Math.Cos(radians) / 2;
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5 - dx, 0.5 - dy), EndPoint = new Point(0.5 + dx, 0.5 + dy) };
        foreach (var stop in stops)
        {
            brush.GradientStops.Add(stop);
        }

        return brush;
    }

    /// <summary>CSS-Verlauf mit Winkel über ein Rechteck der Größe w×h (Länge der Verlaufslinie wie in CSS).</summary>
    private static LinearGradientBrush Css(double cssAngle, double w, double h, GradientStop first, GradientStop last, Point? origin = null)
    {
        var radians = cssAngle * Math.PI / 180;
        double sin = Math.Sin(radians), cos = Math.Cos(radians);
        var length = (Math.Abs(w * sin) + Math.Abs(h * cos)) / 2;
        var o = origin ?? new Point(0, 0);
        var center = new Point(o.X + (w / 2), o.Y + (h / 2));
        return new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(center.X - (sin * length), center.Y + (cos * length)),
            EndPoint = new Point(center.X + (sin * length), center.Y - (cos * length)),
            GradientStops = { first, last },
        };
    }

    private static GradientStop Stop(Color color, double offset) => new(color, offset);

    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    private static Color ParseColor(string hex)
    {
        var (r, g, b) = PixelAnalysis.HexToRgb(hex);
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }

    private static BitmapSource Render(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            draw(dc);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
