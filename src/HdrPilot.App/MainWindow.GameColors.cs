using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.Theming;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Artwork;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App;

/// <summary>
/// „Farben folgen dem Spiel“ (E94): Die Akzente der Bibliothek nehmen die Farben des gewählten Spiels an. Eine eigene
/// Farbebene (<see cref="GameAccentLayer"/>) liegt vor den Ressourcen der App, solange der Schalter an ist und kein hoher
/// Kontrast gilt; Pinsel im Code (Auswahl der Liste, Fensterkante, Naht und Streif der Werkzeugleiste) folgen über
/// <see cref="LiveBrushSet"/>. Überblendet wird im OKLCH-Farbraum zusammen mit den Bildern des Spiels; schnelle Auswahl und
/// gehaltene Tasten hält <see cref="GameAccentScheduler"/> zurück. Graue Spiele bekommen genau das Farbschema, ladende Bilder
/// und eine leere Auswahl behalten die gezeigten Farben. Gelesen werden die Farben nur bei eingeschaltetem Schalter.
/// </summary>
public partial class MainWindow
{
    private const int MapCacheLimit = 64;
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMilliseconds(150);

    private readonly GameAccentScheduler _accentScheduler = new();
    private readonly LiveBrushSet _liveBrushes = new();
    private readonly Dictionary<(GameColors Colors, PaletteMode Mode, Color? Hint), PaletteColors?> _mapCache = [];
    private readonly Stopwatch _accentWatch = Stopwatch.StartNew();
    private PaletteColors _accents = PalettePresets.Default.Colors with { Shine = PalettePresets.DerivedShine(PalettePresets.Default.Colors.Atmosphere) };
    private bool _showingGame;
    private GameAccentLayer? _accentLayer;
    private int _accentLayerGeneration;
    private DispatcherTimer? _accentTimer;
    private DispatcherTimer? _brushSettleTimer;
    private IGameColorsSource? _colorsSource;
    private string? _colorsKey;
    private GameColors? _colors;
    private CancellationTokenSource? _colorsRequest;
    private TimeSpan? _lastKeyRepeat;
    private AppearanceSnapshot? _seenAppearance;

    private Func<TimeSpan>? _accentClock;

    /// <summary>Uhr der Entprellung (Tests setzen ihre eigene).</summary>
    public Func<TimeSpan> AccentClock
    {
        get => _accentClock ?? (() => _accentWatch.Elapsed);
        set => _accentClock = value;
    }

    /// <summary>Nur für Tests: statt des Zeitgebers der Entprellung (Verzögerung, Rückruf).</summary>
    public Action<TimeSpan, Action>? AccentTimerHookForTest { get; set; }

    /// <summary>Zähler der Spielfarben dieses Fensters (Anwenden, Halten, Anfragen; Ebene und Pinsel getrennt).</summary>
    public GameAccentStats AccentStats { get; } = new();

    /// <summary>Gezeigte Akzente (Spielfarben oder Schema).</summary>
    public PaletteColors ShownAccents => _accents;

    /// <summary>Farbebene, solange sie liegt (Tests).</summary>
    public GameAccentLayer? AccentLayer => _accentLayer;

    /// <summary>Bleibende Pinsel im Code (Auswahl, Kante, Werkzeugleiste; Tests).</summary>
    public LiveBrushSet AccentBrushes => _liveBrushes;

    /// <summary>
    /// Kontextmenüs und Tooltips liegen in eigenen Popups: Solange die Farbebene liegt, bekommen die des Bibliotheksfensters sie
    /// beim Öffnen in ihre Ressourcen (sicher auch dann, wenn WPF sie über das Ziel schon fände) und geben sie beim Schließen ab.
    /// </summary>
    private static readonly bool PopupLayerHandlers = RegisterPopupLayerHandlers();

    private bool FollowColors => _viewModel?.Context.Appearance.Current.ColorsFollowGame == true && !_highContrast && _colorsSource is not null;

    /// <summary>In <see cref="Attach"/>, bevor der erste Titel gezeigt wird.</summary>
    private void InitializeGameColors(LibraryViewModel vm)
    {
        _ = PopupLayerHandlers;
        _colorsSource = vm.Context.Artwork as IGameColorsSource;
        var appearance = vm.Context.Appearance.Current;
        _accents = appearance.Palette;
        _seenAppearance = AppearanceSnapshot.Of(appearance);
        vm.Context.Appearance.Changed += OnGameColorsAppearanceChanged;
        PreviewKeyDown += OnAccentKeyDown;
        if (FollowColors)
        {
            _ = GamePalette.PrepareAsync();
        }
    }

