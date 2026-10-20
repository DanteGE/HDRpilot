using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Farbwähler für eigene Farben (E89): Sättigung/Helligkeit (Feld), Farbton (Leiste) und Hex-Eingabe; <see cref="Color"/>
/// ist in beide Richtungen bindbar. Esc schließt nur den Farbwähler (Befehl <c>CancelPickerCommand</c> des Kontexts), nie die
/// Einstellungen.
/// </summary>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(ColorPicker),
        new FrameworkPropertyMetadata(Colors.Red, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    private double _hue;
    private double _saturation = 1;
    private double _value = 1;
    private bool _updating;

    public ColorPicker()
    {
        InitializeComponent();
        Field.GotKeyboardFocus += (_, _) => FieldFocus.Opacity = 1;
        Field.LostKeyboardFocus += (_, _) => FieldFocus.Opacity = 0;
        Hue.GotKeyboardFocus += (_, _) => HueFocus.Opacity = 1;
        Hue.LostKeyboardFocus += (_, _) => HueFocus.Opacity = 0;
        SizeChanged += (_, _) => UpdateVisuals();
        Loaded += (_, _) => FromColor(Color, updateText: true);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Farbton (0–360), Sättigung und Hellwert (0–1) einer Farbe.</summary>
    public static (double H, double S, double V) ToHsv(Color c)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * (((b - r) / d) + 2) : 60 * (((r - g) / d) + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }

    /// <summary>Deckende Farbe aus Farbton, Sättigung und Hellwert.</summary>
    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static byte B(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
        return Color.FromRgb(B(r + m), B(g + m), B(b + m));
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (!picker._updating)
        {
            picker.FromColor((Color)e.NewValue, updateText: true);
        }
    }

    private void FromColor(Color color, bool updateText)
    {
        var (h, s, v) = ToHsv(color);

        // Grau und Schwarz haben keinen Farbton: den bisherigen behalten, damit die Leiste nicht springt.
        if (s > 0 && v > 0)
        {
            _hue = h;
        }

        _saturation = s;
        _value = v;
        UpdateVisuals();
        if (updateText)
        {
            SetHexText(color);
        }
    }

    private void Commit(bool updateText)
    {
        var color = FromHsv(_hue, _saturation, _value);
        _updating = true;
        try
        {
            Color = color;
        }
        finally
        {
            _updating = false;
        }

        UpdateVisuals();
        if (updateText)
        {
            SetHexText(color);
        }
    }

    private void SetHexText(Color color)
    {
        var text = ColorMath.ToHex(color);
        if (!string.Equals(HexBox.Text, text, StringComparison.OrdinalIgnoreCase))
        {
            HexBox.Text = text;
        }

        HexError.Visibility = Visibility.Collapsed;
    }

    private void UpdateVisuals()
    {
        HueLayer.Background = new SolidColorBrush(FromHsv(_hue, 1, 1));
        var color = FromHsv(_hue, _saturation, _value);
        FieldThumbFill.Color = color;
        Swatch.Color = color;
        var w = Field.ActualWidth;
        var h = Field.ActualHeight;
        Canvas.SetLeft(FieldThumb, (_saturation * w) - (FieldThumb.Width / 2));
        Canvas.SetTop(FieldThumb, ((1 - _value) * h) - (FieldThumb.Height / 2));
        Canvas.SetLeft(HueThumb, (_hue / 360 * Hue.ActualWidth) - (HueThumb.Width / 2));
    }

    private void OnFieldDown(object sender, MouseButtonEventArgs e)
    {
        Field.Focus();
        Field.CaptureMouse();
        PickField(e.GetPosition(Field));
        e.Handled = true;
    }

    private void OnFieldMove(object sender, MouseEventArgs e)
    {
        if (Field.IsMouseCaptured)
        {
            PickField(e.GetPosition(Field));
        }
    }

    private void PickField(Point p)
    {
        _saturation = Math.Clamp(p.X / Math.Max(1, Field.ActualWidth), 0, 1);
        _value = 1 - Math.Clamp(p.Y / Math.Max(1, Field.ActualHeight), 0, 1);
        Commit(updateText: true);
    }

    private void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        Hue.Focus();
        Hue.CaptureMouse();
        PickHue(e.GetPosition(Hue));
        e.Handled = true;
    }

    private void OnHueMove(object sender, MouseEventArgs e)
    {
        if (Hue.IsMouseCaptured)
        {
            PickHue(e.GetPosition(Hue));
        }
    }

    private void PickHue(Point p)
    {
        _hue = Math.Clamp(p.X / Math.Max(1, Hue.ActualWidth), 0, 1) * 359.9;
        Commit(updateText: true);
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void OnFieldKey(object sender, KeyEventArgs e)
    {
        const double step = 0.02;
        switch (e.Key)
        {
            case Key.Left:
                _saturation = Math.Max(0, _saturation - step);
                break;
            case Key.Right:
                _saturation = Math.Min(1, _saturation + step);
                break;
            case Key.Up:
                _value = Math.Min(1, _value + step);
                break;
            case Key.Down:
                _value = Math.Max(0, _value - step);
                break;
            default:
                return;
        }

        Commit(updateText: true);
        e.Handled = true;
    }

    private void OnHueKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Down:
                _hue = (_hue + 358) % 360;
                break;
            case Key.Right:
            case Key.Up:
                _hue = (_hue + 2) % 360;
                break;
            default:
                return;
        }

        Commit(updateText: true);
        e.Handled = true;
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !HexBox.IsKeyboardFocusWithin)
        {
            return;
        }

        var text = HexBox.Text.Trim();
        if (ColorMath.TryParseHex(text.StartsWith('#') ? text : "#" + text, out var color) && text.TrimStart('#').Length == 6)
        {
            HexError.Visibility = Visibility.Collapsed;
            FromColor(color, updateText: false);
            Commit(updateText: false);
        }
        else
        {
            HexError.Visibility = Visibility.Visible;
        }
    }

    // Esc schließt nur den Farbwähler; Enter übernimmt.
    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        var command = e.Key switch
        {
            Key.Escape => (DataContext as ViewModels.Shell.AppearancePageViewModel)?.CancelPickerCommand,
            Key.Enter => (DataContext as ViewModels.Shell.AppearancePageViewModel)?.ApplyPickerCommand,
            _ => null,
        };
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
