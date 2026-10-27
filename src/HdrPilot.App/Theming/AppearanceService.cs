using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Services;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace HdrPilot.App.Theming;

/// <summary>
/// Darstellung der App (E89, G10.1): berechnet den wirksamen Zustand aus <see cref="AppSettings.Appearance"/> (Vorlage oder
/// eigene Farben, Duoton/Tritonus, Thema, Effektstufe), schaltet hell/dunkel über <see cref="ThemeManager"/>, erzeugt die
/// Farb-Ressourcen aus der Palette (<see cref="ThemeResources"/>, direkt hinter den Token-Dateien eingehängt) und färbt das
/// Fenstersymbol. Beobachtet Windows: App-Modus (über ThemeManager), Taskleisten-Thema und „Animationseffekte“. Ein Wechsel
/// der Farben blendet bei offenem Fenster einmalig weich über (0,9 s, bei „Reduziert“ 0,3 s, bei „Aus“ sofort); im Leerlauf
/// läuft nichts.
/// </summary>
public sealed class AppearanceService : IAppearanceService
{
    /// <summary>Dauer des weichen Farbwechsels bei „Voll“ (Prototyp: transition --c1 .9s).</summary>
    public static readonly TimeSpan RecolorDuration = TimeSpan.FromSeconds(0.9);

    private readonly ConfigService _config;
    private readonly Dispatcher _dispatcher;
    private readonly Application? _app;
    private readonly ResourceDictionary? _target;
    private readonly WindowIconSource? _icons;
    private readonly ILogger _logger;
    private readonly Func<bool> _lightTaskbar;
    private readonly Func<bool> _windowsAnimations;
    private readonly Func<bool> _hardwareRendering;
    private AppearanceState _current;
    private ThemePalette? _palette;
    private ResourceDictionary? _resources;
    private DispatcherTimer? _settle;
    private bool _disposed;

