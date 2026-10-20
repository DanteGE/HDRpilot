namespace HdrPilot.App.Theming;

/// <summary>
/// Eine Farbvorlage: Kennung (gespeichert in <c>settings.appearance.preset</c>), die drei Farben und die Standardanzahl
/// (Duoton oder Tritonus). Der angezeigte Name kommt aus ShellStrings.resx (Schlüssel <see cref="NameResourceKey"/>).
/// </summary>
public sealed record PalettePreset(string Id, PaletteColors Colors, PaletteMode DefaultMode)
{
    /// <summary>Schlüssel des Namens in ShellStrings.resx, etwa „Preset_abendrot“.</summary>
    public string NameResourceKey => "Preset_" + Id;
}

/// <summary>Die acht Vorlagen des Prototyps v4 (app.js, P) in dieser Reihenfolge; Standard ist „Abendrot“ im Duoton.</summary>
public static class PalettePresets
{
    /// <summary>Kennung für eigene Farben.</summary>
    public const string CustomId = "custom";

    public const string DefaultId = "abendrot";

    public static IReadOnlyList<PalettePreset> All { get; } =
    [
        Preset("abendrot", "#FF8A3D", "#FF3D7F", "#FFD166", PaletteMode.Duotone),
        Preset("cyber", "#00E5FF", "#7C5CFF", "#FF2BD6", PaletteMode.Tritone),
        Preset("neongruen", "#C6FF3D", "#00D68F", "#00B3FF", PaletteMode.Duotone),
        Preset("royal", "#8B5CF6", "#EC4899", "#F5B83D", PaletteMode.Tritone),
        Preset("gletscher", "#9BE7FF", "#3B82F6", "#C4B5FD", PaletteMode.Duotone),
        Preset("synthwave", "#FF71CE", "#B967FF", "#01CDFE", PaletteMode.Tritone),
        Preset("glut", "#FFB238", "#FF3B30", "#FFE08A", PaletteMode.Duotone),
        Preset("polarlicht", "#3DFFB0", "#3DB8FF", "#A35CFF", PaletteMode.Tritone),
    ];

    public static PalettePreset Default => All[0];

    /// <summary>Vorlage zu einer Kennung (Groß- und Kleinschreibung egal); unbekannt: null.</summary>
    public static PalettePreset? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Glanz im Duoton: Atmosphäre zu 45 % mit Weiß gemischt (c3Of() im Prototyp).</summary>
    public static System.Windows.Media.Color DerivedShine(System.Windows.Media.Color atmosphere) =>
        ColorMath.Mix(atmosphere, System.Windows.Media.Colors.White, 0.45);

    private static PalettePreset Preset(string id, string accent, string atmosphere, string shine, PaletteMode mode) =>
        new(id, new PaletteColors(ColorMath.FromHex(accent), ColorMath.FromHex(atmosphere), ColorMath.FromHex(shine)), mode);
}
