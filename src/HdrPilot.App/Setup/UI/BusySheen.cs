using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Views.Shell;

namespace HdrPilot.App.Setup.UI;

/// <summary>
/// Lichtreflex, der über die Akzentlinie im Kopf läuft, solange Setup oder Entfernen arbeitet. Nur bei sichtbarem,
/// aktivem Fenster und erlaubten Animationen (<see cref="AmbientAnimation"/>), mit 30 Bildern/s; danach steht der
/// Reflex außerhalb der Linie und nichts läuft mehr.
/// </summary>
public sealed class BusySheen : IDisposable
{
    private static readonly Duration Sweep = new(TimeSpan.FromSeconds(1.4));

    private readonly FrameworkElement _line;
    private readonly FrameworkElement _sheen;
    private readonly TranslateTransform _transform;
    private readonly AmbientAnimation _ambient;
    private bool _busy;
    private bool _running;
    private bool _disposed;

    public BusySheen(Window window, FrameworkElement line, FrameworkElement sheen, TranslateTransform transform)
    {
        _line = line;
        _sheen = sheen;
        _transform = transform;
        _ambient = new AmbientAnimation(window, Update, Update);
        line.SizeChanged += OnLineSizeChanged;
    }

    public bool IsBusy
    {
        get => _busy;
        set
        {
            _busy = value;
            Update();
        }
    }

    public bool IsRunning => _running;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _line.SizeChanged -= OnLineSizeChanged;
        _ambient.Dispose();
        Stop();
    }

    private void Update()
    {
        // Wird auch aus dem Konstruktor von AmbientAnimation gerufen, dann ist _ambient noch null.
        var run = !_disposed && _busy && _ambient?.IsRunning == true && _line.ActualWidth > 0;
        if (run == _running)
        {
            return;
        }

        if (run)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void OnLineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_running && e.WidthChanged)
        {
            Start();
        }
        else
        {
            Update();
        }
    }

    private void Start()
    {
        _running = true;
        var sweep = new DoubleAnimation(-_sheen.Width, _line.ActualWidth, Sweep)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Timeline.SetDesiredFrameRate(sweep, 30);
        _transform.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    private void Stop()
    {
        _running = false;
        _transform.BeginAnimation(TranslateTransform.XProperty, null);
        _transform.X = -_sheen.Width;
    }
}
