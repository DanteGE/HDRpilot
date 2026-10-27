using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HdrPilot.Tests.Localization;

/// <summary>
/// Englische Übersetzung (E95): Zu jeder neutralen (deutschen) resx unter src gibt es eine .en.resx mit genau denselben
/// Schlüsseln, keinen leeren Werten, denselben Platzhaltern ({0}, {1} …) und ohne deutsche Reste. Liest die Dateien aus dem
/// Repository, läuft also auch unter Linux. Neue Schlüssel ohne Übersetzung (etwa aus einem anderen Zweig) fallen hier auf.
/// </summary>
public sealed class ResourceTranslationTests
{
    /// <summary>Schlüssel, deren englischer Wert bewusst Deutsch enthält: Eigenbezeichnung „Deutsch“, zweisprachige Beschriftung.</summary>
    private static readonly HashSet<string> GermanAllowed = new(StringComparer.Ordinal)
    {
        "ShellStrings.LanguageNameGerman",
        "ShellStrings.LanguageLabel",
    };

    /// <summary>Wörter, die es im Englischen nicht gibt (Heuristik gegen vergessene oder halbe Übersetzungen).</summary>
    private static readonly Regex GermanWords = new(
        @"\b(und|oder|nicht|der|das|dem|ein|eine|einen|einem|ist|sind|mit|für|von|vom|zum|zur|kein|keine|keinen|wird|wurde|werden|"
        + @"bitte|Spiel|Spiele|Spielen|Bildschirm|Fehler|Datei|Ordner|Einstellungen|Hinweis|zuletzt|neu|läuft|gefunden|unbekannt|"
        + @"Sekunden|Minuten|heute|gestern|Strg|Umschalt|Entf|Pos1|Ende|Leertaste|Eingabetaste|auch|noch|schon|nur|wenn|dann|beim|"
        + @"sich|bei|über|unter|nach|jetzt|gilt|Sprache|Bild|Bilder|Quelle|Startoption|Verknüpfung|Einlesen|Automatik|Dateien|"
        + @"Spieles|schalten|einschalten|ausschalten|anlegen|entfernen|speichern|abbrechen|beenden|starten|erledigt|offen|Fertig|"
        + @"Abbrechen|Speichern|Entfernen|Starten|Hilfe|Zeile|Wert|fehlt|defekt|gespeichert|eingeschaltet|ausgeschaltet|"
        + @"\w+ieren|\w+ungen|\w+keit|\w+heit)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex Umlauts = new("[äöüÄÖÜß„]", RegexOptions.CultureInvariant);

    /// <summary>Glossar (E95): steht der deutsche Begriff im neutralen Text, muss der englische in der Übersetzung stehen.</summary>
    private static readonly (string German, string English)[] GlossaryTerms =
    [
        ("kein eigenes HDR", "no native HDR"),
        ("Eigene Datei", "Custom file"),
        ("Eigenes Symbol", "Custom icon"),
    ];

    /// <summary>Frühere, missverständliche Wörter, die das Glossar ersetzt („custom“, „native HDR“, „blurred“).</summary>
    private static readonly Regex Avoided = new(@"\b(own (file|icon|images?)|HDR of its own|smeared)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Typografie (en-US): Leerzeichen vor „…“ oder vor „%“ nach einer Zahl, britisches „Cancelled“.</summary>
    private static readonly Regex Typography = new(@" …|\d %|\bCancell", RegexOptions.CultureInvariant);

    private static readonly Regex Placeholder = new(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.CultureInvariant);

    public static TheoryData<string> NeutralFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in NeutralResx())
        {
            data.Add(Path.GetRelativePath(RepositoryRoot(), file).Replace('\\', '/'));
        }

        return data;
    }

