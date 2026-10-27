using System.Globalization;
using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Localization;

/// <summary>
/// Sprache der Oberfläche (E95): Erkennung nach der Anzeigesprache von Windows, Regeln beim Laden der Konfiguration und
/// Wahl der Kulturen für Texte und Formate.
/// </summary>
public sealed class UiLanguageTests
{
    private const string ConfigFile = @"D:\Ablage\HDRpilot\config.json";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("de-CH", "de")]
    [InlineData("de", "de")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("nl-NL", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData("", "en")]
    public void System_language_german_means_german_everything_else_english(string culture, string expected)
    {
        Assert.Equal(expected, UiLanguage.FromSystem(CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData("en", "en")]
    [InlineData(" EN ", "en")]
    [InlineData("De", "de")]
    [InlineData("fr", null)]
    [InlineData("de-DE", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_de_and_en_are_known(string? value, string? expected)
    {
        Assert.Equal(expected, UiLanguage.Parse(value));
        Assert.Equal(expected ?? "de", UiLanguage.Effective(value));
    }

    [Theory]
    [InlineData("de", "de-AT", "de-AT")]
    [InlineData("de", "de-DE", "de-DE")]
    [InlineData("de", "en-US", "de-DE")]
    [InlineData("de", "fr-FR", "de-DE")]
    [InlineData("en", "en-GB", "en-GB")]
    [InlineData("en", "en-US", "en-US")]
    [InlineData("en", "de-DE", "en-US")]
    [InlineData("en", "fr-FR", "en-US")]
    [InlineData("en", "", "en-US")]
    public void Formats_follow_windows_only_when_its_language_matches(string language, string system, string expected)
    {
        Assert.Equal(expected, UiLanguage.FormattingCulture(language, CultureInfo.GetCultureInfo(system)).Name);
    }

    [Fact]
    public void Formatting_keeps_the_users_own_regional_settings_when_the_language_matches()
    {
        var custom = (CultureInfo)CultureInfo.GetCultureInfo("de-DE").Clone();
        custom.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";

        Assert.Same(custom, UiLanguage.FormattingCulture("de", custom));
        Assert.NotSame(custom, UiLanguage.FormattingCulture("en", custom));
    }

    [Theory]
    [InlineData("de", "de-DE")]
    [InlineData("en", "en-US")]
    [InlineData("xx", "de-DE")]
    public void Text_culture_is_german_or_english(string language, string expected)
    {
        Assert.Equal(expected, UiLanguage.UiCulture(language).Name);
    }

    [Fact]
    public void New_config_gets_the_detected_language_and_stores_it()
    {
        var service = new ConfigService(new ConfigStore(_fs, ConfigFile, _clock, newConfigLanguage: () => "en"), _clock);

        Assert.Equal("en", service.Current.Settings.Language);
        Assert.Contains("\"language\": \"en\"", _fs.ReadAllText(ConfigFile), StringComparison.Ordinal);
        Assert.Equal("en", UiLanguage.ForStartup(_fs, ConfigFile, () => "de"));
    }

    [Fact]
    public void New_config_on_a_german_system_is_german()
    {
        var service = new ConfigService(new ConfigStore(_fs, ConfigFile, _clock, newConfigLanguage: () => "de"), _clock);

        Assert.Equal("de", service.Current.Settings.Language);
        Assert.Contains("\"language\": \"de\"", _fs.ReadAllText(ConfigFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_config_without_language_stays_german_and_unchanged()
    {
        // Aktualisierung von 1.5.x: Wer HDRpilot schon nutzt, bleibt bei Deutsch, auch auf einem englischen Windows.
        const string old = """
            {
              "version": 1,
              "settings": { "pollIntervalMs": 800, "targetDisplays": [] },
              "games": [ { "source": "Steam", "sourceId": "570", "name": "Testspiel", "hdrAuto": true } ]
            }
            """;
        _fs.AddFile(ConfigFile, old);

        var service = new ConfigService(new ConfigStore(_fs, ConfigFile, _clock, newConfigLanguage: () => "en"), _clock);

        Assert.Null(service.LoadWarning);
        Assert.Null(service.Current.Settings.Language);
        Assert.Equal("de", UiLanguage.Effective(service.Current.Settings.Language));
        Assert.Equal("de", UiLanguage.ForStartup(_fs, ConfigFile, () => "en"));
        Assert.Equal(old, _fs.ReadAllText(ConfigFile)); // Laden schreibt nichts

        service.Update(c => c.Settings.GracePeriodSeconds = 12);
        Assert.DoesNotContain("language", _fs.ReadAllText(ConfigFile), StringComparison.Ordinal);
        Assert.Equal(800, service.Current.Settings.PollIntervalMs);
        Assert.True(service.Current.FindGame("Steam:570")!.HdrAuto);
    }

    [Fact]
    public void Unknown_language_counts_as_german_and_survives_saving()
    {
        _fs.AddFile(ConfigFile, """{ "version": 1, "settings": { "language": "fr" }, "games": [] }""");

        var service = new ConfigService(new ConfigStore(_fs, ConfigFile, _clock, newConfigLanguage: () => "en"), _clock);

        Assert.Equal("fr", service.Current.Settings.Language);
        Assert.Equal("de", UiLanguage.Effective(service.Current.Settings.Language));
        Assert.Equal("de", UiLanguage.ForStartup(_fs, ConfigFile, () => "en"));
        service.Update(c => c.Settings.GracePeriodSeconds = 12);
        Assert.Contains("\"language\": \"fr\"", _fs.ReadAllText(ConfigFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Stored_english_wins_over_a_german_system()
    {
        _fs.AddFile(ConfigFile, """{ "version": 1, "settings": { "language": "en" }, "games": [] }""");

        Assert.Equal("en", UiLanguage.ForStartup(_fs, ConfigFile, () => "de"));
        Assert.Equal("en", new ConfigService(new ConfigStore(_fs, ConfigFile, _clock, newConfigLanguage: () => "de"), _clock).Current.Settings.Language);
    }

    [Fact]
    public void Missing_config_at_startup_follows_the_system_without_writing()
    {
        Assert.Equal("en", UiLanguage.ForStartup(_fs, ConfigFile, () => "en"));
        Assert.Equal("de", UiLanguage.ForStartup(_fs, ConfigFile, () => "de"));
        Assert.False(_fs.FileExists(ConfigFile));
    }

    [Fact]
    public void Broken_or_unreadable_config_at_startup_is_german()
    {
        _fs.AddFile(ConfigFile, "{ kaputt");
        Assert.Equal("de", UiLanguage.ForStartup(_fs, ConfigFile, () => "en"));

        var locked = new InMemoryFileSystem();
        locked.AddFile(ConfigFile, """{ "settings": { "language": "en" } }""");
        locked.FailReading(ConfigFile, new IOException("gesperrt"));
        Assert.Equal("de", UiLanguage.ForStartup(locked, ConfigFile, () => "en"));
    }

    [Theory]
    [InlineData("""{ "settings": { "language": "en" } }""", "en")]
    [InlineData("""{ "settings": { "language": "de" }, "games": [] }""", "de")]
    [InlineData("""{ "settings": { "pollIntervalMs": 800 } }""", null)]
    [InlineData("""{ "settings": { "language": 3 } }""", null)]
    [InlineData("""{ "settings": { "language": null } }""", null)]
    [InlineData("""{ "settings": { "language": "de" }, "settings": { "language": "en" } }""", "en")]
    [InlineData("""{ "games": [ { "settings": { "language": "en" } } ] }""", null)]
    [InlineData("// Kommentar\n{ \"settings\": { \"language\": \"en\", }, }", "en")]
    [InlineData("[]", null)]
    [InlineData("", null)]
    [InlineData("kein JSON", null)]
    public void Reading_only_the_language_is_tolerant(string text, string? expected)
    {
        Assert.Equal(expected, UiLanguage.ReadConfigured(text));
    }

    [Fact]
    public void Config_with_english_and_appearance_round_trips()
    {
        var service = new ConfigService(new ConfigStore(_fs, ConfigFile, _clock, newConfigLanguage: () => "de"), _clock);
        service.Update(c =>
        {
            c.Settings.Language = "en";
            c.Settings.Appearance.Theme = "Dark";
        });

        var reloaded = new ConfigService(new ConfigStore(_fs, ConfigFile, _clock, newConfigLanguage: () => "de"), _clock).Current.Settings;
        Assert.Equal("en", reloaded.Language);
        Assert.Equal("Dark", reloaded.Appearance.Theme);
    }
}
