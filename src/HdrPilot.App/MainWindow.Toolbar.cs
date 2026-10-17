using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Resources;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;

namespace HdrPilot.App;

/// <summary>Lage der Teile über der Bildkante, in Koordinaten von ColumnsArea (ein Rechenschritt je Anordnung).</summary>
/// <param name="Left">Linke Kante der Leiste.</param>
/// <param name="Width">Breite der Leiste (= Liste).</param>
/// <param name="CapsuleTop">Oberkante der Kapsel (24 hoch); die Lasche liegt <paramref name="TabTy"/> darunter (negativ: darüber).</param>
/// <param name="SeamY">Linie von Lichtnaht und Lichtstreif (Bildkante, im Regal Mitte der Kapsel).</param>
/// <param name="GripCenter">Mitte des Ziehgriffs (NaN ohne Griff: Raster, Regal).</param>
/// <param name="CapsuleMax">Breiteste Kapsel (höchstens bis 32 px vor die Mitte des Griffs, im Regal neben dem Titelinhalt).</param>
internal readonly record struct ToolbarOverlayGeometry(double Left, double Width, double CapsuleTop, double TabTy, double SeamY, double GripCenter, double CapsuleMax)
{
    public double Right => Left + Width;
}

/// <summary>
/// Werkzeugleiste „Fließleiste mit Lichtnaht“ im Hauptfenster (abgenommene Vorschau leiste.html, Wunsch des Nutzers
/// 2026-10-17): Anordnung (ToolbarPanel) mit Bewegung nur der Schritte beim Ziehen, Ein- und Ausklappen über Lasche bzw.
/// Kapsel mit Lichtnaht, Tastenkürzel, Menü, Ansage für Bildschirmleser und Merken (AppSettings.LibraryToolbarCollapsed).
/// Alle Bewegungen laufen über <see cref="MotionChannels"/> (ein Bildtakt nur, solange etwas läuft) und werden hier
/// angewandt: Deckkraft und RenderTransform, dazu Breite und Höhe der einen Lasche/Kapsel. Minimiert, bei laufendem Spiel
/// und auf der Stufe „Aus“ gilt sofort der Endzustand.
/// </summary>
public partial class MainWindow
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    private MotionChannels _toolbarMotion = null!;
    private ToolbarChoreography _choreography = null!;
    private ToolbarOverlayGeometry _overlay;
    private bool _shownCollapsed;
    private bool _expandFromSearch;
    private bool _windowSizing;
    private bool _columnSizing;
    private bool _capsuleFiltered;
    private bool _capsuleReset;
    private double _capsuleWidth = ToolbarChoreography.TabWidth;
    private int _capsuleItems;
    private TextBlock? _sortLabel;

    /// <summary>Zeilen der Werkzeugleiste, wie zuletzt angeordnet (Tests, Prüfung).</summary>
    public int ToolbarRows => ToolbarArea.Rows;

    /// <summary>Werkzeugleiste eingeklappt, wie gerade gezeigt (Tests, Prüfung).</summary>
    public bool IsToolbarCollapsed => _shownCollapsed;

    /// <summary>Kanäle der Bewegung der Werkzeugleiste (Tests: Werte über die Zeit, laufende Bewegungen).</summary>
    public MotionChannels ToolbarMotion => _toolbarMotion;

    /// <summary>Der Nutzer zieht den Fensterrand oder den Trenner zum Detailbereich (Hysterese, Schritte werden bewegt).</summary>
    public bool IsToolbarResizing => ToolbarArea.IsResizing;

    /// <summary>Ziehen beginnt bzw. endet (Fensterrand per WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE, Trenner, Tests).</summary>
    public void SetToolbarResizing(bool resizing)
    {
        _windowSizing = resizing;
        UpdateResizing();
    }

    /// <summary>
    /// Bildschirmfoto: Tooltip eines Elements der Werkzeugleiste (nach AutomationId), wie er mittig 8 px darunter aufginge.
    /// Ein Tooltip gehört in ein Popup; er wird deshalb ohne Eltern angeordnet und über das Fenster gezeichnet.
    /// </summary>
    public SnapshotOverlay? CreateToolTipPreview(string automationId)
    {
        var target = ToolbarArea.Children.OfType<FrameworkElement>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == automationId);
        if (target?.ToolTip is not ToolTip tip)
        {
            return null;
        }

        target.ToolTip = null; // gehört sonst noch zum Element
        tip.PlacementTarget = target;
        tip.DataContext = DataContext;
        tip.ApplyTemplate();
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = tip.DesiredSize;
        var at = target.TranslatePoint(new Point(target.ActualWidth / 2, target.ActualHeight), Root);
        var x = Math.Clamp(at.X - (size.Width / 2), 8, Math.Max(8, Root.ActualWidth - size.Width - 8));
        return new SnapshotOverlay(tip, new Point(Math.Round(x), Math.Round(at.Y + ToolbarTipPlacement.Gap - ToolbarTipPlacement.ShadowTop)));
    }

    /// <summary>Bildschirmfoto: Menü der Werkzeugleiste wie nach Umschalt+F10 auf der Lasche (rechtsbündig darunter).</summary>
    public SnapshotOverlay? CreateToolbarMenuPreview()
    {
        if (TryFindResource("ToolbarMenu") is not ContextMenu menu)
        {
            return null;
        }

        menu.PlacementTarget = ToolbarToggle;
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = menu.DesiredSize;
        var at = ToolbarToggle.TranslatePoint(new Point(ToolbarToggle.ActualWidth, ToolbarToggle.ActualHeight + 6), Root);
        return new SnapshotOverlay(menu, new Point(Math.Round(Math.Max(8, at.X - size.Width + 12)), Math.Round(at.Y - 4)));
    }

    /// <summary>
    /// Bildschirmfoto: Ein- bzw. Ausklappen an einer Stelle angehalten (wie #t= der Vorschau), mit gestellter Uhr. Danach steht
    /// die Leiste bis zum Schließen still (kein Bildtakt).
    /// </summary>
    public void FreezeToolbarMotion(bool collapse, double milliseconds)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        var time = new FrozenTime();
        _toolbarMotion.UseTime(new FrozenClock(), time);
        if (vm.IsToolbarCollapsed == collapse)
        {
            vm.SetToolbarCollapsed(!collapse, persist: false);
            _toolbarMotion.Finish();
        }

        vm.SetToolbarCollapsed(collapse, persist: false);
        time.Now = TimeSpan.FromMilliseconds(milliseconds);
        _toolbarMotion.Tick();
    }

    /// <summary>Bildschirmfoto: Lasche beim Überfahren (Hinweis „Leiste ausblenden · Strg+Umschalt+F“), ohne Bewegung.</summary>
    public void PreviewTabHover()
    {
        if (!_shownCollapsed)
        {
            _choreography.Hover(true, ToolbarToggle.HoverWidth, ToolbarMotionLevel.Instant);
            _toolbarMotion.Apply();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    /// <summary>Teile der Werkzeugleiste verbinden (Konstruktor, nach InitializeComponent).</summary>
    private void InitializeToolbar()
    {
        _toolbarMotion = new MotionChannels(_frameClock, _effectTime);
        _choreography = new ToolbarChoreography(_toolbarMotion);
        _toolbarMotion.Updated += (_, _) => ApplyToolbarMotion();
        _toolbarMotion.Finished += (_, _) => OnToolbarMotionFinished();
        _choreography.TabGhostRequested += (_, _) => CaptureGhost(ToolbarToggle, TabGhost);
        _choreography.ToolbarGhostRequested += (_, _) => CaptureGhost(ToolbarArea, ToolbarGhost);
        ToolbarArea.Arranged += OnToolbarArranged;
        ListArea.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                RefreshToolbarOverlay();
            }
        };
        ToolbarToggle.ToggleRequested += (_, _) => _viewModel?.ToggleToolbarCommand.Execute(null);
        ToolbarToggle.HoverChanged += OnToolbarTabHover;
        ToolbarToggle.ContextMenuOpening += OnToolbarMenuOpening;
        LibrarySplitter.DragStarted += (_, _) =>
        {
            _columnSizing = true;
            UpdateResizing();
        };
        LibrarySplitter.DragCompleted += (_, _) =>
        {
            _columnSizing = false;
            UpdateResizing();
        };

        // Jedes Element bekommt eine eigene Verschiebung und Skalierung (Welle beim Ein- und Ausklappen, Zeilenwechsel) und
        // die Tooltips der Leiste: nach 1 s, der des Nachbarn innerhalb von 400 ms sofort, stehen bleibend.
        foreach (FrameworkElement item in ToolbarArea.Children)
        {
            item.RenderTransformOrigin = new Point(0.5, 0.5);
            item.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
            SetTipTiming(item);
        }

        foreach (var radio in new FrameworkElement[] { ViewListButton, ViewGridButton, ViewShelfButton })
        {
            SetTipTiming(radio);
        }

        ToolbarArea.RenderTransform = new TranslateTransform();
        ToolbarToggle.SetHighContrast(SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        ApplyToolbarBrushes();
    }

    private static void SetTipTiming(FrameworkElement element)
    {
        ToolTipService.SetInitialShowDelay(element, 1000);
        ToolTipService.SetBetweenShowDelay(element, 400);
        ToolTipService.SetShowDuration(element, 600000);
        ToolTipService.SetShowOnDisabled(element, true);
    }

    /// <summary>Zustand aus dem ViewModel ohne Bewegung herstellen (Öffnen des Fensters).</summary>
    private void AttachToolbar(LibraryViewModel vm)
    {
        _shownCollapsed = vm.IsToolbarCollapsed;
        ApplyToolbarChrome();
        UpdateCapsule(ToolbarMotionLevel.Instant);
        _choreography.Settle(_shownCollapsed, TabTarget(), LargeSink(_shownCollapsed));
        if (_shownCollapsed)
        {
            ToolbarArea.Visibility = Visibility.Collapsed;
        }

        _toolbarMotion.Apply();
    }

    private void DetachToolbar()
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        _toolbarMotion.Finish();
    }

    /// <summary>Wirksame Bewegung: Effektstufe, aber sofort, wenn das Fenster minimiert oder unsichtbar ist oder ein Spiel läuft.</summary>
    private ToolbarMotionLevel ToolbarLevel() =>
        _motion is not { OneShotAllowed: true } motion || !LibraryMotion.HardwareRendering
            ? ToolbarMotionLevel.Instant
            : motion.Level switch
            {
                EffectLevel.Full => ToolbarMotionLevel.Full,
                EffectLevel.Reduced => ToolbarMotionLevel.Reduced,
                _ => ToolbarMotionLevel.Instant,
            };

    // ---------- Ein- und Ausklappen ----------

    /// <summary>
    /// Zustand des ViewModels zeigen (Lasche, Kapsel, Strg+Umschalt+F, Menü, Strg+F): Endlayout sofort, die Liste wird nur
    /// verschoben (FLIP), Bewegung nach Effektstufe; liegt der Fokus beim Einklappen in der Leiste, geht er auf die Kapsel.
    /// Eine Ansage meldet den neuen Zustand.
    /// </summary>
    private void ApplyToolbarState()
    {
        if (_viewModel is not { } vm || vm.IsToolbarCollapsed == _shownCollapsed || _appliedView is not { } view)
        {
            return;
        }

        var collapsed = vm.IsToolbarCollapsed;
        var fromSearch = _expandFromSearch;
        _expandFromSearch = false;
        var level = ToolbarLevel();
        var hero = HeroHeight;
        var toolbar = ToolbarHeight();
        var before = ShowcaseLayout.BodyTop(view, hero, toolbar, _shownCollapsed) + _toolbarMotion.Get(ToolbarChoreography.ListDy);
        var hadFocus = ToolbarArea.IsKeyboardFocusWithin;
        _shownCollapsed = collapsed;
        ApplyToolbarChrome();
        ApplyVerticalLayout();
        UpdateCapsule(ToolbarMotionLevel.Instant, move: false); // nur Inhalt und Breite: die Lasche bewegt gleich die Choreografie
        var dy = before - ShowcaseLayout.BodyTop(view, hero, toolbar, collapsed);
        var order = WaveOrder();
        if (collapsed)
        {
            _choreography.Collapse(level, dy, order, TabTarget(), LargeSink(true));
            if (hadFocus)
            {
                ToolbarToggle.Focus();
            }
        }
        else
        {
            _choreography.Expand(level, dy, order, fromSearch, TabTarget());
        }

        if (level == ToolbarMotionLevel.Instant)
        {
            _toolbarMotion.Finish();
        }
        else
        {
            _toolbarMotion.Apply();
        }

        Announce(collapsed ? LibraryStrings.Toolbar_Hidden : LibraryStrings.Toolbar_Shown);
    }

    /// <summary>
    /// Was nicht bewegt wird: Lasche oder Kapsel, ob die Leiste im Layout Platz hat (im Regal immer: die Kachelreihe bleibt),
    /// ob sie Fokus und Maus annimmt (eingeklappt nie, auch während sie noch ausblendet). Ausgeklappt ist sie sofort da.
    /// </summary>
    private void ApplyToolbarChrome()
    {
        var shelf = _appliedView == LibraryViewMode.Shelf;
        ToolbarToggle.IsExpanded = !_shownCollapsed;
        ToolbarArea.ReservesHeight = !_shownCollapsed || shelf;
        ToolbarArea.IsHitTestVisible = !_shownCollapsed;
        if (_shownCollapsed)
        {
            KeyboardNavigation.SetTabNavigation(ToolbarArea, KeyboardNavigationMode.None);
        }
        else
        {
            ToolbarArea.ClearValue(KeyboardNavigation.TabNavigationProperty);
            ToolbarArea.Visibility = Visibility.Visible;
        }

        ToolTipService.SetIsEnabled(ToolbarToggle, _shownCollapsed);
    }

    /// <summary>Nach der letzten Bewegung: eingeklappt ist die Leiste nicht mehr da (kein Tab-Halt, nicht im Automationsbaum).</summary>
    private void OnToolbarMotionFinished()
    {
        if (_shownCollapsed)
        {
            ToolbarArea.Visibility = Visibility.Collapsed;
        }

        ToolbarArea.Clip = null;
        BodyArea.Clip = null;
        TabGhost.Source = null;
        ToolbarGhost.Source = null;
    }

    /// <summary>Ansage für Bildschirmleser über die Lasche/Kapsel (einmalig, wichtig, die jüngste gilt).</summary>
    private void Announce(string text)
    {
        var peer = UIElementAutomationPeer.FromElement(ToolbarToggle) ?? UIElementAutomationPeer.CreatePeerForElement(ToolbarToggle);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, text, "ToolbarToggled");
    }

    /// <summary>Elemente von links nach rechts (in zwei Zeilen nach ihrer Lage): Die Welle läuft zur Lasche.</summary>
    private List<ToolbarSlot> WaveOrder()
    {
        var arrangement = ToolbarArea.Arrangement ?? ToolbarArea.ArrangeFor(ToolbarWidth());
        return [.. Enum.GetValues<ToolbarSlot>().OrderBy(s => arrangement[s].X).ThenBy(s => arrangement[s].Y)];
    }

    /// <summary>Regal: Knöpfe und Logo sinken mit eingeklappter Leiste (<see cref="ShowcaseLayout.LargeHeroSink"/>), als Verschiebung.</summary>
    private double LargeSink(bool collapsed) => collapsed && _appliedView == LibraryViewMode.Shelf ? ShowcaseLayout.LargeHeroSink : 0;

    private TabTarget TabTarget() => new(_overlay.TabTy, _capsuleWidth, _capsuleItems, _capsuleReset, _capsuleFiltered);

    // ---------- Anordnung beim Ziehen ----------

    private void UpdateResizing() => ToolbarArea.IsResizing = _windowSizing || _columnSizing;

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmEnterSizeMove)
        {
            SetToolbarResizing(true);
        }
        else if (msg == WmExitSizeMove)
        {
            SetToolbarResizing(false);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Neue Anordnung der Leiste (im selben Layout-Durchgang wie die neue Breite): Nur beim Ziehen werden die Schritte
    /// bewegt (Zeilenwechsel als Morph bzw. Überblenden, Sortier-Schritt, Tastenkappe), sonst gilt sofort der Endzustand.
    /// Die Elemente stehen schon an ihrem neuen Platz; die Kanäle führen sie vom alten dorthin (FLIP).
    /// </summary>
    private void OnToolbarArranged(object? sender, ToolbarArrangedEventArgs e)
    {
        var current = e.Current;
        var level = ToolbarLevel();
        var animate = ToolbarArea.IsResizing && e.Previous is not null && !_shownCollapsed && level != ToolbarMotionLevel.Instant;
        ToolbarPanel.SetIconOnly(SortOrder, current.SortIconOnly);
        SortTip.Text = current.SortIconOnly && _viewModel is { } vm ? vm.SortTipCompactText : LibraryStrings.Tip_SortText;
        if (e.Previous is { } previous && animate && (e.RowsChanged || e.SortStepChanged))
        {
            var moved = Enum.GetValues<ToolbarSlot>().Select(slot =>
            {
                var from = previous[slot];
                var to = current[slot];
                var width = slot == ToolbarSlot.Sort && _toolbarMotion.Has(ToolbarChoreography.SortWidth) ? _toolbarMotion.Get(ToolbarChoreography.SortWidth) : from.Width;
                return new SlotMove(
                    slot,
                    from.X + _toolbarMotion.Get(ToolbarChoreography.Morph(slot, 'x')) - to.X,
                    from.Y + _toolbarMotion.Get(ToolbarChoreography.Morph(slot, 'y')) - to.Y,
                    width + _toolbarMotion.Get(ToolbarChoreography.Morph(slot, 'w')) - to.Width);
            }).ToList();
            if (e.RowsChanged)
            {
                var listDy = _toolbarMotion.Get(ToolbarChoreography.ListDy) - (current.Height - previous.Height);
                if (level == ToolbarMotionLevel.Full)
                {
                    _choreography.MorphRows(moved, current.Rows > previous.Rows, listDy);
                    _toolbarMotion.Set(ToolbarChoreography.SortWidth, current[ToolbarSlot.Sort].Width);
                    if (e.SortStepChanged)
                    {
                        _choreography.SortLabel(current.SortIconOnly, delayed: !current.SortIconOnly);
                    }
                }
                else
                {
                    _choreography.CrossfadeRows();
                    _choreography.SettleSort(current[ToolbarSlot.Sort].Width, current.SortIconOnly);
                }
            }
            else
            {
                var fromWidth = _toolbarMotion.Get(ToolbarChoreography.SortWidth, previous[ToolbarSlot.Sort].Width);
                _choreography.SortStep(level, current.SortIconOnly, fromWidth, current[ToolbarSlot.Sort].Width, moved);
            }
        }
        else if (_toolbarMotion.IsRunning(ToolbarChoreography.SortWidth))
        {
            // Laufender Sortier-Schritt: das Ziel folgt dem Fensterrand.
            _toolbarMotion.Retarget(ToolbarChoreography.SortWidth, current[ToolbarSlot.Sort].Width);
        }
        else if (!animate)
        {
            // Nicht gezogen (Maximieren, Einrasten, DPI, Start): sofort der Endzustand, auch mitten in einem Morph.
            _choreography.SettleSort(current[ToolbarSlot.Sort].Width, current.SortIconOnly);
            _choreography.ClearMorph();
        }
        else
        {
            _toolbarMotion.Set(ToolbarChoreography.SortWidth, current[ToolbarSlot.Sort].Width);
        }

        _choreography.SearchKeyTo(current.SearchKey, animate && e.SearchKeyChanged);
        UpdateSearchPadding();
        _toolbarMotion.Apply();
    }

    /// <summary>Platz rechts im Suchfeld: so viel, wie die Tastenkappe „Strg+F“ gerade braucht.</summary>
    private void UpdateSearchPadding()
    {
        var key = ToolbarArea.Arrangement?.SearchKey != false && _viewModel?.HasSearchText != true;
        var padding = SearchBox.Padding;
        var right = key ? 70 : 36;
        if (Math.Abs(padding.Right - right) > 0.1)
        {
            SearchBox.Padding = new Thickness(padding.Left, padding.Top, right, padding.Bottom);
        }
    }

    // ---------- Lasche, Kapsel, Lichtnaht ----------

    private void OnToolbarTabHover(object? sender, bool on)
    {
        if (_shownCollapsed || (on && (_toolbarMotion.IsRunning(ToolbarChoreography.PillHeight) || _toolbarMotion.IsRunning(ToolbarChoreography.PillTy))))
        {
            return;
        }

        _choreography.Hover(on, ToolbarToggle.HoverWidth, ToolbarLevel());
        _toolbarMotion.Apply();
    }

    /// <summary>Per Tastatur geöffnet (Umschalt+F10, Menütaste): das Menü rechtsbündig unter Lasche bzw. Kapsel.</summary>
    private void OnToolbarMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ToolbarToggle.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ToolbarToggle;
            if (e.CursorLeft < 0 && e.CursorTop < 0)
            {
                menu.Placement = PlacementMode.Bottom;
                menu.VerticalOffset = 6;
                menu.Opened += AlignMenuRight;
            }
            else
            {
                menu.Placement = PlacementMode.MousePoint;
                menu.HorizontalOffset = 0;
                menu.VerticalOffset = 0;
            }
        }
    }

    private void AlignMenuRight(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Opened -= AlignMenuRight;
            menu.HorizontalOffset = ToolbarToggle.ActualWidth - menu.ActualWidth + 12;
        }
    }

    /// <summary>
    /// Inhalt der Kapsel aus dem Filterzustand: ungefiltert „⌕ Suchen Strg+F“, sonst Suchbegriff (bis 16 Zeichen), Quelle
    /// (wenn nicht „Alle“), aktive Filter als Mini-Symbole und „x/652“; was nicht passt, wird zu „+2“ (zuerst der Suchbegriff,
    /// dann die Quelle, dann die Filter). Ist sie eingeklappt, gleitet ihre Breite (160 ms) und Hof und Lichtnaht folgen;
    /// ohne <paramref name="move"/> nur Inhalt und Zielbreite (beim Ein- und Ausklappen bewegt die Choreografie die Lasche).
    /// </summary>
    private void UpdateCapsule(ToolbarMotionLevel level, bool move = true)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        var filtered = vm.IsFiltered;
        var parts = CapsuleParts(vm);
        var reset = filtered;
        var chrome = 30 + 2 + (reset ? 26 : 12);
        var max = Math.Max(120, _overlay.CapsuleMax);
        var dropped = 0;
        double content;
        while (true)
        {
            var shown = parts.Where(p => !p.Dropped).ToList();
            var items = CapsuleRow(shown, dropped);
            ToolbarToggle.SetCapsule(items, reset ? 26 : 12);
            content = ToolbarToggle.MeasureCapsule();
            if (chrome + content <= max || !parts.Any(p => p is { Dropped: false, Drop: > 0 }))
            {
                _capsuleItems = items.Count(i => i is not Ellipse);
                break;
            }

            parts.Where(p => p is { Dropped: false, Drop: > 0 }).MaxBy(p => p.Drop)!.Dropped = true;
            dropped++;
        }

        _capsuleWidth = Math.Round(chrome + content);
        _capsuleReset = reset;
        _capsuleFiltered = filtered;
        ToolbarToggle.IsFiltered = filtered;
        ToolbarResetButton.Visibility = reset ? Visibility.Visible : Visibility.Collapsed;
        if (!move)
        {
            return;
        }

        if (_shownCollapsed)
        {
            _choreography.CapsuleChanged(level, TabTarget());
        }

        _toolbarMotion.Apply();
    }

    /// <summary>Teile der Kapsel mit ihrem Rang beim Weglassen (höher: früher weg; 0: bleibt).</summary>
    private List<CapsulePart> CapsuleParts(LibraryViewModel vm)
    {
        var parts = new List<CapsulePart>();
        if (!vm.IsFiltered)
        {
            var search = Row(Glyph(""), Text(LibraryStrings.Toolbar_CapsuleSearch, ThemeKeys.BrushTextMuted), ToolbarToggle.Kbd(LibraryStrings.Key_CtrlF, small: false, new Thickness(1, 0, 0, 0)));
            parts.Add(new CapsulePart(search, 0));
            return parts;
        }

        var term = vm.AppliedSearchText;
        if (term.Length > 0)
        {
            var shortTerm = term.Length > 16 ? term[..16] + "…" : term;
            var text = Text(LibraryText.Format(LibraryStrings.Toolbar_CapsuleTerm, shortTerm), ThemeKeys.BrushText, FontWeights.Bold);
            text.MaxWidth = 130;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            parts.Add(new CapsulePart(Row(Glyph(""), text), 3));
        }

        if (vm.SelectedSource is { Source: { } source } option)
        {
            var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, "Hp.Brush.Source." + source);
            parts.Add(new CapsulePart(Row(dot, Text(option.Label, ThemeKeys.BrushText)), 2));
        }

        var filters = new List<UIElement>();
        if (vm.OnlySelected)
        {
            filters.Add(MiniGlyph(FilterGlyphKind.Switch));
        }

        if (vm.OnlyNew)
        {
            filters.Add(MiniGlyph(FilterGlyphKind.New));
        }

        if (vm.OnlyHdrCapable)
        {
            filters.Add(MiniGlyph(FilterGlyphKind.Hdr));
        }

        if (filters.Count > 0)
        {
            var row = Row([.. filters]);
            ((StackPanel)row).Children.OfType<FrameworkElement>().Skip(1).ToList().ForEach(f => f.Margin = new Thickness(4, 0, 0, 0));
            parts.Add(new CapsulePart(row, 1));
        }

        var count = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.FontFamilyProperty, ThemeKeys.FontMono);
        count.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.BrushTextSecondary);
        var visible = new System.Windows.Documents.Run(vm.VisibleCount.ToString(System.Globalization.CultureInfo.CurrentCulture));
        visible.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, ThemeKeys.BrushText);
        count.Inlines.Add(visible);
        count.Inlines.Add(new System.Windows.Documents.Run("/" + vm.TotalCount.ToString(System.Globalization.CultureInfo.CurrentCulture)));
        parts.Add(new CapsulePart(count, 0));
        return parts;
    }

    /// <summary>Einträge der Kapsel in einer Reihe: erster 10 px nach dem Trenner, dazwischen Punkte (3 px) mit je 8 px.</summary>
    private static List<UIElement> CapsuleRow(List<CapsulePart> shown, int dropped)
    {
        var items = new List<UIElement>();
        void Add(FrameworkElement element)
        {
            if (items.Count > 0)
            {
                var dot = new Ellipse { Width = 3, Height = 3, Opacity = 0.8, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(Shape.FillProperty, ThemeKeys.BrushTextMuted);
                items.Add(dot);
            }

            element.Margin = new Thickness(items.Count == 0 ? 10 : 8, 0, 0, 0);
            element.VerticalAlignment = VerticalAlignment.Center;
            items.Add(element);
        }

        if (dropped > 0)
        {
            var label = Text("+" + dropped.ToString(System.Globalization.CultureInfo.CurrentCulture), ThemeKeys.BrushTextSecondary, FontWeights.Bold);
            label.FontSize = 10;
            var more = new Border { Child = label, Height = 16, Padding = new Thickness(5, 0, 5, 0), CornerRadius = new CornerRadius(5) };
            more.SetResourceReference(Border.BackgroundProperty, ThemeKeys.BrushSurface2);
            Add(more);
        }

        foreach (var part in shown)
        {
            Add(part.Element);
        }

        return items;
    }

    private static FrameworkElement Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            if (panel.Children.Count > 0 && child is FrameworkElement element && element.Margin == default)
            {
                element.Margin = new Thickness(5, 0, 0, 0);
            }

            panel.Children.Add(child);
        }

        return panel;
    }

    private static TextBlock Glyph(string glyph)
    {
        var text = new TextBlock { Text = glyph, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "Hp.Font.Icons");
        text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.BrushTextSecondary);
        return text;
    }

    private static TextBlock Text(string value, string brush, FontWeight? weight = null)
    {
        var text = new TextBlock { Text = value, FontSize = 11.5, FontWeight = weight ?? FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontFamilyProperty, ThemeKeys.FontText);
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return text;
    }

    private static FilterGlyph MiniGlyph(FilterGlyphKind kind)
    {
        var glyph = new FilterGlyph { Kind = kind, OnAmount = 1, KnobPosition = 1, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(FilterGlyph.FillProperty, ThemeKeys.BrushAccent);
        glyph.SetResourceReference(FilterGlyph.OnForegroundProperty, ThemeKeys.BrushTextOnAccent);
        glyph.SetResourceReference(FilterGlyph.StrokeProperty, ThemeKeys.BrushTextSecondary);
        if (kind == FilterGlyphKind.Switch)
        {
            glyph.LayoutTransform = new ScaleTransform(0.9, 0.9);
        }

        return glyph;
    }

    /// <summary>Lage über der Bildkante neu bestimmen, wenn sich nur die Breite der Liste geändert hat (Trenner, Fenster).</summary>
    private void RefreshToolbarOverlay()
    {
        if (_appliedView is { } view && _viewModel is not null)
        {
            var shelf = view == LibraryViewMode.Shelf;
            PlaceToolbarOverlay(view, HeroHeight, ToolbarHeight(), shelf ? ShowcaseLayout.HeaderTop : ShowcaseLayout.PaneTop);
        }
    }

    /// <summary>
    /// Lage von Lasche bzw. Kapsel, Lichtnaht und Streif aus Titelbereich und Breite der Liste (bei jeder senkrechten
    /// Anordnung, auch während der Titelbereich beim Wechsel der Ansicht gleitet, und wenn die Liste breiter oder schmaler wird).
    /// </summary>
    private void PlaceToolbarOverlay(LibraryViewMode view, double hero, double toolbar, double columnsTop)
    {
        var shelf = view == LibraryViewMode.Shelf;
        var left = shelf ? ShowcaseLayout.Pad : 0;
        var width = ToolbarWidth();
        var capsuleTop = ShowcaseLayout.CapsuleTop(view, hero, toolbar) - columnsTop;
        var tabTop = ShowcaseLayout.TabTop(view, hero, toolbar) - columnsTop;
        var grip = view == LibraryViewMode.List ? left + Math.Round(width / 2) : double.NaN;
        var center = left + Math.Round(width / 2);
        var max = shelf ? 380 - 16 : left + width - (center + 32);
        var next = new ToolbarOverlayGeometry(left, width, capsuleTop, tabTop - capsuleTop, shelf ? capsuleTop + (ShowcaseLayout.CapsuleHeight / 2) : hero - columnsTop, grip, max);
        var changed = next != _overlay;
        var maxChanged = Math.Abs(next.CapsuleMax - _overlay.CapsuleMax) > 0.5;
        _overlay = next;
        if (!changed)
        {
            return;
        }

        Canvas.SetLeft(SeamHost, next.Left);
        Canvas.SetTop(SeamHost, next.SeamY - 5);
        SeamHost.Width = Math.Max(0, next.Width);
        Seam.Width = Math.Max(0, next.Width);
        SeamHost.OpacityMask = double.IsNaN(next.GripCenter) ? null : GripGap(next.Width, next.GripCenter - next.Left);
        Canvas.SetTop(Streak, next.SeamY - 7);
        if (maxChanged && _viewModel is not null)
        {
            UpdateCapsule(ToolbarMotionLevel.Instant);
        }

        // Ziel der Lasche (Lage über der Kapsel) folgt dem Titelbereich; ohne laufende Bewegung sofort.
        if (!_shownCollapsed && !_toolbarMotion.IsRunning(ToolbarChoreography.PillTy))
        {
            _toolbarMotion.Set(ToolbarChoreography.PillTy, next.TabTy);
        }
        else if (!_shownCollapsed)
        {
            _toolbarMotion.Retarget(ToolbarChoreography.PillTy, next.TabTy);
        }

        _toolbarMotion.Apply();
    }

    /// <summary>Ruhende Maske der Lichtnaht: eine Lücke um den Ziehgriff (er wird nie durchgestrichen).</summary>
    private static LinearGradientBrush GripGap(double width, double center)
    {
        var w = Math.Max(1, width);
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(w, 0) };
        foreach (var (x, a) in new[] { (0.0, 255), (center - 34, 255), (center - 26, 0), (center + 26, 0), (center + 34, 255), (w, 255) })
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)a, 0, 0, 0), Math.Clamp(x / w, 0, 1)));
        }

        brush.Freeze();
        return brush;
    }

    /// <summary>Breite der Leiste: Spalte der Liste, im Regal die ganze Breite ohne Rand.</summary>
    private double ToolbarWidth()
    {
        if (_appliedView == LibraryViewMode.Shelf)
        {
            return Math.Max(0, (Root.ActualWidth > 0 ? Root.ActualWidth : Width) - (2 * ShowcaseLayout.Pad));
        }

        return ListColumn.ActualWidth > 0 ? ListColumn.ActualWidth : ToolbarLayout.ComfortableWidth;
    }

    /// <summary>Bild eines Teils festhalten (Reduziert: alt blendet aus, neu blendet ein).</summary>
    private void CaptureGhost(FrameworkElement source, Image ghost)
    {
        if (source.ActualWidth <= 0 || source.ActualHeight <= 0 || !source.IsVisible)
        {
            ghost.Source = null;
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(source);
        bitmap.Freeze();
        var at = source.TranslatePoint(default, ToolbarOverlay);
        ghost.Source = bitmap;
        ghost.Width = source.ActualWidth;
        ghost.Height = source.ActualHeight;
        Canvas.SetLeft(ghost, at.X);
        Canvas.SetTop(ghost, at.Y);
    }

    // ---------- Anwenden (einmal je Bild) ----------

    /// <summary>
    /// Werte der Kanäle anwenden: Deckkraft und Verschiebung jedes Elements, Breiten beim Zeilenwechsel, Liste (FLIP, unten
    /// geschnitten, Fläche von unten skaliert), Klammer der Leiste an der Oberkante der Liste, Lasche/Kapsel, Naht, Streif.
    /// </summary>
    private void ApplyToolbarMotion()
    {
        var ch = _toolbarMotion;
        Span<double> widths = stackalloc double[10];
        foreach (FrameworkElement item in ToolbarArea.Children)
        {
            var slot = ToolbarPanel.GetSlot(item);
            var opacity = ch.Get(ToolbarChoreography.Item(slot, 'o'), 1);
            if (Math.Abs(item.Opacity - opacity) > 0.0005)
            {
                item.Opacity = opacity;
            }

            if (item.RenderTransform is TransformGroup { Children: [ScaleTransform scale, TranslateTransform shift] })
            {
                var s = ch.Get(ToolbarChoreography.Item(slot, 's'), 1);
                scale.ScaleX = scale.ScaleY = s;
                shift.X = ch.Get(ToolbarChoreography.Item(slot, 'x')) + ch.Get(ToolbarChoreography.Morph(slot, 'x'));
                shift.Y = ch.Get(ToolbarChoreography.Item(slot, 'y')) + ch.Get(ToolbarChoreography.Morph(slot, 'y'));
            }

            widths[(int)slot] = ch.Get(ToolbarChoreography.Morph(slot, 'w'));
        }

        if (ToolbarArea.Arrangement is { } arrangement && ch.Has(ToolbarChoreography.SortWidth))
        {
            widths[(int)ToolbarSlot.Sort] += ch.Get(ToolbarChoreography.SortWidth) - arrangement[ToolbarSlot.Sort].Width;
        }

        ToolbarArea.SetWidthOffsets(widths);
        ToolbarArea.Opacity = ch.Get(ToolbarChoreography.ToolbarOpacity, 1);
        ToolbarGhost.Opacity = ch.Get(ToolbarChoreography.ToolbarGhost);
        _sortLabel ??= SortOrder.Template?.FindName("Label", SortOrder) as TextBlock;
        if (_sortLabel is { } label)
        {
            label.Opacity = ch.Get(ToolbarChoreography.SortLabelOpacity, 1);
            label.RenderTransform = label.RenderTransform is TranslateTransform { IsFrozen: false } t ? t : new TranslateTransform();
            ((TranslateTransform)label.RenderTransform).X = ch.Get(ToolbarChoreography.SortLabelX);
        }

        SearchKey.Opacity = ch.Get(ToolbarChoreography.SearchKey, 1);
        ApplyListShift(ch.Get(ToolbarChoreography.ListDy));
        ApplyTabMotion();
        if (HeroLargeSink.RenderTransform is TranslateTransform sink)
        {
            sink.Y = ch.Get(ToolbarChoreography.LargeDy);
        }
    }

    /// <summary>
    /// Liste bzw. Raster um <paramref name="dy"/> verschoben (Inhalt), die Fläche der Liste von unten skaliert (unterer Rand
    /// bleibt), unten geschnitten; die Leiste in ihrer Klammer: Unterkante auf der sichtbaren Oberkante der Liste.
    /// </summary>
    private void ApplyListShift(double dy)
    {
        if (_appliedView is not { } view)
        {
            return;
        }

        if (ListCardContent.RenderTransform is TranslateTransform content)
        {
            content.Y = dy;
        }

        if (ListCardBack.RenderTransform is ScaleTransform back)
        {
            var height = ListCard.ActualHeight;
            back.ScaleY = height > 0 && dy != 0 ? Math.Max(0, (height - dy) / height) : 1;
        }

        if (GameGrid.RenderTransform is TranslateTransform grid)
        {
            grid.Y = dy;
        }

        if (NoticeArea.RenderTransform is TranslateTransform notices)
        {
            notices.Y = dy;
        }

        var moving = Math.Abs(dy) > 0.01;
        BodyArea.Clip = moving ? BodyClip(BodyArea.ActualWidth, BodyArea.ActualHeight) : null;

        // Klammer der Leiste (Liste und Raster): alles unter der sichtbaren Oberkante des Inhalts ist abgeschnitten.
        var shelf = view == LibraryViewMode.Shelf;
        var panelTop = ListArea.Margin.Top + (shelf ? ShowcaseLayout.HeaderTop : ShowcaseLayout.PaneTop);
        var offset = ToolbarArea.RenderTransform is TranslateTransform panelShift ? panelShift.Y : 0;
        if (shelf || (!_shownCollapsed && !moving))
        {
            ToolbarArea.Clip = null;
        }
        else
        {
            var below = panelTop + (ToolbarArea.ReservesHeight ? ToolbarArea.ActualHeight : 0) + dy + ShowcaseLayout.ToolbarGap;
            var bottom = below - (panelTop + offset);
            ToolbarArea.Clip = new RectangleGeometry(new Rect(-24, -120, ToolbarArea.ActualWidth + 48, Math.Max(0, bottom + 120)));
        }

    }

    /// <summary>Ausschnitt der Liste während sie gleitet: oben frei, unten mit den runden Ecken der Liste.</summary>
    private static Geometry BodyClip(double width, double height)
    {
        var w = Math.Max(0, width);
        var h = Math.Max(0, height);
        var clip = new CombinedGeometry(
            GeometryCombineMode.Union,
            new RectangleGeometry(new Rect(-24, -400, w + 48, Math.Max(0, h + 400 - 20))),
            new RectangleGeometry(new Rect(0, Math.Max(0, h - 40), w, Math.Min(40, h)), 20, 20));
        clip.Freeze();
        return clip;
    }

    /// <summary>Lasche/Kapsel, ✕, Lichtnaht, Lichtstreif und die Bilder beim Überblenden.</summary>
    private void ApplyTabMotion()
    {
        var ch = _toolbarMotion;
        var g = _overlay;
        var pw = ch.Get(ToolbarChoreography.PillWidth, ToolbarChoreography.TabWidth);
        var ph = ch.Get(ToolbarChoreography.PillHeight, ToolbarChoreography.TabHeight);
        var ty = ch.Get(ToolbarChoreography.PillTy, g.TabTy);
        ToolbarToggle.ApplyMotion(
            pw,
            ph,
            ch.Get(ToolbarChoreography.PillTurn),
            ch.Get(ToolbarChoreography.Divider),
            ch.Get(ToolbarChoreography.Hint),
            ch.Get(ToolbarChoreography.TabOpacity, 1),
            ch.Get(ToolbarChoreography.Halo),
            ch.Get(ToolbarChoreography.RestHalo),
            i => (ch.Get(ToolbarChoreography.CapsuleItem(i, 'o')), ch.Get(ToolbarChoreography.CapsuleItem(i, 'x'))));
        Canvas.SetLeft(ToolbarToggle, Math.Round((g.Right - Math.Round(pw * 10) / 10) * 10) / 10);
        Canvas.SetTop(ToolbarToggle, Math.Round((g.CapsuleTop + ty) * 10) / 10);
        TabGhost.Opacity = ch.Get(ToolbarChoreography.TabGhost);

        var reset = ch.Get(ToolbarChoreography.Reset);
        ToolbarResetButton.Opacity = reset;
        var live = _shownCollapsed && _capsuleReset && reset > 0.5;
        ToolbarResetButton.IsHitTestVisible = live;
        ToolbarResetButton.Focusable = _shownCollapsed && _capsuleReset;
        KeyboardNavigation.SetIsTabStop(ToolbarResetButton, _shownCollapsed && _capsuleReset);
        Canvas.SetLeft(ToolbarResetButton, g.Right - 24);
        Canvas.SetTop(ToolbarResetButton, g.CapsuleTop);

        // Lichtnaht: rechtes Ende immer an der linken Kante von Lasche bzw. Kapsel, auch während diese wächst.
        Seam.Opacity = ch.Get(ToolbarChoreography.SeamOpacity);
        if (Seam.RenderTransform is TransformGroup { Children: [ScaleTransform seamScale, TranslateTransform seamShift] } && g.Width > 0)
        {
            seamScale.ScaleX = ch.Get(ToolbarChoreography.SeamScale) * Math.Max(0, g.Right - pw - g.Left) / g.Width;
            seamShift.X = -pw;
        }

        var streak = ch.Get(ToolbarChoreography.StreakOpacity);
        Streak.Opacity = SystemParameters.HighContrast ? 0 : streak;
        if (streak > 0 && Streak.RenderTransform is TranslateTransform streakShift)
        {
            var capsuleLeft = g.Right - _capsuleWidth;
            var p = ch.Get(ToolbarChoreography.StreakPosition);
            var reversed = _choreography.StreakReversed;
            var x0 = reversed ? capsuleLeft - 20 : g.Left - 160;
            var x1 = reversed ? g.Left - 160 : capsuleLeft - 120;
            streakShift.X = x0 + ((x1 - x0) * p);
            ApplyStreakDirection(reversed);
        }
    }

    // ---------- Farben ----------

    private bool _streakReversed;

    private void ApplyStreakDirection(bool reversed)
    {
        if (reversed != _streakReversed || StreakCore.Background is null)
        {
            _streakReversed = reversed;
            ApplyStreakBrushes();
        }
    }

    /// <summary>Pinsel von Naht und Streif aus den Akzentfarben (Thema, Farbschema); hoher Kontrast: Systemfarbe, kein Schein.</summary>
    private void ApplyToolbarBrushes()
    {
        if (SystemParameters.HighContrast)
        {
            SeamCore.Fill = SystemColors.HighlightBrush;
            SeamGlow.Fill = null;
            ApplyStreakBrushes();
            return;
        }

        var c1 = TryFindResource(ThemeKeys.ColorC1) is Color a ? a : Color.FromRgb(0xFF, 0x8A, 0x3D);
        var c2 = TryFindResource(ThemeKeys.ColorC2) is Color b ? b : Color.FromRgb(0xFF, 0x3D, 0x7F);
        var mid = ColorMath.Mix(c1, c2, 0.6);
        SeamCore.Fill = Frozen(new LinearGradientBrush(
            [new GradientStop(AmbientPalette.Alpha(c1, 0), 0), new GradientStop(AmbientPalette.Alpha(c1, 0.6), _dark ? 0.3 : 0.35), new GradientStop(AmbientPalette.Alpha(mid, 0.85), _dark ? 0.65 : 0.7), new GradientStop(AmbientPalette.Alpha(c2, 0.95), 1)],
            new Point(0, 0.5),
            new Point(1, 0.5)));
        SeamGlow.Fill = Frozen(new LinearGradientBrush(
            [new GradientStop(AmbientPalette.Alpha(c2, 0), 0), new GradientStop(AmbientPalette.Alpha(c2, 0.16), 0.4), new GradientStop(AmbientPalette.Alpha(c2, 0.42), 1)],
            new Point(0, 0.5),
            new Point(1, 0.5)));
        SeamGlow.OpacityMask = Frozen(new LinearGradientBrush(
            [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.45), new GradientStop(Colors.Black, 0.55), new GradientStop(Colors.Transparent, 1)],
            new Point(0.5, 0),
            new Point(0.5, 1)));
        ApplyStreakBrushes();
    }

    private void ApplyStreakBrushes()
    {
        if (SystemParameters.HighContrast)
        {
            StreakCore.Background = SystemColors.HighlightBrush;
            StreakGlow.Background = null;
            return;
        }

        var c1 = TryFindResource(ThemeKeys.ColorC1) is Color a ? a : Color.FromRgb(0xFF, 0x8A, 0x3D);
        var c2 = TryFindResource(ThemeKeys.ColorC2) is Color b ? b : Color.FromRgb(0xFF, 0x3D, 0x7F);
        var c3 = TryFindResource(ThemeKeys.ColorC3) is Color c ? c : Color.FromRgb(0xFF, 0x94, 0xB9);
        var start = _streakReversed ? new Point(1, 0.5) : new Point(0, 0.5);
        var end = _streakReversed ? new Point(0, 0.5) : new Point(1, 0.5);
        StreakCore.Background = Frozen(_dark
            ? new LinearGradientBrush([new GradientStop(AmbientPalette.Alpha(c3, 0), 0), new GradientStop(ColorMath.Mix(c3, Colors.White, 0.55), 0.55), new GradientStop(Colors.White, 0.78), new GradientStop(AmbientPalette.Alpha(c1, 0), 1)], start, end)
            : new LinearGradientBrush([new GradientStop(AmbientPalette.Alpha(c2, 0), 0), new GradientStop(c1, 0.45), new GradientStop(c2, 0.8), new GradientStop(AmbientPalette.Alpha(c2, 0), 1)], start, end));
        var center = new Point(_streakReversed ? 0.28 : 0.72, 0.5);
        StreakGlow.Background = Frozen(new RadialGradientBrush(
            [new GradientStop(AmbientPalette.Alpha(c2, _dark ? 0.8 : 0.62), 0), new GradientStop(AmbientPalette.Alpha(c1, _dark ? 0.28 : 0.34), 0.6), new GradientStop(AmbientPalette.Alpha(c1, 0), 1)])
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = 0.5,
            RadiusY = 0.5,
        });
        StreakGlow.Margin = _streakReversed ? new Thickness(0, 0, 16, 0) : new Thickness(16, 0, 0, 0);
    }

    private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            ToolbarToggle.SetHighContrast(SystemParameters.HighContrast);
            ApplyToolbarBrushes();
        }
    }

    // ---------- Ansicht ----------

    /// <summary>Gewählte Fläche der Ansicht gleitet (Voll 240 ms), Reduziert blendet sie über (150 ms), Aus springt sie.</summary>
    private void MoveViewSelection(LibraryViewMode view, bool animate)
    {
        if (ViewSelection.RenderTransform is not TranslateTransform shift)
        {
            return;
        }

        var x = view switch { LibraryViewMode.Grid => 38, LibraryViewMode.Shelf => 76, _ => 0 };
        var level = animate ? ToolbarLevel() : ToolbarMotionLevel.Instant;
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        ViewSelection.BeginAnimation(OpacityProperty, null);
        if (level == ToolbarMotionLevel.Full && Math.Abs(shift.X - x) > 0.5)
        {
            var slide = new DoubleAnimation(shift.X, x, TimeSpan.FromMilliseconds(240)) { EasingFunction = new KeySplineEase(), FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(slide, EffectPolicy.OneShotFrameRate);
            shift.X = x;
            shift.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        else if (level == ToolbarMotionLevel.Reduced)
        {
            shift.X = x;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop };
            ViewSelection.BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            shift.X = x;
        }
    }

    /// <summary>Kurve der gleitenden Fläche (cubic-bezier .22, 1, .36, 1) als Easing für WPF.</summary>
    private sealed class KeySplineEase : EasingFunctionBase
    {
        public KeySplineEase()
        {
            EasingMode = EasingMode.EaseIn; // die Kurve selbst ist schon die ganze Bewegung
        }

        protected override double EaseInCore(double normalizedTime) => ToolbarEase.Segment(normalizedTime);

        protected override Freezable CreateInstanceCore() => new KeySplineEase();
    }

    /// <summary>Angehaltene Zeit für Bildschirmfotos.</summary>
    private sealed class FrozenTime : IEffectTime
    {
        public TimeSpan Now { get; set; }
    }

    /// <summary>Bildtakt, der nie tickt (Bildschirmfotos: die Bewegung bleibt stehen).</summary>
    private sealed class FrozenClock : IFrameClock
    {
        public event EventHandler? Frame
        {
            add => Subscribers++;
            remove => Subscribers--;
        }

        public int Subscribers { get; private set; }
    }

    /// <summary>Teil der Kapsel mit seinem Rang beim Weglassen.</summary>
    private sealed class CapsulePart(FrameworkElement element, int drop)
    {
        public FrameworkElement Element { get; } = element;

        public int Drop { get; } = drop;

        public bool Dropped { get; set; }
    }
}
