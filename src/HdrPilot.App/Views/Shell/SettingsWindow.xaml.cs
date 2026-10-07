using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.ViewModels.Shell;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Einstellungen. Öffnet mit weicher Einblendung (gestaffelt nur die obersten Karten); der Schimmer der Akzentlinie
/// läuft nur, solange das Fenster sichtbar und aktiv ist. Beim Schließen werden ViewModel und Ereignisse freigegeben.
/// </summary>
public partial class SettingsWindow : ChromeWindow
{
    private SettingsViewModel? _model;
    private AmbientAnimation? _ambient;

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
        if (_model is not null)
        {
            _model.CloseRequested -= OnCloseRequested;
            _model.Dispose();
            _model = null;
        }

        DataContext = null;
    }

    /// <summary>Lichtreflex wandert alle 6 s über die Akzentlinie (30 Bilder/s, nur bei aktivem Fenster).</summary>
    private void StartShimmer()
    {
        var width = AccentLine.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var start = -Sheen.Width;
        var sweep = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6), RepeatBehavior = RepeatBehavior.Forever };
        sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sweep.KeyFrames.Add(new EasingDoubleKeyFrame(width, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(width, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(6))));
        Timeline.SetDesiredFrameRate(sweep, 30);
        sweep.Freeze();
        SheenTransform.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    private void StopShimmer()
    {
        SheenTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SheenTransform.X = -Sheen.Width;
    }
}
