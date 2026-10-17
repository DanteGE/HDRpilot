using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Filter der Werkzeugleiste als Symbolknopf 40×40 (Vorschau .f-flt, Wunsch des Nutzers 2026-10-17): Symbol aus
/// <see cref="FilterGlyph"/> 2 px unter der Mitte, Zähler-Plakette innen oben rechts (bleibt in 40×40; bei „Neu“ in der
/// Neu-Farbe, solange es Neue gibt und der Filter aus ist; an eine neutrale Plakette). An: Fläche C1 18 %, Rahmen
/// AccentStrong, Hof AccentSoft, und das Symbol wird gefüllt (nicht nur über die Farbe). Übergänge nach der Effektstufe:
/// Voll Fläche und Hof 200 ms, Symbol springt kurz an (0,88 → 1,06 → 1 in 240 ms), der Knopf des Schalters gleitet mit der
/// Federkurve (220 ms); Reduziert 150 ms Überblenden; Aus sofort.
/// </summary>
public sealed class FilterToggle : ToggleButton
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(FilterGlyphKind), typeof(FilterToggle), new PropertyMetadata(FilterGlyphKind.Switch));

    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(FilterToggle), new PropertyMetadata(0, (d, _) => ((FilterToggle)d).UpdateBadge()));

    /// <summary>Zähler in der Neu-Farbe, solange er nicht 0 ist und der Filter aus ist („Neu“).</summary>
    public static readonly DependencyProperty HighlightCountProperty = DependencyProperty.Register(
        nameof(HighlightCount), typeof(bool), typeof(FilterToggle), new PropertyMetadata(false, (d, _) => ((FilterToggle)d).UpdateBadge()));

    private Border? _on;
    private Border? _halo;
    private FilterGlyph? _glyph;
    private FrameworkElement? _iconHost;
    private Border? _badge;
    private TextBlock? _badgeText;

    public FilterGlyphKind Kind
    {
        get => (FilterGlyphKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public bool HighlightCount
    {
        get => (bool)GetValue(HighlightCountProperty);
        set => SetValue(HighlightCountProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _on = GetTemplateChild("PART_On") as Border;
        _halo = GetTemplateChild("PART_Halo") as Border;
        _glyph = GetTemplateChild("PART_Glyph") as FilterGlyph;
        _iconHost = GetTemplateChild("PART_Icon") as FrameworkElement;
        _badge = GetTemplateChild("PART_Badge") as Border;
        _badgeText = GetTemplateChild("PART_BadgeText") as TextBlock;
        if (_iconHost is not null)
        {
            _iconHost.RenderTransform = new ScaleTransform(1, 1);
            _iconHost.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        UpdateState(animate: false);
        UpdateBadge();
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        UpdateState(animate: true);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        UpdateState(animate: true);
    }

    private void UpdateState(bool animate)
    {
        var on = IsChecked == true;
        var level = animate && IsLoaded ? LibraryMotion.Instance.Level : EffectLevel.Off;
        if (!LibraryMotion.Enabled)
        {
            level = EffectLevel.Off;
        }

        var fade = level switch
        {
            EffectLevel.Full => TimeSpan.FromMilliseconds(200),
            EffectLevel.Reduced => TimeSpan.FromMilliseconds(150),
            _ => TimeSpan.Zero,
        };
        To(_on, OpacityProperty, on ? 1 : 0, fade, null);
        To(_halo, OpacityProperty, on ? 0.55 : 0, fade, null);
        To(_glyph, FilterGlyph.OnAmountProperty, on ? 1 : 0, fade, null);

        // Knopf des Schalters: Voll mit Federkurve, sonst springt er.
        var knob = level == EffectLevel.Full ? TimeSpan.FromMilliseconds(220) : TimeSpan.Zero;
        To(_glyph, FilterGlyph.KnobPositionProperty, on ? 1 : 0, knob, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });
        if (on && level == EffectLevel.Full && _iconHost?.RenderTransform is ScaleTransform scale)
        {
            var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(240), FillBehavior = FillBehavior.Stop };
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(0.88, KeyTime.FromPercent(0)));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromPercent(0.55), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            Timeline.SetDesiredFrameRate(pop, 60);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        UpdateBadge();
    }

    private void UpdateBadge()
    {
        if (_badge is null || _badgeText is null)
        {
            return;
        }

        _badgeText.Text = Count.ToString(CultureInfo.CurrentCulture);
        var on = IsChecked == true;
        if (on)
        {
            _badge.SetResourceReference(Border.BackgroundProperty, ThemeKeys.BrushText);
            _badge.BorderThickness = default;
            _badgeText.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.BrushMenu);
        }
        else if (HighlightCount && Count > 0)
        {
            _badge.SetResourceReference(Border.BackgroundProperty, "Lib.Brush.NewBadge");
            _badge.BorderThickness = default;
            _badgeText.SetResourceReference(TextBlock.ForegroundProperty, "Lib.Brush.NewBadgeText");
        }
        else
        {
            _badge.SetResourceReference(Border.BackgroundProperty, "Lib.Brush.CountBadge");
            _badge.BorderThickness = new Thickness(1);
            _badgeText.SetResourceReference(TextBlock.ForegroundProperty, Count == 0 ? ThemeKeys.BrushTextMuted : ThemeKeys.BrushTextSecondary);
        }
    }

    private static void To(UIElement? element, DependencyProperty property, double value, TimeSpan duration, IEasingFunction? ease)
    {
        if (element is null)
        {
            return;
        }

        if (duration <= TimeSpan.Zero)
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, value);
            return;
        }

        // Vom sichtbaren Wert aus (auch mitten in einer Bewegung); danach gilt der Grundwert, keine Uhr bleibt stehen.
        var from = (double)element.GetValue(property);
        var animation = new DoubleAnimation(from, value, duration) { EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, 60);
        element.SetValue(property, value);
        element.BeginAnimation(property, animation);
    }
}
