namespace HdrPilot.App.ViewModels;

/// <summary>
/// Wohin das Einstellungsfenster beim Öffnen springt. (Der Name SettingsSection gehört schon der Ansicht
/// Views/Shell/SettingsSection.cs.)
/// </summary>
public enum SettingsTarget
{
    /// <summary>Wie zuletzt bzw. am Anfang.</summary>
    Start,

    /// <summary>Darstellung › Bilder mit dem Fokus im Schlüsselfeld von SteamGridDB („Bild wählen…“ → „Zu den Einstellungen“, E93).</summary>
    Artwork,
}
