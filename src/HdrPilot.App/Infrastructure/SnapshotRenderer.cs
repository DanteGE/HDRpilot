using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HdrPilot.App.Infrastructure;

/// <summary>
/// Entwicklungswerkzeug (<c>HdrPilot.exe --snapshot &lt;ordner&gt; --config-dir &lt;ordner&gt;</c>): rendert Fenster
/// außerhalb des sichtbaren Bildschirms in PNG-Dateien, um das Aussehen hell und dunkel zu prüfen, ohne den
/// Nutzer zu stören.
/// </summary>
public static class SnapshotRenderer
{
    public static async Task RenderAsync(Application app, IReadOnlyList<(string Name, Func<Window> Create)> windows, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var dark in new[] { true, false })
        {
            ThemeManager.Override = dark;
            ThemeManager.Apply(app);
            foreach (var (name, create) in windows)
            {
                var window = create();
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20000;
                window.Top = -20000;
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.Show();
                // Layout, Daten und Symbole laden lassen, Einblend-Animationen abwarten.
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(1500);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Save(window, Path.Combine(directory, $"{name}-{(dark ? "dunkel" : "hell")}.png"));
                window.Close();
            }
        }

        ThemeManager.Override = null;
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var root = (Visual)(VisualTreeHelper.GetChildrenCount(window) > 0 ? VisualTreeHelper.GetChild(window, 0) : content);
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
