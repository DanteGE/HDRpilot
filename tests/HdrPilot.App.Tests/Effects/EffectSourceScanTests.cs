using System.Text.RegularExpressions;
using HdrPilot.App.Tests.Library;

namespace HdrPilot.App.Tests.Effects;

/// <summary>
/// Leistung (E89, G10.9), Prüfung nach dem Zusammenführen: In der ganzen App (Bibliothek, Einstellungen, Tray, Bilderdienst,
/// Setup) gibt es keinen BlurEffect, keinen DropShadowEffect, keine Shader, keine BitmapEffects und keine Mischmodi.
/// Die Teile prüfen jeweils nur ihre eigenen Dateien; diese Prüfung fängt auch Dateien, die keinem Teil gehören.
/// Kommentare zählen nicht (dort darf erklärt werden, warum es keine gibt).
/// </summary>
public sealed partial class EffectSourceScanTests
{
    [Fact]
    public void Effect_free_whole_app_has_no_blur_shadow_shader_or_blend_effects()
    {
        var app = SourceFiles.InRepository("src", "HdrPilot.App");
        var files = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutput(app, f))
            .ToList();
        Assert.True(files.Count >= 150, "Dateien der App: " + files.Count);

        var hits = files
            .SelectMany(f => Forbidden().Matches(WithoutComments(File.ReadAllText(f), f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
                .Select(m => Path.GetRelativePath(app, f) + ": " + m.Value))
            .ToList();
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));

        // Gegenprobe: die Suche findet einen solchen Effekt außerhalb eines Kommentars, aber nicht darin.
        Assert.Matches(Forbidden(), WithoutComments("<Border.Effect><BlurEffect Radius=\"14\" /></Border.Effect>", xaml: true));
        Assert.Matches(Forbidden(), WithoutComments("glow.Effect = new DropShadowEffect();", xaml: false));
        Assert.DoesNotMatch(Forbidden(), WithoutComments("<!-- kein BlurEffect -->", xaml: true));
        Assert.DoesNotMatch(Forbidden(), WithoutComments("// auch kein DropShadowEffect", xaml: false));
    }

    private static bool IsBuildOutput(string app, string file)
    {
        var relative = Path.GetRelativePath(app, file);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutComments(string code, bool xaml) => xaml
        ? XamlComment().Replace(code, string.Empty)
        : CodeComment().Replace(code, string.Empty);

    [GeneratedRegex(@"\b(BlurEffect|DropShadowEffect|ShaderEffect|BitmapEffect\w*|BlendMode)\b|\.Effect>|\sEffect=""|\.Effect\s*=")]
    private static partial Regex Forbidden();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XamlComment();

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CodeComment();
}
