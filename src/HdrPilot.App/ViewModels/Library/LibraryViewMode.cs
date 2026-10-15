namespace HdrPilot.App.ViewModels.Library;

/// <summary>Ansicht der Bibliothek (E89), in dieser Reihenfolge überall: Umschalter, Strg+1/2/3, Menüs.</summary>
public enum LibraryViewMode
{
    /// <summary>Liste (Standard): kompakt, sortierbar, schlanker Titelbereich.</summary>
    List,

    /// <summary>Raster: Kacheln im Hochformat, Spalten nach der Breite.</summary>
    Grid,

    /// <summary>Regal: großes Titelbild und eine Kachelreihe.</summary>
    Shelf,
}

/// <summary>Vorschaubild der Listenzeilen: Querformat-Bild (Standard) oder Symbol.</summary>
public enum LibraryThumbnail
{
    Image,
    Icon,
}

/// <summary>Ablage von Ansicht und Vorschaubild in der Konfiguration (AppSettings.LibraryView, AppSettings.LibraryThumbnail).</summary>
public static class LibraryViewModes
{
    /// <summary>Alle Ansichten in der Reihenfolge der Oberfläche: Liste, Raster, Regal.</summary>
    public static IReadOnlyList<LibraryViewMode> Ordered { get; } = [LibraryViewMode.List, LibraryViewMode.Grid, LibraryViewMode.Shelf];

    /// <summary>Wert aus der Konfiguration; fehlend oder unbekannt (etwa aus einer neueren Version): Liste.</summary>
    public static LibraryViewMode Parse(string? value) =>
        Enum.TryParse<LibraryViewMode>(value?.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode) && !int.TryParse(value, out _)
            ? mode
            : LibraryViewMode.List;

    /// <summary>Wert für die Konfiguration: der Standard (Liste) wird nicht gespeichert (null), damit config.json gleich bleibt.</summary>
    public static string? ToConfig(LibraryViewMode mode) => mode == LibraryViewMode.List ? null : mode.ToString();

    public static LibraryThumbnail ParseThumbnail(string? value) =>
        string.Equals(value?.Trim(), nameof(LibraryThumbnail.Icon), StringComparison.OrdinalIgnoreCase) ? LibraryThumbnail.Icon : LibraryThumbnail.Image;

    public static string? ToConfig(LibraryThumbnail thumbnail) => thumbnail == LibraryThumbnail.Image ? null : thumbnail.ToString();
}
