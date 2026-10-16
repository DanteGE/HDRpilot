using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Resources;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;

namespace HdrPilot.App;

/// <summary>
/// Hauptfenster „Spielebibliothek“ im Stil „Showcase Hero“ (E89). Die Ansicht ordnet an (Titelbereich je Ansicht, Werkzeugleiste
/// mit stufenweisem Falten, Liste/Raster/Regal, Detailbereich bzw. Schublade, Statuszeile), überträgt Umgebungsfarben und
/// Titelbild auf <see cref="AmbientBackdrop"/> und <see cref="HeroStage"/> und führt die Effekte über einen
/// <see cref="MotionController"/>: Dauer-Effekte nur bei Stufe „Voll“, sichtbarem, aktivem Fenster und ohne laufendes Spiel,
/// höchstens 30 Bilder pro Sekunde; der HDR-Moment über <see cref="HdrMomentPlayer"/>. Beim Schließen wird alles entfernt und
/// das ViewModel freigegeben (meldet sich überall ab).
/// </summary>
public partial class MainWindow : ChromeWindow
{
    /// <summary>Höhe des Titelbereichs (animiert beim Wechsel der Ansicht und beim Einklappen der Liste).</summary>
    public static readonly DependencyProperty HeroHeightProperty = DependencyProperty.Register(
        nameof(HeroHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(220.0, (d, _) => ((MainWindow)d).ApplyVerticalLayout()));

    /// <summary>
    /// Stufen der Werkzeugleiste (Prototyp f1–f6): je höher, desto mehr ist gefaltet. Stufe 7 (nicht im Prototyp): Reicht
    /// selbst die gefaltete Leiste nicht (Fenster um 1000 px), stehen Ansicht und Aktionen über den Filtern.
    /// </summary>
    public const int MaxToolbarStage = 7;

    private static readonly TimeSpan HeroMotion = TimeSpan.FromSeconds(0.8);
    private static readonly TimeSpan DrawerMotion = TimeSpan.FromSeconds(0.55);
    private static readonly TimeSpan PointerDuration = TimeSpan.FromSeconds(0.6);

    private readonly CompositionFrameClock _frameClock = new();
    private readonly StopwatchEffectTime _effectTime = new();
    private LibraryViewModel? _viewModel;
    private MotionController? _motion;
    private HdrMomentPlayer? _moment;
    private ContinuousEffect? _pulse;
    private ContinuousEffect? _spin;
    private ContinuousEffect? _ring;
    private ContinuousEffect? _sheen;
    private HeroViewModel? _hero;
    private LibraryViewMode? _appliedView;
    private bool _listCollapsed;
    private bool _dark = true;
    private int _toolbarStage;
    private bool _toolbarQueued;
    private double _appliedDetailsRatio;
    private AmbientPalette _palette;
    private PaletteColors _scheme;
    private ScrollViewer? _listScroll;
    private long _pointerTick;
    private bool _pointerActive;
    private readonly TranslateTransform _sheenShift = new(1.2, 0);
    private VirtualizingTilePanel? _shelfPanel;

    public MainWindow()
    {
        RingClock = new RingClock();
        SheenMask = new LinearGradientBrush(
            [
                new GradientStop(Colors.Transparent, 0.42),
                new GradientStop(Color.FromArgb(0xE6, 0, 0, 0), 0.48),
                new GradientStop(Colors.Black, 0.5),
                new GradientStop(Color.FromArgb(0xE6, 0, 0, 0), 0.52),
                new GradientStop(Colors.Transparent, 0.58),
            ],
            new Point(0, 0.3),
            new Point(1, 0.7))
        {
            RelativeTransform = _sheenShift,
        };
        InitializeComponent();
        FitToWorkArea();
        ApplyDetailsRatio(LibraryLayout.DefaultDetailsRatio);
        Loaded += OnLoaded;
        Root.SizeChanged += (_, _) => ApplyLayout(animate: false);

        // Spalten der Liste nach ihrer eigenen Breite (ändert sich auch mit dem Trenner, ohne dass sich das Fenster ändert).
        ListCard.SizeChanged += (_, _) => ApplyListColumns();
        PreviewMouseMove += OnPointerMove;
        MouseLeave += (_, _) => ResetPointer();
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
    }

    public LibraryViewModel? ViewModel => _viewModel;

    /// <summary>Steuerung der Effekte dieses Fensters (Tests, Bildschirmfotos).</summary>
    public MotionController? Motion => _motion;

    /// <summary>HDR-Moment dieses Fensters (Tests, Bildschirmfotos, Prüfung).</summary>
    public HdrMomentPlayer? Moment => _moment;

    /// <summary>
    /// Takt des Auswahlrings (Dauer-Effekt). Im Code erzeugt und über eine Bindung an das Fenster gelesen: als Ressource oder
    /// Bindungsquelle in einer Vorlage würde WPF ihn einfrieren, dann ließe er sich nicht mehr animieren.
    /// </summary>
    public RingClock RingClock { get; }

    /// <summary>Maske des Glanzes über dem Namen: ein schmales Band, das „TitleSheen“ verschiebt (aus demselben Grund im Code).</summary>
    public Brush SheenMask { get; }

    /// <summary>Stufe der Werkzeugleiste (0: alles ausgeschrieben, 6: alles gefaltet).</summary>
    public int ToolbarStage => _toolbarStage;

    /// <summary>Titelbereich (für Tests und Bildschirmfotos).</summary>
    public HeroStage HeroStage => Hero;

    public double HeroHeight
    {
        get => (double)GetValue(HeroHeightProperty);
        set => SetValue(HeroHeightProperty, value);
    }

    /// <summary>Gerade aktive Ansicht, wie sie angeordnet ist.</summary>
    public LibraryViewMode? AppliedView => _appliedView;

    /// <summary>Verschiebung des Detailbereichs (Schublade im Regal).</summary>
    private TranslateTransform DrawerShift => OwnShift(DetailsArea);

    public void Attach(LibraryViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.RevealRequested += OnRevealRequested;
        viewModel.HdrMomentRequested += OnHdrMomentRequested;
        viewModel.CloseRequested += OnCloseRequested;

        _motion = new MotionController(viewModel.Context.Appearance);
        _motion.Attach(this);
        _motion.Changed += OnMotionChanged;
        Hero.RegisterEffects(_motion);
        _pulse = _motion.Register("CapsulePulse", this, CreatePulse, wanted: viewModel.ShowsHdrOn);
        _spin = _motion.Register("RescanSpin", this, CreateSpin, wanted: viewModel.IsScanning);
        _ring = _motion.Register("SelectionRing", this, RingClock.CreateStoryboard);
        _sheen = _motion.Register("TitleSheen", this, CreateSheen);
        _moment = new HdrMomentPlayer(Hero.MomentParts(EdgeGlow), _frameClock, _effectTime);
        _motion.SetGameRunning(viewModel.IsGameRunning);
        OnMotionChanged(this, EventArgs.Empty);

        // Gemerkte Aufteilung sofort setzen (Sternbreiten); an die tatsächliche Breite angepasst wird beim Laden.
        ApplyDetailsRatio(LibraryLayout.Normalize(viewModel.DetailsRatio));
        viewModel.SelectInitial();
        ReadTheme();
        ApplyView(viewModel.ViewMode, animate: false);
        UpdateHero(instant: true);
        ApplyHdrState(instant: true);
    }

    /// <summary>
    /// Ordnet die Werkzeugleiste für die verfügbare Breite an (Prototyp fitToolbar): Stufe 1 „Spiel hinzufügen“ nur als
    /// Symbol, 2 Ansichten ohne Beschriftung, 3 Suche schmal, 4 Quelle schmal, 5 Sortierung nur als Symbol, 6 Filter-Chips in
    /// „Filter“, 7 zwei Zeilen. Anders als im Prototyp (f5 Chips, f6 Sortierung) falten die Chips zuletzt: Bei der
    /// Standardgröße (1600 px) bleiben die drei Filter direkt schaltbar. Gemessen wird die gewünschte Breite der Leiste (passt
    /// sich an andere Sprachen an).
    /// </summary>
    public void UpdateToolbarLayout()
    {
        _toolbarQueued = false;
        var available = ToolbarArea.ActualWidth;
        if (available <= 0)
        {
            return;
        }

        var stage = 0;
        for (; stage <= MaxToolbarStage; stage++)
        {
            ApplyToolbarStage(stage);

            // Ohne Begrenzung messen; geänderte Breiten und Sichtbarkeiten innen machen die Leiste selbst nicht ungültig.
            InvalidateMeasureTree(ToolbarArea);
            ToolbarArea.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (stage == MaxToolbarStage || FilterGroup.DesiredSize.Width + ActionGroup.DesiredSize.Width <= available + 0.5)
            {
                break;
            }
        }

        _toolbarStage = Math.Min(stage, MaxToolbarStage);
        ApplyToolbarStage(_toolbarStage);
    }

    /// <summary>
    /// Gemerkte Aufteilung an die aktuelle Fenstergröße anpassen: Liste und Detailbereich behalten ihre Mindestbreite.
    /// Ohne gemerkte Aufteilung hat der Detailbereich die Breite des Prototyps (23 % der Fensterbreite, 340–396 px).
    /// </summary>
    public void FitDetailsColumn()
    {
        if (_appliedView == LibraryViewMode.Shelf)
        {
            return;
        }

        var width = ListColumn.ActualWidth + DetailsColumn.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var stored = _viewModel?.DetailsRatio;
        var ratio = stored is null
            ? LibraryLayout.Clamp(ShowcaseLayout.PaneWidth(Root.ActualWidth > 0 ? Root.ActualWidth : ActualWidth) / width, width)
            : LibraryLayout.Clamp(stored, width);
        ApplyDetailsRatio(ratio);
    }

    /// <summary>HDR-Moment ohne HDR-Schaltung (Prüfung G10.9, --test-hdr-moment).</summary>
    public HdrMomentKind PlayTestHdrMoment() => PlayMoment(transientBoost: true);

    /// <summary>Bildschirmfoto: HDR an zeigen (ohne Schaltung) und den Moment bei diesem Anteil anhalten.</summary>
    public void FreezeHdrMoment(double fraction)
    {
        if (_viewModel is not { } vm || _moment is null)
        {
            return;
        }

        vm.SetPreviewHdr(true);
        ApplyHdrState(instant: true);
        _moment.Freeze(Hero.MomentScene(_palette.ParticleColors(_scheme, vm.Context.Appearance.Current.BackgroundFollowsGame)), fraction, transientBoost: false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!e.Handled)
        {
            e.Handled = HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, Keyboard.FocusedElement);
        }
    }

