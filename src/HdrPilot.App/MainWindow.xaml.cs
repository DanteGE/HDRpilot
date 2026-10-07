using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;

namespace HdrPilot.App;

/// <summary>
/// Hauptfenster „Spielebibliothek“. Die Ansicht kümmert sich nur um Bewegung: Einblenden beim Öffnen, gestaffelte
/// erste Zeilen, Wechsel im Detailbereich und das Leuchten des HDR-Status. Dauerhafte Animationen (Leuchten,
/// Dreh-Symbol beim Einlesen) laufen nur, solange das Fenster sichtbar und aktiv ist.
/// Beim Schließen wird das ViewModel freigegeben (meldet sich überall ab).
/// </summary>
public partial class MainWindow : ChromeWindow
{
    private const int StaggeredRows = 12;

    private LibraryViewModel? _viewModel;
    private Storyboard? _pulse;
    private Storyboard? _spin;
    private bool _pulseRunning;
    private bool _spinRunning;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Activated += OnActivityChanged;
        Deactivated += OnActivityChanged;
        StateChanged += OnActivityChanged;
        IsVisibleChanged += (_, _) => UpdateAmbientMotion();
        Closed += OnClosed;
    }

    public LibraryViewModel? ViewModel => _viewModel;

    public void Attach(LibraryViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.RevealRequested += OnRevealRequested;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused && _viewModel is { HasSearchText: true } vm)
        {
            vm.SearchText = string.Empty;
            vm.ApplySearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SearchBox.IsKeyboardFocused)
        {
            _viewModel?.ApplySearch();
            e.Handled = true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Weiches Öffnen: Bereiche nacheinander, dann die ersten sichtbaren Zeilen gestaffelt.
        LibraryMotion.FadeIn(HeaderArea, TimeSpan.Zero, offsetY: 12);
        LibraryMotion.FadeIn(ToolbarArea, TimeSpan.FromMilliseconds(50), offsetY: 12);
        LibraryMotion.FadeIn(ListArea, TimeSpan.FromMilliseconds(90), offsetY: 16);
        LibraryMotion.FadeIn(DetailsArea, TimeSpan.FromMilliseconds(130), offsetX: 18);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, StaggerFirstRows);
        AnimateHdrTint();
        UpdateAmbientMotion();
    }

    private void StaggerFirstRows()
    {
        if (!LibraryMotion.Enabled)
        {
            return;
        }

        var shown = 0;
        for (var i = 0; i < GameList.Items.Count && shown < StaggeredRows; i++)
        {
            if (GameList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem { IsVisible: true } row)
            {
                LibraryMotion.FadeIn(row, TimeSpan.FromMilliseconds(140 + (shown * 30)), offsetY: 10, durationMs: 240);
                shown++;
            }
            else if (shown > 0)
            {
                break;
            }
        }
    }

    private void OnActivityChanged(object? sender, EventArgs e) => UpdateAmbientMotion();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LibraryViewModel.IsHdrOn):
            case nameof(LibraryViewModel.IsScanning):
                UpdateAmbientMotion();
                AnimateHdrTint();
                break;
            case nameof(LibraryViewModel.Details):
                if (_viewModel?.Details is not null)
                {
                    DetailsPanel.ScrollToTop();
                    LibraryMotion.FadeIn(DetailsContent, TimeSpan.Zero, offsetX: 14, durationMs: 220);
                }

                break;
            case nameof(LibraryViewModel.HasStatus):
                if (_viewModel?.HasStatus == true)
                {
                    LibraryMotion.FadeIn(StatusToast, TimeSpan.Zero, offsetY: 12, durationMs: 220);
                }

                break;
        }
    }

    private void OnRevealRequested(object? sender, GameItemViewModel item)
    {
        GameList.ScrollIntoView(item);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (GameList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row)
            {
                row.Focus();
            }
        });
    }

    /// <summary>Zustandswechsel des HDR-Status: Tönung weich ein- bzw. ausblenden.</summary>
    private void AnimateHdrTint()
    {
        var target = _viewModel?.IsHdrOn == true ? 0.12 : 0.0;
        if (!LibraryMotion.Enabled)
        {
            HdrPillTint.BeginAnimation(OpacityProperty, null);
            HdrPillTint.Opacity = target;
            return;
        }

        HdrPillTint.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Dauerhafte Animationen nur bei sichtbarem, aktivem Fenster und erlaubten Animationen.</summary>
    private void UpdateAmbientMotion()
    {
        var vm = _viewModel;
        var live = vm is not null && IsVisible && IsActive && WindowState != WindowState.Minimized && LibraryMotion.Enabled;

        var pulse = live && vm!.IsHdrOn;
        if (pulse && !_pulseRunning)
        {
            _pulse ??= CreatePulse();
            _pulse.Begin(HdrHalo, isControllable: true);
            _pulseRunning = true;
        }
        else if (!pulse && _pulseRunning)
        {
            _pulse?.Stop(HdrHalo);
            _pulseRunning = false;
        }

        // Ohne Animation leuchtet der Status ruhig, solange HDR an ist.
        HdrHalo.Opacity = !pulse && vm?.IsHdrOn == true ? 0.6 : 0;

        var spin = live && vm!.IsScanning;
        if (spin && !_spinRunning)
        {
            _spin ??= CreateSpin();
            _spin.Begin(RescanGlyph, isControllable: true);
            _spinRunning = true;
        }
        else if (!spin && _spinRunning)
        {
            _spin?.Stop(RescanGlyph);
            _spinRunning = false;
        }
    }

    private static Storyboard CreatePulse()
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(1400));
        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var opacity = new DoubleAnimation(0.25, 0.95, duration) { EasingFunction = ease };
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        var scaleX = new DoubleAnimation(0.9, 1.7, duration) { EasingFunction = ease };
        Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        var scaleY = new DoubleAnimation(0.9, 1.7, duration) { EasingFunction = ease };
        Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        board.Children.Add(opacity);
        board.Children.Add(scaleX);
        board.Children.Add(scaleY);
        return board;
    }

    private static Storyboard CreateSpin()
    {
        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var rotate = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(900)));
        Storyboard.SetTargetProperty(rotate, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        board.Children.Add(rotate);
        return board;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _pulse?.Stop(HdrHalo);
        _spin?.Stop(RescanGlyph);
        _pulseRunning = false;
        _spinRunning = false;
        if (_viewModel is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.RevealRequested -= OnRevealRequested;
            vm.Dispose();
        }

        _viewModel = null;
        DataContext = null;
    }
}
