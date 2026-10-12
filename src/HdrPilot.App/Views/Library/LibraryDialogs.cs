using System.Windows;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using Microsoft.Win32;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Dialoge der Spielebibliothek. Sie öffnen sich nur auf einen Klick im Hauptfenster hin und gehören zu dem gerade
/// offenen Fenster der Bibliothek (nie ungefragt, nie über einem Spiel). <paramref name="testIconFile"/> ersetzt nur in
/// Prüfläufen mit eigener Ablage die Dateiauswahl für „Symbol ändern…“.
/// </summary>
public sealed class LibraryDialogs(Window mainWindow, string? testIconFile = null) : ILibraryDialogs
{
    public string? PickIconFile(string? initialDirectory)
    {
        if (testIconFile is not null)
        {
            return testIconFile;
        }

        var dialog = new OpenFileDialog
        {
            Title = LibraryStrings.Icon_PickerTitle,
            Filter = LibraryStrings.Icon_PickerFilter,
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = initialDirectory ?? string.Empty,
        };
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public string? PickExecutable(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = LibraryStrings.Exe_PickerTitle,
            Filter = LibraryStrings.Exe_PickerFilter,
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = initialDirectory ?? string.Empty,
        };
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LibraryStrings.AddGame_FolderPickerTitle,
            Multiselect = false,
            InitialDirectory = initialDirectory ?? string.Empty,
        };
        return dialog.ShowDialog(Owner()) == true ? dialog.FolderName : null;
    }

    public bool Confirm(string title, string message, string confirmText, bool isDanger)
    {
        var dialog = new ConfirmDialog(title, message, confirmText, isDanger) { Owner = Owner() };
        return dialog.ShowDialog() == true;
    }

    public bool ShowAddGame(AddGameViewModel viewModel)
    {
        var dialog = new AddGameDialog(viewModel) { Owner = Owner() };
        return dialog.ShowDialog() == true;
    }

    /// <summary>Oberstes sichtbares Fenster der Bibliothek (etwa „Spiel hinzufügen“ für dessen Dateiauswahl).</summary>
    private Window Owner()
    {
        Window owner = mainWindow;
        while (owner.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible && w is AddGameDialog or ConfirmDialog) is { } child)
        {
            owner = child;
        }

        return owner;
    }
}