    private void DetachGameColors()
    {
        _accentTimer?.Stop();
        _brushSettleTimer?.Stop();
        _colorsRequest?.Cancel();
        _colorsRequest?.Dispose();
        _colorsRequest = null;
        _accentLayer?.Settle();
        _accentLayer?.Dispose();
        _accentLayer = null;
        _liveBrushes.Settle();
        PreviewKeyDown -= OnAccentKeyDown;
        if (_viewModel is { } vm)
        {
            vm.Context.Appearance.Changed -= OnGameColorsAppearanceChanged;
        }
    }

    /// <summary>Partikel des HDR-Moments: Bildfarben bzw. Schema samt den gezeigten Akzenten (Spielfarben, E94).</summary>
    public IReadOnlyList<Color> MomentParticleColors =>
        _palette.ParticleColors(_accents, _viewModel?.Context.Appearance.Current.BackgroundFollowsGame == true);

    /// <summary>Farben des Funkenkranzes am HDR-Schalter (Akzent und Glanz der gezeigten Akzente).</summary>
    public IReadOnlyList<Color> ButtonBurstColors => [_accents.Accent, _accents.Shine];

    /// <summary>Taste gedrückt (aus PreviewKeyDown; Tests): Wiederholungen der letzten 150 ms gelten als „schnell“.</summary>
    public void NoteAccentKey(bool repeat) => _lastKeyRepeat = repeat ? AccentClock() : null;

    private void OnAccentKeyDown(object sender, KeyEventArgs e) => NoteAccentKey(e.IsRepeat);

    private bool KeyRepeating() => _lastKeyRepeat is { } at && AccentClock() - at < RepeatWindow;

    /// <summary>Neue Auswahl (aus <see cref="UpdateHero"/>, vor den Umgebungsfarben).</summary>
    private void OnGameAccentSelection(bool instant)
    {
        if (!FollowColors)
        {
            ApplySchemeAccents(instant);
            return;
        }

        if (_hero is not { } hero)
        {
            return; // leere Auswahl: gezeigte Farben bleiben
        }

        RequestColors(hero);
        var decision = instant ? GameAccentDecision.ApplyNow : _accentScheduler.Selected(AccentClock(), KeyRepeating());
        if (decision.Action == GameAccentAction.Hold)
        {
            StartAccentTimer(decision.DueAt);
            AccentStats.Held++;
            return;
        }

        _accentTimer?.Stop();
        ApplySelectedAccents(instant);
    }

    /// <summary>Farben des Spiels: aus dem Speicher des Bilderdienstes sofort, sonst einmal angefragt.</summary>
    private void RequestColors(HeroViewModel? hero)
    {
        if (hero is null || _colorsSource is not { } source)
        {
            return;
        }

        if (_colorsKey == hero.Key && _colors is not null)
        {
            return;
        }

        _colorsRequest?.Cancel();
        _colorsRequest?.Dispose();
        _colorsRequest = null;
        _colorsKey = hero.Key;
        _colors = null;
        if (source.TryPeek(hero.Item.Config, out var known))
        {
            _colors = known;
            return;
        }

        AccentStats.Requests++;
        _colorsRequest = new CancellationTokenSource();
        ReadColorsAsync(source, hero, _colorsRequest.Token);
    }

    private async void ReadColorsAsync(IGameColorsSource source, HeroViewModel hero, CancellationToken token)
    {
        var key = hero.Key;
        try
        {
            var colors = await source.GetGameColorsAsync(hero.Item.Config, token);
            if (token.IsCancellationRequested || _colorsKey != key)
            {
                return;
            }

            _colors = colors;
            OnAccentInputsArrived();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            EffectsLog?.LogDebug("Spielfarben: nicht gelesen ({Error}).", ex.GetType().Name);
        }
    }

    /// <summary>Farben oder Bilder der Auswahl sind angekommen: anwenden, sofern nicht gerade gehalten wird.</summary>
    private void OnAccentInputsArrived()
    {
        if (FollowColors && _accentScheduler.ColorsArrived().Action == GameAccentAction.ApplyNow)
        {
            ApplySelectedAccents(instant: false);
        }
    }

