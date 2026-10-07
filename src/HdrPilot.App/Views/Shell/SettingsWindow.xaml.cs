using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Shell;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Einstellungen. Öffnet mit weicher Einblendung (gestaffelt nur die obersten Karten); der Schimmer der Akzentlinie
/// läuft nur, solange das Fenster sichtbar und aktiv ist. Beim Schließen werden ViewModel und Ereignisse freigegeben.
/// </summary>
public partial class SettingsWindow : ChromeWindow
{
    private static readonly TimeSpan ShimmerSweep = TimeSpan.FromSeconds(1.8);
    private static readonly TimeSpan ShimmerPeriod = TimeSpan.FromSeconds(6);

    private SettingsViewModel? _model;
    private AmbientAnimation? _ambient;
    private DispatcherTimer? _shimmerPause;

    public SettingsWindow()
    {
        InitializeComponent();

        // Höhe nach Inhalt, aber nie größer als der Arbeitsbereich: auf kleinen Bildschirmen scrollt der Inhalt.
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height * 0.94);
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>Verbindet das Fenster mit seinem ViewModel (vor dem Anzeigen).</summary>
    public void Attach(SettingsViewModel model)
    {
        _model = model;
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ShellMotion.PlayEntrance(Root, [Header, DisplaysSection, SearchSection, AutomationSection, HelpersSection]);
        _ambient = new AmbientAnimation(this, StartShimmer, StopShimmer);
        AccentLine.SizeChanged += OnAccentLineSizeChanged;
    }

    private void OnAccentLineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_ambient?.IsRunning == true && e.WidthChanged)
        {
            StartShimmer();
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        AccentLine.SizeChanged -= OnAccentLineSizeChanged;
        _ambient?.Dispose();
        _ambient = null;
        _shimmerPause?.Stop();
        _shimmerPause = null;
        if (_model is not null)
        {
            _model.CloseRequested -= OnCloseRequested;
            _model.Dispose();
            _model = null;
        }

        DataContext = null;
    }

    /// <summary>
    /// Lichtreflex wandert alle 6 s über die Akzentlinie (1,8 s mit 30 Bildern/s, nur bei aktivem Fenster). In der
    /// Pause dazwischen läuft keine Animation: ein einmaliger Zeitgeber startet den nächsten Durchgang, sodass der
    /// UI-Thread zwischen den Durchgängen ruht.
    /// </summary>
    private void StartShimmer()
    {
        _shimmerPause?.Stop();
        var width = AccentLine.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var sweep = new DoubleAnimation(-Sheen.Width, width, new Duration(ShimmerSweep))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        Timeline.SetDesiredFrameRate(sweep, 30);
        sweep.Completed += OnShimmerCompleted;
        SheenTransform.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    private void OnShimmerCompleted(object? sender, EventArgs e)
    {
        if (_ambient?.IsRunning != true)
        {
            return;
        }

        _shimmerPause ??= CreateShimmerPause();
        _shimmerPause.Start();
    }

    private DispatcherTimer CreateShimmerPause()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = ShimmerPeriod - ShimmerSweep };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_ambient?.IsRunning == true)
            {
                StartShimmer();
            }
        };
        return timer;
    }

    private void StopShimmer()
    {
        _shimmerPause?.Stop();
        SheenTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SheenTransform.X = -Sheen.Width;
    }
}
