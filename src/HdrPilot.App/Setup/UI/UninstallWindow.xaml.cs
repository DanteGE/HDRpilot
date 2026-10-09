using System.ComponentModel;
using System.Windows;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Views.Shell;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Rückfrage vor dem Entfernen. Öffnet mit weicher Einblendung, Fokus auf „Entfernen“ (Eingabetaste bestätigt, Esc
/// bricht ab); solange die Kopie im Temp-Ordner angelegt wird, lässt es sich nicht schließen.
/// </summary>
public partial class UninstallWindow : ChromeWindow
{
    private UninstallViewModel? _model;

    public UninstallWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Verbindet das Fenster mit seinem ViewModel (vor dem Anzeigen).</summary>
    public void Attach(UninstallViewModel model)
    {
        _model = model;
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ShellMotion.PlayEntrance(Root, [Header, Body]);
        ConfirmButton.Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_model?.IsBusy == true)
        {
            e.Cancel = true;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        Closing -= OnClosing;
        if (_model is not null)
        {
            _model.CloseRequested -= OnCloseRequested;
            _model = null;
        }

        DataContext = null;
    }
}
