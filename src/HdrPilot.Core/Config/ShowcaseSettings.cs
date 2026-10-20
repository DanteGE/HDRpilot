namespace HdrPilot.Core.Config;

/// <summary>
/// Darstellung (E89, Showcase Hero): Farbschema, Thema und Effekte. Werte bewusst als Text statt Aufzählung: ein
/// unbekannter Wert (etwa aus einer neueren Version) macht die Datei nicht unlesbar, sondern gilt als Standard.
/// Fehlt der Abschnitt (ältere Konfiguration), gelten alle Standardwerte.
/// </summary>
public sealed class AppearanceSettings
{
    /// <summary>"System" (Standard, folgt Windows), "Light" oder "Dark".</summary>
    public string? Theme { get; set; }

    /// <summary>"Duotone" (Standard: zwei Farben, Glanz abgeleitet) oder "Tritone" (drei Farben).</summary>
    public string? PaletteMode { get; set; }

    /// <summary>Kennung der Farbvorlage (etwa "abendrot", Standard) oder "custom" für eigene Farben.</summary>
    public string? Preset { get; set; }

    /// <summary>Eigene Farbe „Akzent“ als "#RRGGBB" (nur mit <see cref="Preset"/> "custom").</summary>
    public string? Accent { get; set; }

    /// <summary>Eigene Farbe „Atmosphäre“ als "#RRGGBB" (nur mit <see cref="Preset"/> "custom").</summary>
    public string? Atmosphere { get; set; }

    /// <summary>Eigene Farbe „Glanz“ als "#RRGGBB" (nur mit <see cref="Preset"/> "custom" und Tritonus).</summary>
    public string? Shine { get; set; }

    /// <summary>Textfarbe auf Akzentflächen automatisch so wählen, dass der Kontrast reicht.</summary>
    public bool AutoTextOnAccent { get; set; } = true;

    /// <summary>Umgebungslicht und Leuchten übernehmen die Farben des gewählten Titelbilds.</summary>
    public bool BackgroundFollowsGame { get; set; } = true;

    /// <summary>"Full" (Standard), "Reduced" oder "Off".</summary>
    public string? EffectLevel { get; set; }

    /// <summary>Solange ein Spiel läuft, steht jede Dauer-Animation still.</summary>
    public bool PauseWhilePlaying { get; set; } = true;

    /// <summary>Inaktives oder verdecktes Fenster: Dauer-Animationen stehen still (minimiert immer).</summary>
    public bool PauseInBackground { get; set; } = true;

    /// <summary>Ist die Windows-Option „Animationseffekte“ aus, gilt mindestens „Reduziert“.</summary>
    public bool RespectWindowsAnimations { get; set; } = true;

    /// <summary>Nach einem Start über die Bibliothek schließt das Fenster, HDRpilot läuft im Infobereich weiter.</summary>
    public bool MinimizeToTrayAfterLaunch { get; set; }
}

/// <summary>Spielbilder (E89). Lokale Launcher-Caches sind immer aktiv; SteamGridDB nur auf Wunsch.</summary>
public sealed class ArtworkSettings
{
    /// <summary>
    /// SteamGridDB füllt fehlende Bilder (Netzwerkzugriff, eigener API-Schlüssel in steamgriddb-key.dat). Standard aus.
    /// </summary>
    public bool SteamGridDb { get; set; }
}

/// <summary>
/// Eigene Bildwahl eines Spiels („Bild wählen…“), je Bildart ein Kennzeichen aus <see cref="Artwork.ArtworkOverride"/>:
/// null (automatisch), "gen" (erzeugtes Bild), "alt:&lt;Datei&gt;" (anderes lokales Bild des Launchers) oder
/// "custom:&lt;Datei&gt;" (eigene Datei im Ordner art\custom des Datenordners).
/// </summary>
public sealed class GameArtworkChoice
{
    public string? Hero { get; set; }

    public string? Logo { get; set; }

    public string? Cover { get; set; }

    /// <summary>True, wenn keine Bildart eine eigene Wahl hat (dann wird der Abschnitt nicht gespeichert).</summary>
    public bool IsEmpty => Hero is null && Logo is null && Cover is null;

    /// <summary>Ungültige Kennzeichen (von Hand geändert) gelten als automatisch; ohne Wahl: null.</summary>
    public static GameArtworkChoice? Normalize(GameArtworkChoice? choice)
    {
        if (choice is null)
        {
            return null;
        }

        choice.Hero = Artwork.ArtworkOverride.IsValid(choice.Hero) ? choice.Hero : null;
        choice.Logo = Artwork.ArtworkOverride.IsValid(choice.Logo) ? choice.Logo : null;
        choice.Cover = Artwork.ArtworkOverride.IsValid(choice.Cover) ? choice.Cover : null;
        return choice.IsEmpty ? null : choice;
    }
}