    [Fact]
    public void All_known_resource_files_are_found()
    {
        var names = NeutralResx().Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.Ordinal);
        string[] expected = ["ArtworkStrings", "CommonStrings", "LibraryStrings", "SetupStrings", "ShellStrings", "CoreStrings", "ScanStrings", "CliStrings"];
        Assert.Empty(expected.Except(names));
    }

    [Theory]
    [MemberData(nameof(NeutralFiles))]
    public void English_file_exists_with_the_same_keys(string relative)
    {
        var neutral = Read(relative);
        var english = Read(EnglishOf(relative));

        var missing = neutral.Keys.Except(english.Keys).Order(StringComparer.Ordinal).ToList();
        var extra = english.Keys.Except(neutral.Keys).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{relative}: ohne englische Übersetzung: {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0, $"{relative}: nur englisch, im neutralen Text fehlt: {string.Join(", ", extra)}");
    }

    [Theory]
    [MemberData(nameof(NeutralFiles))]
    public void No_value_is_empty(string relative)
    {
        foreach (var file in new[] { relative, EnglishOf(relative) })
        {
            var empty = Read(file).Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key).ToList();
            Assert.True(empty.Count == 0, $"{file}: leere Werte: {string.Join(", ", empty)}");
        }
    }

    [Theory]
    [MemberData(nameof(NeutralFiles))]
    public void Placeholders_are_identical_and_valid(string relative)
    {
        var neutral = Read(relative);
        var english = Read(EnglishOf(relative));
        var problems = new List<string>();
        foreach (var (key, value) in neutral)
        {
            if (!english.TryGetValue(key, out var translated))
            {
                continue; // meldet English_file_exists_with_the_same_keys
            }

            if (!Indices(value).SetEquals(Indices(translated)))
            {
                problems.Add($"{key}: „{value}“ ↔ „{translated}“");
            }

            foreach (var text in new[] { value, translated })
            {
                if (!IsValidFormat(text))
                {
                    problems.Add($"{key}: ungültige Formatvorlage „{text}“");
                }
            }
        }

        Assert.True(problems.Count == 0, $"{relative}:\n" + string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(NeutralFiles))]
    public void English_values_contain_no_german(string relative)
    {
        var name = Path.GetFileNameWithoutExtension(relative);
        var leftovers = Read(EnglishOf(relative))
            .Where(e => !GermanAllowed.Contains(name + "." + e.Key))
            .Where(e => Umlauts.IsMatch(e.Value) || GermanWords.IsMatch(e.Value))
            .Select(e => $"{e.Key}: {e.Value}")
            .ToList();
        Assert.True(leftovers.Count == 0, $"{EnglishOf(relative)}: vermutlich deutsch:\n" + string.Join("\n", leftovers));
    }

    [Fact]
    public void Heuristics_find_german_and_accept_english()
    {
        // Gegenprobe der Heuristik: typische deutsche Reste werden erkannt, englische UI-Texte nicht.
        Assert.Matches(GermanWords, "Spiel hinzufügen");
        Assert.Matches(GermanWords, "Strg+F blendet die Leiste ein");
        Assert.Matches(GermanWords, "Dateien kopieren");
        Assert.Matches(GermanWords, "Einstellungen gespeichert");
        Assert.Matches(Umlauts, "Schließen");
        Assert.Matches(Umlauts, "„HDR automatisch“");
        Assert.DoesNotMatch(GermanWords, "Turn HDR on now · Ctrl+F shows the toolbar, Shift+F10 opens its menu.");
        Assert.DoesNotMatch(Umlauts, "“HDR on launch” is on");

        Assert.True(Indices("{0} von {1:N0} Spielen").SetEquals([0, 1]));
        Assert.False(Indices("{0} games").SetEquals(Indices("{0} von {1} Spielen")));
        Assert.Empty(Indices("{{0}} wörtlich"));
        Assert.False(IsValidFormat("{0 kaputt"));
    }

    [Fact]
    public void Glossary_keeps_hdr_on_launch_apart_from_windows_auto_hdr()
    {
        // „HDR automatisch“ (Schalter von HDRpilot) heißt „HDR on launch“, nie „Auto HDR“ (das ist der Schalter von Windows).
        var library = Read("src/HdrPilot.App/Resources/LibraryStrings.en.resx");
        Assert.Equal("HDR on launch", library["Details_HdrAuto"]);
        Assert.Equal("HDR on launch", library["Filter_OnlySelected"]);
        Assert.Equal("Windows Auto HDR", library["Details_AutoHdr"]);
        var german = Read("src/HdrPilot.App/Resources/LibraryStrings.resx");
        var hdrAutoKeys = german.Where(e => e.Value.Contains("HDR automatisch", StringComparison.Ordinal)).Select(e => e.Key).ToList();
        Assert.NotEmpty(hdrAutoKeys);
        foreach (var key in hdrAutoKeys)
        {
            Assert.Contains("HDR on launch", library[key], StringComparison.OrdinalIgnoreCase);
            if (!german[key].Contains("Auto HDR", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("Auto HDR", library[key], StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [MemberData(nameof(NeutralFiles))]
    public void Glossary_is_followed_in_every_file(string relative)
    {
        // Glossar aus E95 für alle Dateien: „HDR automatisch“ ist „HDR on launch“, „Auto HDR“ nur dort, wo auch der deutsche
        // Text den Schalter von Windows meint; „kein eigenes HDR“ ist „no native HDR“, eigene Dateien und Symbole sind „custom“.
        var neutral = Read(relative);
        var english = Read(EnglishOf(relative));
        var problems = new List<string>();
        foreach (var (key, german) in neutral)
        {
            if (!english.TryGetValue(key, out var translated))
            {
                continue; // meldet English_file_exists_with_the_same_keys
            }

            var windowsAutoHdr = german.Contains("Auto HDR", StringComparison.OrdinalIgnoreCase) || german.Contains("Auto-HDR", StringComparison.OrdinalIgnoreCase);
            if (german.Contains("HDR automatisch", StringComparison.Ordinal) && !translated.Contains("HDR on launch", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{key}: „HDR automatisch“ ohne „HDR on launch“: {translated}");
            }

            if (!windowsAutoHdr && translated.Contains("Auto HDR", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{key}: „Auto HDR“ ist nur der Schalter von Windows: {translated}");
            }

            foreach (var (de, en) in GlossaryTerms)
            {
                if (german.Contains(de, StringComparison.OrdinalIgnoreCase) && !translated.Contains(en, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{key}: „{de}“ ohne „{en}“: {translated}");
                }
            }

            if (Avoided.Match(translated) is { Success: true } avoided)
            {
                problems.Add($"{key}: „{avoided.Value}“ vermeiden: {translated}");
            }
        }

        Assert.True(problems.Count == 0, $"{EnglishOf(relative)}:\n" + string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(NeutralFiles))]
    public void English_typography_is_consistent(string relative)
    {
        // en-US: „…“ ohne Leerzeichen davor, „50%“ ohne Leerzeichen, „Canceled“ mit einem l.
        var problems = Read(EnglishOf(relative))
            .Where(e => Typography.IsMatch(e.Value))
            .Select(e => $"{e.Key}: „{Typography.Match(e.Value).Value}“ in {e.Value}")
            .ToList();
        Assert.True(problems.Count == 0, $"{EnglishOf(relative)}:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Glossary_and_typography_checks_find_what_they_should()
    {
        // Gegenprobe der beiden Prüfungen oben.
        Assert.Matches(Avoided, "Own file…");
        Assert.Matches(Avoided, "NO HDR OF ITS OWN");
        Assert.Matches(Avoided, "smeared capsule → crop");
        Assert.DoesNotMatch(Avoided, "Please put the EXE in a folder of its own.");
        Assert.DoesNotMatch(Avoided, "Custom file: {0}");
        Assert.Matches(Typography, "Scanning …");
        Assert.Matches(Typography, "0 % GPU");
        Assert.Matches(Typography, "Cancelled.");
        Assert.DoesNotMatch(Typography, "Scanning…");
        Assert.DoesNotMatch(Typography, "0% GPU · Canceled.");
        Assert.DoesNotMatch(Typography, "{0} %APPDATA%");
    }

    private static IEnumerable<string> NeutralResx() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.resx", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !Path.GetFileNameWithoutExtension(f).Contains('.', StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static string EnglishOf(string relative) => relative[..^".resx".Length] + ".en.resx";

    private static Dictionary<string, string> Read(string relative)
    {
        var path = Path.Combine(RepositoryRoot(), relative);
        Assert.True(File.Exists(path), "Datei fehlt: " + relative);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty, StringComparer.Ordinal);
    }

    private static HashSet<int> Indices(string text) =>
        Placeholder.Matches(text.Replace("{{", string.Empty, StringComparison.Ordinal).Replace("}}", string.Empty, StringComparison.Ordinal))
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToHashSet();

    /// <summary>Gültige Vorlage für string.Format (Klammern, Indizes); Formatangaben prüft das nicht, Objekte ignorieren sie.</summary>
    private static bool IsValidFormat(string text)
    {
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, text, Enumerable.Range(0, 10).Select(_ => new object()).ToArray());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

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
