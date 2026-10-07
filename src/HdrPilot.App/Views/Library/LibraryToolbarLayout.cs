namespace HdrPilot.App.Views.Library;

/// <summary>Anordnung der Werkzeugleiste im Hauptfenster, je nach verfügbarer Breite.</summary>
public enum ToolbarMode
{
    /// <summary>Alles in einer Zeile, „Neu einlesen“ mit Text.</summary>
    Wide,

    /// <summary>Eine Zeile, „Neu einlesen“ nur als Symbol.</summary>
    Compact,

    /// <summary>Filter und Sortierung in einer zweiten Zeile unter der Suche.</summary>
    Stacked,
}

/// <summary>Entscheidet die Anordnung der Werkzeugleiste aus gemessenen Breiten (ohne Fenster prüfbar).</summary>
public static class LibraryToolbarLayout
{
    /// <param name="available">Verfügbare Breite der Werkzeugleiste.</param>
    /// <param name="searchMin">Mindestbreite des Suchfelds.</param>
    /// <param name="filters">Breite von Quellen-Filter, Sortierung und Filter-Chips (mit Abständen).</param>
    /// <param name="actions">Breite der Aktionen mit Text bei „Neu einlesen“ (mit Abständen).</param>
    /// <param name="rescanText">Breite, die „Neu einlesen“ ohne Text spart.</param>
    public static ToolbarMode Choose(double available, double searchMin, double filters, double actions, double rescanText)
    {
        var wide = searchMin + filters + actions;
        if (available >= wide)
        {
            return ToolbarMode.Wide;
        }

        return available >= wide - rescanText ? ToolbarMode.Compact : ToolbarMode.Stacked;
    }

    /// <summary>„Neu einlesen“ nur als Symbol: in der kompakten Anordnung, gestapelt nur, wenn Suche und Aktionen sonst nicht passen.</summary>
    public static bool RescanIconOnly(ToolbarMode mode, double available, double searchMin, double actions) => mode switch
    {
        ToolbarMode.Wide => false,
        ToolbarMode.Compact => true,
        _ => available < searchMin + actions,
    };
}
