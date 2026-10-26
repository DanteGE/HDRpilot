using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Farben der Bilder eines Spiels (E94, „Farben folgen dem Spiel“). Umgesetzt von <see cref="ArtworkService"/>; andere
/// Umsetzungen von <see cref="IArtworkService"/> müssen sie nicht anbieten (dann gilt das Farbschema). Aufrufer erreichen sie
/// mit <c>context.Artwork as IGameColorsSource</c>; gelesen wird nur auf Anfrage, nie beim Laden der Bilder.
/// </summary>
public interface IGameColorsSource
{
    /// <summary>
    /// Bekannte Farben des aktuellen Bildstands ohne jede Arbeit: true nur aus dem Speicher (kein Plattenzugriff, keine
    /// Sperre), etwa beim Öffnen des Fensters für das zuletzt gezeigte Spiel.
    /// </summary>
    bool TryPeek(GameConfig game, out GameColors colors);

    /// <summary>
    /// Farben des aktuellen Bildstands: aus dem Speicher, aus <c>art\index.json</c> oder einmal auf dem Bilder-Thread
    /// berechnet. Wirft nur beim Abbrechen. null: nicht gelesen (unerwarteter Fehler, Bilderdienst geleert), weder gemerkt
    /// noch gespeichert. Das ist etwas anderes als <see cref="GameColors.None"/> (graue Bilder, es gilt das Schema): Für
    /// Aufrufer sind die Farben dann unbekannt, gezeigte Farben bleiben (E94).
    /// </summary>
    Task<GameColors?> GetGameColorsAsync(GameConfig game, CancellationToken cancellationToken = default);
}