    /// <param name="app">Anwendung, deren Ressourcen umgeschaltet werden (null in Tests: nur rechnen).</param>
    /// <param name="icons">Fenstersymbol, das die Palettenfarben bekommt (null: keins).</param>
    /// <param name="lightTaskbar">Liest, ob die Taskleiste hell ist (Standard: Windows-Einstellung).</param>
    /// <param name="windowsAnimations">Liest die Windows-Option „Animationseffekte“ (Standard: SystemParameters).</param>
    /// <param name="hardwareRendering">Liest, ob WPF mit der Grafikkarte zeichnet (Standard: RenderCapability).</param>
    /// <param name="resources">Wörterbuch, in das die Farb-Ressourcen kommen (Standard: die der Anwendung; Tests: ein eigenes).</param>
    public AppearanceService(
        ConfigService config,
        Dispatcher dispatcher,
        Application? app,
        WindowIconSource? icons = null,
        ILogger? logger = null,
        Func<bool>? lightTaskbar = null,
        Func<bool>? windowsAnimations = null,
        Func<bool>? hardwareRendering = null,
        ResourceDictionary? resources = null)
    {
        _config = config;
        _dispatcher = dispatcher;
        _app = app;
        _target = resources ?? app?.Resources;
        _icons = icons;
        _logger = logger ?? NullLogger.Instance;
        _lightTaskbar = lightTaskbar ?? ReadLightTaskbar;
        _windowsAnimations = windowsAnimations ?? (() => SystemParameters.ClientAreaAnimation);
        _hardwareRendering = hardwareRendering ?? (() => (RenderCapability.Tier >> 16) > 0);
        _current = Compute(config.Current.Settings.Appearance, ThemeManager.IsDark, _windowsAnimations(), _hardwareRendering(), _lightTaskbar());
        config.Changed += OnConfigChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
        if (app is not null)
        {
            // Windows-Einstellungen wirken sofort (statische Ereignisse: im Dispose abmelden).
            SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        Apply();
    }

    public event EventHandler? Changed;

    public AppearanceState Current => _current;

    /// <summary>Aktuelle Farben des Themas (nach dem ersten Anwenden).</summary>
    public ThemePalette Palette => _palette ?? PaletteEngine.Compute(_current);

    /// <summary>Das eingehängte Wörterbuch (Prüfung); null ohne Anwendung.</summary>
    public ResourceDictionary? Resources => _resources;

    /// <summary>True, solange ein weicher Farbwechsel läuft.</summary>
    public bool IsRecoloring => _settle is not null;

    public void Update(Action<AppearanceSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _config.Update(c => change(c.Settings.Appearance ??= new AppearanceSettings()));
    }

    public void ResetToDefaults() => _config.Update(c => c.Settings.Appearance = new AppearanceSettings());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _config.Changed -= OnConfigChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        if (_app is not null)
        {
            SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        _settle?.Stop();
        _settle = null;
        if (_target is not null && _resources is not null && _dispatcher.CheckAccess())
        {
            _target.MergedDictionaries.Remove(_resources);
        }

        _resources = null;
        Changed = null;
    }

    /// <summary>Wirksamer Zustand aus den Einstellungen (reine Funktion; ungültige Werte gelten als Standard).</summary>
    public static AppearanceState Compute(AppearanceSettings? settings, bool isDark, bool windowsAnimationsOn, bool hardwareRendering, bool lightTaskbar)
    {
        settings ??= new AppearanceSettings();
        var preset = PalettePresets.Find(settings.Preset);
        var custom = string.Equals(settings.Preset, PalettePresets.CustomId, StringComparison.OrdinalIgnoreCase);
        var basis = preset ?? PalettePresets.Default;
        var mode = settings.PaletteMode switch
        {
            _ when string.Equals(settings.PaletteMode, nameof(PaletteMode.Tritone), StringComparison.OrdinalIgnoreCase) => PaletteMode.Tritone,
            _ when string.Equals(settings.PaletteMode, nameof(PaletteMode.Duotone), StringComparison.OrdinalIgnoreCase) => PaletteMode.Duotone,
            _ => basis.DefaultMode,
        };

        var accent = custom && ColorMath.TryParseHex(settings.Accent, out var a) ? a : basis.Colors.Accent;
        var atmosphere = custom && ColorMath.TryParseHex(settings.Atmosphere, out var m) ? m : basis.Colors.Atmosphere;
        Color shine;
        if (mode == PaletteMode.Duotone)
        {
            shine = PalettePresets.DerivedShine(atmosphere);
        }
        else
        {
            shine = custom && ColorMath.TryParseHex(settings.Shine, out var s) ? s : basis.Colors.Shine;
        }

        var theme = settings.Theme switch
        {
            _ when string.Equals(settings.Theme, nameof(ThemePreference.Light), StringComparison.OrdinalIgnoreCase) => ThemePreference.Light,
            _ when string.Equals(settings.Theme, nameof(ThemePreference.Dark), StringComparison.OrdinalIgnoreCase) => ThemePreference.Dark,
            _ => ThemePreference.System,
        };

        var chosen = EffectPolicy.Parse(settings.EffectLevel);
        var effective = EffectPolicy.Effective(chosen, settings.RespectWindowsAnimations, windowsAnimationsOn, hardwareRendering);
        return new AppearanceState(
            new PaletteColors(Opaque(accent), Opaque(atmosphere), Opaque(shine)),
            mode,
            custom ? PalettePresets.CustomId : basis.Id,
            theme,
            isDark,
            chosen,
            effective,
            ReducedByWindows: effective != chosen && hardwareRendering,
            settings.AutoTextOnAccent,
            settings.BackgroundFollowsGame,
            settings.PauseWhilePlaying,
            settings.PauseInBackground,
            settings.RespectWindowsAnimations,
            settings.MinimizeToTrayAfterLaunch,
            lightTaskbar,
            settings.ColorsFollowGame);
    }

    /// <summary>Ob sich die Farben der Ressourcen ändern (Palette, Thema, Textfarbe auf Akzent).</summary>
    public static bool AffectsResources(AppearanceState? before, AppearanceState after) =>
        before is null || before.Palette != after.Palette || before.IsDark != after.IsDark || before.AutoTextOnAccent != after.AutoTextOnAccent;

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

    private static bool ReadLightTaskbar()
    {
        try
        {
            return ThemeManager.ReadSystemTaskbarLight();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    // ConfigService.Changed kann von einem Hintergrund-Thread kommen.
    private void OnConfigChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(Apply);

    private void OnThemeChanged(object? sender, EventArgs e) => Recompute(animate: false);

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            _dispatcher.BeginInvoke(() => Recompute(animate: false));
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color)
        {
            _dispatcher.BeginInvoke(() => Recompute(animate: false));
        }
    }

    private void Apply()
    {
        if (_disposed)
        {
            return;
        }

        var preference = Compute(_config.Current.Settings.Appearance, ThemeManager.IsDark, true, true, false).ThemePreference;
        ThemeManager.Preference = preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => null,
        };
        if (_app is not null)
        {
            ThemeManager.Apply(_app);
        }

        Recompute(animate: true);
    }