    /// <summary>
    /// Tastenkürzel der Bibliothek: Strg+F Suche, Strg+1/2/3 Liste/Raster/Regal, Esc leert die Suche bzw. schließt die
    /// Schublade, auf einer Zeile oder Kachel Enter starten, Leertaste „HDR automatisch“, I Details (Regal), Pfeile im Raster
    /// und Regal. Modifikatoren und Fokus kommen als Werte (Tests: ohne angezeigtes Fenster gibt es keinen Tastaturfokus).
    /// </summary>
    /// <returns>true, wenn die Taste erledigt ist.</returns>
    public bool HandleKey(Key key, ModifierKeys modifiers, IInputElement? focused)
    {
        if (_viewModel is not { } vm)
        {
            return false;
        }

        var ctrl = modifiers == ModifierKeys.Control;
        var inSearch = ReferenceEquals(focused, SearchBox);
        if (key == Key.F && ctrl)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            return true;
        }

        if (ctrl && key is Key.D1 or Key.NumPad1 or Key.D2 or Key.NumPad2 or Key.D3 or Key.NumPad3)
        {
            // Strg+1/2/3 in der Reihenfolge der Oberfläche: Liste, Raster, Regal.
            vm.ViewMode = key is Key.D1 or Key.NumPad1 ? LibraryViewMode.List : key is Key.D2 or Key.NumPad2 ? LibraryViewMode.Grid : LibraryViewMode.Shelf;
            return true;
        }

        if (key == Key.Escape && inSearch && vm.HasSearchText)
        {
            vm.SearchText = string.Empty;
            vm.ApplySearch();
            return true;
        }

        if (key == Key.Escape && vm.IsDetailsDrawerOpen)
        {
            vm.IsDetailsDrawerOpen = false;
            return true;
        }

        if (key == Key.Enter && inSearch)
        {
            vm.ApplySearch();
            return true;
        }

        if (modifiers == ModifierKeys.None && focused is ListBoxItem item && IsGameContainer(item))
        {
            return HandleGameKey(vm, key, item);
        }

        if (key == Key.I && modifiers == ModifierKeys.None && vm.IsShelfView && focused is not TextBox)
        {
            vm.IsDetailsDrawerOpen = !vm.IsDetailsDrawerOpen;
            return true;
        }

