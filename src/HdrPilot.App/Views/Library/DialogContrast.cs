using System.Windows;
using System.Windows.Media;
using HdrPilot.App.Theming;

namespace HdrPilot.App.Views.Library;

/// <summary>Systemfarben eines Kontrastdesigns (aus <see cref="SystemColors"/> oder, für Tests und Bildschirmfotos, vorgegeben).</summary>
public sealed record HighContrastPalette(Color Window, Color WindowText, Color GrayText, Color Highlight, Color HighlightText, Color HotTrack)
{
    /// <summary>Die Farben des aktiven Kontrastdesigns von Windows.</summary>
    public static HighContrastPalette FromSystem() => new(
        SystemColors.WindowColor, SystemColors.WindowTextColor, SystemColors.GrayTextColor, SystemColors.HighlightColor, SystemColors.HighlightTextColor, SystemColors.HotTrackColor);

    /// <summary>Nachgebildet nach dem Windows-Kontrastdesign „Nachthimmel“ (dunkel).</summary>
    public static HighContrastPalette NightSky { get; } = new(
        Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xA6, 0xA6, 0xA6), Color.FromRgb(0xD6, 0xB4, 0xFD), Color.FromRgb(0x2B, 0x2B, 0x2B), Color.FromRgb(0x80, 0x80, 0xFF));

    /// <summary>Nachgebildet nach dem Windows-Kontrastdesign „Wüste“ (hell).</summary>
    public static HighContrastPalette Desert { get; } = new(
        Color.FromRgb(0xFF, 0xFA, 0xEF), Color.FromRgb(0x3D, 0x3D, 0x3D), Color.FromRgb(0x67, 0x67, 0x67), Color.FromRgb(0x90, 0x39, 0x09), Color.FromRgb(0xFF, 0xF5, 0xE3), Color.FromRgb(0x1C, 0x5E, 0x75));
}

/// <summary>
/// Hoher Kontrast für einen Dialog (E93, D23): Jeder Pinsel des Themas, den der Dialog nutzt, wird in seinen Ressourcen durch
/// eine Systemfarbe ersetzt (Flächen und Glas → Fensterfarbe, Glanz und weiche Flächen → durchsichtig, Text und Linien →
/// Fenstertext, gedämpfter Text → GrayText, Akzent und Ringe → Highlight, Akzenttext → HotTrack). Bedeutung tragen dann
/// Symbol und Text. Ohne Palette werden die Ersetzungen wieder entfernt; alles darin folgt über DynamicResource. Dass jeder
/// Pinsel, den Vorlagen und Stile des Dialogs erreichen, hier steht, prüft ein Test am XAML-Text (DialogContrastCoverageTests).
/// </summary>
public static class DialogContrast
{
    /// <summary>Pinsel des Themas mit ihrer Rolle im Kontrastdesign.</summary>
    public static IReadOnlyList<(string Key, Func<HighContrastPalette, Brush> Brush)> Map { get; } =
    [
        .. new[] { ThemeKeys.BrushWindowBackground, ThemeKeys.BrushTitleBar, ThemeKeys.BrushSurface, ThemeKeys.BrushSurface1, ThemeKeys.BrushSurface2, ThemeKeys.BrushSurface3, ThemeKeys.BrushSurfaceInput, ThemeKeys.BrushPanel, ThemeKeys.BrushPlate, ThemeKeys.BrushSurfaceHover, ThemeKeys.BrushSurfaceSelected, ThemeKeys.BrushMenu, ThemeKeys.BrushGlass }
            .Select(k => (k, (Func<HighContrastPalette, Brush>)(p => Solid(p.Window)))),
        .. new[] { ThemeKeys.BrushBackgroundGlow, ThemeKeys.BrushBackgroundGlow2, ThemeKeys.BrushAccentShine, ThemeKeys.BrushAtmosphereSoft, ThemeKeys.BrushShine, ThemeKeys.BrushAccentSoft, ThemeKeys.BrushSelectionFill, ThemeKeys.BrushSuccessSoft, ThemeKeys.BrushWarningSoft, ThemeKeys.BrushDangerSoft, ThemeKeys.BrushPanelHighlight }
            .Select(k => (k, (Func<HighContrastPalette, Brush>)(_ => Solid(Colors.Transparent)))),
        .. new[] { ThemeKeys.BrushText, ThemeKeys.BrushTextSecondary, ThemeKeys.BrushLine, ThemeKeys.BrushLineStrong, ThemeKeys.BrushBorder, ThemeKeys.BrushBorderStrong, ThemeKeys.BrushDanger, ThemeKeys.BrushWarning, ThemeKeys.BrushSuccess, ThemeKeys.BrushTextOnArt, ThemeKeys.BrushTextOnArtSecondary }
            .Select(k => (k, (Func<HighContrastPalette, Brush>)(p => Solid(p.WindowText)))),
        (ThemeKeys.BrushTextMuted, p => Solid(p.GrayText)),
        .. new[] { ThemeKeys.BrushAccent, ThemeKeys.BrushAccentStrong, ThemeKeys.BrushAccentSolid, ThemeKeys.BrushSelectionRing, ThemeKeys.BrushFocusRing }
            .Select(k => (k, (Func<HighContrastPalette, Brush>)(p => Solid(p.Highlight)))),
        (ThemeKeys.BrushTextOnAccent, p => Solid(p.HighlightText)),
        (ThemeKeys.BrushAccentText, p => Solid(p.HotTrack)),
        ("Lib.LogoPlate", p => Solid(p.Window)),
        ("Lib.Brush.ContrastFrame", p => Solid(p.WindowText)),
    ];

    /// <summary>Setzt (Palette) oder entfernt (null) die Ersetzungen in <paramref name="resources"/>.</summary>
    public static void Apply(ResourceDictionary resources, HighContrastPalette? palette)
    {
        foreach (var (key, brush) in Map)
        {
            if (palette is not null)
            {
                resources[key] = brush(palette);
            }
            else
            {
                resources.Remove(key);
            }
        }

        // Rahmen um Platten und getönte Hinweise: nur im Kontrastdesign sichtbar (1 DIP, Fenstertext).
        if (palette is not null)
        {
            resources["Lib.ContrastFrameThickness"] = new Thickness(1);
            resources["Lib.StatusTintOpacity"] = 0.0;
        }
        else
        {
            resources.Remove("Lib.ContrastFrameThickness");
            resources.Remove("Lib.StatusTintOpacity");
        }
    }

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
