using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Linien-Symbol wie im Prototyp (SVG-Pfad auf 24 × 24 Einheiten, Strich 1,8, runde Enden). Strich in der Schriftfarbe
/// (<see cref="TextElement.ForegroundProperty"/>, erbt wie Text), optional gefüllt. Skaliert mit der Größe des Elements;
/// gezeichnet wird einmal je Layout, nichts läuft im Hintergrund.
/// </summary>
public sealed class LineIcon : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(LineIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(LineIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LineIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(LineIcon), new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));

    static LineIcon()
    {
        WidthProperty.OverrideMetadata(typeof(LineIcon), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(LineIcon), new FrameworkPropertyMetadata(16.0));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(LineIcon), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Pfad in Einheiten des 24er-Rasters.</summary>
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Füllung (etwa das Startdreieck); null: nur Linien.</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Strichbreite in Einheiten des 24er-Rasters.</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Data is not { } data || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(ActualWidth, ActualHeight) / 24.0;
        var pen = Foreground is { } brush
            ? new Pen(brush, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }
            : null;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - (24 * scale)) / 2, (ActualHeight - (24 * scale)) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawGeometry(Fill, pen, data);
        drawingContext.Pop();
        drawingContext.Pop();
    }
}
