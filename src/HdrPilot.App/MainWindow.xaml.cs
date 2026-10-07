using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;

namespace HdrPilot.App;

/// <summary>
/// Hauptfenster „Spielebibliothek“. Die Ansicht kümmert sich um Bewegung (Einblenden beim Öffnen, gestaffelte erste
/// Zeilen, Wechsel im Detailbereich, Leuchten des HDR-Status) und um die Anordnung: Werkzeugleiste je nach Breite,
/// Aufteilung zwischen Liste und Detailbereich über den Trenner (gemerkt im ViewModel). Dauerhafte Animationen
/// (Leuchten, Dreh-Symbol beim Einlesen) laufen nur, solange das Fenster sichtbar und aktiv ist.
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
    private ToolbarMode _toolbarMode = ToolbarMode.Wide;
    private bool _rescanIconOnly;
    private double _filtersWidth;
    private double _actionsWidth;
    private double _rescanTextWidth;
    private double _appliedDetailsRatio;

    public MainWindow()
    {
        InitializeComponent();
        ApplyDetailsRatio(LibraryLayout.DefaultDetailsRatio);
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

    /// <summary>Aktuelle Anordnung der Werkzeugleiste (für Tests und Sichtprüfung).</summary>
    public ToolbarMode ToolbarMode => _toolbarMode;

    public void Attach(LibraryViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.RevealRequested += OnRevealRequested;

        // Gemerkte Aufteilung sofort setzen (Sternbreiten, unabhängig von der Fenstergröße); an die tatsächliche
        // Breite angepasst wird beim Laden.
        ApplyDetailsRatio(LibraryLayout.Normalize(viewModel.DetailsRatio));
    }

    /// <summary>
    /// Ordnet die Werkzeugleiste für die verfügbare Breite an: breit eine Zeile, etwas schmaler „Neu einlesen“ nur
    /// als Symbol, schmal Filter und Sortierung in einer zweiten Zeile. Die nötigen Breiten werden in der breiten
    /// Anordnung gemessen (passt sich an andere Sprachen an).
    /// </summary>
    public void UpdateToolbarLayout(double available)
    {
        if (_toolbarMode == ToolbarMode.Wide)
        {
            // In Spalten mit Auto-Breite messen die Gruppen ungekürzt; „Neu einlesen“ hat hier seinen Text.
            _filtersWidth = FilterGroup.DesiredSize.Width;
            _actionsWidth = ActionGroup.DesiredSize.Width;
            _rescanTextWidth = RescanText.DesiredSize.Width + RescanGlyph.Margin.Right;
        }

        var searchMin = ToolbarArea.ColumnDefinitions[0].MinWidth;
        var mode = LibraryToolbarLayout.Choose(available, searchMin, _filtersWidth, _actionsWidth, _rescanTextWidth);
        var iconOnly = LibraryToolbarLayout.RescanIconOnly(mode, available, searchMin, _actionsWidth);
        if (mode == _toolbarMode && iconOnly == _rescanIconOnly)
        {
            return;
        }

        _toolbarMode = mode;
        _rescanIconOnly = iconOnly;
        RescanText.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
        RescanGlyph.Margin = iconOnly ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        MarkAllSeenButton.Margin = iconOnly ? new Thickness(8, 0, 0, 0) : new Thickness(18, 0, 0, 0);

        // Gestapelt: Filter und Sortierung in der zweiten Zeile, bündig unter der Suche.
        var stacked = mode == ToolbarMode.Stacked;
        Grid.SetRow(FilterGroup, stacked ? 1 : 0);
        Grid.SetColumn(FilterGroup, stacked ? 0 : 1);
        Grid.SetColumnSpan(FilterGroup, stacked ? 3 : 1);
        FilterGroup.Margin = stacked ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        SourceFilter.Margin = stacked ? new Thickness(0) : new Thickness(10, 0, 0, 0);
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
        FitDetailsColumn();
        AnimateHdrTint();
        UpdateAmbientMotion();
    }

    /// <summary>
    /// Gemerkte Aufteilung an die aktuelle Fenstergröße anpassen: Liste und Detailbereich behalten ihre Mindestbreite.
    /// Der gemerkte Wert selbst bleibt (in einem größeren Fenster gilt er wieder).
    /// </summary>
    public void FitDetailsColumn()
    {
        var width = ListColumn.ActualWidth + DetailsColumn.ActualWidth;
        if (width > 0)
        {
            ApplyDetailsRatio(LibraryLayout.Clamp(_viewModel?.DetailsRatio, width));
        }
    }

    /// <summary>Anteil des Detailbereichs als Sternbreiten: beide Spalten wachsen mit dem Fenster im selben Verhältnis.</summary>
    private void ApplyDetailsRatio(double ratio)
    {
        _appliedDetailsRatio = ratio;
        ListColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
        DetailsColumn.Width = new GridLength(ratio, GridUnitType.Star);
    }

    /// <summary>
    /// Nach dem Ziehen oder den Pfeiltasten: Der Trenner hinterlässt die Pixelbreiten als Sternwerte. Daraus den Anteil
    /// bilden, auf Anteile zurückführen und merken (nur bei einer echten Änderung, nicht nach einem bloßen Klick).
    /// </summary>
    private void RememberDetailsRatio()
    {
        var ratio = ListColumn.Width.IsStar && DetailsColumn.Width.IsStar
            ? LibraryLayout.RatioOf(ListColumn.Width.Value, DetailsColumn.Width.Value)
            : LibraryLayout.RatioOf(ListColumn.ActualWidth, DetailsColumn.ActualWidth);
        if (ratio is not { } r || Math.Abs(r - _appliedDetailsRatio) < 0.0005)
        {
            return;
        }

        ApplyDetailsRatio(r);
        _viewModel?.SetDetailsRatio(r);
    }

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e) => RememberDetailsRatio();

    private void OnSplitterKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
        {
            RememberDetailsRatio();
        }
    }

    /// <summary>Doppelklick auf den Trenner: zurück zur Standardaufteilung (nichts mehr gemerkt).</summary>
    private void OnSplitterDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var width = ListColumn.ActualWidth + DetailsColumn.ActualWidth;
        ApplyDetailsRatio(width > 0 ? LibraryLayout.Clamp(null, width) : LibraryLayout.DefaultDetailsRatio);
        _viewModel?.SetDetailsRatio(null);
        e.Handled = true;
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
            case nameof(LibraryViewModel.SelectedSortOption):
                // Neue Reihenfolge: oben anfangen (nach „Zuletzt gespielt“ steht dort das letzte Spiel).
                if (GameList.Template?.FindName("ScrollHost", GameList) is ScrollViewer scroll)
                {
                    scroll.ScrollToTop();
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

    /// <summary>Werkzeugleiste an die Fensterbreite anpassen (siehe <see cref="UpdateToolbarLayout"/>).</summary>
    private void UpdateToolbarDensity()
    {
        // Nicht ToolbarArea.ActualWidth: Ein überlaufendes Grid meldet seine ungekürzte Wunschbreite.
        // Verfügbar ist die Fensterbreite ohne Rahmen (maximiert: 7 px je Seite) und ohne Außenabstand des Inhalts.
        var frame = WindowState == WindowState.Maximized ? 14 : 2;
        UpdateToolbarLayout(ActualWidth - frame - Root.Margin.Left - Root.Margin.Right);
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
