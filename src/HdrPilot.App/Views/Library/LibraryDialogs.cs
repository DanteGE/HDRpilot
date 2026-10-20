using System.Windows;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using Microsoft.Win32;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Dialoge der Spielebibliothek. Sie öffnen sich nur auf einen Klick im Hauptfenster hin und gehören zu dem gerade
/// offenen Fenster der Bibliothek (nie ungefragt, nie über einem Spiel). <paramref name="testIconFile"/> und
/// <paramref name="testArtFile"/> ersetzen nur in Prüfläufen mit eigener Ablage die Dateiauswahl für „Symbol ändern…“
/// bzw. „Bild wählen…“ → „Eigene Datei…“; <paramref name="testLog"/> meldet dort, wohin der Fokus einer offenen Rückfrage
/// zeigt (<see cref="WindowConfirmDialogs"/>).
/// </summary>
public sealed class LibraryDialogs(Window mainWindow, string? testIconFile = null, string? testArtFile = null, Action<string>? testLog = null) : ILibraryDialogs
{
    /// <summary>Rückfrage mit Aufzählung, Warnung und Häkchen (E90, „Einstellungen entfernen…“).</summary>
    public ConfirmAnswer Confirm(ConfirmRequest request) => new WindowConfirmDialogs(Owner, testLog).Confirm(request);

    public string? PickArtworkFile(string? initialDirectory)
    {
        if (testArtFile is not null)
        {
            return testArtFile;
        }

        var dialog = new OpenFileDialog
        {
            Title = LibraryStrings.Picker_FileTitle,
            Filter = LibraryStrings.Picker_FileFilter,
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = initialDirectory ?? string.Empty,
        };
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public bool ShowArtworkPicker(ArtworkPickerViewModel viewModel)
    {
        var dialog = new ArtworkPickerDialog(viewModel) { Owner = Owner() };
        return dialog.ShowDialog() == true;
    }

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
        while (owner.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible && w is AddGameDialog or ConfirmDialog or ArtworkPickerDialog) is { } child)
        {
            owner = child;
        }

        return owner;
    }
}

/// <summary>
/// Rückfragen mit Aufzählung, Warnung und Häkchen über einem Fenster (E90: Bibliothek und Einstellungen). In Prüfläufen
/// mit eigener Ablage schreibt <paramref name="testLog"/> nach dem ersten Zeichnen, wohin der Fokus der Rückfrage zeigt: im
/// aktiven Fenster der Tastaturfokus, sonst der, den es beim Aktivieren wiederherstellt (UI-Automation sieht nur den ersten).
/// </summary>
public sealed class WindowConfirmDialogs(Func<Window> owner, Action<string>? testLog = null) : IConfirmDialogs
{
    public ConfirmAnswer Confirm(ConfirmRequest request)
    {
        var dialog = new ConfirmDialog(request) { Owner = owner() };
        if (testLog is { } log)
        {
            dialog.ContentRendered += (_, _) =>
            {
                var focused = (dialog.IsActive ? System.Windows.Input.Keyboard.FocusedElement : System.Windows.Input.FocusManager.GetFocusedElement(dialog)) as DependencyObject;
                var id = focused is null ? string.Empty : System.Windows.Automation.AutomationProperties.GetAutomationId(focused);
                log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Prüfung: Rückfrage „{0}“ offen, Fokus {1}.", request.Title, string.IsNullOrEmpty(id) ? "keiner" : id));
            };
        }

        dialog.ShowDialog();
        return dialog.Answer;
    }
}
