using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HdrPilot.Tests.Architecture;

/// <summary>
/// Hoher Kontrast in „Bild wählen…“ (E93, D23) ohne WPF: Jeder Pinsel des Themas (Hp.Brush.*), den der Dialog über seine
/// Vorlagen und Stile erreicht, muss in DialogContrast.Map stehen. Sonst behielte er im Kontrastdesign die Farbe des Themas
/// (etwa die helle Glasfläche hinter den Reitern unter weißem Text in „Nachthimmel“, Prüfung 2026-10-26). Der Test liest
/// den XAML-Text des Dialogs, folgt jedem StaticResource und DynamicResource in Controls.xaml, Library.xaml und Shell.xaml
/// (dazu dem Fensterstil von ChromeWindow) und vergleicht mit den Schlüsseln in DialogContrast.cs.
/// </summary>
public sealed class DialogContrastCoverageTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex Reference = new(@"\{(?:Static|Dynamic)Resource\s+([^}\s]+)\s*\}", RegexOptions.CultureInvariant);

    [Fact]
    public void Every_theme_brush_the_picker_dialog_reaches_is_mapped_for_high_contrast()
    {
        var reached = ReachedBrushes();
        var mapped = MappedKeys();

        var missing = reached.Where(k => !mapped.Contains(k)).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, "Ohne Systemfarbe im Kontrastdesign: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_walk_follows_styles_templates_and_the_window_style()
    {
        // Gegenprobe: Die Suche erreicht Pinsel nur über Stile (Glas hinter den Reitern über Lib.SegmentFrame, Titelleiste
        // über den Fensterstil); ohne einen Eintrag in der Liste fiele er auf.
        var reached = ReachedBrushes();
        Assert.Contains("Hp.Brush.Glass", reached);
        Assert.Contains("Hp.Brush.TitleBar", reached);
        Assert.Contains("Hp.Brush.Plate", reached);
        Assert.DoesNotContain("Hp.Brush.Source.Steam", reached); // Bibliothek, nicht der Dialog

        var mapped = MappedKeys();
        Assert.Contains("Hp.Brush.Glass", mapped);
        Assert.Contains("Lib.LogoPlate", mapped);
        mapped.Remove("Hp.Brush.Glass");
        Assert.Contains("Hp.Brush.Glass", reached.Where(k => !mapped.Contains(k)));
    }

    /// <summary>Alle Hp.Brush.*, die der Dialog direkt oder über Stile, Vorlagen und den Fensterstil erreicht.</summary>
    private static HashSet<string> ReachedBrushes()
    {
        var root = RepositoryRoot();
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in new[] { "Controls.xaml", "Library.xaml", "Shell.xaml" })
        {
            var document = XDocument.Load(Path.Combine(root, "src", "HdrPilot.App", "Themes", file));
            foreach (var element in document.Descendants().Where(e => e.Attribute(Xaml + "Key") is not null))
            {
                definitions.TryAdd(element.Attribute(Xaml + "Key")!.Value, element.ToString());
            }
        }

        var dialog = File.ReadAllText(Path.Combine(root, "src", "HdrPilot.App", "Views", "Library", "ArtworkPickerDialog.xaml"));

        // ChromeWindow setzt seinen Stil selbst (SetResourceReference auf Hp.ChromeWindowStyle).
        var pending = new Stack<string>(References(dialog).Append("Hp.ChromeWindowStyle"));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var key))
        {
            if (seen.Add(key) && definitions.TryGetValue(key, out var text))
            {
                foreach (var next in References(text))
                {
                    pending.Push(next);
                }
            }
        }

        return seen.Where(k => k.StartsWith("Hp.Brush.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Schlüssel in DialogContrast.cs: ThemeKeys-Konstanten (aufgelöst) und Schlüssel als Zeichenkette.</summary>
    private static HashSet<string> MappedKeys()
    {
        var root = RepositoryRoot();
        var constants = Regex.Matches(File.ReadAllText(Path.Combine(root, "src", "HdrPilot.App", "Theming", "ThemeKeys.cs")), @"const string (\w+) = ""([^""]+)"";")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        var source = File.ReadAllText(Path.Combine(root, "src", "HdrPilot.App", "Views", "Library", "DialogContrast.cs"));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(source, @"\bThemeKeys\.(\w+)"))
        {
            keys.Add(constants.TryGetValue(match.Groups[1].Value, out var value) ? value : throw new InvalidOperationException("Unbekannte Konstante " + match.Value));
        }

        foreach (Match match in Regex.Matches(source, @"""((?:Hp|Lib)\.[\w.]+)"""))
        {
            keys.Add(match.Groups[1].Value);
        }

        return keys;
    }

    private static IEnumerable<string> References(string text) => Reference.Matches(text).Select(m => m.Groups[1].Value);

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