    /// <summary>Nur für Tests: statt des Zeitgebers, der die Pinsel im Code ohne Farbebene beruhigt (Verzögerung, Rückruf).</summary>
    public Action<TimeSpan, Action>? BrushSettleHookForTest { get; set; }

    /// <summary>Beruhigt die Pinsel im Code nach einer Überblendung ohne Farbebene (Schalter aus, Schema gewechselt).</summary>
    private void StartBrushSettle(TimeSpan delay)
    {
        if (BrushSettleHookForTest is { } hook)
        {
            hook(delay, _liveBrushes.Settle);
            return;
        }

        if (_brushSettleTimer is null)
        {
            _brushSettleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _brushSettleTimer.Tick += (_, _) =>
            {
                _brushSettleTimer?.Stop();
                _liveBrushes.Settle();
            };
        }

        _brushSettleTimer.Stop();
        _brushSettleTimer.Interval = delay;
        _brushSettleTimer.Start();
    }

    private void StartAccentTimer(TimeSpan dueAt)
    {
        var delay = dueAt - AccentClock();
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        if (AccentTimerHookForTest is { } hook)
        {
            hook(delay, OnAccentTimer);
            return;
        }

        if (_accentTimer is null)
        {
            _accentTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _accentTimer.Tick += (_, _) =>
            {
                _accentTimer?.Stop();
                OnAccentTimer();
            };
        }

        _accentTimer.Stop();
        _accentTimer.Interval = delay;
        _accentTimer.Start();
    }

    private void OnAccentTimer()
    {
        var decision = _accentScheduler.Elapsed(AccentClock());
        switch (decision.Action)
        {
            case GameAccentAction.ApplyNow:
                ApplySelectedAccents(instant: false);
                break;
            case GameAccentAction.Hold:
                StartAccentTimer(decision.DueAt);
                break;
        }
    }

    /// <summary>Spielfarben der aktuellen Auswahl anwenden, sobald Farben und Bilder da sind (sonst bleibt, was gezeigt wird).</summary>
    private void ApplySelectedAccents(bool instant)
    {
        if (!FollowColors)
        {
            ApplySchemeAccents(instant);
            return;
        }

        if (_hero is not { } hero || _colorsKey != hero.Key || _colors is not { } colors || hero.Artwork is null)
        {
            return;
        }

        var state = _viewModel!.Context.Appearance.Current;
        UpdateGameAccents(MapCached(colors, state.Mode, GlowHint(state)), instant);
    }

    private void ApplySchemeAccents(bool instant) => UpdateGameAccents(null, instant);

    /// <summary>Gezeigte Farben nach geänderter Umgebung neu anwenden (Thema, hoher Kontrast).</summary>
    private void RefreshShownAccents(bool instant)
    {
        if (!FollowColors)
        {
            ApplySchemeAccents(instant);
        }
        else if (_showingGame)
        {
            UpdateGameAccents(_accents, instant);
        }
        else
        {
            ApplySelectedAccents(instant);
        }
    }

    /// <summary>
    /// Zeigt <paramref name="game"/> (null: das Farbschema): Farbebene einbauen bzw. überblenden, bei ausgeschaltetem Schalter
    /// zum Schema zurück und danach entfernen; Pinsel im Code, Titelbereich und – bei grauem Titelbild – die Umgebung folgen.
    /// </summary>
    private void UpdateGameAccents(PaletteColors? game, bool instant)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        var state = vm.Context.Appearance.Current;
        var follow = FollowColors;
        var duration = instant ? TimeSpan.Zero : _motion?.Transition(AppearanceService.RecolorDuration) ?? TimeSpan.Zero;
        var useGame = follow && game is not null;
        var accents = useGame ? game!.Value : state.Palette;
        var target = useGame ? GamePalette.Compute(accents, state, _dark) : GamePalette.Scheme(state, _dark);
        if (follow)
        {
            if (_accentLayer is null)
            {
                // Unsichtbar einbauen (genau die Werte der App), dann wie jeden Wechsel überblenden.
                _accentLayer = new GameAccentLayer(Dispatcher, GamePalette.Scheme(state, _dark));
                Resources.MergedDictionaries.Insert(0, _accentLayer.Dictionary);
                _accentLayerGeneration++;
            }

            _accentLayer.TransitionTo(target, duration, _liveBrushes.Settle);
        }
        else if (_accentLayer is { } layer)
        {
            var generation = ++_accentLayerGeneration;
            layer.TransitionTo(target, duration, () =>
            {
                _liveBrushes.Settle();
                if (generation == _accentLayerGeneration && !FollowColors && ReferenceEquals(_accentLayer, layer))
                {
                    Resources.MergedDictionaries.Remove(layer.Dictionary);
                    layer.Dispose();
                    _accentLayer = null;
                }
            });
        }
        else if (duration > TimeSpan.Zero)
        {
            StartBrushSettle(duration + GameAccentLayer.SettleMargin); // ohne Ebene (Schema): eigener Zeitgeber
        }

