using System.Xml.Linq;
using HdrPilot.App.Theming;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Tests.Theming;

/// <summary>Vertrag der Farb-Ressourcen (E89): jeder Schlüssel steht in beiden Token-Dateien; Zustand aus den Einstellungen.</summary>
public sealed class ThemeKeysTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Tokens.Dark.xaml")]
    [InlineData("Tokens.Light.xaml")]
    public void Theme_token_files_define_every_key(string file)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "HdrPilot.App", "Themes", file);
        var keys = XDocument.Load(path).Root!.Elements()
            .Select(e => (string?)e.Attribute(Xaml + "Key"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var missing = ThemeKeys.All.Where(k => !keys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, file + " ohne: " + string.Join(", ", missing));
        Assert.Contains("Hp.Brush.Accent", ThemeKeys.All); // Gegenprobe: bisherige Schlüssel gehören dazu
    }

    [Fact]
    public void Theme_state_follows_the_settings()
    {
        var state = AppearanceService.Compute(new AppearanceSettings { Preset = "cyber", Theme = "Light", EffectLevel = "Reduced" }, isDark: false, windowsAnimationsOn: true, hardwareRendering: true, lightTaskbar: false);

        Assert.Equal("cyber", state.PresetId);
        Assert.Equal(PaletteMode.Tritone, state.Mode);
        Assert.Equal(ColorMathHex("#FF2BD6"), state.Palette.Shine);
        Assert.Equal(ThemePreference.Light, state.ThemePreference);
        Assert.Equal(EffectLevel.Reduced, state.EffectLevel);
    }

    [Fact]
    public void Theme_state_derives_shine_in_duotone_and_accepts_custom_colors()
    {
        var state = AppearanceService.Compute(new AppearanceSettings { Preset = "custom", Accent = "#112233", Atmosphere = "#445566", PaletteMode = "Duotone" }, true, true, true, false);

        Assert.Equal("custom", state.PresetId);
        Assert.Equal(ColorMathHex("#112233"), state.Palette.Accent);
        Assert.Equal(PalettePresets.DerivedShine(ColorMathHex("#445566")), state.Palette.Shine);

        // Gegenprobe: ungültige eigene Farbe fällt auf die Vorlage zurück, unbekannte Werte auf den Standard.
        var fallback = AppearanceService.Compute(new AppearanceSettings { Preset = "custom", Accent = "rot", Theme = "Neon", EffectLevel = "Ultra" }, true, true, true, false);
        Assert.Equal(PalettePresets.Default.Colors.Accent, fallback.Palette.Accent);
        Assert.Equal(ThemePreference.System, fallback.ThemePreference);
        Assert.Equal(EffectLevel.Full, fallback.EffectLevel);
    }

    private static System.Windows.Media.Color ColorMathHex(string hex) => ColorMath.FromHex(hex);

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HdrPilot.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("HdrPilot.slnx nicht gefunden.");
    }
}
