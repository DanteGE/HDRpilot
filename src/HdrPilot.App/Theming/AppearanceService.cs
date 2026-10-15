using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Theming;

/// <summary>
/// Darstellung der App (E89). Gerüst: berechnet den wirksamen Zustand aus <see cref="AppSettings.Appearance"/> (Vorlage
/// oder eigene Farben, Duoton/Tritonus, Thema, Effektstufe) und schaltet hell/dunkel über <see cref="ThemeManager"/>.
/// Die erzeugten Farb-Ressourcen aus der Palette, Tray- und Fenstersymbol in den Palettenfarben und die Taskleisten-Erkennung
/// folgen mit G10.1/G10.2.
/// </summary>
public sealed class AppearanceService : IAppearanceService
{
    private readonly ConfigService _config;
    private readonly Dispatcher _dispatcher;
    private readonly Application? _app;
    private AppearanceState _current;
    private bool _disposed;

    /// <param name="app">Anwendung, deren Ressourcen umgeschaltet werden (null in Tests: nur rechnen).</param>
    public AppearanceService(ConfigService config, Dispatcher dispatcher, Application? app)
    {
        _config = config;
        _dispatcher = dispatcher;
        _app = app;
        _current = Compute(config.Current.Settings.Appearance, isDark: ThemeManager.IsDark, WindowsAnimationsOn(), HardwareRendering(), lightTaskbar: false);
        config.Changed += OnConfigChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Apply();
    }

    public event EventHandler? Changed;

    public AppearanceState Current => _current;

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
            new PaletteColors(accent, atmosphere, shine),
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
            lightTaskbar);
    }

    private static bool WindowsAnimationsOn() => SystemParameters.ClientAreaAnimation;

    private static bool HardwareRendering() => (RenderCapability.Tier >> 16) > 0;

    // ConfigService.Changed kann von einem Hintergrund-Thread kommen.
    private void OnConfigChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(Apply);

    private void OnThemeChanged(object? sender, EventArgs e) => Recompute();

    private void Apply()
    {
        if (_disposed)
        {
            return;
        }

        var settings = _config.Current.Settings.Appearance;
        var preference = Compute(settings, ThemeManager.IsDark, WindowsAnimationsOn(), HardwareRendering(), lightTaskbar: false).ThemePreference;
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

        Recompute();
    }

    private void Recompute()
    {
        if (_disposed)
        {
            return;
        }

        var next = Compute(_config.Current.Settings.Appearance, ThemeManager.IsDark, WindowsAnimationsOn(), HardwareRendering(), lightTaskbar: false);
        if (next == _current)
        {
            return;
        }

        _current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
