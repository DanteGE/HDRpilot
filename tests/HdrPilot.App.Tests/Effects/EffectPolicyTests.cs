using HdrPilot.App.Infrastructure.Effects;
using HdrPilot.App.Theming;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Effects;

/// <summary>Regeln der Effekte (E89, G10.6): Stufen, Windows-Animationseffekte, Pausen bei Minimieren, Hintergrund und Spiel.</summary>
public sealed class EffectPolicyTests
{
    private static readonly MotionContext Open = new(WindowVisible: true, Minimized: false, Active: true, GameRunning: false, PauseInBackground: true, PauseWhilePlaying: true);

    [Theory]
    [InlineData(null, EffectLevel.Full)]
    [InlineData("Full", EffectLevel.Full)]
    [InlineData("reduced", EffectLevel.Reduced)]
    [InlineData("Off", EffectLevel.Off)]
    [InlineData("Ultra", EffectLevel.Full)]
    public void Effect_level_is_parsed_robustly(string? value, EffectLevel expected) => Assert.Equal(expected, EffectPolicy.Parse(value));

    [Fact]
    public void Effect_level_respects_windows_animations_and_render_tier()
    {
        Assert.Equal(EffectLevel.Full, EffectPolicy.Effective(EffectLevel.Full, respectWindowsAnimations: true, windowsAnimationsOn: true, hardwareRendering: true));
        Assert.Equal(EffectLevel.Reduced, EffectPolicy.Effective(EffectLevel.Full, true, windowsAnimationsOn: false, hardwareRendering: true));
        Assert.Equal(EffectLevel.Off, EffectPolicy.Effective(EffectLevel.Off, true, windowsAnimationsOn: false, hardwareRendering: true));

        // Gegenprobe: Schalter „Windows-Animationseffekte beachten“ aus, dann bleibt „Voll“; ohne Hardware immer „Aus“.
        Assert.Equal(EffectLevel.Full, EffectPolicy.Effective(EffectLevel.Full, respectWindowsAnimations: false, windowsAnimationsOn: false, hardwareRendering: true));
        Assert.Equal(EffectLevel.Off, EffectPolicy.Effective(EffectLevel.Full, true, true, hardwareRendering: false));
        Assert.Equal(EffectLevel.Reduced, EffectPolicy.Effective(new AppearanceSettings(), windowsAnimationsOn: false, hardwareRendering: true));
    }

    [Fact]
    public void Continuous_effects_pause_when_minimized_hidden_inactive_or_playing()
    {
        Assert.True(EffectPolicy.ContinuousAllowed(EffectLevel.Full, Open));
        Assert.False(EffectPolicy.ContinuousAllowed(EffectLevel.Reduced, Open));
        Assert.False(EffectPolicy.ContinuousAllowed(EffectLevel.Full, Open with { Minimized = true }));
        Assert.False(EffectPolicy.ContinuousAllowed(EffectLevel.Full, Open with { WindowVisible = false }));
        Assert.False(EffectPolicy.ContinuousAllowed(EffectLevel.Full, Open with { Active = false }));
        Assert.False(EffectPolicy.ContinuousAllowed(EffectLevel.Full, Open with { GameRunning = true }));

        // Gegenprobe: mit ausgeschalteten Pausen-Schaltern laufen sie inaktiv bzw. beim Spielen weiter, minimiert nie.
        Assert.True(EffectPolicy.ContinuousAllowed(EffectLevel.Full, Open with { Active = false, PauseInBackground = false }));
        Assert.True(EffectPolicy.ContinuousAllowed(EffectLevel.Full, Open with { GameRunning = true, PauseWhilePlaying = false }));
        Assert.False(EffectPolicy.ContinuousAllowed(EffectLevel.Full, Open with { Minimized = true, PauseInBackground = false }));
    }

    [Fact]
    public void One_shot_effects_need_a_visible_window_and_at_least_reduced()
    {
        Assert.True(EffectPolicy.OneShotAllowed(EffectLevel.Full, Open with { Active = false }));
        Assert.True(EffectPolicy.OneShotAllowed(EffectLevel.Reduced, Open));
        Assert.False(EffectPolicy.OneShotAllowed(EffectLevel.Off, Open));
        Assert.False(EffectPolicy.OneShotAllowed(EffectLevel.Full, Open with { Minimized = true }));
        Assert.False(EffectPolicy.OneShotAllowed(EffectLevel.Full, Open with { GameRunning = true }));
    }

    [Fact]
    public void Transitions_are_capped_by_level()
    {
        var full = TimeSpan.FromSeconds(0.9);
        Assert.Equal(full, EffectPolicy.Transition(EffectLevel.Full, full));
        Assert.Equal(TimeSpan.FromMilliseconds(300), EffectPolicy.Transition(EffectLevel.Reduced, full));
        Assert.Equal(TimeSpan.FromMilliseconds(120), EffectPolicy.Transition(EffectLevel.Reduced, TimeSpan.FromMilliseconds(120)));
        Assert.Equal(TimeSpan.Zero, EffectPolicy.Transition(EffectLevel.Off, full));
        Assert.Equal(30, EffectPolicy.ContinuousFrameRate);
        Assert.Equal(160, EffectPolicy.MaxParticles);
        Assert.Equal(TimeSpan.FromSeconds(2.5), EffectPolicy.HdrMomentDuration);
    }
}
