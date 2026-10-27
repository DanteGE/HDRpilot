using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Views.Library;

/// <summary>
/// Text, der beim Wechsel weich überblendet (Titel im Kopf der Bibliothek: die gewählte Quelle, Wunsch des Nutzers
/// 2026-10-22). Der alte Text blendet aus, der neue ein, die Breite gleitet vom alten zum neuen Maß (was danach steht,
/// etwa der Pfeil des Quellenmenüs, wandert mit statt zu springen). Nach der Effektstufe: Voll 280 ms mit 4 px Weg nach
/// oben, Reduziert 150 ms nur Deckkraft, Aus (oder Fenster noch nicht geladen) sofort. Ein neuer Wechsel mitten in der
/// Überblendung beginnt bei der Breite, die gerade zu sehen ist. Schrift, Größe und Farbe erben die beiden Textzeilen
/// (TextElement.* am Element oder darüber). Nach dem Ende hängt keine Uhr mehr an dem Element.
/// </summary>
public sealed class CrossfadeText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CrossfadeText), new PropertyMetadata(string.Empty, (d, e) => ((CrossfadeText)d).OnTextChanged((string?)e.OldValue)));

    /// <summary>Fortschritt der Überblendung (0: alter Text, 1: neuer Text); nur während einer Überblendung animiert.</summary>
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(CrossfadeText),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((CrossfadeText)d).ApplyProgress()));

    private const double Rise = 4;

    private readonly TextBlock _front = new();
    private readonly TextBlock _back = new() { Visibility = Visibility.Collapsed };
    private readonly TranslateTransform _frontShift = new();
    private readonly TranslateTransform _backShift = new();
    private double _fromWidth;
    private bool _moves;
    private bool _animating;
    private int _generation;

    public CrossfadeText()
    {
        _front.RenderTransform = _frontShift;
        _back.RenderTransform = _backShift;
        _back.IsHitTestVisible = false;
        foreach (var block in new[] { _back, _front })
        {
            AddLogicalChild(block);
            AddVisualChild(block);
        }
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Dauer der letzten Überblendung (0: sofort; Tests und Prüfung).</summary>
    public TimeSpan LastTransition { get; private set; }

    /// <summary>Der ausblendende alte Text (leer ohne Überblendung; Tests).</summary>
    public string FadingText => _back.Visibility == Visibility.Visible ? _back.Text : string.Empty;

    /// <summary>Eine Überblendung läuft (Tests).</summary>
    public bool IsAnimating => _animating;

    protected override int VisualChildrenCount => 2;

    protected override IEnumerator LogicalChildren => new[] { _back, _front }.GetEnumerator();

    /// <summary>Überblendung für diese Effektstufe: Dauer und ob sich die Zeilen dabei bewegen (nur „Voll“).</summary>
    public static (TimeSpan Duration, bool Moves) TransitionFor(EffectLevel level, bool enabled) => !enabled ? (TimeSpan.Zero, false) : level switch
    {
        EffectLevel.Full => (TimeSpan.FromMilliseconds(280), true),
        EffectLevel.Reduced => (TimeSpan.FromMilliseconds(150), false),
        _ => (TimeSpan.Zero, false),
    };

    protected override Visual GetVisualChild(int index) => index == 0 ? _back : _front;

    protected override Size MeasureOverride(Size availableSize)
    {
        var free = new Size(double.PositiveInfinity, availableSize.Height);
        _front.Measure(free);
        _back.Measure(free);
        var width = _front.DesiredSize.Width;
        var p = Progress;
        if (_animating && p < 1)
        {
            width = _fromWidth + ((width - _fromWidth) * p); // Progress läuft schon mit der Kurve
        }

        return new Size(width, Math.Max(_front.DesiredSize.Height, _back.Visibility == Visibility.Visible ? _back.DesiredSize.Height : 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Jede Zeile in ihrer ganzen Breite (keine Kürzung während die Breite gleitet).
        _front.Arrange(new Rect(new Point(0, 0), new Size(Math.Max(finalSize.Width, _front.DesiredSize.Width), finalSize.Height)));
        _back.Arrange(new Rect(new Point(0, 0), new Size(Math.Max(finalSize.Width, _back.DesiredSize.Width), finalSize.Height)));
        return finalSize;
    }

    private void OnTextChanged(string? old)
    {
        var text = Text ?? string.Empty;
        var generation = ++_generation;
        var (duration, moves) = TransitionFor(LibraryMotion.Instance.Level, LibraryMotion.Enabled && IsLoaded);
        if (duration <= TimeSpan.Zero || string.IsNullOrEmpty(old) || string.Equals(old, text, StringComparison.Ordinal))
        {
            _front.Text = text;
            _back.Text = string.Empty;
            LastTransition = TimeSpan.Zero;
            Finish();
            return;
        }

        // Von dem aus, was gerade zu sehen ist: Breite wie gezeichnet, ausblenden der Text, der vorne stand.
        _fromWidth = ActualWidth > 0 ? ActualWidth : _front.DesiredSize.Width;
        _back.Text = _front.Text;
        _front.Text = text;
        _moves = moves;
        _animating = true;
        LastTransition = duration;
        var animation = new DoubleAnimation(0, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, 60);
        animation.Completed += (_, _) =>
        {
            if (generation == _generation)
            {
                Finish();
            }
        };
        Progress = 1; // Grundwert: Nach dem Ende gilt der neue Text.
        BeginAnimation(ProgressProperty, animation);
        ApplyProgress();
        InvalidateMeasure();
    }

    /// <summary>Endzustand: nur der neue Text, volle Deckkraft, ohne Weg, keine Uhr mehr am Element.</summary>
    private void Finish()
    {
        _animating = false;
        BeginAnimation(ProgressProperty, null);
        Progress = 1;
        ApplyProgress();
        InvalidateMeasure();
    }

    private void ApplyProgress()
    {
        var p = _animating ? Progress : 1;
        _back.Visibility = !_animating || _back.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _front.Opacity = p;
        _back.Opacity = 1 - p;
        _frontShift.Y = _moves && _animating ? Rise * (1 - p) : 0;
        _backShift.Y = _moves && _animating ? -Rise * p : 0;
    }
}
