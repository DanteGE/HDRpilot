using System.Text.RegularExpressions;

namespace HdrPilot.Core.Games.Scanners.Ubisoft;

/// <summary>Eine vom Produkt-Cache genannte Start-EXE, relativ zum Installations- bzw. Arbeitsordner.</summary>
internal sealed record UbisoftExecutable(string? RelativePath, string? TrialRelativePath, string? WorkingDirectoryAppend);

/// <summary>Für HdrPilot relevante Angaben eines Produkts aus dem Ubisoft-Connect-Produkt-Cache.</summary>
internal sealed partial record UbisoftProductConfig(string? Name, IReadOnlyList<UbisoftExecutable> Executables)
{
    /// <summary>
    /// Wertet das YAML eines Produkts aus (gemessen am Zielrechner, Version 2.0):
    /// <c>root.name</c> ist ein Name oder ein Schlüssel in <c>localizations.default</c> (etwa <c>l1</c>, <c>GAMENAME</c>);
    /// <c>root.start_game.&lt;online|offline|…&gt;.executables[]</c> nennt je Eintrag <c>path.relative</c>,
    /// <c>trial.relative</c> (Ubisoft+-Variante) und <c>working_directory.append</c>.
    /// </summary>
    /// <exception cref="FormatException">YAML zu tief verschachtelt.</exception>
    public static UbisoftProductConfig FromYaml(string yaml)
    {
        var document = UbisoftYaml.Parse(yaml);
        var root = document["root"];
        var defaults = document["localizations"]?["default"];

        string? name = null;
        var rawName = root?["name"]?.Text?.Trim();
        if (!string.IsNullOrEmpty(rawName))
        {
            var localized = defaults?[rawName]?.Text?.Trim();
            if (!string.IsNullOrEmpty(localized))
            {
                name = localized;
            }
            else if (!LocalizationKey().IsMatch(rawName))
            {
                name = rawName;
            }
        }

        var executables = new List<UbisoftExecutable>();
        if (root?["start_game"] is YamlMapping startGame)
        {
            foreach (var (_, mode) in startGame.Entries)
            {
                if (mode["executables"] is not YamlSequence list)
                {
                    continue;
                }

                foreach (var item in list.Items)
                {
                    var path = item["path"]?["relative"]?.Text?.Trim();
                    var trial = item["trial"]?["relative"]?.Text?.Trim();
                    var append = item["working_directory"]?["append"]?.Text?.Trim();
                    if (!string.IsNullOrEmpty(path) || !string.IsNullOrEmpty(trial))
                    {
                        executables.Add(new UbisoftExecutable(
                            string.IsNullOrEmpty(path) ? null : path,
                            string.IsNullOrEmpty(trial) ? null : trial,
                            string.IsNullOrEmpty(append) ? null : append));
                    }
                }
            }
        }

        return new UbisoftProductConfig(name, executables);
    }

    /// <summary>Unaufgelöster Lokalisierungsschlüssel wie "l1": kein Anzeigename.</summary>
    [GeneratedRegex(@"^l\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizationKey();
}