    private void Recompute(bool animate)
    {
        if (_disposed)
        {
            return;
        }

        var before = _palette is null ? null : _current;
        var next = Compute(_config.Current.Settings.Appearance, ThemeManager.IsDark, _windowsAnimations(), _hardwareRendering(), _lightTaskbar());
        var resourcesChanged = AffectsResources(before, next);
        if (next == _current && !resourcesChanged)
        {
            return;
        }

        var previous = _palette;
        _current = next;
        if (resourcesChanged)
        {
            _palette = PaletteEngine.Compute(next);
            var duration = animate && previous is not null
                ? TransitionFor(next.EffectLevel, AnyWindowVisible(), previous.IsDark == _palette.IsDark)
                : TimeSpan.Zero;
            ApplyResources(previous, _palette, duration);
        }

        _icons?.SetAppearance(IconPalette.From(PaletteEngine.Compute(next.Palette, dark: true, next.AutoTextOnAccent)), next.LightTaskbar);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Dauer des weichen Farbwechsels: nur bei sichtbarem Fenster und gleichem Thema (hell/dunkel wechselt sofort, wie im
    /// Prototyp); bei „Voll“ 0,9 s, bei „Reduziert“ höchstens 0,3 s, bei „Aus“ sofort.
    /// </summary>
    public static TimeSpan TransitionFor(EffectLevel level, bool windowVisible, bool themeUnchanged) =>
        windowVisible && themeUnchanged ? EffectPolicy.Transition(level, RecolorDuration) : TimeSpan.Zero;

    private bool AnyWindowVisible() => _app?.Windows.Cast<Window>().Any(w => w.IsVisible) == true;

    /// <summary>Hängt das neue Wörterbuch an die Stelle des alten (oder direkt hinter die Token-Datei).</summary>
    private void ApplyResources(ThemePalette? previous, ThemePalette palette, TimeSpan duration)
    {
        if (_target is null)
        {
            return;
        }

        try
        {
            _settle?.Stop();
            _settle = null;
            var next = previous is not null && duration > TimeSpan.Zero
                ? ThemeResources.BuildTransition(previous, palette, duration)
                : ThemeResources.Build(palette);
            Replace(next);
            if (ThemeResources.HasRunningTransitions(next))
            {
                // Nach dem Überblenden die eingefrorene Fassung einhängen: keine Uhren, keine Animationen bleiben übrig.
                var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = duration + TimeSpan.FromMilliseconds(80) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (ReferenceEquals(_settle, timer) && !_disposed)
                    {
                        _settle = null;
                        Replace(ThemeResources.Build(palette));
                    }
                };
                _settle = timer;
                timer.Start();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            _logger.LogError(ex, "Farben der Darstellung ließen sich nicht anwenden; es gelten die Token-Dateien.");
        }
    }

    private void Replace(ResourceDictionary next)
    {
        var merged = _target!.MergedDictionaries;
        var index = _resources is null ? -1 : merged.IndexOf(_resources);
        if (index >= 0)
        {
            merged[index] = next;
        }
        else
        {
            var tokens = merged.Select((d, i) => (d, i)).FirstOrDefault(x => x.d.Source?.OriginalString.Contains("/Themes/Tokens.", StringComparison.Ordinal) == true);
            merged.Insert(tokens.d is null ? 0 : tokens.i + 1, next);
        }

        _resources = next;
    }
}