        return false;
    }

    /// <summary>Tasten auf einer Zeile oder Kachel: Enter startet, Leertaste schaltet „HDR automatisch“, Pfeile im Raster und Regal.</summary>
    private bool HandleGameKey(LibraryViewModel vm, Key key, ListBoxItem item)
    {
        switch (key)
        {
            case Key.Enter:
                if (vm.Details?.LaunchCommand is { } launch && launch.CanExecute(null))
                {
                    launch.Execute(null);
                }

                return true;
            case Key.Space:
                if (vm.SelectedGame is { } game)
                {
                    game.HdrAuto = !game.HdrAuto;
                }

                return true;
            case Key.I when vm.IsShelfView:
                vm.IsDetailsDrawerOpen = !vm.IsDetailsDrawerOpen;
                return true;
        }

        if (ItemsControl.ItemsControlFromItemContainer(item) is not ListBox list || ReferenceEquals(list, GameList))
        {
            return false; // Liste: Pfeiltasten wie gewohnt (ListBox)
        }

        var panel = FindTilePanel(list);
        var columns = Math.Max(1, panel?.Columns ?? 1);
        var index = list.SelectedIndex;
        var count = list.Items.Count;
        var target = key switch
        {
            Key.Left => index - 1,
            Key.Right => index + 1,
            Key.Up => ReferenceEquals(list, GameShelf) ? index - 1 : index - columns,
            Key.Down => ReferenceEquals(list, GameShelf) ? index + 1 : index + columns,
            Key.Home => 0,
            Key.End => count - 1,
            Key.PageUp => index - (columns * 3),
            Key.PageDown => index + (columns * 3),
            _ => int.MinValue,
        };
        if (target == int.MinValue)
        {
            return false;
        }

        target = Math.Clamp(target, 0, Math.Max(0, count - 1));
        if (count > 0 && target != index)
        {
            list.SelectedIndex = target;
            panel?.BringIndexIntoViewPublic(target);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => (list.ItemContainerGenerator.ContainerFromIndex(target) as ListBoxItem)?.Focus());
        }

        return true;
    }

    private bool IsGameContainer(ListBoxItem item) =>
        ItemsControl.ItemsControlFromItemContainer(item) is { } owner
        && (ReferenceEquals(owner, GameList) || ReferenceEquals(owner, GameGrid) || ReferenceEquals(owner, GameShelf));

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _listScroll = GameList.Template?.FindName("ScrollHost", GameList) as ScrollViewer;
        if (_listScroll is not null)
        {
            _listScroll.ScrollChanged += OnListScrolled;
        }

        ApplyLayout(animate: false);
        FitDetailsColumn();
        if (LibraryMotion.Instance.IsFull)
        {
            // Weiches Öffnen: Bereiche nacheinander.
            LibraryMotion.FadeIn(HeaderArea, TimeSpan.Zero, offsetY: 10);
            LibraryMotion.FadeIn(HeroContent, TimeSpan.FromMilliseconds(60), offsetY: 18, durationMs: 420);
            LibraryMotion.FadeIn(ListArea, TimeSpan.FromMilliseconds(110), offsetY: 16, durationMs: 380);
            if (_appliedView != LibraryViewMode.Shelf)
            {
                LibraryMotion.FadeIn(DetailsArea, TimeSpan.FromMilliseconds(160), offsetX: 18, durationMs: 380); // im Regal: Schublade, eigene Bewegung
            }
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateToolbarLayout);
    }

    /// <summary>Fenstergröße des Prototyps (1600 × 1000), auf kleineren Bildschirmen auf 92 % der Arbeitsfläche begrenzt.</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        if (area.Width > 0 && area.Height > 0)
        {
            Width = Math.Max(MinWidth, Math.Min(Width, area.Width * 0.92));
            Height = Math.Max(MinHeight, Math.Min(Height, area.Height * 0.92));
        }
    }

    // ---------- Anordnung ----------

    /// <summary>Ansicht anordnen: Titelbereich, Spalten, Schublade, Karte, aktive Liste (nur sie hat Daten).</summary>
    private void ApplyView(LibraryViewMode view, bool animate)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        var changed = _appliedView != view;
        _appliedView = view;
        Hero.SetView(view);
        var shelf = view == LibraryViewMode.Shelf;
        HeroCompact.Visibility = shelf ? Visibility.Collapsed : Visibility.Visible;
        HeroLarge.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
        AutomationCard.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
        DrawerCloseButton.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
        if (view != LibraryViewMode.List)
        {
            _listCollapsed = false;
        }

        // Nur die aktive Ansicht hat Daten und eine Auswahl-Bindung (sonst schriebe eine leere Liste die Auswahl auf null).
        Activate(GameList, view == LibraryViewMode.List, vm);
        Activate(GameGrid, view == LibraryViewMode.Grid, vm);
        Activate(GameShelf, shelf, vm);
        ListCard.Visibility = view == LibraryViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        GameGrid.Visibility = view == LibraryViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        ShelfArea.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;

        // Spalten: im Regal nimmt die Liste die ganze Breite, der Detailbereich wird zur Schublade.
        Grid.SetColumnSpan(ListArea, shelf ? 3 : 1);
        LibrarySplitter.Visibility = shelf ? Visibility.Collapsed : Visibility.Visible;
        if (shelf)
        {
            ListColumn.MinWidth = 0;
            DetailsColumn.MinWidth = 0;
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitterColumn.Width = new GridLength(0);
            DetailsColumn.Width = new GridLength(0);
            Grid.SetColumn(DetailsArea, 0);
            Grid.SetColumnSpan(DetailsArea, 3);
            DetailsArea.SetResourceReference(Border.BackgroundProperty, "Lib.Brush.Drawer");
            DetailsArea.HorizontalAlignment = HorizontalAlignment.Right;
            Panel.SetZIndex(DrawerShade, 5);
            Panel.SetZIndex(DetailsArea, 6);
            Panel.SetZIndex(ColumnsArea, 10);
        }
        else
        {
            ListColumn.MinWidth = LibraryLayout.ListMinWidth;
            DetailsColumn.MinWidth = LibraryLayout.DetailsMinWidth;
            SplitterColumn.Width = new GridLength(24);
            Grid.SetColumn(DetailsArea, 2);
            Grid.SetColumnSpan(DetailsArea, 1);
            DetailsArea.SetResourceReference(Border.BackgroundProperty, "Hp.Brush.Panel");
            DetailsArea.HorizontalAlignment = HorizontalAlignment.Stretch;
            DetailsArea.Width = double.NaN;
            DrawerShift.BeginAnimation(TranslateTransform.XProperty, null);
            DrawerShift.X = 0;
            DetailsArea.Visibility = Visibility.Visible;
            DrawerShade.Opacity = 0;
            DrawerShade.IsHitTestVisible = false;
            Panel.SetZIndex(ColumnsArea, 0);
            ApplyDetailsRatio(_appliedDetailsRatio);
        }

        ApplyLayout(animate);
        ApplyDrawer(instant: true);
        if (changed)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                HookShelfPanel();
                FitDetailsColumn();
                UpdateToolbarLayout();
                RevealSelection(center: true);
            });
        }
    }

    /// <summary>Das Kachel-Panel des Regals entsteht erst, wenn das Regal sichtbar ist: dann „1–7 von 651“ nachführen.</summary>
    private void HookShelfPanel()
    {
        if (_shelfPanel is null && FindTilePanel(GameShelf) is { } panel)
        {
            _shelfPanel = panel;
            panel.LayoutUpdatedTiles += (_, _) => UpdateShelfPosition();
            UpdateShelfPosition();
        }
    }

    private static void Activate(ListBox list, bool active, LibraryViewModel vm)
    {
        if (active)
        {
            if (!ReferenceEquals(list.ItemsSource, vm.GamesView))
            {
                list.ItemsSource = vm.GamesView;
            }

            if (BindingOperations.GetBinding(list, Selector.SelectedItemProperty) is null)
            {
                list.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(LibraryViewModel.SelectedGame)) { Mode = BindingMode.TwoWay });
            }
        }
        else
        {
            BindingOperations.ClearBinding(list, Selector.SelectedItemProperty);
            list.ItemsSource = null;
        }
    }

    /// <summary>Waagerechte Anordnung und Höhe des Titelbereichs für die aktuelle Fenstergröße.</summary>
    private void ApplyLayout(bool animate)
    {
        if (_viewModel is null || _appliedView is not { } view)
        {
            return;
        }

        var width = Root.ActualWidth > 0 ? Root.ActualWidth : Width;
        var height = Root.ActualHeight > 0 ? Root.ActualHeight : Height;
        var compact = width < ShowcaseLayout.CompactWidth;
        HeaderStats.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        MinimumReadout.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ScaleBlock.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ViewSummary.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        KeyHints.Visibility = width < ShowcaseLayout.KeyHintsWidth ? Visibility.Collapsed : Visibility.Visible;

        var shelf = view == LibraryViewMode.Shelf;
        ColumnsArea.Margin = shelf
            ? new Thickness(0, ShowcaseLayout.HeaderTop, 0, ShowcaseLayout.PaneBottom)
            : new Thickness(ShowcaseLayout.Pad, ShowcaseLayout.PaneTop, ShowcaseLayout.Pad, ShowcaseLayout.PaneBottom);
        ToolbarArea.Margin = shelf ? new Thickness(ShowcaseLayout.Pad, 0, ShowcaseLayout.Pad, 0) : default;
        NoticeArea.Margin = shelf ? new Thickness(ShowcaseLayout.Pad, 0, ShowcaseLayout.Pad, 0) : default;
        if (shelf)
        {
            DetailsArea.Width = Math.Min(420, Math.Max(300, width - 64));
            DetailsArea.Margin = new Thickness(0, 0, ShowcaseLayout.Pad, 0);
        }
        else
        {
            DetailsArea.Margin = default;
        }

        if (_viewModel is { } vm)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            vm.HeroDecodeWidth = (int)Math.Ceiling(width * dpi.DpiScaleX);
        }

        var target = ShowcaseLayout.HeroHeight(view, height, _listCollapsed);
        var duration = animate && _motion is { } motion ? motion.Transition(HeroMotion) : TimeSpan.Zero;
        if (duration > TimeSpan.Zero && Math.Abs(target - HeroHeight) > 0.5)
        {
            BeginAnimation(HeroHeightProperty, new DoubleAnimation(target, duration) { EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 } }, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            BeginAnimation(HeroHeightProperty, null);
            HeroHeight = target;
        }

        ApplyVerticalLayout();
        ApplyListColumns();
        UpdateLight();
        QueueToolbarLayout();
    }

    /// <summary>Senkrechte Anordnung nach der (animierten) Höhe des Titelbereichs.</summary>
    private void ApplyVerticalLayout()
    {
        if (_appliedView is not { } view)
        {
            return;
        }

        var hero = HeroHeight;
        Hero.Height = hero;
        HeroContent.Height = hero;
        var shelf = view == LibraryViewMode.Shelf;
        var toolbarTop = ShowcaseLayout.ToolbarTop(view, hero);
        var columnsTop = shelf ? ShowcaseLayout.HeaderTop : ShowcaseLayout.PaneTop;
        ListArea.Margin = new Thickness(0, Math.Max(0, toolbarTop - columnsTop), 0, 0);
        LibrarySplitter.Margin = new Thickness(0, Math.Max(0, toolbarTop - columnsTop + ShowcaseLayout.ToolbarToBody), 0, 0);

        // Text im Titelbereich: Liste/Raster links vom Detailbereich, Regal links von der Automatik-Karte.
        var width = Root.ActualWidth > 0 ? Root.ActualWidth : Width;
        var paneWidth = DetailsColumn.ActualWidth > 0 ? DetailsColumn.ActualWidth : ShowcaseLayout.PaneWidth(width);
        var right = shelf ? ShowcaseLayout.Pad + 380 : ShowcaseLayout.Pad + paneWidth + 28;
        HeroContent.Margin = new Thickness(ShowcaseLayout.Pad, 0, Math.Max(0, right), 0);

        // Regal in niedrigen Fenstern: Logo bzw. Name verkleinern (zuletzt ohne Kennzeile), damit nichts in den Kopf ragt.
        var room = shelf ? ShowcaseLayout.LargeTitleRoom(hero) : double.PositiveInfinity;
        var overline = room >= 80;
        LargeOverline.Visibility = overline ? Visibility.Visible : Visibility.Collapsed;
        room += overline ? 0 : 30;
        LargeTitle.MaxHeight = room >= ShowcaseLayout.LargeTitleHeight ? double.PositiveInfinity : Math.Max(48, Math.Floor(room));
        HeroCompact.Opacity = _listCollapsed ? 0 : 1;
        HeroCompact.IsHitTestVisible = !_listCollapsed;
    }

    private void ApplyListColumns()
    {
        if (ListCard.ActualWidth > 0)
        {
            var thumb = _viewModel?.IsThumbnailIcon == true ? 32 : 64;
            GameListColumns.Apply(ListCard, ShowcaseLayout.ListColumnsFor(ListCard.ActualWidth - 2, thumb));
        }
    }

    private void QueueToolbarLayout()
    {
        if (!_toolbarQueued && IsLoaded)
        {
            _toolbarQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, UpdateToolbarLayout);
        }
    }

    private void ApplyToolbarStage(int stage)
    {
        AddGameLabel.Visibility = stage >= 1 ? Visibility.Collapsed : Visibility.Visible;
        AddGameButton.Padding = stage >= 1 ? new Thickness(0) : new Thickness(11, 0, 11, 0);
        AddGameButton.Width = stage >= 1 ? 40 : double.NaN;
        var labels = stage >= 2 ? Visibility.Collapsed : Visibility.Visible;
        ViewListLabel.Visibility = labels;
        ViewGridLabel.Visibility = labels;
        ViewShelfLabel.Visibility = labels;
        SearchHost.Width = stage >= 3 ? 150 : 236;
        SearchKey.Opacity = stage >= 3 ? 0 : 1;
        SourceFilter.Width = stage >= 4 ? 150 : 188;
        ChipGroup.Visibility = stage >= 6 ? Visibility.Collapsed : Visibility.Visible;
        FilterMenuButton.Visibility = stage >= 6 ? Visibility.Visible : Visibility.Collapsed;
        SortOrder.Width = stage >= 5 ? 60 : 196;
        SortOrder.Tag = stage >= 5 ? "Compact" : null; // nur das Zeichen der Sortierung (Lib.ComboBox)

        // Stufe 7: Ansicht und Aktionen in einer eigenen Zeile über den Filtern (rechtsbündig wie sonst).
        var stacked = stage >= 7;
        DockPanel.SetDock(ActionGroup, stacked ? Dock.Top : Dock.Right);
        ActionGroup.HorizontalAlignment = stacked ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        ActionGroup.Margin = stacked ? new Thickness(0, 0, 0, 8) : default;
        ListArea.RowDefinitions[0].Height = stacked ? GridLength.Auto : new GridLength(40);
    }

    /// <summary>Anteil des Detailbereichs als Sternbreiten: beide Spalten wachsen mit dem Fenster im selben Verhältnis.</summary>
    private void ApplyDetailsRatio(double ratio)
    {
        _appliedDetailsRatio = ratio;
        if (_appliedView == LibraryViewMode.Shelf)
        {
            return;
        }

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
        ApplyLayout(animate: false);
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
        _viewModel?.SetDetailsRatio(null);
        FitDetailsColumn();
        ApplyLayout(animate: false);
        e.Handled = true;
    }

    /// <summary>Klick auf die Kachel „Symbol“ im Detailbereich: öffnet das Menü „Symbol ändern…“ darunter (E86).</summary>
    private void OnDetailIconClick(object sender, RoutedEventArgs e) => OpenMenuBelow(sender);

    /// <summary>„···“ im Kopf des Detailbereichs: Kontextmenü des Spiels.</summary>
    private void OnProfileMenuClick(object sender, RoutedEventArgs e) => OpenMenuBelow(sender);

    /// <summary>Zusammengefaltete Filter: Menü mit den drei Filtern.</summary>
    private void OnFilterMenuClick(object sender, RoutedEventArgs e) => OpenMenuBelow(sender);

    private static void OpenMenuBelow(object sender)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } target)
        {
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    // ---------- Liste, Raster, Regal ----------

    private void OnListScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_appliedView != LibraryViewMode.List || e.VerticalChange == 0)
        {
            return;
        }

        // Beim Scrollen klappt der Titelbereich auf 120 px zusammen (mehr Zeilen); ganz oben wieder auf.
        var offset = ((ScrollViewer)sender).VerticalOffset;
        var collapse = offset > 60 || (offset > 0 && _listCollapsed);
        if (collapse != _listCollapsed)
        {
            _listCollapsed = collapse;
            ApplyLayout(animate: true);
        }
    }

    private void OnShelfPrevious(object sender, RoutedEventArgs e) => PageShelf(-1);

    private void OnShelfNext(object sender, RoutedEventArgs e) => PageShelf(1);

    private void PageShelf(int pages)
    {
        if (FindTilePanel(GameShelf) is { } panel)
        {
            panel.PageBy(pages, _motion?.Transition(TimeSpan.FromSeconds(0.7)) ?? TimeSpan.Zero);
        }
    }

    private void UpdateShelfPosition()
    {
        if (FindTilePanel(GameShelf) is not { } panel)
        {
            return;
        }

        var count = panel.ItemCount;
        var first = count == 0 ? 0 : panel.FirstVisibleIndex + 1;
        var last = Math.Min(count, panel.FirstVisibleIndex + Math.Max(1, panel.VisibleCount));
        var text = LibraryText.Format(LibraryStrings.Shelf_Position, first, count == 0 ? 0 : last, count);
        if (!string.Equals(ShelfPosition.Text, text, StringComparison.Ordinal))
        {
            ShelfPosition.Text = text;
            AutomationProperties.SetName(ShelfPosition, text);
        }

        ShelfPrevButton.IsEnabled = panel.FirstVisibleIndex > 0;
        ShelfNextButton.IsEnabled = panel.FirstVisibleIndex + panel.VisibleCount < count;
    }

    /// <summary>Glanz unter der Maus auf einer Kachel (nur Stufe „Voll“; Pinsel je Bewegung neu, die Vorlage teilt eingefrorene).</summary>
    private void OnTileMouseMove(object sender, MouseEventArgs e)
    {
        if (!LibraryMotion.Instance.IsFull || sender is not ListBoxItem item || FindNamed<Border>(item, "Shine") is not { } shine || shine.ActualWidth <= 0)
        {
            return;
        }

        var p = e.GetPosition(shine);
        var center = new Point(Math.Clamp(p.X / shine.ActualWidth, 0, 1), Math.Clamp(p.Y / shine.ActualHeight, 0, 1));
        var brush = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = 260 / Math.Max(1, shine.ActualWidth),
            RadiusY = 260 / Math.Max(1, shine.ActualHeight),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x42, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0x0D, 255, 255, 255), 0.35),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.6),
            },
        };
        brush.Freeze();
        shine.Background = brush;
    }

    private void OnRevealRequested(object? sender, GameItemViewModel item) => RevealSelection(center: true);

    /// <summary>Gewähltes Spiel in der aktiven Ansicht sichtbar machen (Liste: in die Mitte, damit der Hinweis es nicht verdeckt).</summary>
    private void RevealSelection(bool center)
    {
        if (_viewModel?.SelectedGame is not { } item)
        {
            return;
        }

        switch (_appliedView)
        {
            case LibraryViewMode.List:
                GameList.ScrollIntoView(item);
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (GameList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row)
                    {
                        if (center)
                        {
                            CenterInList(row);
                        }

                        if (IsKeyboardFocusWithin && !SearchBox.IsKeyboardFocused)
                        {
                            row.Focus();
                        }
                    }
                });
                break;
            case LibraryViewMode.Grid:
            case LibraryViewMode.Shelf:
                var list = _appliedView == LibraryViewMode.Grid ? GameGrid : GameShelf;
                var index = list.Items.IndexOf(item);
                if (index >= 0 && FindTilePanel(list) is { } panel)
                {
                    if (_appliedView == LibraryViewMode.Shelf && (index < panel.FirstVisibleIndex || index >= panel.FirstVisibleIndex + panel.VisibleCount))
                    {
                        panel.ScrollToIndex(Math.Max(0, index - (panel.VisibleCount / 2)), TimeSpan.Zero);
                    }
                    else
                    {
                        panel.BringIndexIntoViewPublic(index);
                    }
                }

                break;
        }
    }

    /// <summary>Holt die Zeile in die Mitte der Liste (ScrollIntoView legt sie an den Rand).</summary>
    private void CenterInList(ListBoxItem row)
    {
        if (_listScroll is not { } scroll || !row.IsDescendantOf(scroll))
        {
            return;
        }

        var top = row.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
        var target = scroll.VerticalOffset + top - ((scroll.ViewportHeight - row.ActualHeight) / 2);
        scroll.ScrollToVerticalOffset(Math.Clamp(target, 0, scroll.ScrollableHeight));
    }

    // ---------- ViewModel ----------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(LibraryViewModel.ViewMode):
                ApplyView(vm.ViewMode, animate: true);
                break;
            case nameof(LibraryViewModel.Hero):
                UpdateHero(instant: false);
                break;
            case nameof(LibraryViewModel.IsHdrOn):
            case nameof(LibraryViewModel.ShowsHdrOn):
                ApplyHdrState(instant: false);
                break;
            case nameof(LibraryViewModel.IsScanning):
                _spin?.SetWanted(vm.IsScanning);
                break;
            case nameof(LibraryViewModel.IsGameRunning):
                _motion?.SetGameRunning(vm.IsGameRunning);
                break;
            case nameof(LibraryViewModel.IsDetailsDrawerOpen):
                ApplyDrawer(instant: false);
                break;
            case nameof(LibraryViewModel.Details):
                if (vm.Details is not null)
                {
                    DetailsPanel.ScrollToTop();
                    if (LibraryMotion.Instance.IsFull)
                    {
                        LibraryMotion.FadeIn(DetailsContent, TimeSpan.Zero, offsetX: 14, durationMs: 260);
                    }
                }

                break;
            case nameof(LibraryViewModel.HasStatus):
                if (vm.HasStatus && LibraryMotion.Enabled)
                {
                    LibraryMotion.FadeIn(StatusToast, TimeSpan.Zero, offsetY: 16, durationMs: 300);
                }

                break;
            case nameof(LibraryViewModel.SelectedSortOption):
                // Neue Reihenfolge: oben anfangen (nach „Zuletzt gespielt“ steht dort das letzte Spiel).
                _listScroll?.ScrollToTop();
                if (FindTilePanel(GameGrid) is { } grid)
                {
                    grid.SetVerticalOffset(0);
                }

                if (FindTilePanel(GameShelf) is { } shelf)
                {
                    shelf.ScrollToIndex(0, TimeSpan.Zero);
                }

                break;
            case nameof(LibraryViewModel.IsThumbnailIcon):
                ApplyListColumns();
                break;
            case nameof(LibraryViewModel.AutomationState):
                ApplyStatusDot(vm.AutomationState);
                break;
            case nameof(LibraryViewModel.Nits):
                ApplyLayout(animate: false);
                break;
        }
    }

    /// <summary>Neues gewähltes Spiel: Titelbereich mit Überblendung, Umgebungsfarben; Bilder kommen nach (Artwork).</summary>
    private void UpdateHero(bool instant)
    {
        if (_hero is { } previous)
        {
            previous.PropertyChanged -= OnHeroPropertyChanged;
        }

        _hero = _viewModel?.Hero;
        if (_hero is { } hero)
        {
            hero.PropertyChanged += OnHeroPropertyChanged;
        }

        ApplyAmbient(instant);
        Hero.Show(_hero, _hero?.Artwork, instant ? TimeSpan.Zero : HeroTransition());
        if (!instant && _hero is not null && LibraryMotion.Instance.IsFull)
        {
            // Logo und Angaben gestaffelt nachziehen (Prototyp .hc.k1 .an).
            LibraryMotion.FadeIn(HeroCompact.Visibility == Visibility.Visible ? HeroCompact : HeroLarge, TimeSpan.FromMilliseconds(60), offsetY: 26, durationMs: 900);
        }
    }

    private void OnHeroPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HeroViewModel.Artwork) && ReferenceEquals(sender, _hero))
        {
            ApplyAmbient(instant: false);
            Hero.Show(_hero, _hero?.Artwork, HeroTransition());
        }
    }

    private TimeSpan HeroTransition() => _motion?.Level switch
    {
        EffectLevel.Full => HeroStage.CrossfadeDuration,
        EffectLevel.Reduced => EffectPolicy.ReducedTransition,
        _ => TimeSpan.Zero,
    };

    /// <summary>Umgebungsfarben und alles, was davon abhängt (Fenstergrund, Abdunklung, Auswahl, Karte, Kante).</summary>
    private void ApplyAmbient(bool instant)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        var appearance = vm.Context.Appearance.Current;
        _scheme = appearance.Palette;
        var follow = appearance.BackgroundFollowsGame;
        var artwork = _hero?.Artwork;
        var ambient = _hero is null ? null : artwork?.Ambient ?? _hero.Ambient;
        _palette = AmbientPalette.Resolve(follow ? ambient : null, _scheme, follow, _dark);
        var transition = instant || _motion is null ? TimeSpan.Zero : _motion.Transition(AmbientBackdrop.ColorDuration);
        Ambient.Apply(_palette, follow ? artwork?.Field?.Image : null, _dark, transition);
        Hero.SetPalette(_palette, _scheme, _dark);
        ApplyWindowBrushes();
        UpdateLight();
    }

    /// <summary>Pinsel, die von Thema und Spielfarbe abhängen, in den Ressourcen des Fensters ersetzen.</summary>
    private void ApplyWindowBrushes()
    {
        var p = _palette;
        var bg = p.Background;
        Resources["Lib.Brush.RowSelection"] = _dark
            ? Frozen(new LinearGradientBrush(
                [new GradientStop(AmbientPalette.Alpha(p.A2, 0.30), 0), new GradientStop(AmbientPalette.Alpha(p.A2, 0.08), 0.6), new GradientStop(AmbientPalette.Alpha(p.A2, 0), 1)],
                new Point(0, 0.5),
                new Point(1, 0.5)))
            : Frozen(new SolidColorBrush(ColorMath.WithAlpha(_scheme.Accent, 0.14)));
        Resources["Lib.Brush.ListCard"] = Frozen(new SolidColorBrush(_dark ? AmbientPalette.Alpha(bg, 0.55) : Color.FromArgb(0x9E, 255, 255, 255)));

        // Schublade im Regal: fast deckend (WPF hat keinen Weichzeichner für den Hintergrund wie der Prototyp).
        Resources["Lib.Brush.Drawer"] = Frozen(new SolidColorBrush(_dark
            ? AmbientPalette.Alpha(ColorMath.MixOklab(bg, Color.FromRgb(0x0B, 0x0C, 0x12), 0.55), 0.97)
            : Color.FromArgb(0xF7, 0xF8, 0xF9, 0xFC)));
        Resources["Lib.Brush.PaneGlow"] = Frozen(new RadialGradientBrush(
            [new GradientStop(AmbientPalette.Alpha(p.A2, _dark ? 0.22 : 0.10), 0), new GradientStop(AmbientPalette.Alpha(p.A2, 0), 1)])
        {
            Center = new Point(0, 0),
            GradientOrigin = new Point(0, 0),
            RadiusX = 1.1,
            RadiusY = 0.4,
        });
        Resources["Lib.Brush.ShelfFadeLeft"] = Frozen(new LinearGradientBrush(bg, AmbientPalette.Alpha(bg, 0), 0));
        Resources["Lib.Brush.ShelfFadeRight"] = Frozen(new LinearGradientBrush(AmbientPalette.Alpha(bg, 0), bg, 0));
        Resources["Lib.Brush.HeroTitle"] = Frozen(new LinearGradientBrush(
            [new GradientStop(Colors.White, 0.3), new GradientStop(ColorMath.Mix(p.A3, Colors.White, 0.55), 1)],
            new Point(0.5, 0),
            new Point(0.5, 1)));
        ApplySheenBrush();
        GameListColumns.SetLightTheme(ListCard, !_dark);
        if (_dark)
        {
            Resources.Remove("Lib.Brush.Card");
            Resources.Remove("Lib.Brush.CardLine");
            Resources.Remove("Lib.Brush.CardText");
            Resources.Remove("Lib.Brush.CardSecondary");
            Resources.Remove("Lib.Brush.CardMuted");
        }
        else
        {
            Resources["Lib.Brush.Card"] = Frozen(new LinearGradientBrush(Color.FromArgb(0xE6, 255, 255, 255), Color.FromArgb(0xEB, 0xF6, 0xF7, 0xFA), 45));
            Resources["Lib.Brush.CardLine"] = Frozen(new SolidColorBrush(Color.FromArgb(0x1A, 0x11, 0x13, 0x20)));
            Resources["Lib.Brush.CardText"] = Frozen(new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x1A)));
            Resources["Lib.Brush.CardSecondary"] = Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0x52, 0x68)));
            Resources["Lib.Brush.CardMuted"] = Frozen(new SolidColorBrush(Color.FromRgb(0x66, 0x6C, 0x80)));
        }

        // Fensterkante: Puls in den Spielfarben (HDR-Moment), feine Linie in der Leuchtfarbe bei HDR an.
        EdgeGlow.BorderBrush = Frozen(new LinearGradientBrush(
            [new GradientStop(p.A3, 0), new GradientStop(p.A2, 0.45), new GradientStop(_scheme.Accent, 0.75), new GradientStop(p.A3, 1)],
            new Point(0, 0),
            new Point(1, 1)));
        ApplyEdgeLine();
    }

    /// <summary>
    /// Glanz über dem Namen (Prototyp .tt::after): schmaler heller Streifen in der hellen Spielfarbe, den der Dauer-Effekt
    /// „TitleSheen“ verschiebt (<see cref="SheenMask"/>).
    /// </summary>
    private void ApplySheenBrush() =>
        Resources["Lib.Brush.HeroSheen"] = Frozen(new SolidColorBrush(ColorMath.MixOklab(_palette.A3, Colors.White, 0.55)));

    private void ApplyEdgeLine() =>
        EdgeLine.BorderBrush = _viewModel?.ShowsHdrOn == true
            ? Frozen(new SolidColorBrush(AmbientPalette.Alpha(_palette.A2, 0.55)))
            : (Brush)FindResource(ThemeKeys.BrushLine);

    /// <summary>Spitzlicht des Titelbilds, links vom Detailbereich bzw. der Karte und unter der Kopfzeile geklemmt.</summary>
    private void UpdateLight()
    {
        if (Hero.ActualWidth <= 0)
        {
            return;
        }

        var blocker = _appliedView == LibraryViewMode.Shelf ? AutomationCard : (FrameworkElement)DetailsArea;
        var left = blocker.IsVisible || blocker.ActualWidth > 0
            ? blocker.TranslatePoint(default, Hero).X
            : double.NaN;
        var (x, y) = ShowcaseLayout.LightPosition(Hero.ActualWidth, Hero.ActualHeight, _palette.LightX, _palette.LightY, left);
        Hero.SetLight(x, y, _palette.Highlight);
    }

    /// <summary>HDR an oder aus (oder nur angezeigt): Titelbild hell, Kapsel leuchtet, Logo-Kachel im Verlauf, Puls an.</summary>
    private void ApplyHdrState(bool instant)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        var on = vm.ShowsHdrOn;
        var transition = instant || _motion is null ? TimeSpan.Zero : _motion.Transition(TimeSpan.FromSeconds(1.2));
        Hero.SetHdr(on, transition);
        _pulse?.SetWanted(on);
        CapsuleDot.Fill = on ? (Brush)FindResource(ThemeKeys.BrushShine) : Frozen(new SolidColorBrush(Color.FromRgb(0x7A, 0x7F, 0x94)));
        CapsuleRing.Opacity = on ? 1 : 0;
        CapsuleHalo.Opacity = on ? 0.6 : 0;
        CapsulePulse.Opacity = 0;
        BrandTileOn.Opacity = on ? 1 : 0;
        BrandFill.Opacity = on ? 1 : 0;
        BrandGlyph.Stroke = on ? (Brush)FindResource(ThemeKeys.BrushTextOnAccent) : (Brush)FindResource(ThemeKeys.BrushAccentText);
        var valueBrush = on ? Brushes.White : Frozen(new SolidColorBrush(Color.FromRgb(0xAE, 0xB3, 0xC6)));
        PeakValue.Foreground = valueBrush;
        FullFrameValue.Foreground = valueBrush;
        MinimumValue.Foreground = valueBrush;
        NitsScale.Fill = on ? (Brush)FindResource(ThemeKeys.BrushAccentShine) : Frozen(new SolidColorBrush(Color.FromRgb(0x6E, 0x73, 0x88)));
        ApplyEdgeLine();
    }

    private void ApplyStatusDot(AutomationState state) =>
        StatusDot.Fill = state switch
        {
            AutomationState.Paused => (Brush)FindResource("Lib.Brush.Amber"),
            AutomationState.Running => (Brush)FindResource(ThemeKeys.BrushShine),
            _ => (Brush)FindResource("Lib.Brush.Ok"),
        };

    /// <summary>Regal: Schublade ein- bzw. ausfahren (0,55 s nach Effektstufe), Karte aus, Abdunklung an.</summary>
    private void ApplyDrawer(bool instant)
    {
        if (_viewModel is not { } vm || _appliedView != LibraryViewMode.Shelf)
        {
            return;
        }

        var open = vm.IsDetailsDrawerOpen;
        var shift = DrawerShift;
        var duration = instant || _motion is null ? TimeSpan.Zero : _motion.Transition(DrawerMotion);
        var hidden = DetailsArea.Width + 60;
        DetailsArea.Visibility = Visibility.Visible;
        DrawerShade.IsHitTestVisible = open;
        AutomationCard.IsHitTestVisible = !open;
        if (duration <= TimeSpan.Zero)
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.X = open ? 0 : hidden;
            DrawerShade.BeginAnimation(OpacityProperty, null);
            DrawerShade.Opacity = open ? 1 : 0;
            AutomationCard.BeginAnimation(OpacityProperty, null);
            AutomationCard.Opacity = open ? 0 : 1;
            DetailsArea.Visibility = open ? Visibility.Visible : Visibility.Hidden;
        }
        else
        {
            var slide = new DoubleAnimation(open ? 0 : hidden, duration) { EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 } };
            if (!open)
            {
                slide.Completed += (_, _) =>
                {
                    if (_viewModel is { IsDetailsDrawerOpen: false } && _appliedView == LibraryViewMode.Shelf)
                    {
                        DetailsArea.Visibility = Visibility.Hidden;
                    }
                };
            }

            shift.BeginAnimation(TranslateTransform.XProperty, slide);
            DrawerShade.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, duration));
            AutomationCard.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, duration));
        }

        if (open)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => DrawerCloseButton.Focus());
        }
    }

    private void OnDrawerShadeClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is { IsDetailsDrawerOpen: true } vm)
        {
            vm.IsDetailsDrawerOpen = false;
            e.Handled = true;
        }
    }

    private void OnDrawerClose(object sender, RoutedEventArgs e)
    {
        if (_viewModel is { } vm)
        {
            vm.IsDetailsDrawerOpen = false;
        }
    }

    // ---------- Effekte ----------

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        if (_motion is not { } motion || _viewModel is not { } vm)
        {
            return;
        }

        LibraryMotion.Instance.SetLevel(motion.Level);
        vm.SetEffectsState(motion.Level, motion.Pause, vm.Context.Appearance.Current.ReducedByWindows);
        EffectsGlyph.Foreground = vm.EffectsPaused ? (Brush)FindResource("Lib.Brush.Amber") : (Brush)FindResource(ThemeKeys.BrushAccentText);
        if (!motion.OneShotAllowed)
        {
            _moment?.Stop();
        }

        if (!motion.ContinuousAllowed)
        {
            ResetPointer();
        }
    }

    private void OnHdrMomentRequested(object? sender, EventArgs e) => PlayMoment(transientBoost: false);

    private HdrMomentKind PlayMoment(bool transientBoost)
    {
        if (_motion is not { } motion || _moment is null || _viewModel is not { } vm)
        {
            return HdrMomentKind.None;
        }

        UpdateLight();
        var scene = Hero.MomentScene(_palette.ParticleColors(_scheme, vm.Context.Appearance.Current.BackgroundFollowsGame));
        var kind = _moment.Play(motion.Level, motion.Context, scene, transientBoost);
        if (kind == HdrMomentKind.Full)
        {
            PlayButtonBurst();
        }

        return kind;
    }

    /// <summary>Kleiner Funkenkranz am HDR-Schalter (Prototyp .bu, 0,9 s, nur bei „Voll“).</summary>
    private void PlayButtonBurst()
    {
        HdrBurst.Children.Clear();
        var board = new Storyboard();
        Timeline.SetDesiredFrameRate(board, EffectPolicy.OneShotFrameRate);
        for (var i = 0; i < 12; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(i % 2 == 1 ? _scheme.Accent : _scheme.Shine),
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Canvas.SetLeft(dot, -3.5);
            Canvas.SetTop(dot, -3.5);
            var angle = new RotateTransform(i * 30);
            var reach = new TranslateTransform(4, 0);
            var group = new TransformGroup();
            group.Children.Add(reach);
            group.Children.Add(angle);
            dot.RenderTransform = group;
            HdrBurst.Children.Add(dot);
            var length = i % 2 == 1 ? 84 + ((i % 3) * 4) : 64 + ((i % 4) * 3);
            var duration = TimeSpan.FromSeconds(0.9);
            var fly = new DoubleAnimation(4, length, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fly, reach);
            Storyboard.SetTargetProperty(fly, new PropertyPath(TranslateTransform.XProperty));
            var fade = new DoubleAnimation(1, 0, duration);
            Storyboard.SetTarget(fade, dot);
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            board.Children.Add(fly);
            board.Children.Add(fade);
        }

        board.Completed += (_, _) => HdrBurst.Children.Clear();
        board.Begin(HdrBurst);
    }

    /// <summary>Tiefenwirkung: Zeiger über dem Titelbild verschiebt die Ebenen (nur Dauer-Effekte erlaubt, gedrosselt auf 30 Hz).</summary>
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (_motion is not { ContinuousAllowed: true } || Hero.ActualWidth <= 0)
        {
            return;
        }

        var p = e.GetPosition(Hero);
        if (p.Y < 0 || p.Y > Hero.ActualHeight)
        {
            ResetPointer();
            return;
        }

        var tick = Environment.TickCount64;
        if (tick - _pointerTick < 33)
        {
            return;
        }

        _pointerTick = tick;
        _pointerActive = true;
        var px = ((p.X / Hero.ActualWidth) - 0.5) * 2;
        var py = ((p.Y / Hero.ActualHeight) - 0.5) * 2;
        Hero.SetPointer(px, py, PointerDuration);
        var shift = OwnShift(HeroContent);
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(7 * Math.Clamp(px, -1, 1), PointerDuration) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4 * Math.Clamp(py, -1, 1), PointerDuration) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
    }

    private void ResetPointer()
    {
        if (!_pointerActive)
        {
            return;
        }

        _pointerActive = false;
        var duration = _motion?.ContinuousAllowed == true ? PointerDuration : TimeSpan.Zero;
        Hero.SetPointer(0, 0, duration);
        var shift = OwnShift(HeroContent);
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        shift.X = 0;
        shift.Y = 0;
    }

    private Storyboard CreatePulse()
    {
        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var duration = TimeSpan.FromSeconds(2.2);
        var scale = (ScaleTransform)CapsulePulse.RenderTransform;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        AddTo(board, new DoubleAnimation(1, 3.2, duration) { EasingFunction = ease }, scale, ScaleTransform.ScaleXProperty);
        AddTo(board, new DoubleAnimation(1, 3.2, duration) { EasingFunction = ease }, scale, ScaleTransform.ScaleYProperty);
        AddTo(board, new DoubleAnimation(0.9, 0, duration), CapsulePulse, OpacityProperty);
        return board;
    }

    private Storyboard CreateSpin()
    {
        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        AddTo(board, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)), (RotateTransform)RescanGlyph.RenderTransform, RotateTransform.AngleProperty);
        return board;
    }

    /// <summary>Glanz über dem Namen: alle 7 s einmal quer (45 % der Zeit), sonst steht er außerhalb.</summary>
    private Storyboard CreateSheen()
    {
        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var move = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(7), BeginTime = TimeSpan.FromSeconds(1.5) };
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.2, KeyTime.FromPercent(0)));
        move.KeyFrames.Add(new EasingDoubleKeyFrame(-0.3, KeyTime.FromPercent(0.45), new SineEase { EasingMode = EasingMode.EaseInOut }));
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(-0.3, KeyTime.FromPercent(1)));
        AddTo(board, move, _sheenShift, TranslateTransform.XProperty);
        return board;
    }

    private static void AddTo(Storyboard board, Timeline animation, DependencyObject target, DependencyProperty property)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        board.Children.Add(animation);
    }

    // ---------- Thema, Schließen ----------

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ReadTheme();
        ApplyAmbient(instant: true);
        ApplyHdrState(instant: true);
    }

    /// <summary>Dunkel oder hell nach den aktiven Farb-Ressourcen (heller Text heißt dunkles Thema; gilt auch im Testhost).</summary>
    private void ReadTheme()
    {
        _dark = TryFindResource(ThemeKeys.BrushText) is not SolidColorBrush text || ColorMath.RelativeLuminance(text.Color) > 0.5;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Dispatcher.BeginInvoke(DispatcherPriority.Background, Close);

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _moment?.Dispose();
        _motion?.Dispose();
        _moment = null;
        _motion = null;
        if (_listScroll is not null)
        {
            _listScroll.ScrollChanged -= OnListScrolled;
        }

        if (_hero is not null)
        {
            _hero.PropertyChanged -= OnHeroPropertyChanged;
            _hero = null;
        }

        if (_viewModel is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.RevealRequested -= OnRevealRequested;
            vm.HdrMomentRequested -= OnHdrMomentRequested;
            vm.CloseRequested -= OnCloseRequested;
            vm.Dispose();
        }

        // Bilder freigeben: keine Bitmap bleibt am geschlossenen Fenster hängen.
        Hero.Show(null, null, TimeSpan.Zero);
        Ambient.Apply(_palette, null, _dark, TimeSpan.Zero);
        GameList.ItemsSource = null;
        GameGrid.ItemsSource = null;
        GameShelf.ItemsSource = null;
        _viewModel = null;
        DataContext = null;
    }

    // ---------- Hilfen ----------

    /// <summary>Eigene Verschiebung eines Elements (aus dem XAML); entsteht neu, falls jemand sie ersetzt hat.</summary>
    private static TranslateTransform OwnShift(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } shift)
        {
            return shift;
        }

        var created = new TranslateTransform();
        element.RenderTransform = created;
        return created;
    }

    private static void InvalidateMeasureTree(DependencyObject root)
    {
        if (root is UIElement element)
        {
            element.InvalidateMeasure();
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            InvalidateMeasureTree(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>Kachel-Panel einer Liste (null, solange keine Vorlage angewandt ist).</summary>
    public static VirtualizingTilePanel? FindTilePanel(ListBox list) => FindVisual<VirtualizingTilePanel>(list);

    private static T? FindVisual<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindVisual<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindNamed<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (FindNamed<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
