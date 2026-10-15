using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Services;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Darstellung zur Laufzeit (E89, G10.1/G10.2): erzeugte Ressourcen direkt hinter den Token-Dateien, Neuberechnung bei
/// Farbwechsel, Thema an ThemeManager weitergereicht, Windows-Animationseffekte und Taskleiste im Zustand, Fenstersymbol in den
/// Palettenfarben. In einem eigenen Ressourcen-Wörterbuch statt der Anwendung, damit andere Tests unberührt bleiben.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class ThemeAppearanceServiceTests
{
    private static readonly Uri DarkTokens = new("pack://application:,,,/HdrPilot;component/Themes/Tokens.Dark.xaml", UriKind.Absolute);

    [Fact]
    public void Theme_service_inserts_generated_resources_after_the_tokens_and_removes_them_on_dispose()
    {
        WpfTestHost.Run(() =>
        {
            var (service, target, _) = Create();
            var merged = target.MergedDictionaries;
            Assert.Equal(3, merged.Count);
            Assert.Equal(DarkTokens, merged[0].Source);
            Assert.Same(service.Resources, merged[1]); // direkt hinter den Tokens, vor allem anderen
            Assert.True(merged[1].Contains(ThemeResources.MarkerKey));
            Assert.Equal(PalettePresets.Default.Colors.Accent, (Color)merged[1][ThemeKeys.ColorC1]);

            service.Dispose();
            Assert.Equal(2, merged.Count);
            Assert.DoesNotContain(merged, d => d.Contains(ThemeResources.MarkerKey));
        });
    }

    [Fact]
    public void Theme_service_recolors_on_a_preset_change_and_reports_it_once()
    {
        WpfTestHost.Run(() =>
        {
            var (service, target, icons) = Create();
            var changes = 0;
            service.Changed += (_, _) => changes++;
            var before = service.Resources;

            service.Update(s => s.Preset = "cyber");
            WpfTestHost.DoEvents();

            var cyber = PalettePresets.Find("cyber")!.Colors;
            Assert.Equal(1, changes);
            Assert.Equal("cyber", service.Current.PresetId);
            Assert.NotSame(before, service.Resources);
            Assert.Equal(cyber.Accent, (Color)target.MergedDictionaries[1][ThemeKeys.ColorC1]);
            Assert.Equal(cyber.Accent, icons.Spec.Colors.Accent); // Fenstersymbol in den neuen Farben
            service.Dispose();
        });
    }

    [Fact]
    public void Theme_service_keeps_the_resources_when_only_the_effect_level_changes()
    {
        WpfTestHost.Run(() =>
        {
            var (service, _, _) = Create();
            var before = service.Resources;
            var changes = 0;
            service.Changed += (_, _) => changes++;

            service.Update(s => s.EffectLevel = "Reduced");
            WpfTestHost.DoEvents();
            Assert.Equal(EffectLevel.Reduced, service.Current.EffectLevel);
            Assert.Equal(1, changes);
            Assert.Same(before, service.Resources);

            // Gegenprobe: Textfarbe auf Akzent ändert die Farben, also neue Ressourcen.
            service.Update(s => s.AutoTextOnAccent = false);
            WpfTestHost.DoEvents();
            Assert.NotSame(before, service.Resources);
            Assert.Equal(Colors.White, ((SolidColorBrush)service.Resources![ThemeKeys.BrushTextOnAccent]).Color);
            service.Dispose();
        });
    }

    [Fact]
    public void Theme_choice_is_handed_to_the_theme_manager()
    {
        WpfTestHost.Run(() =>
        {
            var (service, _, _) = Create();
            try
            {
                Assert.Null(ThemeManager.Preference); // Automatisch: wie Windows
                service.Update(s => s.Theme = "Light");
                WpfTestHost.DoEvents();
                Assert.Equal(false, ThemeManager.Preference);
                Assert.Equal(ThemePreference.Light, service.Current.ThemePreference);
                service.Update(s => s.Theme = "Dark");
                WpfTestHost.DoEvents();
                Assert.Equal(true, ThemeManager.Preference);
                service.ResetToDefaults();
                WpfTestHost.DoEvents();
                Assert.Null(ThemeManager.Preference);
            }
            finally
            {
                service.Dispose();
                ThemeManager.Preference = null;
            }
        });
    }

    [Fact]
    public void Theme_state_follows_windows_animations_and_the_taskbar()
    {
        WpfTestHost.Run(() =>
        {
            var (service, _, icons) = Create(windowsAnimations: false, lightTaskbar: true);
            Assert.Equal(EffectLevel.Full, service.Current.EffectLevelSetting);
            Assert.Equal(EffectLevel.Reduced, service.Current.EffectLevel);
            Assert.True(service.Current.ReducedByWindows);
            Assert.True(service.Current.LightTaskbar);
            Assert.True(icons.Spec.LightTaskbar);

            // Gegenprobe: Windows-Option nicht beachten → „Voll“ bleibt wirksam.
            service.Update(s => s.RespectWindowsAnimations = false);
            WpfTestHost.DoEvents();
            Assert.Equal(EffectLevel.Full, service.Current.EffectLevel);
            Assert.False(service.Current.ReducedByWindows);
            service.Dispose();
        });
    }

    [Fact]
    public void Theme_old_configs_without_appearance_load_as_abendrot_duotone_automatic()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(ShellTestFactory.ConfigPath, """{ "version": 1, "settings": { "pollIntervalMs": 1000 }, "games": [] }""");
        var config = ShellTestFactory.NewConfig(fs);
        var state = AppearanceService.Compute(config.Current.Settings.Appearance, true, true, true, false);

        Assert.Equal(PalettePresets.DefaultId, state.PresetId);
        Assert.Equal(PaletteMode.Duotone, state.Mode);
        Assert.Equal(ThemePreference.System, state.ThemePreference);
        Assert.Equal(EffectLevel.Full, state.EffectLevel);
        Assert.True(state.AutoTextOnAccent && state.BackgroundFollowsGame && state.PauseWhilePlaying && state.PauseInBackground && state.RespectWindowsAnimations);
        Assert.False(state.MinimizeToTrayAfterLaunch);
        Assert.Equal(PalettePresets.DerivedShine(PalettePresets.Default.Colors.Atmosphere), state.Palette.Shine);
    }

    [Theory]
    [InlineData(EffectLevel.Full, true, true, 900)]
    [InlineData(EffectLevel.Reduced, true, true, 300)]
    [InlineData(EffectLevel.Off, true, true, 0)]
    [InlineData(EffectLevel.Full, false, true, 0)]
    [InlineData(EffectLevel.Full, true, false, 0)]
    public void Theme_recolor_blends_only_with_a_visible_window_and_the_same_theme(EffectLevel level, bool visible, bool sameTheme, int milliseconds) =>
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), AppearanceService.TransitionFor(level, visible, sameTheme));

    private static (AppearanceService Service, ResourceDictionary Target, WindowIconSource Icons) Create(bool windowsAnimations = true, bool lightTaskbar = false)
    {
        var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
        var target = new ResourceDictionary();
        target.MergedDictionaries.Add(new ResourceDictionary { Source = DarkTokens });
        target.MergedDictionaries.Add(new ResourceDictionary { ["Hp.Test.After"] = 1 });
        var icons = new WindowIconSource();
        var service = new AppearanceService(
            config, WpfTestHost.Dispatcher, app: null, icons, lightTaskbar: () => lightTaskbar, windowsAnimations: () => windowsAnimations, hardwareRendering: () => true, resources: target);
        return (service, target, icons);
    }
}
