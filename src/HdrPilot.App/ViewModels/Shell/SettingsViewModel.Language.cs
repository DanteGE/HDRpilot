using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.Core.Config;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>Eintrag der Auswahl „Sprache“ (E95): Name immer in der eigenen Sprache („Deutsch“, „English“).</summary>
public sealed record LanguageOption(string Value, string Label)
{
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new(UiLanguage.German, ShellStrings.LanguageNameGerman),
        new(UiLanguage.English, ShellStrings.LanguageNameEnglish),
    ];

    public override string ToString() => Label;
}

/// <summary>
/// Sprache der Oberfläche (E95) in der Karte „Start und Hinweise“: übernommen mit „Speichern“ wie die übrigen allgemeinen
/// Einstellungen, wirksam nach einem Neustart (die Texte stehen beim Öffnen der Fenster fest). Weicht die Wahl von der
/// angezeigten Sprache ab, erscheinen ein Hinweis und „Jetzt neu starten“: speichert alles wie „Speichern“ und startet
/// HDRpilot neu. Ein unbekannter oder fehlender Wert in config.json bleibt unverändert, solange die Wahl nicht geändert wird.
/// </summary>
public sealed partial class SettingsViewModel
{
    private string _language = UiLanguage.German;
    private string _loadedLanguage = UiLanguage.German;
    private AsyncRelayCommand? _restartNowCommand;

    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;

    /// <summary>Sprache, in der dieses Fenster gerade angezeigt wird ("de" oder "en").</summary>
    public string DisplayedLanguage { get; } = UiLanguage.Current;

    /// <summary>Gewählte Sprache ("de" oder "en"); andere Werte werden übergangen.</summary>
    public string Language
    {
        get => _language;
        set
        {
            if (UiLanguage.Parse(value) is { } language && SetProperty(ref _language, language))
            {
                OnPropertyChanged(nameof(LanguageNeedsRestart));
                UpdateChanges();
            }
        }
    }

    /// <summary>True, wenn die gewählte Sprache erst nach einem Neustart sichtbar wird (Hinweis und „Jetzt neu starten“).</summary>
    public bool LanguageNeedsRestart => !string.Equals(_language, DisplayedLanguage, StringComparison.Ordinal);

    /// <summary>„Jetzt neu starten“: speichert alle Einstellungen (wie „Speichern“) und startet HDRpilot neu.</summary>
    public AsyncRelayCommand RestartNowCommand => _restartNowCommand ??= new AsyncRelayCommand(RestartNowAsync);

    private void LoadLanguage(AppSettings settings)
    {
        _loadedLanguage = UiLanguage.Effective(settings.Language);
        _language = _loadedLanguage;
    }

    /// <summary>Schreibt die Sprache nur, wenn sie geändert wurde: Ältere Konfigurationen bleiben sonst ohne das Feld.</summary>
    private void StoreLanguage(AppSettings settings)
    {
        if (!string.Equals(_language, _loadedLanguage, StringComparison.Ordinal))
        {
            settings.Language = _language;
        }
    }

    private async Task RestartNowAsync()
    {
        if (!SaveSettings())
        {
            return; // Fehler steht schon im Fuß, nichts neu gestartet
        }

        if (!await _context.RestartAsync())
        {
            ErrorMessage = ShellStrings.RestartFailed;
        }
    }
}
