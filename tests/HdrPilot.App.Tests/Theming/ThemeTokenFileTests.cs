using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Tests.Theming;

/// <summary>
/// Die Token-Dateien (Themes/Tokens.Dark.xaml, Tokens.Light.xaml) gelten vor dem ersten Anwenden der Darstellung und im
/// Testhost. Sie müssen genau der Ausgabe der Farbrechnung für den Auslieferungszustand entsprechen, sonst blitzen beim Start
/// andere Farben auf. Mit HDRPILOT_WRITE_TOKENS=1 schreibt der Test sie neu (nach einer Änderung der Formeln).
/// </summary>
public sealed class ThemeTokenFileTests
{
    [Theory]
    [InlineData(true, "Tokens.Dark.xaml")]
    [InlineData(false, "Tokens.Light.xaml")]
    public void Theme_token_files_equal_the_engine_output_for_the_default_palette(bool dark, string file)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "HdrPilot.App", "Themes", file);
        var (expected, actual, written) = WpfTestHost.Run(() =>
        {
            var generated = ThemeResources.Build(DefaultPalette(dark));
            var text = Write(generated, dark);
            if (Environment.GetEnvironmentVariable("HDRPILOT_WRITE_TOKENS") == "1")
            {
                File.WriteAllText(path, text, new UTF8Encoding(false));
                return (Describe(generated), Describe(generated), true);
            }

            var loaded = (ResourceDictionary)XamlReader.Parse(File.ReadAllText(path));
            return (Describe(generated), Describe(loaded), false);
        });

        Assert.False(written, "Token-Datei neu geschrieben: Test ohne HDRPILOT_WRITE_TOKENS wiederholen");
        var differences = expected.Keys.Where(k => !actual.TryGetValue(k, out var v) || v != expected[k]).ToList();
        Assert.True(differences.Count == 0, file + " weicht ab bei: " + string.Join(", ", differences.Take(12)));

        // Gegenprobe: eine andere Vorlage ergibt andere Werte, der Vergleich ist also nicht leer.
        var cyber = WpfTestHost.Run(() => Describe(ThemeResources.Build(PaletteEngine.Compute(PalettePresets.Find("cyber")!.Colors, dark, autoTextOnAccent: true))));
        Assert.NotEqual(expected[ThemeKeys.BrushAccent], cyber[ThemeKeys.BrushAccent]);
    }

    /// <summary>Auslieferungszustand: Abendrot im Duoton (Glanz abgeleitet), Textfarbe automatisch.</summary>
    internal static ThemePalette DefaultPalette(bool dark) =>
        PaletteEngine.Compute(AppearanceService.Compute(null, dark, true, true, false));

    private static Dictionary<string, string> Describe(ResourceDictionary dictionary) =>
        ThemeKeys.All.ToDictionary(k => k, k => dictionary.Contains(k) ? DescribeValue(dictionary[k]) : "fehlt", StringComparer.Ordinal);

    private static string DescribeValue(object? value) => value switch
    {
        Color c => Hex(c),
        SolidColorBrush b => "solid " + Hex(b.Color),
        LinearGradientBrush l => string.Create(CultureInfo.InvariantCulture, $"linear {l.StartPoint}-{l.EndPoint} ") + Stops(l),
        RadialGradientBrush r => string.Create(CultureInfo.InvariantCulture, $"radial {r.Center} {r.RadiusX:0.###}/{r.RadiusY:0.###} ") + Stops(r),
        FontFamily f => "font " + f.Source,
        _ => value?.GetType().Name ?? "null",
    };

    private static string Stops(GradientBrush brush) =>
        string.Join(" ", brush.GradientStops.Select(s => Hex(s.Color) + "@" + s.Offset.ToString("0.###", CultureInfo.InvariantCulture)));

    private static string Hex(Color c) => string.Create(CultureInfo.InvariantCulture, $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");

    /// <summary>XAML der Token-Datei aus einem erzeugten Wörterbuch (Reihenfolge wie <see cref="ThemeKeys.All"/>).</summary>
    private static string Write(ResourceDictionary dictionary, bool dark)
    {
        var sb = new StringBuilder();
        sb.Append("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n");
        sb.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n");
        sb.Append("    <!--\n");
        sb.Append(dark
            ? "        Design-Token, dunkle Variante (E89): alle Schlüssel aus Theming/ThemeKeys.cs mit den Werten der Farbrechnung für\n"
            : "        Design-Token, helle Variante (E89): alle Schlüssel aus Theming/ThemeKeys.cs mit den Werten der Farbrechnung für\n");
        sb.Append("        den Auslieferungszustand (Abendrot, Duoton, Textfarbe automatisch). Zur Laufzeit erzeugt die Darstellung dieselben\n");
        sb.Append("        Schlüssel aus Palette und Thema neu (ThemeResources); diese Werte gelten im Testhost und vor dem ersten Anwenden.\n");
        sb.Append("        Nicht von Hand ändern: ThemeTokenFileTests vergleicht sie mit der Rechnung (HDRPILOT_WRITE_TOKENS=1 schreibt neu).\n");
        sb.Append("    -->\n");
        foreach (var key in ThemeKeys.All)
        {
            switch (dictionary[key])
            {
                case Color c:
                    sb.Append(CultureInfo.InvariantCulture, $"    <Color x:Key=\"{key}\">{Hex(c)}</Color>\n");
                    break;
                case SolidColorBrush b:
                    sb.Append(CultureInfo.InvariantCulture, $"    <SolidColorBrush x:Key=\"{key}\" Color=\"{Hex(b.Color)}\" />\n");
                    break;
                case LinearGradientBrush l:
                    sb.Append(CultureInfo.InvariantCulture, $"    <LinearGradientBrush x:Key=\"{key}\" StartPoint=\"{Point(l.StartPoint)}\" EndPoint=\"{Point(l.EndPoint)}\">\n");
                    AppendStops(sb, l);
                    sb.Append("    </LinearGradientBrush>\n");
                    break;
                case RadialGradientBrush r:
                    sb.Append(CultureInfo.InvariantCulture, $"    <RadialGradientBrush x:Key=\"{key}\" Center=\"{Point(r.Center)}\" GradientOrigin=\"{Point(r.GradientOrigin)}\" RadiusX=\"{Number(r.RadiusX)}\" RadiusY=\"{Number(r.RadiusY)}\">\n");
                    AppendStops(sb, r);
                    sb.Append("    </RadialGradientBrush>\n");
                    break;
                case FontFamily f:
                    sb.Append(CultureInfo.InvariantCulture, $"    <FontFamily x:Key=\"{key}\">{f.Source}</FontFamily>\n");
                    break;
                default:
                    throw new InvalidOperationException("Unbekannter Wert für " + key);
            }
        }

        sb.Append("</ResourceDictionary>\n");
        return sb.ToString();
    }

    private static void AppendStops(StringBuilder sb, GradientBrush brush)
    {
        foreach (var stop in brush.GradientStops)
        {
            sb.Append(CultureInfo.InvariantCulture, $"        <GradientStop Color=\"{Hex(stop.Color)}\" Offset=\"{Number(stop.Offset)}\" />\n");
        }
    }

    private static string Point(System.Windows.Point p) => Number(p.X) + "," + Number(p.Y);

    private static string Number(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    internal static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HdrPilot.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("HdrPilot.slnx nicht gefunden.");
    }
}
