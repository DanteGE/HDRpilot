using System.IO;
using System.Windows;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Shell;

namespace HdrPilot.App.Views.Shell;

/// <summary>Ordnerauswahl über den Windows-Dialog (OpenFolderDialog), gebunden an das Einstellungen-Fenster.</summary>
public sealed class DialogFolderPicker(Window owner) : IFolderPicker
{
    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = ShellStrings.SearchFolderDialogTitle,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
}
