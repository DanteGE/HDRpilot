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
    private bool _compactToolbar;
    private double _toolbarNeeded;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateToolbarDensity();
        DetailsArea.SizeChanged += (_, e) => FitCandidates(e.NewSize.Height);
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
                CenterInList(row);
                row.Focus();
            }
        });
    }

    /// <summary>
    /// Holt die Zeile in die Mitte der Liste (ScrollIntoView legt sie an den Rand, wo die Hinweiszeile sie verdecken
    /// würde). Die Liste scrollt pixelweise, der Versatz ist also in geräteunabhängigen Pixeln.
    /// </summary>
    private void CenterInList(ListBoxItem row)
    {
        if (GameList.Template?.FindName("ScrollHost", GameList) is not ScrollViewer scroll || !row.IsDescendantOf(scroll))
        {
            return;
        }

        var top = row.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
        var target = scroll.VerticalOffset + top - ((scroll.ViewportHeight - row.ActualHeight) / 2);
        scroll.ScrollToVerticalOffset(Math.Clamp(target, 0, scroll.ScrollableHeight));
    }

    /// <summary>
    /// Vorschläge dürfen die Spieleliste nicht verdrängen: Ihre Liste ist höchstens zwei Zeilen hoch und schrumpft in
    /// niedrigen Fenstern bis auf eine Zeile, damit für die Spiele etwa 260 px bleiben. Gemessen an der Höhe des
    /// Detailbereichs (gleiche Zeile, wird anders als die Liste beim Überlaufen nicht größer gemeldet).
    /// </summary>
    private void FitCandidates(double rowHeight)
    {
        const double headerAndMargins = 84;
        const double keepForGames = 260;
        CandidateList.MaxHeight = Math.Clamp(rowHeight - headerAndMargins - keepForGames, 56, 122);
    }

    /// <summary>
    /// Schmales Fenster: „Neu einlesen“ zeigt nur das Symbol, damit Suche und „Spiel hinzufügen“ Platz behalten.
    /// Die nötige Breite wird im normalen Zustand gemessen (passt sich an andere Sprachen an).
    /// </summary>
    private void UpdateToolbarDensity()
    {
        if (!_compactToolbar)
        {
            var needed = ToolbarArea.ColumnDefinitions[0].MinWidth;
            foreach (UIElement child in ToolbarArea.Children)
            {
                if (Grid.GetColumn(child) > 0)
                {
                    needed += child.DesiredSize.Width; // mit Außenabstand
                }
            }

            _toolbarNeeded = needed;
        }

        // Nicht ToolbarArea.ActualWidth: Ein überlaufendes Grid meldet seine ungekürzte Wunschbreite.
        // Verfügbar ist die Fensterbreite ohne Rahmen (maximiert: 7 px je Seite) und ohne Außenabstand des Inhalts.
        var frame = WindowState == WindowState.Maximized ? 14 : 2;
        var available = ActualWidth - frame - Root.Margin.Left - Root.Margin.Right;
        var compact = available < _toolbarNeeded;
        if (compact == _compactToolbar)
        {
            return;
        }

        _compactToolbar = compact;
        RescanText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RescanGlyph.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        MarkAllSeenButton.Margin = compact ? new Thickness(8, 0, 0, 0) : new Thickness(18, 0, 0, 0);
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
