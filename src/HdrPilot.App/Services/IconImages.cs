using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HdrPilot.App.Services;

/// <summary>
/// Tray- und Fenstersymbol als eingefrorenes WPF-Bild (Vorschau in „Darstellung → Symbole und Tray“). Dieselbe Zeichnung
/// wie das echte Symbol (<see cref="TrayIconRenderer"/>), ohne Symbol-Handle und ohne GDI-Bitmap-Handle.
/// </summary>
public static class IconImages
{
    /// <summary>Symbol in genau <paramref name="pixels"/> × <paramref name="pixels"/> Bildpunkten.</summary>
    public static BitmapSource Render(TrayIconSpec spec, int pixels)
    {
        pixels = Math.Clamp(pixels, TrayIconRenderer.MinSize, TrayIconRenderer.MaxSize);
        using var bitmap = TrayIconRenderer.Render(spec, pixels);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, pixels, pixels), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            // GDI+ 32bppArgb liegt als B, G, R, A im Speicher, nicht vormultipliziert: entspricht Bgra32.
            var source = BitmapSource.Create(pixels, pixels, 96, 96, PixelFormats.Bgra32, null, data.Scan0, data.Stride * pixels, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
