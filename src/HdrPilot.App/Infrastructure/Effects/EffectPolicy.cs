using HdrPilot.App.Theming;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Infrastructure.Effects;

/// <summary>
/// Lage eines Fensters für die Effekte: sichtbar, minimiert, aktiv, läuft ein Spiel, dazu die beiden Schalter der
/// Darstellung. Geschlossene Fenster haben keine Lage (dort läuft nie etwas).
/// </summary>
public readonly record struct MotionContext(
    bool WindowVisible,
    bool Minimized,
    bool Active,
    bool GameRunning,
    bool PauseInBackground,
    bool PauseWhilePlaying);

/// <summary>
/// Regeln der Effekte (E89, G10.6), als reine Funktionen für Bibliothek, Einstellungen und Tests:
/// <list type="bullet">
/// <item>Wirksame Stufe: ohne Hardware-Darstellung (RenderCapability.Tier 0, Remotedesktop) „Aus“; ist die Windows-Option
/// „Animationseffekte“ aus und soll sie beachtet werden, mindestens „Reduziert“.</item>
/// <item>Dauer-Effekte (langsamer Zoom, Lichtstrahlen, Lichtpunkte, Glanz, Auswahlring) nur bei „Voll“, sichtbarem, nicht
/// minimiertem Fenster, aktiv (oder „Im Hintergrund anhalten“ aus) und ohne laufendes Spiel (oder „Beim Spielen anhalten“
/// aus); höchstens <see cref="ContinuousFrameRate"/> Bilder pro Sekunde.</item>
/// <item>Einmal-Effekte (HDR-Moment, Spielwechsel) ab „Reduziert“, nie bei minimiertem oder unsichtbarem Fenster; bei
/// „Reduziert“ höchstens <see cref="ReducedTransition"/>, ohne Partikel und Lichtstreifen.</item>
/// </list>
/// </summary>
public static class EffectPolicy
{
    /// <summary>Höchste Bildrate der Dauer-Effekte (Timeline.DesiredFrameRate).</summary>
    public const int ContinuousFrameRate = 30;

    /// <summary>Bildrate kurzer Einmal-Effekte (HDR-Moment, Spielwechsel).</summary>
    public const int OneShotFrameRate = 60;

    /// <summary>Höchstzahl der Partikel im HDR-Moment.</summary>
    public const int MaxParticles = 160;

    /// <summary>Längste Dauer des HDR-Moments; danach ist keine Bildschleife mehr angemeldet.</summary>
    public static readonly TimeSpan HdrMomentDuration = TimeSpan.FromSeconds(2.5);

    /// <summary>Längster Übergang bei „Reduziert“.</summary>
    public static readonly TimeSpan ReducedTransition = TimeSpan.FromMilliseconds(300);

    /// <summary>Stufe aus der Konfiguration ("Full", "Reduced", "Off"; unbekannt oder fehlend: Voll).</summary>
    public static EffectLevel Parse(string? value) => value switch
    {
        _ when string.Equals(value, nameof(EffectLevel.Reduced), StringComparison.OrdinalIgnoreCase) => EffectLevel.Reduced,
        _ when string.Equals(value, nameof(EffectLevel.Off), StringComparison.OrdinalIgnoreCase) => EffectLevel.Off,
        _ => EffectLevel.Full,
    };

    /// <summary>Wirksame Stufe aus der gewählten, der Windows-Option „Animationseffekte“ und der Hardware-Darstellung.</summary>
    public static EffectLevel Effective(EffectLevel setting, bool respectWindowsAnimations, bool windowsAnimationsOn, bool hardwareRendering)
    {
        if (!hardwareRendering)
        {
            return EffectLevel.Off;
        }

        return respectWindowsAnimations && !windowsAnimationsOn && setting == EffectLevel.Full ? EffectLevel.Reduced : setting;
    }

    /// <summary>Wirksame Stufe aus den gespeicherten Einstellungen.</summary>
    public static EffectLevel Effective(AppearanceSettings settings, bool windowsAnimationsOn, bool hardwareRendering) =>
        Effective(Parse(settings.EffectLevel), settings.RespectWindowsAnimations, windowsAnimationsOn, hardwareRendering);

    /// <summary>Dürfen Dauer-Effekte jetzt laufen?</summary>
    public static bool ContinuousAllowed(EffectLevel effective, MotionContext context) =>
        effective == EffectLevel.Full
        && context.WindowVisible
        && !context.Minimized
        && (context.Active || !context.PauseInBackground)
        && (!context.GameRunning || !context.PauseWhilePlaying);

    /// <summary>Dürfen Einmal-Effekte (HDR-Moment, Spielwechsel) jetzt spielen?</summary>
    public static bool OneShotAllowed(EffectLevel effective, MotionContext context) =>
        effective != EffectLevel.Off
        && context.WindowVisible
        && !context.Minimized
        && (!context.GameRunning || !context.PauseWhilePlaying);

    /// <summary>Dauer eines Übergangs auf dieser Stufe: Voll unverändert, Reduziert höchstens 0,3 s, Aus sofort.</summary>
    public static TimeSpan Transition(EffectLevel effective, TimeSpan full) => effective switch
    {
        EffectLevel.Full => full,
        EffectLevel.Reduced => full < ReducedTransition ? full : ReducedTransition,
        _ => TimeSpan.Zero,
    };
}
