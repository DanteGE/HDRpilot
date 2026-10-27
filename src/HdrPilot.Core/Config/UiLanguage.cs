using System.Globalization;
using System.Text.Json;
using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Config;

/// <summary>
/// Sprache der Oberfläche (E95): Deutsch ("de") oder Englisch ("en"). Gespeichert als <see cref="AppSettings.Language"/>
/// (Feld <c>language</c> in config.json); gilt ab dem nächsten Start, weil die Texte beim Erzeugen der Fenster fest
/// eingesetzt werden.
/// <list type="bullet">
/// <item>Neue Konfiguration (Erstinstallation, erster Start einer Kopie ohne Installation): nach der Anzeigesprache von
/// Windows, Deutsch bei Deutsch, sonst Englisch; der Wert wird gleich gespeichert.</item>
/// <item>Vorhandene Konfiguration ohne <c>language</c> (Aktualisierung von 1.5.x und älter) oder mit unbekanntem Wert:
/// Deutsch, damit bisherige Nutzer nicht plötzlich umgestellt werden. Die Datei bleibt dabei unverändert.</item>
/// </list>
/// Formate (Datum, Zahlen) folgen den Regionseinstellungen von Windows, solange deren Sprache zur Oberfläche passt;
/// sonst gelten die von de-DE bzw. en-US (<see cref="FormattingCulture"/>).
/// </summary>
public static class UiLanguage
{
    public const string German = "de";

    public const string English = "en";

    /// <summary>
    /// Anzeigesprache und Regionsformat von Windows, festgehalten beim ersten Zugriff auf diese Klasse, also bevor
    /// <see cref="Apply"/> die Kulturen des Prozesses umstellt (ein Neustart über die Einstellungen soll wieder vom
    /// System ausgehen, nicht von der zuletzt gesetzten Sprache).
    /// </summary>
    private static readonly CultureInfo SystemUi = CultureInfo.CurrentUICulture;

    private static readonly CultureInfo SystemFormat = CultureInfo.CurrentCulture;

    /// <summary>Anzeigesprache von Windows beim Start des Prozesses (Sprache des Benutzers).</summary>
    public static CultureInfo SystemUiCulture => SystemUi;

    /// <summary>Regionsformat von Windows beim Start des Prozesses.</summary>
    public static CultureInfo SystemFormattingCulture => SystemFormat;

    /// <summary>Sprache, in der der Prozess gerade Texte anzeigt (nach <see cref="Apply"/>).</summary>
    public static string Current => FromSystem(CultureInfo.CurrentUICulture);

    /// <summary>Deutsch, wenn die Kultur deutsch ist (de, de-DE, de-AT, de-CH …), sonst Englisch (auch für die invariante Kultur).</summary>
    public static string FromSystem(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, German, StringComparison.OrdinalIgnoreCase) ? German : English;

    /// <summary>Sprache einer neuen Konfiguration: nach der Anzeigesprache von Windows beim Start.</summary>
    public static string ForNewConfig() => FromSystem(SystemUi);

    /// <summary>"de" oder "en" (ohne Rücksicht auf Groß- und Kleinschreibung und Leerraum); alles andere: null.</summary>
    public static string? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        German => German,
        English => English,
        _ => null,
    };

    /// <summary>Wirksame Sprache einer vorhandenen Konfiguration: der gespeicherte Wert, fehlt er oder ist er unbekannt, Deutsch.</summary>
    public static string Effective(string? configured) => Parse(configured) ?? German;

    /// <summary>
    /// Sprache für den Start, bevor die Konfiguration geladen ist: ohne config.json wie eine neue Konfiguration
    /// (<paramref name="newConfigLanguage"/>, Standard <see cref="ForNewConfig"/>), sonst <see cref="Effective"/> aus der
    /// Datei. Eine unlesbare oder defekte Datei gilt als vorhanden (Deutsch). Wirft nie.
    /// </summary>
    public static string ForStartup(IFileSystem fileSystem, string configFile, Func<string>? newConfigLanguage = null)
    {
        try
        {
            if (!fileSystem.FileExists(configFile))
            {
                return (newConfigLanguage ?? ForNewConfig)();
            }

            return Effective(ReadConfigured(fileSystem.ReadAllText(configFile)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return German;
        }
    }

    /// <summary>
    /// Liest nur <c>settings.language</c> aus dem Text einer config.json (ohne die ganze Datei zu deserialisieren). Null, wenn
    /// das Feld fehlt, kein Text ist oder die Datei kein gültiges JSON ist.
    /// </summary>
    public static string? ReadConfigured(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(configText, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Wie die Deserialisierung (camelCase, bei doppelten Feldern das letzte).
            string? language = null;
            foreach (var settings in document.RootElement.EnumerateObject().Where(p => p.NameEquals("settings") && p.Value.ValueKind == JsonValueKind.Object))
            {
                language = null;
                foreach (var field in settings.Value.EnumerateObject().Where(p => p.NameEquals("language")))
                {
                    language = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() : null;
                }
            }

            return language;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Kultur der Texte: de-DE oder en-US (die Ressourcen kennen nur Deutsch und Englisch).</summary>
    public static CultureInfo UiCulture(string language) =>
        CultureInfo.GetCultureInfo(Parse(language) == English ? "en-US" : "de-DE");

    /// <summary>
    /// Kultur für Datum und Zahlen: das Regionsformat von Windows (<paramref name="systemCulture"/>, samt eigener Anpassungen),
    /// wenn seine Sprache zur Oberfläche passt (etwa de-AT zu Deutsch, en-GB zu Englisch); sonst de-DE bzw. en-US, damit
    /// ein englischer Satz kein deutsches Datum trägt und umgekehrt.
    /// </summary>
    public static CultureInfo FormattingCulture(string language, CultureInfo systemCulture)
    {
        var effective = Parse(language) ?? German;
        return string.Equals(systemCulture.TwoLetterISOLanguageName, effective, StringComparison.OrdinalIgnoreCase)
            ? systemCulture
            : UiCulture(effective);
    }

    /// <summary>
    /// Stellt den Prozess auf die Sprache um: Texte (<see cref="CultureInfo.CurrentUICulture"/>) und Formate
    /// (<see cref="CultureInfo.CurrentCulture"/>) des aktuellen Threads und als Vorgabe für alle weiteren Threads. Vor dem
    /// ersten Fenster und der ersten Ressource aufrufen.
    /// </summary>
    public static void Apply(string language)
    {
        var ui = UiCulture(language);
        var format = FormattingCulture(language, SystemFormat);
        CultureInfo.DefaultThreadCurrentUICulture = ui;
        CultureInfo.DefaultThreadCurrentCulture = format;
        CultureInfo.CurrentUICulture = ui;
        CultureInfo.CurrentCulture = format;
    }
}
