using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Views.Shell;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Setup-Fenster im Stil der Einstellungen. Öffnet mit weicher Einblendung, jede neue Seite gleitet ein; solange
/// installiert wird, läuft ein Lichtreflex über die Akzentlinie (nur bei aktivem Fenster) und das Fenster lässt sich
/// nicht schließen.
/// </summary>
public partial class SetupWindow : ChromeWindow
{
    private SetupViewModel? _model;
    private BusySheen? _sheen;

    public SetupWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Verbindet das Fenster mit seinem ViewModel (vor dem Anzeigen).</summary>
    public void Attach(SetupViewModel model)
    {
        _model = model;
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        model.PropertyChanged += OnModelPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ShellMotion.PlayEntrance(Root, [Header, OptionsPage]);
        _sheen = new BusySheen(this, AccentLine, Sheen, SheenTransform) { IsBusy = _model?.IsBusy == true };
        FocusPrimary();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SetupViewModel.Page) || _model is null)
        {
            return;
        }

        if (_sheen is not null)
        {
            _sheen.IsBusy = _model.IsBusy;
        }

        if (ShellMotion.IsEnabled && IsLoaded)
        {
            ShellMotion.FadeSlideIn(_model.IsResultPage ? ResultCard : WorkPage, TimeSpan.Zero, 12, 240);
        }

        // Erst nach den Bindungen (Sichtbarkeit der Knöpfe) den Fokus setzen.
        Dispatcher.BeginInvoke(FocusPrimary, DispatcherPriority.Input);
    }

    /// <summary>
    /// Fokus auf den Knopf, den die Eingabetaste auslöst (Installieren, Erneut versuchen, Fertig, Abbrechen); bei
    /// einer älteren Version zuerst auf die Bestätigung.
    /// </summary>
    private void FocusPrimary()
    {
        UIElement? target = _model switch
        {
            { IsOptionsPage: true, CanInstall: false } => AllowDowngradeBox,
            { IsOptionsPage: true } => InstallButton,
            { CanRetry: true } => RetryButton,
            { Succeeded: true } => CloseButton,
            { HasError: true } => CancelButton,
            _ => null,
        };
        target?.Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Während der Installation nicht schließen: Ersetzen und Rückfall auf die alte Version brauchen ihr Ende.
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
