using System.Windows.Controls;

namespace HdrPilot.App.Views.Shell;

/// <summary>Kontextmenü des Tray-Symbols. Wird beim Rechtsklick erzeugt und nach dem Schließen verworfen.</summary>
public partial class TrayMenu : ContextMenu
{
    public TrayMenu()
    {
        InitializeComponent();
    }
}
