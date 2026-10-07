using System.Windows;
using System.Windows.Controls;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Kontextmenü des Tray-Symbols. Wird beim Rechtsklick erzeugt und nach dem Schließen verworfen. Beim Öffnen gleitet
/// es weich ein (nur wenn Windows Animationen erlaubt).
/// </summary>
public partial class TrayMenu : ContextMenu
{
    private static readonly TimeSpan EntranceDelay = TimeSpan.Zero;

    public TrayMenu()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object sender, RoutedEventArgs e)
    {
        if (ShellMotion.IsEnabled && GetTemplateChild("PART_Body") is UIElement body)
        {
            ShellMotion.FadeSlideIn(body, EntranceDelay, offset: 8, milliseconds: 200);
        }
    }
}
