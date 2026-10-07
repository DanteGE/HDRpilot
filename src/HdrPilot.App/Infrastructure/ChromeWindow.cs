using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace HdrPilot.App.Infrastructure;

/// <summary>
/// Fenster mit eigenem Rahmen im HdrPilot-Stil (Hp.ChromeWindowStyle): Titelleisten-Befehle, unter Windows 11
/// runde Ecken und dunkle Fensterrahmen-Farbe passend zum Design.
/// </summary>
public class ChromeWindow : Window
{
    private static readonly BitmapFrame AppIcon = BitmapFrame.Create(new Uri("pack://application:,,,/HdrPilot;component/Assets/HdrPilot.ico", UriKind.Absolute));

    public ChromeWindow()
    {
        SetResourceReference(StyleProperty, "Hp.ChromeWindowStyle");
        Icon = AppIcon;
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyFrameAttributes();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyFrameAttributes();

    private unsafe void ApplyFrameAttributes()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2 (ab Windows 11; ältere Systeme ignorieren es).
        var corner = 2;
        _ = DwmSetWindowAttribute(hwnd, 33, &corner, sizeof(int));

        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
        var dark = ThemeManager.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, &dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern unsafe int DwmSetWindowAttribute(IntPtr hwnd, int attribute, void* value, int size);
}
