using HdrPilot.App.Resources;
using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using HdrPilot.Core.Config;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Setup.UI;

/// <summary>
/// Sprache von Setup und Entfernen (E95): wie die Anzeigesprache von Windows, nie aus den Einstellungen von HDRpilot; nur
/// ein Prüflauf (<c>--test-root</c>) kann sie mit <c>--ui-language</c> festlegen, auch für die Kopie zum Entfernen.
/// </summary>
public sealed class SetupLanguageTests
{
    private const string Root = @"D:\Pruefung\Setup";

    [Fact]
    public void Without_a_test_root_the_setup_follows_windows()
    {
        Assert.Equal(UiLanguage.ForNewConfig(), SetupArguments.Parse([]).EffectiveUiLanguage);
        Assert.Equal(UiLanguage.ForNewConfig(), SetupArguments.Parse(["--ui-language", "en"]).EffectiveUiLanguage);
        Assert.Equal("de", UiLanguage.ForNewConfig()); // Tests laufen mit deutscher Anzeigesprache
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("de", "de")]
    [InlineData("EN", "en")]
    public void Test_runs_can_fix_the_language(string value, string expected)
    {
        var args = SetupArguments.Parse(["--test-root", Root, "--ui-language", value]);

        Assert.Equal(expected, args.EffectiveUiLanguage);
        Assert.Empty(args.Unknown);
    }

    [Fact]
    public void Unknown_language_is_an_unknown_switch()
    {
        var args = SetupArguments.Parse(["--quiet", "--test-root", Root, "--ui-language", "fr"]);

        Assert.Null(args.UiLanguage);
        Assert.Equal(["--ui-language", "fr"], args.Unknown);
        Assert.Equal(["--ui-language", "fr"], SetupMode.RefusedArguments(SetupModeKind.Setup, args));
    }

    [Fact]
    public void The_uninstall_copy_keeps_the_fixed_language()
    {
        var handOff = SetupArguments.Parse(["--uninstall", "--test-root", Root, "--ui-language", "en"]).ToContinueArguments(4242);

        Assert.Equal(["--uninstall-continue", "--wait-pid", "4242", "--test-root", Root, "--ui-language", "en"], handOff);
        Assert.Equal("en", SetupArguments.Parse(handOff).EffectiveUiLanguage);
        Assert.DoesNotContain("--ui-language", SetupArguments.Parse(["--uninstall"]).ToContinueArguments(1));
    }

    [Fact]
    public void Setup_texts_exist_in_english()
    {
        using var _ = TestCulture.Use("en-US");

        Assert.Equal("Install HDRpilot 1.6.0", SetupText.Format(SetupStrings.SetupTitleInstall, "1.6.0"));
        Assert.Equal("Uninstall HDRpilot", SetupStrings.UninstallWindowTitle);
        Assert.Equal("Register under “Installed apps”", SetupText.Step(SetupStep.Register));
        Assert.Equal("done", SetupText.State(StepState.Done));
        Assert.StartsWith("Unexpected error: disk on fire. The previous version is unchanged.", SetupText.Error(SetupErrorKind.Unexpected, "disk on fire", @"D:\Pruefung\setup.log"), StringComparison.Ordinal);
    }
}
