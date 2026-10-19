using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Bild eines Elements, wie es gerade zu sehen ist (Werkzeugleiste, Stufe „Reduziert“: Lasche bzw. Kapsel und die Leiste
/// beim Zeilenwechsel blenden als Bild aus, während das Neue einblendet).
/// </summary>
public static class ElementBitmap
{
    /// <summary>
    /// Element samt allem, was es zeichnet (auch über seine Ränder hinaus), in Gerätepixeln rendern; <c>Bounds</c> ist die
    /// Fläche in seinen eigenen Koordinaten, an die das Bild gehört. Über einen <see cref="VisualBrush"/> in einem eigenen
    /// <see cref="DrawingVisual"/>: <see cref="RenderTargetBitmap.Render"/> mit dem Element selbst übernähme dessen Versatz
    /// im Elternelement (etwa 1223/207 im Canvas) und zeichnete am Bild vorbei. Null, wenn es nichts zeichnet.
    /// </summary>
    public static (BitmapSource Bitmap, Rect Bounds)? Render(Visual source, DpiScale dpi)
    {
        var bounds = VisualTreeHelper.GetDescendantBounds(source);
        if (bounds.IsEmpty || !(bounds.Width > 0) || !(bounds.Height > 0))
        {
            return null;
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(source), null, new Rect(bounds.Size));
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(bounds.Width * dpi.DpiScaleX),
            (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return (bitmap, bounds);
    }
}