        _showingGame = useGame;
        if (accents == _accents)
        {
            return; // die Ebene hat Thema oder Flächen schon nachgezogen
        }

        _accents = accents;
        Hero.SetPalette(_palette, _accents, _dark);
        ApplyAccentBrushes(duration);
        if (follow && state.BackgroundFollowsGame && GrayArt())
        {
            ApplyAmbient(instant: duration == TimeSpan.Zero); // graue Bilder mischen sich mit den gezeigten Akzenten
        }

        AccentStats.Applied++;
        EffectsLog?.LogDebug(
            "Spielfarben: Akzent aus {Source}, Sicherheit {Confidence:0.00}, Übergang {Duration} ms",
            useGame ? _colors?.AccentSource : GameColorSource.None,
            _colors?.Confidence ?? 0,
            (int)duration.TotalMilliseconds);
    }

    /// <summary>Pinsel im Code, die dem Akzent folgen (Tabelle 5.3 der Planung): Auswahl, Fensterkante, Werkzeugleiste.</summary>
    private void ApplyAccentBrushes(TimeSpan duration)
    {
        ApplyRowSelection(duration);
        ApplyEdgeGlow(accentDuration: duration);
        ApplyToolbarBrushes(duration);
    }

    /// <summary>
    /// Auswahl der Liste: mit Spielfarben dunkel ein Verlauf der Atmosphäre, hell der Akzent zu 14 %; ohne wie bisher (dunkel
    /// die Leuchtfarbe der Umgebung, hell der Akzent des Schemas). Ein bleibender Pinsel; ersetzt nur beim Wechsel hell/dunkel.
    /// </summary>
    private void ApplyRowSelection(TimeSpan duration)
    {
        var glow = FollowColors ? _accents.Atmosphere : _palette.A2;
        Brush target = _dark
            ? new LinearGradientBrush(
                [new GradientStop(AmbientPalette.Alpha(glow, 0.30), 0), new GradientStop(AmbientPalette.Alpha(glow, 0.08), 0.6), new GradientStop(AmbientPalette.Alpha(glow, 0), 1)],
                new Point(0, 0.5),
                new Point(1, 0.5))
            : new SolidColorBrush(ColorMath.WithAlpha(_accents.Accent, 0.14));
        var live = Resources.Contains("Lib.Brush.RowSelection") ? Resources["Lib.Brush.RowSelection"] as Brush : null;
        var result = _liveBrushes.Track(live, target, FollowColors ? duration : TimeSpan.Zero);
        if (!ReferenceEquals(result, live))
        {
            Resources["Lib.Brush.RowSelection"] = result;
        }
    }

    /// <summary>Fensterkante (HDR-Moment): Stopps der Umgebung sofort, der Akzent-Stopp mit der Überblendung.</summary>
    private void ApplyEdgeGlow(TimeSpan accentDuration)
    {
        var p = _palette;
        var target = new LinearGradientBrush(
            [new GradientStop(p.A3, 0), new GradientStop(p.A2, 0.45), new GradientStop(_accents.Accent, 0.75), new GradientStop(p.A3, 1)],
            new Point(0, 0),
            new Point(1, 1));
        var live = EdgeGlow.BorderBrush;
        var result = _liveBrushes.Track(live, target, i => i == 2 ? accentDuration : TimeSpan.Zero);
        if (!ReferenceEquals(result, live))
        {
            EdgeGlow.BorderBrush = result;
        }
    }

    /// <summary>Hinweis für die Atmosphäre: Leuchtfarbe eines echten, nicht grauen Titelbilds, nur wenn beide Schalter an sind.</summary>
    private Color? GlowHint(AppearanceState state) =>
        state.BackgroundFollowsGame
        && _hero?.Artwork is { } art
        && art.Hero is { Origin: not (ArtworkOrigin.Generated or ArtworkOrigin.None) }
        && art.Ambient.Dullness != AmbientDullness.Gray
            ? LibraryArt.Parse(art.Ambient.Glow)
            : null;

    private bool GrayArt() => _hero?.Artwork?.Ambient.Dullness == AmbientDullness.Gray;

    private PaletteColors? MapCached(GameColors colors, PaletteMode mode, Color? hint)
    {
        var key = (colors, mode, hint);
        if (_mapCache.TryGetValue(key, out var known))
        {
            return known;
        }

        if (_mapCache.Count >= MapCacheLimit)
        {
            _mapCache.Clear();
        }

        var mapped = GamePalette.Map(colors, mode, hint);
        _mapCache[key] = mapped;
        return mapped;
    }

    /// <summary>
    /// Darstellung geändert (Schalter, Schema, Modus, Textfarbe, Hintergrund): Umgebung und Akzente sofort neu, ohne
    /// Entprellung. Hell/dunkel kommt über <see cref="OnThemeChanged"/> (nach <see cref="ReadTheme"/>).
    /// </summary>
    private void OnGameColorsAppearanceChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        var now = AppearanceSnapshot.Of(vm.Context.Appearance.Current);
        if (now == _seenAppearance)
        {
            return;
        }

        var switchedOn = now.ColorsFollowGame && _seenAppearance?.ColorsFollowGame != true;
        _seenAppearance = now;
        if (switchedOn)
        {
            _ = GamePalette.PrepareAsync();
        }

        ApplyAmbient(instant: false);
        if (FollowColors)
        {
            _colorsKey = null;
            RequestColors(_hero);
            ApplySelectedAccents(instant: false);
        }
        else
        {
            ApplySchemeAccents(instant: false);
        }
    }

    private static bool RegisterPopupLayerHandlers()
    {
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu), System.Windows.Controls.ContextMenu.OpenedEvent, new RoutedEventHandler(OnPopupOpened));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu), System.Windows.Controls.ContextMenu.ClosedEvent, new RoutedEventHandler(OnPopupClosed));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ToolTip), System.Windows.Controls.ToolTip.OpenedEvent, new RoutedEventHandler(OnPopupOpened));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ToolTip), System.Windows.Controls.ToolTip.ClosedEvent, new RoutedEventHandler(OnPopupClosed));
        return true;
    }

    private static void OnPopupOpened(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement popup && !ReferenceEquals(e.OriginalSource, popup))
        {
            return;
        }

        if (sender is FrameworkElement owner && PlacementOf(owner) is { } target
            && Window.GetWindow(target) is MainWindow { _accentLayer: { } layer }
            && !owner.Resources.MergedDictionaries.Contains(layer.Dictionary))
        {
            RemoveLayers(owner);
            owner.Resources.MergedDictionaries.Add(layer.Dictionary);
        }
    }

    private static void OnPopupClosed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement owner && ReferenceEquals(e.OriginalSource, owner))
        {
            RemoveLayers(owner);
        }
    }

    private static void RemoveLayers(FrameworkElement owner)
    {
        var merged = owner.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Contains(GameAccentLayer.MarkerKey))
            {
                merged.RemoveAt(i);
            }
        }
    }

    private static UIElement? PlacementOf(FrameworkElement popup) => popup switch
    {
        System.Windows.Controls.ContextMenu menu => menu.PlacementTarget,
        System.Windows.Controls.ToolTip tip => tip.PlacementTarget,
        _ => null,
    };

    /// <summary>Was die Spielfarben von der Darstellung lesen (ohne hell/dunkel, das kommt über das Thema).</summary>
    private sealed record AppearanceSnapshot(PaletteColors Palette, PaletteMode Mode, bool AutoTextOnAccent, bool BackgroundFollowsGame, bool ColorsFollowGame)
    {
        public static AppearanceSnapshot Of(AppearanceState s) => new(s.Palette, s.Mode, s.AutoTextOnAccent, s.BackgroundFollowsGame, s.ColorsFollowGame);
    }
}
