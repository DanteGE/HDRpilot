using System.Text.RegularExpressions;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Leistung (E89, G10.9): In den Dateien der Darstellung, der Einstellungen und des Tray-Menüs gibt es keinen BlurEffect,
/// keinen DropShadowEffect, keine BitmapEffects und keine Mischmodi. Leuchten und Schatten sind vorab gezeichnete Verläufe.
/// Kommentare zählen nicht (dort darf erklärt werden, warum es keine gibt).
/// </summary>
public sealed partial class ThemeEffectScanTests
{
    private static readonly string[] Owned =
    [
        "src/HdrPilot.App/Themes/Controls.xaml",
        "src/HdrPilot.App/Themes/Shell.xaml",
        "src/HdrPilot.App/Themes/Tokens.Dark.xaml",
        "src/HdrPilot.App/Themes/Tokens.Light.xaml",
        "src/HdrPilot.App/App.xaml",
        "src/HdrPilot.App/Theming",
        "src/HdrPilot.App/Views/Shell",
        "src/HdrPilot.App/ViewModels/Shell",
        "src/HdrPilot.App/Infrastructure/ChromeWindow.cs",
        "src/HdrPilot.App/Infrastructure/ThemeManager.cs",
        "src/HdrPilot.App/Infrastructure/Motion.cs",
        "src/HdrPilot.App/Services/TrayService.cs",
        "src/HdrPilot.App/Services/TrayIconRenderer.cs",
        "src/HdrPilot.App/Services/WindowIconRenderer.cs",
        "src/HdrPilot.App/Services/IconImages.cs",
    ];

    [Fact]
    public void Theme_files_use_no_blur_shadow_bitmap_effects_or_blend_modes()
    {
        var root = ThemeTokenFileTests.RepositoryRoot();
        var files = Owned.SelectMany(p =>
        {
            var full = Path.Combine(root, p);
            return Directory.Exists(full)
                ? Directory.EnumerateFiles(full, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
                : [full];
        }).ToList();
        Assert.True(files.Count >= 20, $"nur {files.Count} Dateien gefunden");

        var hits = files
            .SelectMany(f => Forbidden().Matches(StripComments(File.ReadAllText(f))).Select(m => Path.GetFileName(f) + ": " + m.Value))
            .ToList();
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));

        // Gegenprobe: die Suche findet einen solchen Effekt außerhalb eines Kommentars, aber nicht darin.
        Assert.Single(Forbidden().Matches(StripComments("<Border.Effect><BlurEffect Radius=\"14\" /></Border.Effect>")));
        Assert.Empty(Forbidden().Matches(StripComments("<!-- kein BlurEffect --> // auch kein DropShadowEffect")));
        Assert.Single(Forbidden().Matches(StripComments("glow.Effect = new DropShadowEffect();")));
    }

    private static string StripComments(string text)
    {
        text = XmlComment().Replace(text, string.Empty);
        text = BlockComment().Replace(text, string.Empty);
        return LineComment().Replace(text, string.Empty);
    }

    [GeneratedRegex(@"BlurEffect|DropShadowEffect|BitmapEffect|BlendMode", RegexOptions.CultureInvariant)]
    private static partial Regex Forbidden();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();
}
