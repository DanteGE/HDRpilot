using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Spalten der Listenzeilen (E89): vererbbare Schalter und Breiten, die das Fenster an der Liste setzt (<see cref="ShowcaseLayout.ListColumnsFor"/>)
/// und die Kopfzeile und jede Zeile lesen. So passen Kopf und Zeilen ohne gemeinsame Spaltendefinition zusammen.
/// </summary>
public static class GameListColumns
{
    public static readonly DependencyProperty ShowSourceProperty = DependencyProperty.RegisterAttached(
        "ShowSource", typeof(bool), typeof(GameListColumns), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ShowLastPlayedProperty = DependencyProperty.RegisterAttached(
        "ShowLastPlayed", typeof(bool), typeof(GameListColumns), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ShowHdrProperty = DependencyProperty.RegisterAttached(
        "ShowHdr", typeof(bool), typeof(GameListColumns), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SourceWidthProperty = DependencyProperty.RegisterAttached(
        "SourceWidth", typeof(double), typeof(GameListColumns), new FrameworkPropertyMetadata(ShowcaseLayout.SourceColumn, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty LastPlayedWidthProperty = DependencyProperty.RegisterAttached(
        "LastPlayedWidth", typeof(double), typeof(GameListColumns), new FrameworkPropertyMetadata(ShowcaseLayout.LastPlayedColumn, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty HdrWidthProperty = DependencyProperty.RegisterAttached(
        "HdrWidth", typeof(double), typeof(GameListColumns), new FrameworkPropertyMetadata(ShowcaseLayout.HdrColumn, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Helles Thema: gewählte Zeile zusätzlich mit Ring (Akzent 45 %), damit sie auf Weiß deutlich bleibt.</summary>
    public static readonly DependencyProperty LightThemeProperty = DependencyProperty.RegisterAttached(
        "LightTheme", typeof(bool), typeof(GameListColumns), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetLightTheme(DependencyObject d) => (bool)d.GetValue(LightThemeProperty);

    public static void SetLightTheme(DependencyObject d, bool value) => d.SetValue(LightThemeProperty, value);

    public static bool GetShowSource(DependencyObject d) => (bool)d.GetValue(ShowSourceProperty);

    public static void SetShowSource(DependencyObject d, bool value) => d.SetValue(ShowSourceProperty, value);

    public static bool GetShowLastPlayed(DependencyObject d) => (bool)d.GetValue(ShowLastPlayedProperty);

    public static void SetShowLastPlayed(DependencyObject d, bool value) => d.SetValue(ShowLastPlayedProperty, value);

    public static bool GetShowHdr(DependencyObject d) => (bool)d.GetValue(ShowHdrProperty);

    public static void SetShowHdr(DependencyObject d, bool value) => d.SetValue(ShowHdrProperty, value);

    public static double GetSourceWidth(DependencyObject d) => (double)d.GetValue(SourceWidthProperty);

    public static void SetSourceWidth(DependencyObject d, double value) => d.SetValue(SourceWidthProperty, value);

    public static double GetLastPlayedWidth(DependencyObject d) => (double)d.GetValue(LastPlayedWidthProperty);

    public static void SetLastPlayedWidth(DependencyObject d, double value) => d.SetValue(LastPlayedWidthProperty, value);

    public static double GetHdrWidth(DependencyObject d) => (double)d.GetValue(HdrWidthProperty);

    public static void SetHdrWidth(DependencyObject d, double value) => d.SetValue(HdrWidthProperty, value);

    /// <summary>Spalten an einem Element setzen (nur bei Änderung, die Vererbung läuft sonst durch alle Zeilen).</summary>
    public static void Apply(DependencyObject target, ListColumns columns)
    {
        if (GetSourceWidth(target) != columns.SourceWidth)
        {
            SetSourceWidth(target, columns.SourceWidth);
        }

        if (GetLastPlayedWidth(target) != columns.LastPlayedWidth)
        {
            SetLastPlayedWidth(target, columns.LastPlayedWidth);
        }

        if (GetHdrWidth(target) != columns.HdrWidth)
        {
            SetHdrWidth(target, columns.HdrWidth);
        }

        if (GetShowSource(target) != columns.Source)
        {
            SetShowSource(target, columns.Source);
        }

        if (GetShowLastPlayed(target) != columns.LastPlayed)
        {
            SetShowLastPlayed(target, columns.LastPlayed);
        }

        if (GetShowHdr(target) != columns.Hdr)
        {
            SetShowHdr(target, columns.Hdr);
        }
    }
}

/// <summary>Pfad mit Auslassung in der Mitte (Prototyp midPath): „C:\…\Binaries\Win64“ bleibt lesbar, keine Bidi-Tricks.</summary>
public static class PathEllipsis
{
    public const string Ellipsis = "…";

    /// <summary>
    /// Kürzt einen Pfad so, dass <paramref name="measure"/> höchstens <paramref name="maxWidth"/> ergibt: erst ganze
    /// Ordner nach dem ersten Teil weglassen, dann vom Anfang des Rests kürzen.
    /// </summary>
    public static string Shorten(string path, double maxWidth, Func<string, double> measure)
    {
        if (string.IsNullOrEmpty(path) || !double.IsFinite(maxWidth) || maxWidth <= 0 || measure(path) <= maxWidth)
        {
            return path ?? string.Empty;
        }

        var parts = path.Split('\\');
        var head = parts[0];
        var tail = parts.Skip(1).ToList();
        string Join() => string.Join('\\', new[] { head, Ellipsis }.Concat(tail));
        while (tail.Count > 1 && measure(Join()) > maxWidth)
        {
            tail.RemoveAt(0);
        }

        var shortened = Join();
        if (measure(shortened) <= maxWidth)
        {
            return shortened;
        }

        var rest = string.Join('\\', tail);
        while (rest.Length > 4 && measure(Ellipsis + rest) > maxWidth)
        {
            rest = rest[1..];
        }

        return Ellipsis + rest;
    }
}

/// <summary>
/// Bild als Füllung eines Rahmens (Standard UniformToFill, abgerundete Ecken des Rahmens), ohne Bindung an einem Pinsel:
/// Bindungen an Freezables in Vorlagen melden beim Wiederverwenden von Zeilen Fehler ohne Datenkontext. Steht im Rahmen
/// schon ein <see cref="ImageBrush"/> (etwa mit Zoom-Transformation für eine Animation), bekommt eine Kopie davon das Bild.
/// <see cref="StretchProperty"/> wählt die Füllung (Logos in „Bild wählen…“: Uniform, nie beschnitten, E93);
/// <see cref="FadeOnArrivalProperty"/> blendet ein später eintreffendes Bild in 160 ms ein (nicht auf der Effektstufe „Aus“).
/// </summary>
public static class ImageFill
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(ImageSource), typeof(ImageFill), new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.RegisterAttached(
        "Stretch", typeof(Stretch), typeof(ImageFill), new PropertyMetadata(Stretch.UniformToFill, OnStretchChanged));

    public static readonly DependencyProperty FadeOnArrivalProperty = DependencyProperty.RegisterAttached(
        "FadeOnArrival", typeof(bool), typeof(ImageFill), new PropertyMetadata(false));

    public static ImageSource? GetSource(DependencyObject d) => (ImageSource?)d.GetValue(SourceProperty);

    public static void SetSource(DependencyObject d, ImageSource? value) => d.SetValue(SourceProperty, value);

    public static Stretch GetStretch(DependencyObject d) => (Stretch)d.GetValue(StretchProperty);

    public static void SetStretch(DependencyObject d, Stretch value) => d.SetValue(StretchProperty, value);

    public static bool GetFadeOnArrival(DependencyObject d) => (bool)d.GetValue(FadeOnArrivalProperty);

    public static void SetFadeOnArrival(DependencyObject d, bool value) => d.SetValue(FadeOnArrivalProperty, value);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border)
        {
            return;
        }

        if (e.NewValue is not ImageSource source)
        {
            border.ClearValue(Border.BackgroundProperty);
            return;
        }

        var brush = border.Background is ImageBrush existing
            ? existing.CloneCurrentValue()
            : new ImageBrush { Stretch = GetStretch(border) };
        brush.ImageSource = source;
        brush.Stretch = GetStretch(border);
        border.Background = brush;
        if (e.OldValue is null && GetFadeOnArrival(border) && border.IsLoaded && LibraryMotion.Enabled)
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
            border.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border { Background: ImageBrush brush } border && brush.Stretch != (Stretch)e.NewValue)
        {
            var copy = brush.CloneCurrentValue();
            copy.Stretch = (Stretch)e.NewValue;
            border.Background = copy;
        }
    }
}

/// <summary>
/// Knopf, der einen Bereich auf- und zuklappt (Spielwahl „Spiel: … ▾“ in „Bild wählen…“, E93): ein ToggleButton, dessen
/// Automationspeer Umschalten und Auf-/Zuklappen (ExpandCollapse) bietet. Alt+Pfeil runter und F4 schalten wie bei einer
/// Auswahlliste; der Winkel (<see cref="ChevronAngle"/>) dreht auf der Stufe „Voll“ in 160 ms, sonst sofort.
/// </summary>
public sealed class ExpandToggle : System.Windows.Controls.Primitives.ToggleButton
{
    public static readonly DependencyProperty ChevronAngleProperty = DependencyProperty.Register(
        nameof(ChevronAngle), typeof(double), typeof(ExpandToggle), new PropertyMetadata(0.0));

    /// <summary>Drehung des Winkels: 0 zugeklappt, 180 aufgeklappt.</summary>
    public double ChevronAngle
    {
        get => (double)GetValue(ChevronAngleProperty);
        set => SetValue(ChevronAngleProperty, value);
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        Turn(180);
        Raise(false, true);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        Turn(0);
        Raise(true, false);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.F4
            || (key == System.Windows.Input.Key.Down && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)))
        {
            OnToggle();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ExpandTogglePeer(this);

    private void Turn(double angle)
    {
        if (IsLoaded && LibraryMotion.Instance.IsFull)
        {
            var turn = new System.Windows.Media.Animation.DoubleAnimation(angle, TimeSpan.FromMilliseconds(160)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
            turn.Completed += (_, _) => ChevronAngle = angle;
            BeginAnimation(ChevronAngleProperty, turn);
        }
        else
        {
            BeginAnimation(ChevronAngleProperty, null);
        }

        ChevronAngle = angle;
    }

    private void Raise(bool oldExpanded, bool newExpanded)
    {
        if (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(this) is ExpandTogglePeer peer)
        {
            peer.RaisePropertyChangedEvent(
                System.Windows.Automation.ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                oldExpanded ? System.Windows.Automation.ExpandCollapseState.Expanded : System.Windows.Automation.ExpandCollapseState.Collapsed,
                newExpanded ? System.Windows.Automation.ExpandCollapseState.Expanded : System.Windows.Automation.ExpandCollapseState.Collapsed);
        }
    }

    /// <summary>Umschalten wie jeder ToggleButton und dazu Auf-/Zuklappen (aufgeklappt = angehakt).</summary>
    private sealed class ExpandTogglePeer(ExpandToggle owner)
        : System.Windows.Automation.Peers.ToggleButtonAutomationPeer(owner), System.Windows.Automation.Provider.IExpandCollapseProvider
    {
        public System.Windows.Automation.ExpandCollapseState ExpandCollapseState => owner.IsChecked == true
            ? System.Windows.Automation.ExpandCollapseState.Expanded
            : System.Windows.Automation.ExpandCollapseState.Collapsed;

        public void Expand()
        {
            ThrowIfDisabled();
            owner.SetCurrentValue(IsCheckedProperty, true);
        }

        public void Collapse()
        {
            ThrowIfDisabled();
            owner.SetCurrentValue(IsCheckedProperty, false);
        }

        public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface patternInterface) =>
            patternInterface == System.Windows.Automation.Peers.PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        protected override string GetClassNameCore() => nameof(ExpandToggle);

        private void ThrowIfDisabled()
        {
            if (!IsEnabled())
            {
                throw new System.Windows.Automation.ElementNotEnabledException();
            }
        }
    }
}

/// <summary>Textzeile, die einen Pfad in der Mitte kürzt, sobald er nicht passt (gemessen mit FormattedText).</summary>
public sealed class MiddleEllipsisText : TextBlock
{
    public static readonly DependencyProperty FullTextProperty = DependencyProperty.Register(
        nameof(FullText), typeof(string), typeof(MiddleEllipsisText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((MiddleEllipsisText)d).Update(((MiddleEllipsisText)d).ActualWidth)));

    public MiddleEllipsisText()
    {
        TextWrapping = TextWrapping.NoWrap;

        // Mit Kürzung misst ein TextBlock höchstens den verfügbaren Platz; sonst ordnet WPF ihn in voller Textbreite an,
        // schneidet nur ab und SizeChanged meldet nie die schmalere Spalte (Ende des Pfads unter der Herkunft verdeckt).
        TextTrimming = TextTrimming.CharacterEllipsis;

        // MeasureOverride von TextBlock ist versiegelt: gekürzt wird nach jeder Größenänderung des Platzes.
        SizeChanged += (_, e) => Update(e.NewSize.Width);
    }

    public string FullText
    {
        get => (string)GetValue(FullTextProperty);
        set => SetValue(FullTextProperty, value);
    }

    private void Update(double width)
    {
        var full = FullText ?? string.Empty;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        double Measure(string s) => new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
        var shown = double.IsInfinity(width) || width <= 0 ? full : PathEllipsis.Shorten(full, width, Measure);
        if (!string.Equals(Text, shown, StringComparison.Ordinal))
        {
            Text = shown;
        }

        if (!string.Equals(ToolTip as string, full, StringComparison.Ordinal))
        {
            ToolTip = full;
        }
    }
}

/// <summary>
/// Nit-Skala der Bildschirm-Kapsel (Anmerkung 3 des Nutzers): von 0 bis zur Spitze bei eingeschaltetem HDR
/// (<see cref="System.Windows.Controls.Primitives.RangeBase.Maximum"/> = Spitze, nie 1000), gefüllt bis zur Spitze, mit
/// Marken bei Vollbild und Spitze. Für UI-Automation ein nur lesbarer Wertebereich (ProgressBar).
/// </summary>
public sealed class NitsScaleBar : ProgressBar
{
    public static readonly DependencyProperty FullFrameProperty = DependencyProperty.Register(
        nameof(FullFrame), typeof(double), typeof(NitsScaleBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(NitsScaleBar), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(NitsScaleBar), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkProperty = DependencyProperty.Register(
        nameof(Mark), typeof(Brush), typeof(NitsScaleBar), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly ControlTemplate Empty = CreateEmpty();

    public NitsScaleBar()
    {
        Template = Empty;
        Height = 12;
        Focusable = false;
        IsTabStop = false;
    }

    /// <summary>Vollbild-Helligkeit (Marke auf der Skala).</summary>
    public double FullFrame
    {
        get => (double)GetValue(FullFrameProperty);
        set => SetValue(FullFrameProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public Brush Mark
    {
        get => (Brush)GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var w = ActualWidth;
        if (w <= 0 || Maximum <= 0)
        {
            return;
        }

        var y = (ActualHeight - 6) / 2;
        drawingContext.DrawRoundedRectangle(Track, null, new Rect(0, y, w, 6), 3, 3);
        var fill = Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) * w;
        if (fill > 0)
        {
            drawingContext.DrawRoundedRectangle(Fill, null, new Rect(0, y, fill, 6), 3, 3);
        }

        var full = Math.Clamp(FullFrame / Maximum, 0, 1) * w;
        drawingContext.PushOpacity(0.55);
        drawingContext.DrawRoundedRectangle(Mark, null, new Rect(Math.Max(0, full - 1), y - 3, 2, 12), 1, 1);
        drawingContext.Pop();
        drawingContext.PushOpacity(0.9);
        drawingContext.DrawRoundedRectangle(Mark, null, new Rect(Math.Max(0, w - 2), y - 3, 2, 12), 1, 1);
        drawingContext.Pop();
    }

    private static ControlTemplate CreateEmpty()
    {
        var template = new ControlTemplate(typeof(NitsScaleBar)) { VisualTree = new FrameworkElementFactory(typeof(Grid)) };
        template.Seal();
        return template;
    }
}
