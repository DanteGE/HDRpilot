using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HdrPilot.App.Resources;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Lasche und Kapsel der Werkzeugleiste in einem Element (Vorschau #pill, Wunsch des Nutzers 2026-10-17). Ausgeklappt eine
/// Lasche 36×18 mit Pfeil rechts über der Leiste (ihre Unterkante 8 px über der Leiste), die beim Überfahren nach links zu
/// „Leiste ausblenden · Strg+Umschalt+F“ wächst; eingeklappt eine Kapsel 24 hoch auf der Bildkante mit Suchbegriff, Quelle,
/// aktiven Filtern als Mini-Symbolen und „x/652“. Gefiltert hat sie einen Rahmen in AccentStrong und einen ruhenden Hof.
/// Breite, Höhe, Lage und Deckkraft setzt das Fenster aus den Kanälen der <see cref="ToolbarChoreography"/>
/// (<see cref="ApplyMotion"/>); das Element zeichnet nur. Klick, Eingabetaste und Leertaste melden
/// <see cref="ToggleRequested"/>; für UI-Automation ein Knopf mit ExpandCollapse (Name „Werkzeugleiste“, ItemStatus mit
/// den aktiven Filtern). Hoher Kontrast: Systemfarben, kein Hof.
/// </summary>
public sealed class ToolbarToggle : Grid
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(ToolbarToggle), new PropertyMetadata(true, OnIsExpandedChanged));

    /// <summary>Gefiltert und eingeklappt: Rahmen in AccentStrong.</summary>
    public static readonly DependencyProperty IsFilteredProperty = DependencyProperty.Register(
        nameof(IsFiltered), typeof(bool), typeof(ToolbarToggle), new PropertyMetadata(false, (d, _) => ((ToolbarToggle)d).UpdateLook()));

    private readonly Border _restHalo = new() { IsHitTestVisible = false, Opacity = 0, CornerRadius = new CornerRadius(16) };
    private readonly Border _halo = new() { IsHitTestVisible = false, Opacity = 0, CornerRadius = new CornerRadius(22) };
    private readonly Rectangle _hit = new() { Fill = Brushes.Transparent, Margin = new Thickness(-4, -3, -4, -3) };
    private readonly Border _pill = new() { BorderThickness = new Thickness(1), SnapsToDevicePixels = true };
    private readonly Grid _content = new();
    private readonly RectangleGeometry _clip = new();
    private readonly Path _arrow = new()
    {
        Data = Geometry.Parse("M1.2,5 L5,1.2 L8.8,5"),
        StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Width = 10,
        Height = 6,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(13, 0, 0, 0),
        RenderTransformOrigin = new Point(0.5, 0.5),
    };

    private readonly RotateTransform _turn = new();
    private readonly StackPanel _hint = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0), Opacity = 0, IsHitTestVisible = false };
    private readonly Border _divider = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(29, 5, 0, 5), Opacity = 0, IsHitTestVisible = false };
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private bool _pressed;
    private bool _highContrast;

    public ToolbarToggle()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Width = ToolbarChoreography.TabWidth;
        Height = ToolbarChoreography.TabHeight;

        _halo.Background = HaloBrush();
        _restHalo.SetResourceReference(Border.BackgroundProperty, ThemeKeys.BrushAccentSoft);
        _arrow.RenderTransform = _turn;
        _arrow.SetResourceReference(Shape.StrokeProperty, ThemeKeys.BrushText);
        _divider.SetResourceReference(Border.BackgroundProperty, ThemeKeys.BrushLineStrong);

        var hintText = new TextBlock { Text = LibraryStrings.Toolbar_Hide, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.BrushText);
        hintText.SetResourceReference(TextBlock.FontFamilyProperty, ThemeKeys.FontText);
        _hint.Children.Add(hintText);
        _hint.Children.Add(Kbd(LibraryStrings.Key_CtrlShiftF, small: true, new Thickness(7, 0, 0, 0)));

        _content.ClipToBounds = false;
        _content.Clip = _clip;
        _content.Children.Add(_divider);
        _content.Children.Add(_items);
        _content.Children.Add(_hint);
        _content.Children.Add(_arrow);
        _pill.Child = _content;

        Children.Add(_hit);
        Children.Add(_restHalo);
        Children.Add(_halo);
        Children.Add(_pill);
        UpdateLook();
        ApplyMotion(ToolbarChoreography.TabWidth, ToolbarChoreography.TabHeight, 0, 0, 0, 1, 0, 0, null);
    }

    /// <summary>Klick, Eingabetaste, Leertaste oder UI-Automation: ein- bzw. ausblenden.</summary>
    public event EventHandler? ToggleRequested;

    /// <summary>Maus über der Lasche bzw. Kapsel (true) oder weg.</summary>
    public event EventHandler<bool>? HoverChanged;

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public bool IsFiltered
    {
        get => (bool)GetValue(IsFilteredProperty);
        set => SetValue(IsFilteredProperty, value);
    }

    /// <summary>Breite der Lasche mit dem Hinweis „Leiste ausblenden · Strg+Umschalt+F“ (beim Überfahren).</summary>
    public double HoverWidth
    {
        get
        {
            _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Ceiling(28 + _hint.DesiredSize.Width + 12);
        }
    }

    /// <summary>Einträge der Kapsel (Kapsel-Inhalt), rechts mit <paramref name="rightPadding"/> Abstand (Platz für ✕ oder Rand).</summary>
    public void SetCapsule(IReadOnlyList<UIElement> items, double rightPadding)
    {
        _items.Children.Clear();
        foreach (var item in items)
        {
            _items.Children.Add(item);
        }

        _items.Margin = new Thickness(0, 0, rightPadding, 0);
    }

    /// <summary>Einträge der Kapsel, wie zuletzt gesetzt (Tests).</summary>
    public IReadOnlyList<UIElement> CapsuleItems => _items.Children.Cast<UIElement>().ToList();

    /// <summary>Breite des Inhalts der Kapsel (gemessen, aufgerundet).</summary>
    public double MeasureCapsule()
    {
        _items.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(_items.DesiredSize.Width - _items.Margin.Left - _items.Margin.Right);
    }

    /// <summary>
    /// Werte eines Bilds anwenden: Breite und Höhe (Lage setzt das Fenster), Drehung des Pfeils, Deckkraft von Trenner,
    /// Hinweis, den beiden Höfen und je Eintrag der Kapsel (Deckkraft und Versatz).
    /// </summary>
    public void ApplyMotion(double width, double height, double turn, double divider, double hint, double tabOpacity, double halo, double restHalo, Func<int, (double Opacity, double X)>? item)
    {
        var w = Math.Max(1, Math.Round(width * 10) / 10);
        var h = Math.Max(1, Math.Round(height * 10) / 10);
        if (Math.Abs(Width - w) > 0.01)
        {
            Width = w;
        }

        if (Math.Abs(Height - h) > 0.01 || Math.Abs(_pill.CornerRadius.TopLeft - (h / 2)) > 0.01)
        {
            Height = h;
            _pill.CornerRadius = new CornerRadius(h / 2);
            _halo.Margin = new Thickness(-10, -(40 - h) / 2, -10, -(40 - h) / 2);
            _restHalo.Margin = new Thickness(-4, -(32 - h) / 2, -4, -(32 - h) / 2);
        }

        _clip.Rect = new Rect(0, 0, Math.Max(0, w - 2), Math.Max(0, h - 2));
        _clip.RadiusX = _clip.RadiusY = Math.Max(0, (h / 2) - 1);
        _turn.Angle = turn;
        _divider.Opacity = divider;
        _hint.Opacity = hint;
        Opacity = tabOpacity;
        _halo.Opacity = _highContrast ? 0 : halo;
        _restHalo.Opacity = _highContrast ? 0 : restHalo;
        if (item is not null)
        {
            for (var i = 0; i < _items.Children.Count; i++)
            {
                var (o, x) = item(i);
                var child = _items.Children[i];
                child.Opacity = o;
                if (child.RenderTransform is TranslateTransform { IsFrozen: false } shift)
                {
                    shift.X = x;
                }
                else
                {
                    child.RenderTransform = new TranslateTransform(x, 0);
                }
            }
        }
    }

    /// <summary>Tastenkappe (Prototyp .kbd), auch für die Kapsel.</summary>
    public static Border Kbd(string text, bool small, Thickness margin)
    {
        var label = new TextBlock { Text = text, FontSize = small ? 9.5 : 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.FontFamilyProperty, ThemeKeys.FontMono);
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.BrushTextSecondary);
        var kbd = new Border
        {
            Child = label,
            Height = small ? 14 : 16,
            Padding = new Thickness(4, 0, 4, 1),
            CornerRadius = new CornerRadius(small ? 4 : 5),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Center,
        };
        kbd.SetResourceReference(Border.BackgroundProperty, ThemeKeys.BrushSurface2);
        kbd.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.BrushLine);
        return kbd;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ToolbarTogglePeer(this);

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateLook();
        HoverChanged?.Invoke(this, true);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pressed = false;
        UpdateLook();
        HoverChanged?.Invoke(this, false);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _pressed = true;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_pressed)
        {
            _pressed = false;
            e.Handled = true;
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Hoher Kontrast an oder aus: Systemfarben, kein Hof (gesetzt vom Fenster).</summary>
    public void SetHighContrast(bool on)
    {
        _highContrast = on;
        UpdateLook();
    }

    internal void Request() => ToggleRequested?.Invoke(this, EventArgs.Empty);

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toggle = (ToolbarToggle)d;
        toggle._hit.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        toggle.UpdateLook();
        if (UIElementAutomationPeer.FromElement(toggle) is ToolbarTogglePeer peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                (bool)e.OldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                (bool)e.NewValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    /// <summary>Großer Schein beim Aufleuchten der Kapsel (Vorschau .chalo): Akzentfarben, nach außen auslaufend.</summary>
    private static RadialGradientBrush HaloBrush()
    {
        var brush = new RadialGradientBrush
        {
            RadiusX = 0.6,
            RadiusY = 0.7,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x8C, 0xFF, 0x3D, 0x7F), 0),
                new GradientStop(Color.FromArgb(0x38, 0xFF, 0x8A, 0x3D), 0.55),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0x8A, 0x3D), 0.75),
            },
        };
        brush.Freeze();
        return brush;
    }

    private void UpdateLook()
    {
        if (_highContrast)
        {
            _pill.Background = SystemColors.ControlBrush;
            _pill.BorderBrush = IsFiltered && !IsExpanded ? SystemColors.HighlightBrush : SystemColors.ControlTextBrush;
            _arrow.Stroke = SystemColors.ControlTextBrush;
            return;
        }

        _arrow.SetResourceReference(Shape.StrokeProperty, ThemeKeys.BrushText);
        _pill.SetResourceReference(Border.BackgroundProperty, "Lib.Brush.TabGlass");
        _pill.SetResourceReference(
            Border.BorderBrushProperty,
            IsFiltered && !IsExpanded ? ThemeKeys.BrushAccentStrong : IsMouseOver ? ThemeKeys.BrushLineStrong : "Lib.Brush.TabLine");
    }

    /// <summary>Lasche/Kapsel für UI-Automation: Knopf mit ExpandCollapse (aufgeklappt = Werkzeugleiste zu sehen).</summary>
    private sealed class ToolbarTogglePeer(ToolbarToggle owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public void Expand()
        {
            if (!owner.IsExpanded)
            {
                owner.Request();
            }
        }

        public void Collapse()
        {
            if (owner.IsExpanded)
            {
                owner.Request();
            }
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(ToolbarToggle);

        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
