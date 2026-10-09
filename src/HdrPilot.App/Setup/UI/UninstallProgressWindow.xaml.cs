using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Views.Shell;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Fortschritt der Kopie im Temp-Ordner beim Entfernen. Solange gearbeitet wird, läuft der Lichtreflex über die
/// Akzentlinie und das Fenster lässt sich nicht schließen; das Ergebnis gleitet ein, „Fertig“ bekommt den Fokus.
/// </summary>
public partial class UninstallProgressWindow : ChromeWindow
{
    private UninstallProgressViewModel? _model;
    private BusySheen? _sheen;

    public UninstallProgressWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Verbindet das Fenster mit seinem ViewModel (vor dem Anzeigen).</summary>
    public void Attach(UninstallProgressViewModel model)
    {
        _model = model;
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        model.PropertyChanged += OnModelPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ShellMotion.PlayEntrance(Root, [Header, Body]);
        _sheen = new BusySheen(this, AccentLine, Sheen, SheenTransform) { IsBusy = _model?.IsBusy == true };
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        if (e.PropertyName == nameof(UninstallProgressViewModel.IsBusy) && _sheen is not null)
        {
            _sheen.IsBusy = _model.IsBusy;
        }
        else if (e.PropertyName == nameof(UninstallProgressViewModel.IsDone) && _model.IsDone)
        {
            if (ShellMotion.IsEnabled && IsLoaded)
            {
                ShellMotion.FadeSlideIn(ResultCard, TimeSpan.Zero, 12, 240);
            }

            Dispatcher.BeginInvoke(() => CloseButton.Focus(), DispatcherPriority.Input);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Erst schließen, wenn das Ergebnis feststeht (die Arbeit läuft in diesem Prozess).
        if (_model is { IsDone: false })
        {
            e.Cancel = true;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        Closing -= OnClosing;
        _sheen?.Dispose();
        _sheen = null;
        if (_model is not null)
        {
            _model.CloseRequested -= OnCloseRequested;
            _model.PropertyChanged -= OnModelPropertyChanged;
            _model = null;
        }

        DataContext = null;
    }
}
