using System.Windows.Media;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Theming;

/// <summary>Thema der Oberfläche (<see cref="AppearanceSettings.Theme"/>).</summary>
public enum ThemePreference
{
    /// <summary>Automatisch: folgt dem App-Modus von Windows.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Duoton (zwei Farben, Glanz abgeleitet) oder Tritonus (drei Farben).</summary>
public enum PaletteMode
{
    Duotone,
    Tritone,
}

/// <summary>Effektstufe (<see cref="AppearanceSettings.EffectLevel"/>).</summary>
public enum EffectLevel
{
    /// <summary>Alle Effekte: Parallaxe, langsamer Zoom, Lichtstreifen, Partikel (nur 2,5 s), Dauer-Effekte höchstens 30 fps.</summary>
    Full,

    /// <summary>Nur kurze Übergänge bis 0,3 s, keine Dauer-Animation.</summary>
    Reduced,

    /// <summary>Keine Bewegung, sofortige Wechsel.</summary>
    Off,
}

/// <summary>Die drei Farben des Schemas: Akzent (Schaltflächen, Auswahl), Atmosphäre (Hintergrund, Leuchten), Glanz (HDR-Moment).</summary>
public readonly record struct PaletteColors(Color Accent, Color Atmosphere, Color Shine);

/// <summary>
/// Wirksame Darstellung zu einem Zeitpunkt (unveränderlich). <see cref="Palette"/> enthält im Duoton den abgeleiteten Glanz;
/// <see cref="EffectLevel"/> ist die wirksame Stufe (Windows-Animationseffekte und Hardware-Darstellung berücksichtigt,
/// siehe <c>EffectPolicy</c>), <see cref="EffectLevelSetting"/> die gewählte.
/// </summary>
public sealed record AppearanceState(
    PaletteColors Palette,
    PaletteMode Mode,
    string PresetId,
    ThemePreference ThemePreference,
    bool IsDark,
    EffectLevel EffectLevelSetting,
    EffectLevel EffectLevel,
    bool ReducedByWindows,
    bool AutoTextOnAccent,
    bool BackgroundFollowsGame,
    bool PauseWhilePlaying,
    bool PauseInBackground,
    bool RespectWindowsAnimations,
    bool MinimizeToTrayAfterLaunch,
    bool LightTaskbar,
    bool ColorsFollowGame = false);

/// <summary>
/// Darstellung der ganzen App (E89, G10.1): Farbschema, Thema, Effektstufe. Erzeugt die Farb-Ressourcen
/// (<see cref="ThemeKeys"/>), färbt Tray- und Fenstersymbol und meldet jede Änderung (auch Windows-Thema, Taskleiste,
/// Animationseffekte) über <see cref="Changed"/> auf dem UI-Thread. Änderungen nur hinzufügend und mit Standardumsetzung.
/// </summary>
public interface IAppearanceService : IDisposable
{
    event EventHandler? Changed;

    AppearanceState Current { get; }

    /// <summary>Ändert <see cref="AppSettings.Appearance"/>, wendet es sofort an und speichert (Seite „Darstellung“).</summary>
    void Update(Action<AppearanceSettings> change);

    /// <summary>„Standard“: alle Werte der Darstellung zurück auf den Auslieferungszustand.</summary>
    void ResetToDefaults();
}

/// <summary>
/// Darstellung ohne Konfiguration und ohne Ressourcen (Tests, Gerüst): rechnet den Zustand aus eigenen Einstellungen,
/// <see cref="Update"/> wirkt sofort und meldet <see cref="Changed"/>. Fenster, Thema und Windows-Optionen bleiben unberührt.
/// </summary>
public sealed class StaticAppearanceService(AppearanceSettings? settings = null, bool isDark = true, bool windowsAnimationsOn = true, bool hardwareRendering = true) : IAppearanceService
{
    private AppearanceSettings _settings = settings ?? new AppearanceSettings();

    public event EventHandler? Changed;

    public AppearanceSettings Settings => _settings;

    public bool IsDark { get; set; } = isDark;

    public bool WindowsAnimationsOn { get; set; } = windowsAnimationsOn;

    public bool HardwareRendering { get; set; } = hardwareRendering;

    public AppearanceState Current => AppearanceService.Compute(_settings, IsDark, WindowsAnimationsOn, HardwareRendering, lightTaskbar: false);

    public void Update(Action<AppearanceSettings> change)
    {
        change(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToDefaults()
    {
        _settings = new AppearanceSettings();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Meldet eine Änderung von außen (etwa Windows-Animationseffekte umgestellt).</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}
