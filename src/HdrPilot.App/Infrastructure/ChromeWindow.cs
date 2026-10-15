using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services;

namespace HdrPilot.App.Infrastructure;

/// <summary>
/// Fenster mit eigenem Rahmen im HdrPilot-Stil (Hp.ChromeWindowStyle): Titelleisten-Befehle, unter Windows 11
/// runde Ecken und dunkle Fensterrahmen-Farbe passend zum Design. Das Fenstersymbol folgt Farbschema und HDR-Zustand
/// (<see cref="WindowIconSource"/>, E89); ohne Darstellung (Setup) bleibt das feste Programmsymbol.
/// </summary>
public class ChromeWindow : Window
{
    /// <summary>
    /// Inhalt reicht unter die Titelleiste (E89, Bibliothek: das Titelbild beginnt am oberen Fensterrand). Titel und
    /// Fensterknöpfe liegen dann hell über dem Bild (Hp.Brush.TextOnArt), der Inhalt beginnt in Zeile 0 der Vorlage.
    /// </summary>
    public static readonly DependencyProperty ContentUnderTitleBarProperty = DependencyProperty.Register(
        nameof(ContentUnderTitleBar), typeof(bool), typeof(ChromeWindow), new PropertyMetadata(false));

    private static readonly BitmapFrame AppIcon = BitmapFrame.Create(new Uri("pack://application:,,,/HDRpilot;component/Assets/HdrPilot.ico", UriKind.Absolute));

    private readonly WindowIconSource _icons;

    public ChromeWindow()
        : this(WindowIconSource.Shared)
    {
    }

    /// <param name="icons">Quelle des Fenstersymbols (Tests: eigene Quelle).</param>
    protected ChromeWindow(WindowIconSource icons)
    {
        _icons = icons;
        SetResourceReference(StyleProperty, "Hp.ChromeWindowStyle");
        Icon = _icons.Icon ?? AppIcon;
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));
        ThemeManager.ThemeChanged += OnThemeChanged;
        _icons.Changed += OnIconChanged;
        Closed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _icons.Changed -= OnIconChanged;
        };
    }

    /// <inheritdoc cref="ContentUnderTitleBarProperty"/>
    public bool ContentUnderTitleBar
    {
        get => (bool)GetValue(ContentUnderTitleBarProperty);
        set => SetValue(ContentUnderTitleBarProperty, value);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyFrameAttributes();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyFrameAttributes();

    // Neues Symbol erst zeichnen, wenn ein Fenster offen ist (die Quelle zeichnet nur auf Anfrage und je Zustand einmal).
    private void OnIconChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            Icon = _icons.Icon ?? AppIcon;
        }
        else
        {
            Dispatcher.BeginInvoke(() => Icon = _icons.Icon ?? AppIcon);
        }
    }

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
