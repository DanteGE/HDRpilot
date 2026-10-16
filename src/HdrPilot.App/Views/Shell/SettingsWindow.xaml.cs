using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Shell;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Einstellungen im Showcase-Stil (E89, G10.8): Navigation links springt zu Ankern im einen scrollenden Inhalt und zeigt,
/// welcher Bereich gerade oben steht. Öffnet mit weicher Einblendung (gestaffelt nur die obersten Karten). Dauer-Effekte der
/// Seite „Darstellung“ laufen nur nach den Regeln der Effekte (<see cref="SettingsMotion"/>). Beim Schließen werden
/// ViewModel, Ereignisse, Animationen und die Passwortfelder freigegeben.
/// </summary>
public partial class SettingsWindow : ChromeWindow
{
    /// <summary>Unter dieser Fensterhöhe weicht die Bibliothekskarte der Navigation.</summary>
    public const double LibraryCardMinHeight = 720;

    private static readonly TimeSpan PreviewMoment = TimeSpan.FromSeconds(1.6);
    private static readonly TimeSpan PreviewFade = TimeSpan.FromSeconds(1.2);

    private readonly List<(Button Nav, FrameworkElement Target)> _anchors;
    private SettingsViewModel? _model;
    private SettingsMotion? _motion;

    public SettingsWindow()
    {
        InitializeComponent();

        // Startgröße wie im Prototyp, nie größer als der Arbeitsbereich: auf kleinen Bildschirmen scrollt der Inhalt.
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, work.Width * 0.94));
        Height = Math.Max(MinHeight, Math.Min(Height, work.Height * 0.94));
        MaxHeight = Math.Max(MinHeight, work.Height + 16);

        _anchors =
        [
            (NavGeneral, GroupGeneral),
            (NavDisplays, DisplaysSection),
            (NavAutomation, AutomationSection),
            (NavSearchFolders, SearchSection),
            (NavHelpers, HelpersSection),
            (NavAppearance, GroupAppearance),
            (NavAppearanceColors, SectionPalette),
            (NavAppearanceArtwork, SectionArtwork),
            (NavAppearanceTray, SectionTray),
            (NavAdvanced, GroupAdvanced),
            (NavLogs, LogsSection),
        ];
        SettingsSection.SetIsCurrent(NavGeneral, true);
        var now = DateTime.Now;
        ClockTime.Text = now.ToString("t", CultureInfo.CurrentCulture);
        ClockDate.Text = now.ToString("d", CultureInfo.CurrentCulture);
        Loaded += OnLoaded;
        Closed += OnClosed;
        SizeChanged += OnWindowSizeChanged;
        PageContent.SizeChanged += OnPageSizeChanged;
    }

    /// <summary>Navigationseintrag, dessen Bereich gerade oben steht (Prüfung).</summary>
    public string CurrentSection => _anchors.FirstOrDefault(a => SettingsSection.GetIsCurrent(a.Nav)).Nav?.Name ?? NavGeneral.Name;

    /// <summary>Laufen gerade Dauer-Effekte (Vorführkarte, Zoom der Vorschau)?</summary>
    public bool ContinuousEffectsRunning => _motion?.IsRunning == true;

    /// <summary>Verbindet das Fenster mit seinem ViewModel (vor dem Anzeigen).</summary>
    public void Attach(SettingsViewModel model)
    {
        _model = model;
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        model.HdrList.PasswordClearRequested += OnPasswordClearRequested;
        model.Appearance.SteamGridDbKeyClearRequested += OnSteamGridDbKeyClearRequested;
        model.Appearance.PropertyChanged += OnAppearancePropertyChanged;
    }

    /// <summary>Springt zum Bereich eines Navigationseintrags (Name wie „NavAppearance“); false bei unbekanntem Namen.</summary>
    public bool ScrollToSection(string navName)
    {
        var anchor = _anchors.FirstOrDefault(a => string.Equals(a.Nav.Name, navName, StringComparison.Ordinal));
        if (anchor.Nav is null)
        {
            return false;
        }

        ScrollTo(anchor.Target);
        return true;
    }

    /// <summary>PasswordBox ist nicht bindbar: das Bot-Passwort geht nur in diese Richtung ans ViewModel.</summary>
    private void OnPcgwPasswordChanged(object sender, RoutedEventArgs e) => _model?.HdrList.SetPassword(PcgwPassword.Password);

    private void OnPasswordClearRequested(object? sender, EventArgs e) => PcgwPassword.Clear();

    /// <summary>API-Schlüssel von SteamGridDB: nur in diese Richtung, gespeichert wird erst mit „Prüfen“.</summary>
    private void OnSteamGridDbKeyChanged(object sender, RoutedEventArgs e)
    {
        KeyPlaceholder.Visibility = SteamGridDbKey.SecurePassword.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _model?.Appearance.SetSteamGridDbKey(SteamGridDbKey.Password);
    }

    private void OnSteamGridDbKeyClearRequested(object? sender, EventArgs e) => SteamGridDbKey.Clear();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ShellMotion.PlayEntrance(Root, [GroupGeneral, StartupSection, DisplaysSection, AutomationSection]);
        if (_model is { } model)
        {
            model.Appearance.DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _motion = new SettingsMotion(this, () => model.Appearance.State, model.Appearance, StartContinuous, StopContinuous);
        }

        UpdateLibraryCard();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        SizeChanged -= OnWindowSizeChanged;
        PageContent.SizeChanged -= OnPageSizeChanged;
        _motion?.Dispose();
        _motion = null;
        StopPreviewMoment();
        if (_model is not null)
        {
            _model.CloseRequested -= OnCloseRequested;
            _model.HdrList.PasswordClearRequested -= OnPasswordClearRequested;
            _model.Appearance.SteamGridDbKeyClearRequested -= OnSteamGridDbKeyClearRequested;
            _model.Appearance.PropertyChanged -= OnAppearancePropertyChanged;
            _model.Dispose();
            _model = null;
        }

        PcgwPassword.Clear();
        SteamGridDbKey.Clear();
        DataContext = null;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLibraryCard();

    /// <summary>Gruppenköpfe stehen bündig über dem zentrierten Raster (gleicher Rand wie die Spalten).</summary>
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
        {
            return;
        }

        var columns = SettingsGridLayout.Compute(e.NewSize.Width);
        foreach (var header in new[] { GroupGeneral, GroupAppearance, GroupAdvanced })
        {
            var margin = header.Margin;
            header.Margin = new Thickness(columns.Left, margin.Top, columns.Left, margin.Bottom);
        }
    }

    private void UpdateLibraryCard() =>
        LibraryCard.Visibility = ActualHeight <= 0 || ActualHeight >= LibraryCardMinHeight ? Visibility.Visible : Visibility.Collapsed;

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        var anchor = _anchors.FirstOrDefault(a => ReferenceEquals(a.Nav, sender));
        if (anchor.Target is not null)
        {
            ScrollTo(anchor.Target);
        }
    }

    private void ScrollTo(FrameworkElement target)
    {
        Page.UpdateLayout();
        var top = TopOf(target);
        Page.ScrollToVerticalOffset(Math.Max(0, top - 8));
        Page.UpdateLayout();
        MarkCurrent(_anchors.First(a => ReferenceEquals(a.Target, target)).Nav);
    }

    private double TopOf(FrameworkElement element) =>
        element.IsDescendantOf(PageContent) ? element.TransformToAncestor(PageContent).Transform(new Point(0, 0)).Y : 0;

    /// <summary>Bereich oben im sichtbaren Teil markieren (am Ende der Seite: der letzte, dessen Anfang zu sehen ist).</summary>
    private void OnPageScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
        {
            return;
        }

        var offset = Page.VerticalOffset;
        var atEnd = Page.ScrollableHeight > 0 && offset >= Page.ScrollableHeight - 1;
        var limit = atEnd ? offset + (Page.ViewportHeight * 0.5) : offset + 64;
        Button? current = null;
        foreach (var (nav, target) in _anchors)
        {
            if (TopOf(target) <= limit)
            {
                current = nav;
            }
        }

        MarkCurrent(current ?? NavGeneral);
    }

    private void MarkCurrent(Button current)
    {
        foreach (var (nav, _) in _anchors)
        {
            SettingsSection.SetIsCurrent(nav, ReferenceEquals(nav, current));
        }
    }

    // ---- Dauer-Effekte der Seite „Darstellung“ (nur nach EffectPolicy, höchstens 30 Bilder/s) ----

    private void StartContinuous()
    {
        DemoSweep.Opacity = 1;
        var sweep = Forever(new DoubleAnimation(-60, 260, new Duration(TimeSpan.FromSeconds(2.6))) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        DemoSweepMove.BeginAnimation(TranslateTransform.XProperty, sweep);
        DemoRise.BeginAnimation(TranslateTransform.YProperty, Forever(new DoubleAnimation(0, -30, new Duration(TimeSpan.FromSeconds(2.2)))));
        DemoParticles.BeginAnimation(OpacityProperty, Forever(new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(2.2)))));

        // Langsamer Zoom der Vorschau (Ken Burns): Maßstab 1,02 → 1,12 und leichte Drift, hin und zurück.
        var zoom = Forever(new DoubleAnimation(1.02, 1.12, new Duration(TimeSpan.FromSeconds(24))) { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        StageZoom.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        StageZoom.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        StageDrift.BeginAnimation(TranslateTransform.XProperty, Forever(new DoubleAnimation(0, -12, new Duration(TimeSpan.FromSeconds(24))) { AutoReverse = true }));
        StageDrift.BeginAnimation(TranslateTransform.YProperty, Forever(new DoubleAnimation(0, -4, new Duration(TimeSpan.FromSeconds(24))) { AutoReverse = true }));
    }

    private void StopContinuous()
    {
        DemoSweepMove.BeginAnimation(TranslateTransform.XProperty, null);
        DemoSweep.Opacity = 0;
        DemoParticles.BeginAnimation(OpacityProperty, null);

        // Die Animation selbst abmelden: Ein nur abgehängtes Transform behielte seine Endlos-Uhr, und WPF zeichnete weiter
        // 30 Bilder pro Sekunde, auch nach dem Schließen des Fensters (gemessen: 1,5 % eines Kerns im Infobereich).
        DemoRise.BeginAnimation(TranslateTransform.YProperty, null);
        DemoParticles.Opacity = 1;
        StageZoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        StageZoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        StageDrift.BeginAnimation(TranslateTransform.XProperty, null);
        StageDrift.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private static DoubleAnimation Forever(DoubleAnimation animation)
    {
        animation.RepeatBehavior = RepeatBehavior.Forever;
        Timeline.SetDesiredFrameRate(animation, EffectPolicy.ContinuousFrameRate);
        return animation;
    }

    // ---- HDR-Moment der Vorschau (einmalig beim Einschalten, ohne HDR zu schalten) ----

    private void OnAppearancePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppearancePageViewModel.PreviewHdr) && _model is { } model)
        {
            PlayPreviewMoment(model.Appearance.PreviewHdr);
        }
    }

    private void PlayPreviewMoment(bool on)
    {
        var level = _model?.Appearance.State.EffectLevel ?? EffectLevel.Off;
        var allowed = _motion?.OneShotAllowed == true;
        var fade = allowed ? EffectPolicy.Transition(level, PreviewFade) : TimeSpan.Zero;
        Fade(StageBoost, on ? 1 : 0, fade);
        Fade(StageBloom, on ? 1 : 0, fade);
        Fade(StageVeil, on ? 0 : 1, fade);
        StageDot.Fill = on ? (Brush)FindResource(ThemeKeys.BrushShine) : new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0xA5));
        if (on && allowed && level == EffectLevel.Full)
        {
            // Lichtstreifen einmal über die Bühne (1,6 s), danach ist nichts mehr angemeldet.
            var width = Math.Max(Stage.ActualWidth, 200);
            var move = new DoubleAnimation(-220, width + 60, new Duration(PreviewMoment)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(move, EffectPolicy.OneShotFrameRate);
            var shine = new DoubleAnimationUsingKeyFrames { Duration = new Duration(PreviewMoment), FillBehavior = FillBehavior.Stop };
            shine.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.1)));
            shine.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.8)));
            shine.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Timeline.SetDesiredFrameRate(shine, EffectPolicy.OneShotFrameRate);
            StageSweepMove.BeginAnimation(TranslateTransform.XProperty, move);
            StageSweep.BeginAnimation(OpacityProperty, shine);
        }
        else
        {
            StopPreviewMoment();
        }
    }

    private void StopPreviewMoment()
    {
        StageSweepMove.BeginAnimation(TranslateTransform.XProperty, null);
        StageSweep.BeginAnimation(OpacityProperty, null);
        StageSweep.Opacity = 0;
    }

    private static void Fade(UIElement element, double to, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            return;
        }

        var animation = new DoubleAnimation(to, new Duration(duration));
        Timeline.SetDesiredFrameRate(animation, EffectPolicy.OneShotFrameRate);
        animation.Completed += (_, _) =>
        {
            // Endwert fest setzen und die Uhr lösen: danach läuft nichts mehr.
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
        };
        element.BeginAnimation(OpacityProperty, animation);
    }
}
